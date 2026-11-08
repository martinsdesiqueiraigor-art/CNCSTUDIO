using CncStudio.Core.Cam;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Setup;
using CncStudio.Core.Simulation;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>CAM-13 PR 2: o Code usa o pacote do CAM (quem vale, avisos, arquivo ao lado, Siemens, bruto no simulador).</summary>
public class CamHandoffCodeTests
{
    private static string Dir => Path.Combine(AppContext.BaseDirectory, "trabalhos-referencia");

    private static readonly IReadOnlyDictionary<int, Point3> NoOffsets = new Dictionary<int, Point3>();

    private static CamJob Load(string name)
    {
        JobLoadResult r = CamJobJson.FromJson(File.ReadAllText(Path.Combine(Dir, name + ".casillas-job")));
        Assert.True(r.Ok, r.Error);
        return r.Job!;
    }

    private static JobStockShape Margins() => new(StockShapeKind.Margins) { XMinus = 15, XPlus = 15, YMinus = 15, YPlus = 15, ZTop = 1, ZBottom = 5 };

    // centro com Z0 na base, bloco por sobra: prisma 90 × 70 × 26 com canto em (−45, −35, −5)
    private static (CamHandoff Package, string Program) Sent(CamDialect dialect = CamDialect.Fanuc, JobStockShape? shape = null)
    {
        CamJob job = Load("fresa-zero-centro-base");
        job.Dialect = dialect;
        job.SetStock(null, shape ?? Margins());
        string program = CamJobBuilder.Build(job, null).Program;
        return (CamHandoff.Create(job, program, null), program);
    }

    private static IReadOnlyList<HandoffNoticeKind> Kinds(CamHandoff? p, MachiningSetup? setup, string text, Func<int, double>? table = null,
        IReadOnlyDictionary<int, Point3>? offsets = null, string? read = null) =>
        CamHandoffCode.Notices(p, read, setup, text, table ?? (_ => 0), offsets ?? NoOffsets).Select(n => n.Kind).ToList();

    [Fact]
    public void Bruto_vazio_recebe_o_do_CAM_sem_material_e_com_a_etiqueta()
    {
        (CamHandoff p, string program) = Sent();
        MachiningSetup setup = MachiningSetup.CreateDefault("PECA.nc");
        Assert.True(CamHandoffCode.FillStock(setup, p));
        Assert.True(setup.Stock is { Kind: SetupStockKind.Prism, X: -45, Y: -35, Z: -5, Width: 90, Depth: 70, Height: 26, Material: "" });
        Assert.True(CamHandoffCode.StockFromCam(setup, p));
        Assert.True(setup.IsValid());
        Assert.Empty(setup.Tools); // nenhuma SetupTool com padrão escondido
        Assert.Empty(Kinds(p, setup, program));

        // editar o bruto tira a etiqueta (e aí ele é "já informado" e diferente: aviso 2)
        setup.Stock.Width = 91;
        Assert.False(CamHandoffCode.StockFromCam(setup, p));
        Assert.Equal([HandoffNoticeKind.StockDiffers], Kinds(p, setup, program));
    }

    [Fact]
    public void Bruto_informado_e_diferente_nao_e_sobrescrito_e_avisa()
    {
        (CamHandoff p, string program) = Sent();
        var informed = new SetupStock { X = 0, Y = 0, Z = -20, Width = 60, Depth = 40, Height = 20, Material = "Alumínio" };
        MachiningSetup setup = MachiningSetup.CreateDefault("PECA.nc");
        setup.Stock = informed;
        Assert.False(CamHandoffCode.FillStock(setup, p));
        Assert.Same(informed, setup.Stock);
        Assert.Equal("Alumínio", setup.Stock.Material);
        HandoffNotice n = Assert.Single(CamHandoffCode.Notices(p, null, setup, program, _ => 0, NoOffsets));
        Assert.Equal(
            "Este programa já tem um bruto informado no Code. O bruto do CAM (prisma 90 × 70 × 26 mm, canto X -45 Y -35 Z -5) é diferente e não foi aplicado.",
            n.Text);

        // o mesmo bruto (material à parte) não avisa
        setup.Stock = CamHandoffCode.ToSetupStock(p.Stock)!;
        setup.Stock.Material = "Aço";
        Assert.Empty(Kinds(p, setup, program));
    }

