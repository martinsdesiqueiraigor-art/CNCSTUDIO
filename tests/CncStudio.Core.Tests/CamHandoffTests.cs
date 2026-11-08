using System.Text.Json.Nodes;
using CncStudio.Core.Cam;
using CncStudio.Core.Dialects;
using CncStudio.Core.Geometry;
using CncStudio.Core.Setup;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>CAM-13 PR 1: pacote do CAM para o Code (bruto no referencial do programa, ferramentas, zero, fixação, hash, JSON v1).</summary>
public class CamHandoffTests
{
    private static string Dir => Path.Combine(AppContext.BaseDirectory, "trabalhos-referencia");

    private static CamJob Load(string name)
    {
        JobLoadResult r = CamJobJson.FromJson(File.ReadAllText(Path.Combine(Dir, name + ".casillas-job")));
        Assert.True(r.Ok, r.Error);
        return r.Job!;
    }

    // peça 0..60 × 0..40 (as referências), altura 20; sobra 15 nos lados, 1 em cima e 5 embaixo
    private static JobStockShape Margins() => new(StockShapeKind.Margins) { XMinus = 15, XPlus = 15, YMinus = 15, YPlus = 15, ZTop = 1, ZBottom = 5 };

    private static void Near(double expected, double actual) => Assert.Equal(expected, actual, 9);