    [Fact]
    public void T_em_conflito_vale_o_do_CAM_no_programa_e_a_tabela_fica_igual()
    {
        (CamHandoff p, string program) = Sent();
        var table = new Dictionary<int, double> { [1] = 12, [7] = 3 };
        Func<int, double> tableOf = t => table.GetValueOrDefault(t);
        MachineSettings settings = new() { ToolDiameterOf = tableOf };

        MachineSettings used = CamHandoffCode.ApplyDiameters(settings, p);
        Assert.Equal(10, used.ToolDiameterOf!(1)); // CAM
        Assert.Equal(6, used.ToolDiameterOf!(2));  // T novo: só no CAM
        Assert.Equal(3, used.ToolDiameterOf!(7));  // fora do CAM: tabela
        Assert.Equal(12, table[1]);
        Assert.Equal(2, table.Count);

        HandoffNotice n = Assert.Single(CamHandoffCode.Notices(p, null, null, program, tableOf, NoOffsets), x => x.Kind == HandoffNoticeKind.ToolDiameter);
        Assert.Equal("T1: o CAM usou Ø 10 mm e a tabela tem Ø 12 mm. Neste programa vale o do CAM; a tabela não foi alterada.", n.Text);
        // T que a tabela não tem (vai entrar nela como hoje, no envio) não avisa
        Assert.DoesNotContain(CamHandoffCode.Notices(p, null, null, program, tableOf, NoOffsets), x => x.Text.StartsWith("T2", StringComparison.Ordinal));

        // a ferramenta do setup do programa (escolha do usuário) segue na frente do CAM
        var setup = new MachiningSetup { Tools = [new SetupTool { Number = 1, Diameter = 8 }] };
        Assert.Equal(8, setup.ApplyTo(used).ToolDiameterOf!(1));
    }

    [Fact]
    public void Tabela_de_zeros_com_valor_so_avisa()
    {
        (CamHandoff p, string program) = Sent();
        var offsets = new Dictionary<int, Point3> { [54] = new Point3(100, 50.5, -200), [55] = Point3.Origin };
        HandoffNotice n = Assert.Single(CamHandoffCode.Notices(p, null, null, program, _ => 0, offsets), x => x.Kind == HandoffNoticeKind.WorkOffset);
        Assert.Equal("A tabela de zeros da máquina tem G54 diferente de zero (100, 50,5, -200). A trajetória fica deslocada em relação ao bruto.", n.Text);
        Assert.Equal(new Point3(100, 50.5, -200), offsets[54]);
        offsets[54] = Point3.Origin;
        Assert.DoesNotContain(HandoffNoticeKind.WorkOffset, Kinds(p, null, program, offsets: offsets));
    }

    [Fact]
    public void Programa_editado_ou_renumerado_avisa_e_o_aviso_fica()
    {
        (CamHandoff p, string program) = Sent();
        Assert.Empty(Kinds(p, null, program.Replace("\n", "\r\n")));
        string edited = program.Replace("S3000", "S3100");
        Assert.Equal([HandoffNoticeKind.ProgramChanged], Kinds(p, null, edited));
        Assert.Equal(CamHandoffCode.ProgramChangedText, CamHandoffCode.Notices(p, null, null, edited, _ => 0, NoOffsets)[0].Text);
    }

    [Fact]
    public void STEP_e_sem_bruto_avisam_e_usam_o_automatico()
    {
        var summary = new StepStockSummary(-1, -2, -22, 31, 27, 0, 18000);
        (CamHandoff step, string program) = Sent(shape: new JobStockShape(StockShapeKind.Step) { StepPath = @"C:\pecas\bruto.step", StepSummary = summary });
        MachiningSetup setup = MachiningSetup.CreateDefault("PECA.nc");
        Assert.False(CamHandoffCode.FillStock(setup, step));
        Assert.Null(setup.Stock);
        HandoffNotice n = Assert.Single(CamHandoffCode.Notices(step, null, setup, program, _ => 0, NoOffsets));
        Assert.Equal("O bruto do CAM é um STEP (bruto.step, 32 × 29 × 22 mm, 18 cm³). O simulador não desenha bruto em STEP.", n.Text);
        Assert.Equal(StockSource.Automatic, SetupBinding.EffectiveStock(setup, MachineKind.Mill, program, Bounds3.Empty).Source);

        (CamHandoff none, string program2) = Sent(shape: Margins() with { XPlus = null });
        Assert.Null(none.Stock);
        Assert.False(CamHandoffCode.FillStock(setup, none));
        Assert.Equal([HandoffNoticeKind.NoStock], Kinds(none, setup, program2));
        Assert.Equal(CamHandoffCode.NoStockText, CamHandoffCode.Notices(none, null, setup, program2, _ => 0, NoOffsets)[0].Text);
    }