    public static TheoryData<ZeroPoint, ZeroLevel, double, double, double> Zeros()
    {
        var data = new TheoryData<ZeroPoint, ZeroLevel, double, double, double>();
        foreach (ZeroLevel level in new[] { ZeroLevel.Top, ZeroLevel.Bottom })
        {
            double z = level == ZeroLevel.Top ? 0 : -20;
            data.Add(ZeroPoint.Drawing, level, 0, 0, z);
            data.Add(ZeroPoint.XMinYMin, level, 0, 0, z);
            data.Add(ZeroPoint.XMidYMin, level, 30, 0, z);
            data.Add(ZeroPoint.XMaxYMin, level, 60, 0, z);
            data.Add(ZeroPoint.XMinYMid, level, 0, 20, z);
            data.Add(ZeroPoint.Center, level, 30, 20, z);
            data.Add(ZeroPoint.XMaxYMid, level, 60, 20, z);
            data.Add(ZeroPoint.XMinYMax, level, 0, 40, z);
            data.Add(ZeroPoint.XMidYMax, level, 30, 40, z);
            data.Add(ZeroPoint.XMaxYMax, level, 60, 40, z);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Zeros))]
    public void Caixa_sai_deslocada_pelo_zero_nos_9_pontos_e_no_Z0(ZeroPoint point, ZeroLevel level, double zx, double zy, double zz)
    {
        CamJob job = Load("fresa-zero-centro-base");
        job.Zero = new JobZero(point, level);
        job.SetStock(null, Margins());
        HandoffStock s = HandoffStock.InProgram(job)!;
        Assert.Equal(HandoffStockKind.Prism, s.Kind);
        Near(-15 - zx, s.X);
        Near(-15 - zy, s.Y);
        Near(-25 - zz, s.Z);
        (double w, double d, double h) = (s.Width, s.Depth, s.Height);
        Assert.Equal((90.0, 70.0, 26.0), (w, d, h));
    }

    [Fact]
    public void Referencias_com_zero_e_bloco_antigo()
    {
        // centro e Z0 na base da peça de 20: zero interno (30, 20, −20)
        CamJob center = Load("fresa-zero-centro-base");
        center.SetStock(null, Margins());
        HandoffStock c = HandoffStock.InProgram(center)!;
        Assert.Equal((-45.0, -35.0, -5.0), (c.X, c.Y, c.Z));

        // G55 no canto inferior direito (60, 0), Z0 no topo, com o bloco absoluto (−2,5; −3; 65 × 46 × 18,5)
        HandoffStock g55 = HandoffStock.InProgram(Load("fresa-zero-g55-canto"))!;
        Assert.Equal((-62.5, -3.0, -18.5, 65.0, 46.0, 18.5), (g55.X, g55.Y, g55.Z, g55.Width, g55.Depth, g55.Height));

        // arquivo antigo (Siemens, bloco absoluto, zero "como antes"): o bruto de hoje, sem deslocamento
        CamJob siemens = Load("fresa-bloco-siemens");
        Assert.True(siemens.Zero is { AsBefore: true });
        HandoffStock old = HandoffStock.InProgram(siemens)!;
        StockSolid today = CamJobStock.Check(siemens).Solid!;
        Assert.Equal((today.MinX, today.MinY, today.MinZ, today.SizeX, today.SizeY, today.SizeZ), (old.X, old.Y, old.Z, old.Width, old.Depth, old.Height));
        Assert.Equal((-5.0, -5.0, -20.0), (old.X, old.Y, old.Z));
    }

    [Fact]
    public void Sem_bruto_ou_sem_zero_nao_tem_bruto_no_pacote()
    {
        Assert.Null(HandoffStock.InProgram(Load("fresa-sem-bruto")));
        CamJob job = Load("fresa-zero-centro-base");
        job.SetStock(null, Margins());
        job.Zero = null;
        Assert.Null(HandoffStock.InProgram(job));
        job.Zero = new JobZero(ZeroPoint.Center, ZeroLevel.Top);
        job.SetStock(null, Margins() with { XPlus = null });
        Assert.Null(HandoffStock.InProgram(job));
    }

    [Theory]
    [InlineData(StockAxis.Z, false)]
    [InlineData(StockAxis.X, false)]
    [InlineData(StockAxis.Y, false)]
    [InlineData(StockAxis.Z, true)]
    [InlineData(StockAxis.X, true)]
    [InlineData(StockAxis.Y, true)]
    public void Cilindro_e_tubo_nos_tres_eixos(StockAxis axis, bool tube)
    {
        // zero no centro com Z0 na base: interno (30, 20, −20); centro padrão do cilindro (30, 20, −10) → programa (0, 0, 10)
        CamJob job = Load("fresa-zero-centro-base");
        var shape = new JobStockShape(tube ? StockShapeKind.Tube : StockShapeKind.Cylinder)
        {
            Axis = axis, Diameter = 80, InnerDiameter = tube ? 30 : null, Length = 120,
        };
        job.SetStock(null, shape);
        HandoffStock s = HandoffStock.InProgram(job)!;
        Assert.Equal(tube ? HandoffStockKind.Tube : HandoffStockKind.Round, s.Kind);
        Assert.Equal(axis, s.Axis);
        (double x, double y, double z) expected = axis switch
        {
            StockAxis.X => (-60, 0, 10),
            StockAxis.Y => (0, -60, 10),
            _ => (0, 0, -50),
        };
        Assert.Equal(expected, (s.X, s.Y, s.Z));
        Assert.Equal((120.0, 80.0, tube ? 30.0 : 0.0), (s.Height, s.Diameter, s.InnerDiameter));

        // a mesma caixa que o bruto do setup daria com estes campos (mesma convenção)
        var setup = new SetupStock
        {
            Kind = tube ? SetupStockKind.Tube : SetupStockKind.Round, Axis = (SetupAxis)(int)axis, X = s.X, Y = s.Y, Z = s.Z,
            Height = s.Height, Diameter = s.Diameter, InnerDiameter = tube ? s.InnerDiameter : 40,
        };
        Assert.Equal(setup.Extent, s.Extent());
        StockSolid inner = CamJobStock.Check(job).Solid!;
        Assert.Equal(new Point3(inner.MinX - 30, inner.MinY - 20, inner.MinZ + 20), s.Extent()!.Value.Min);
    }

    [Fact]
    public void Pacote_ida_e_volta_com_fixacao_zero_e_ferramentas()
    {
        CamJob job = Load("fresa-zero-g55-canto");
        string program = CamJobBuilder.Build(job, null).Program;
        CamHandoff p = CamHandoff.Create(job, program, new MachineProfile { Name = "VF2 oficina" });
        string json = CamHandoffJson.ToJson(p);
        HandoffReadResult back = CamHandoffJson.Parse(json);
        Assert.Null(back.Warning);
        Assert.Equal(json, CamHandoffJson.ToJson(back.Package!));

        CamHandoff r = back.Package!;
        Assert.Equal((CamDialect.Fanuc, "fresa", "VF2 oficina", (int?)2024, (int?)55), (r.Controller, r.Machine, r.MachineProfile, r.ProgramNumber, r.WorkOffset));
        Assert.Equal(new HandoffZero(ZeroPoint.XMaxYMin, ZeroLevel.Top, false, CamJobZero.Summary(job)), r.Zero);
        Assert.Equal(new JobFixture("morsa", "Morsa 6 pol"), r.Fixture);
        Assert.Equal([1, 2], r.Tools.Select(t => t.Number));
        Assert.Equal(new HandoffTool(1, 10, "Fresa D10", 3000, 500, 200), r.Tools[0]);
        Assert.True(r.Matches(program));
    }

    [Fact]
    public void Material_nao_vai_e_dados_da_ferramenta_que_o_CAM_nao_tem_ficam_nulos()
    {
        CamJob job = Load("fresa-zero-centro-base");
        job.SetStock(null, Margins());
        string json = CamHandoffJson.ToJson(CamHandoff.Create(job, CamJobBuilder.Build(job, null).Program, null));
        Assert.DoesNotContain("material", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Aço", json, StringComparison.Ordinal);

        var tool = (JsonObject)JsonNode.Parse(json)!["ferramentas"]![1]!;
        foreach (string key in new[] { "tipo", "raioCanto", "anguloPonta", "comprimentoCorte", "suporte" })
        {
            Assert.True(tool.ContainsKey(key), key);
            Assert.Null(tool[key]);
        }

        HandoffTool read = CamHandoffJson.Parse(json).Package!.Tools[1];
        Assert.Equal((2, 6.0), (read.Number, read.Diameter));
        Assert.True(read is { Type: null, CornerRadius: null, TipAngle: null, CutLength: null, Holder: null });
    }

    [Fact]
    public void Campo_ausente_continua_nulo()
    {
        CamJob job = Load("fresa-zero-g55-canto");
        var root = (JsonObject)JsonNode.Parse(CamHandoffJson.ToJson(CamHandoff.Create(job, "G0 X0\n", null)))!;
        foreach (string key in new[] { "perfilMaquina", "programaNumero", "g5x", "zero", "fixacao", "bruto" })
        {
            root.Remove(key);
        }

        CamHandoff p = CamHandoffJson.Parse(root.ToJsonString()).Package!;
        Assert.True(p is { MachineProfile: null, ProgramNumber: null, WorkOffset: null, Zero: null, Fixture: null, Stock: null });
        Assert.Equal(2, p.Tools.Count);
    }

    [Fact]
    public void Versao_futura_e_pacote_estragado_sao_ignorados_com_aviso()
    {
        var root = (JsonObject)JsonNode.Parse(CamHandoffJson.ToJson(CamHandoff.Create(Load("fresa-sem-bruto"), "G0 X0\n", null)))!;
        root["versao"] = 2;
        Assert.Equal(new HandoffReadResult(null, CamHandoffJson.FutureWarning), CamHandoffJson.Parse(root.ToJsonString()));
        Assert.Equal(new HandoffReadResult(null, CamHandoffJson.InvalidWarning), CamHandoffJson.Parse("{ nada"));
        Assert.Equal(new HandoffReadResult(null, CamHandoffJson.InvalidWarning), CamHandoffJson.Parse("{\"formato\":\"casillas-job\",\"versao\":1}"));
        root["versao"] = 1;
        root["maquina"] = "plasma";
        Assert.Equal(CamHandoffJson.InvalidWarning, CamHandoffJson.Parse(root.ToJsonString()).Warning);
        Assert.Equal(new HandoffReadResult(null, null), CamHandoffJson.Parse(""));
    }

    [Fact]
    public void Bruto_STEP_leva_caminho_resumo_e_a_marca_nao_desenhado_sem_virar_bloco()
    {
        CamJob job = Load("fresa-zero-centro-base");
        var summary = new StepStockSummary(-1, -2, -22, 31, 27, 0, 18000);
        job.SetStock(null, new JobStockShape(StockShapeKind.Step) { StepPath = @"C:\pecas\bruto.step", StepSummary = summary });
        HandoffStock s = HandoffStock.InProgram(job)!;
        Assert.True(s is { Kind: HandoffStockKind.Step, NotDrawn: true, StepPath: @"C:\pecas\bruto.step" });
        Assert.Equal(summary, s.StepSummary);
        Assert.Null(s.Extent());

        string json = CamHandoffJson.ToJson(CamHandoff.Create(job, "G0 X0\n", null));
        var stock = (JsonObject)JsonNode.Parse(json)!["bruto"]!;
        Assert.Equal("step", (string?)stock["forma"]);
        Assert.True((bool)stock["naoDesenhado"]!);
        Assert.False(stock.ContainsKey("largura") || stock.ContainsKey("x") || stock.ContainsKey("diametro"));
        HandoffStock read = CamHandoffJson.Parse(json).Package!.Stock!;
        Assert.Equal((HandoffStockKind.Step, @"C:\pecas\bruto.step", summary), (read.Kind, read.StepPath, read.StepSummary));
    }

    [Fact]
    public void Hash_ignora_a_quebra_de_linha_e_denuncia_programa_editado_ou_outro()
    {
        CamJob job = Load("fresa-zero-centro-base");
        string program = CamJobBuilder.Build(job, null).Program.Replace("\r\n", "\n");
        CamHandoff p = CamHandoff.Create(job, program, null);
        Assert.Equal(CamHandoff.Hash(program), CamHandoff.Hash(program.Replace("\n", "\r\n")));
        Assert.Matches("^[0-9a-f]{64}$", p.ProgramHash);
        Assert.True(p.Matches(program.Replace("\n", "\r\n")));
        Assert.False(p.Matches(program.Replace("S3000", "S3100")));

        string other = CamJobBuilder.Build(Load("fresa-zero-g55-canto"), null).Program;
        Assert.False(p.Matches(other));
        Assert.Equal("PECA.nc.casillas-cam", CamHandoff.FileNameFor("PECA.nc"));
        Assert.NotEqual(CamHandoff.FileNameFor("PECA.nc"), CamHandoff.FileNameFor("PECA.mpf"));
    }

    [Fact]
    public void Montar_o_pacote_nao_muda_o_programa()
    {
        CamJob job = Load("fresa-zero-centro-base");
        string before = CamJobBuilder.Build(job, null).Program;
        job.SetStock(null, Margins());
        _ = CamHandoffJson.ToJson(CamHandoff.Create(job, before, null));
        Assert.Equal(before, CamJobBuilder.Build(job, null).Program);
    }

    [Theory]
    [InlineData(CamDialect.Fanuc, ZeroPoint.Center, ZeroLevel.Bottom)]
    [InlineData(CamDialect.Siemens, ZeroPoint.Center, ZeroLevel.Bottom)]
    [InlineData(CamDialect.Fanuc, ZeroPoint.XMaxYMin, ZeroLevel.Top)]
    [InlineData(CamDialect.Siemens, ZeroPoint.Drawing, ZeroLevel.Bottom)]
    public void Corte_do_programa_fica_dentro_do_bruto_do_pacote(CamDialect dialect, ZeroPoint point, ZeroLevel level)
    {
        CamJob job = Load("fresa-zero-centro-base");
        (job.Dialect, job.Zero) = (dialect, new JobZero(point, level));
        job.SetStock(null, Margins());
        JobBuild build = CamJobBuilder.Build(job, null);
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        Bounds3 stock = CamHandoff.Create(job, build.Program, null).Stock!.Extent()!.Value;

        // o simulador lê o programa como o Code lê: a caixa do corte (G1/G2/G3) precisa estar dentro do bruto
        var analysis = ProgramAnalyzer.Analyze(build.Program, dialect == CamDialect.Siemens ? Dialect.Siemens : Dialect.Fanuc);
        Bounds3 cut = analysis.ToolPath.CuttingBounds;
        Assert.False(cut.IsEmpty);
        Assert.InRange(cut.Min.X, stock.Min.X, stock.Max.X);
        Assert.InRange(cut.Max.X, stock.Min.X, stock.Max.X);
        Assert.InRange(cut.Min.Y, stock.Min.Y, stock.Max.Y);
        Assert.InRange(cut.Max.Y, stock.Min.Y, stock.Max.Y);
        // o fundo do corte (−6 do topo) fica dentro do bruto: acima do fundo e abaixo do topo (a descida em avanço começa acima)
        Assert.InRange(cut.Min.Z, stock.Min.Z, stock.Max.Z);
        Near(-6 - (level == ZeroLevel.Bottom ? -20 : 0), cut.Min.Z);
    }
}