    [Fact]
    public void Arquivo_ao_lado_salvar_salvar_como_abrir_e_renomear()
    {
        (CamHandoff p, string program) = Sent();
        string dir = Path.Combine(Path.GetTempPath(), "cam13-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string nc = Path.Combine(dir, "PECA.nc");
            File.WriteAllText(nc, program);
            CamHandoffFile.Write(nc, p);
            Assert.True(File.Exists(Path.Combine(dir, "PECA.nc.casillas-cam")));
            HandoffReadResult read = CamHandoffFile.Read(nc);
            Assert.Null(read.Warning);
            Assert.Equal(CamHandoffJson.ToJson(p), CamHandoffJson.ToJson(read.Package!));
            Assert.True(read.Package!.Matches(File.ReadAllText(nc)));

            // "Salvar como": o pacote vai para o nome novo, com o mesmo hash
            string other = Path.Combine(dir, "NOVO.nc");
            File.WriteAllText(other, program);
            CamHandoffFile.Write(other, read.Package);
            Assert.Equal(p.ProgramHash, CamHandoffFile.Read(other).Package!.ProgramHash);

            // renomear o programa sem o arquivo ao lado: abre como hoje (sem pacote, sem aviso)
            string renamed = Path.Combine(dir, "RENOMEADO.nc");
            File.Move(nc, renamed);
            Assert.Equal(new HandoffReadResult(null, null), CamHandoffFile.Read(renamed));

            // versão futura e ilegível: avisos 7 e 8
            File.WriteAllText(CamHandoffFile.PathFor(renamed), CamHandoffJson.ToJson(p).Replace("\"versao\": 1", "\"versao\": 9"));
            HandoffReadResult future = CamHandoffFile.Read(renamed);
            Assert.Equal([HandoffNoticeKind.FutureVersion], Kinds(future.Package, null, program, read: future.Warning));
            Assert.Equal("O arquivo de dados do CAM é de uma versão mais nova e foi ignorado.", future.Warning);
            File.WriteAllText(CamHandoffFile.PathFor(renamed), "{ nada");
            HandoffReadResult bad = CamHandoffFile.Read(renamed);
            Assert.Equal([HandoffNoticeKind.Unreadable], Kinds(bad.Package, null, program, read: bad.Warning));
            Assert.Equal("O arquivo de dados do CAM está ilegível e foi ignorado.", bad.Warning);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Siemens_vai_com_mpf_e_o_dialeto_vem_do_pacote()
    {
        Assert.Equal("PECA.mpf", CamHandoffCode.ProgramFileName("PECA", CamDialect.Siemens));
        Assert.Equal("PECA.nc", CamHandoffCode.ProgramFileName("PECA", CamDialect.Fanuc));
        Assert.Equal(Dialect.Siemens, DialectDetector.FromFileName("PECA.mpf"));
        Assert.Equal(Dialect.Fanuc, DialectDetector.FromFileName("PECA.nc"));
        Assert.Equal(Dialect.Siemens, CamHandoffCode.DialectOf(Sent(CamDialect.Siemens).Package));
        Assert.Equal(Dialect.Fanuc, CamHandoffCode.DialectOf(Sent().Package));
    }

    public static TheoryData<CamDialect, ZeroPoint, ZeroLevel, StockShapeKind, StockAxis> Placements()
    {
        var data = new TheoryData<CamDialect, ZeroPoint, ZeroLevel, StockShapeKind, StockAxis>();
        foreach (CamDialect d in new[] { CamDialect.Fanuc, CamDialect.Siemens })
        {
            data.Add(d, ZeroPoint.Center, ZeroLevel.Bottom, StockShapeKind.Margins, StockAxis.Z);
            data.Add(d, ZeroPoint.XMaxYMin, ZeroLevel.Top, StockShapeKind.Margins, StockAxis.Z);
            foreach (StockAxis axis in new[] { StockAxis.Z, StockAxis.X, StockAxis.Y })
            {
                data.Add(d, ZeroPoint.Center, ZeroLevel.Bottom, StockShapeKind.Cylinder, axis);
                data.Add(d, ZeroPoint.XMinYMin, ZeroLevel.Top, StockShapeKind.Tube, axis);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Placements))]
    public void Bruto_do_setup_montado_envolve_o_corte_do_programa(CamDialect dialect, ZeroPoint point, ZeroLevel level, StockShapeKind kind, StockAxis axis)
    {
        CamJob job = Load("fresa-zero-centro-base");
        (job.Dialect, job.Zero) = (dialect, new JobZero(point, level));
        // redondo e tubo grandes o bastante para a peça 60 × 40 × 20 em qualquer eixo
        job.SetStock(null, kind == StockShapeKind.Margins ? Margins() : new JobStockShape(kind)
        {
            Axis = axis, Diameter = 120, InnerDiameter = kind == StockShapeKind.Tube ? 4 : null, Length = 120,
        });
        string program = CamJobBuilder.Build(job, null).Program;
        MachiningSetup setup = MachiningSetup.CreateDefault("PECA");
        Assert.True(CamHandoffCode.FillStock(setup, CamHandoff.Create(job, program, null)));

        // o Code lê o programa (dialeto do pacote) e o simulador usa o bruto do setup
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(program, dialect == CamDialect.Siemens ? Dialect.Siemens : Dialect.Fanuc, setup.ApplyTo(MachineSettings.Default));
        Bounds3 cut = analysis.ToolPath.CuttingBounds;
        EffectiveStockResult effective = SetupBinding.EffectiveStock(setup, MachineKind.Mill, program, cut);
        Assert.Equal(StockSource.Setup, effective.Source);
        Bounds3 box = setup.Stock!.Extent;
        Assert.InRange(cut.Min.X, box.Min.X, box.Max.X);
        Assert.InRange(cut.Max.X, box.Min.X, box.Max.X);
        Assert.InRange(cut.Min.Y, box.Min.Y, box.Max.Y);
        Assert.InRange(cut.Max.Y, box.Min.Y, box.Max.Y);
        Assert.InRange(cut.Min.Z, box.Min.Z, box.Max.Z);
        if (setup.Stock.IsRound)
        {
            // o fundo do corte (no meio da peça em XY) tem material no cilindro: eixo e centro na convenção do setup
            double cx = (cut.Min.X + cut.Max.X) / 2, cy = (cut.Min.Y + cut.Max.Y) / 2;
            Assert.True(setup.Stock.RadialDistance(cx, cy, cut.Min.Z) <= setup.Stock.Diameter / 2);
        }

        // o mesmo setup na simulação de material: monta sem erro, com o bruto do setup
        MaterialSimulationParts parts = SimulationFactory.Create(analysis, setup, program, 64);
        Assert.NotNull(parts.Stock);
    }
}
