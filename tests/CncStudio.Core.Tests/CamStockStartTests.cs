using CncStudio.Core.Cam;
using CncStudio.Core.Dialects;
using CncStudio.Core.Parsing;
using CncStudio.Core.Simulation;
using Xunit.Abstractions;

namespace CncStudio.Core.Tests;

/// <summary>CAM-AR-01: começar no material (topo pelo bruto que sobrou das operações anteriores).</summary>
public class CamStockStartTests(ITestOutputHelper output)
{
    private static string Dir => Path.Combine(AppContext.BaseDirectory, "trabalhos-referencia");

    private static string Read(string file) => File.ReadAllText(Path.Combine(Dir, file)).Replace("\r\n", "\n");

    private static CamJob Load(string name)
    {
        JobLoadResult loaded = CamJobJson.FromJson(Read(name + ".casillas-job"));
        Assert.True(loaded.Ok, loaded.Error);
        return loaded.Job!;
    }

    private static ToolBank Flat() => new("fresa", "Fresa", ToolBankMachine.Mill)
    {
        Tools = [new BankTool(1) { Name = "Fresa D10", Diameter = 10, Type = BankToolType.EndMill }],
    };

    // bloco 70 × 50: faceamento de Z0 a Z−2 sobre o retângulo e bolsão no círculo Ø24 (dentro da área faceada) de Z0 a Z−8, passe 3
    private static CamJob FacingThenPocket(bool fromStock, double pocketBottom = -8)
    {
        CamJob job = Load("fresa-bloco-siemens");
        CamOperationSpec facing = job.Operations[0] with { Levels = new CamLevels(50, 2, 0, -2, 3) };
        CamOperationSpec pocket = job.Operations[2] with { Levels = new CamLevels(50, 2, 0, pocketBottom, 3), TopFromStock = fromStock };
        job.Operations.Clear();
        job.Operations.Add(facing);
        job.Operations.Add(pocket);
        return job;
    }

    private static IEnumerable<double> FlatCutLevels(CamOperation op)
    {
        for (int k = 1; k < op.Moves.Count; k++)
        {
            ToolMove a = op.Moves[k - 1], b = op.Moves[k];
            if (b.Kind != MoveKind.Rapid && Math.Abs(a.Z - b.Z) < 1e-9 && (Math.Abs(a.X - b.X) > 1e-9 || Math.Abs(a.Y - b.Y) > 1e-9 || b.IsArc))
            {
                yield return Math.Round(b.Z, 6);
            }
        }
    }

    [Fact]
    public void Ajuste_desce_topo_e_avanco_e_reconta_passes()
    {
        StockStart s = CamStockStart.Adjust(new CamLevels(50, 2, 0, -8, 3), -2, 1.34);
        Assert.True(s.Adjusted);
        Assert.Equal(-2, s.Levels.TopZ);
        Assert.Equal(0, s.Levels.ClearanceZ, 9);
        Assert.Equal(50, s.Levels.SafeZ);
        Assert.Equal(-8, s.Levels.BottomZ);
        Assert.Equal((3, 2), (s.PassesBefore, s.PassesAfter));

        // distância digitada menor que a folga: vale a folga
        StockStart small = CamStockStart.Adjust(new CamLevels(50, 0.5, 0, -8, 3), -2, 1.34);
        Assert.Equal(-0.66, small.Levels.ClearanceZ, 9);
    }

    [Fact]
    public void Ajuste_nunca_sobe_acima_do_digitado()
    {
        var levels = new CamLevels(50, 2, 0, -8, 3);
        StockStart above = CamStockStart.Adjust(levels, 5, 1.34);
        Assert.False(above.Adjusted);
        Assert.Null(above.Reason);
        Assert.Equal(levels, above.Levels);

        // material logo abaixo do topo: a altura de avanço não passa da digitada
        StockStart near = CamStockStart.Adjust(levels, -0.2, 1.34);
        Assert.True(near.Adjusted);
        Assert.True(near.Levels.ClearanceZ <= levels.ClearanceZ);
        Assert.Null(near.Levels.Validate());

        StockStart none = CamStockStart.Adjust(levels, -9, 1.34);
        Assert.False(none.Adjusted);
        Assert.Contains("sem material", none.Reason);
        Assert.Equal(levels, none.Levels);
    }

    [Fact]
    public void Bolsao_depois_do_faceamento_parte_no_material()
    {
        JobBuild build = CamJobBuilder.Build(FacingThenPocket(true), null, Flat());
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        CamOperation pocket = build.Operations[1];
        List<double> levels = [.. FlatCutLevels(pocket).Distinct().Order()];
        output.WriteLine(string.Join(" ", levels));
        Assert.Equal([-8.0, -5.0], levels);
        // nada cortado entre o topo digitado (Z0) e o material (Z−2): só a descida em avanço até ele
        Assert.DoesNotContain(FlatCutLevels(pocket), z => z > -2 + 1e-9);
        Assert.Contains(build.Warnings, w => w.Contains("Operação 2 parte em Z-2 pelo bruto da operação 1") && w.Contains("confira na simulação"));
        Assert.True(pocket.Moves.Where(m => m.Kind != MoveKind.Rapid).Max(m => m.Z) <= 0 + 1e-9);

        JobBuild typed = CamJobBuilder.Build(FacingThenPocket(false), null, Flat());
        // pelo topo digitado: 3 passes iguais de Z0 a Z−8
        Assert.Equal(3, FlatCutLevels(typed.Operations[1]).Distinct().Count());
        Assert.NotEqual(typed.Program, build.Program);
    }

    [Fact]
    public void Ferramenta_fora_do_banco_gera_pelo_digitado_e_avisa()
    {
        JobBuild typed = CamJobBuilder.Build(FacingThenPocket(false), null);
        JobBuild noBank = CamJobBuilder.Build(FacingThenPocket(true), null);
        Assert.True(noBank.Ok, string.Join("; ", noBank.Errors));
        Assert.Equal(typed.Program, noBank.Program);
        Assert.Contains(noBank.Warnings, w => w.Contains("forma da ferramenta T1 desconhecida") && w.Contains("topo digitado"));
    }

    [Fact]
    public void Sem_material_na_area_gera_pelo_digitado_e_avisa()
    {
        // bolsão de Z0 a Z−1, mas o faceamento já tirou até Z−2: nada a cortar ali
        JobBuild typed = CamJobBuilder.Build(FacingThenPocket(false, -1), null, Flat());
        JobBuild start = CamJobBuilder.Build(FacingThenPocket(true, -1), null, Flat());
        Assert.Equal(typed.Program, start.Program);
        Assert.Contains(start.Warnings, w => w.Contains("sem material na área"));
    }

    [Fact]
    public void Material_no_topo_digitado_nada_muda_e_nao_avisa()
    {
        CamJob on = FacingThenPocket(true), off = FacingThenPocket(false);
        on.Operations[0] = on.Operations[0] with { Enabled = false };
        off.Operations[0] = off.Operations[0] with { Enabled = false };
        JobBuild a = CamJobBuilder.Build(off, null, Flat()), b = CamJobBuilder.Build(on, null, Flat());
        Assert.Equal(a.Program, b.Program);
        Assert.DoesNotContain(b.Warnings, w => w.Contains("material"));
    }

    [Fact]
    public void Chanfro_e_furacao_nao_usam()
    {
        Assert.False(CamStockStart.Applies(JobOpKind.Chamfer));
        Assert.False(CamStockStart.Applies(JobOpKind.Drilling));
        Assert.False(CamStockStart.Applies(JobOpKind.WrapText));
        Assert.True(CamStockStart.Applies(JobOpKind.Facing) && CamStockStart.Applies(JobOpKind.Contour) && CamStockStart.Applies(JobOpKind.Pocket));
    }

    [Fact]
    public void Voxel_gera_pelo_digitado_e_avisa()
    {
        CamJob job = Load("fresa-3mais2");
        (MachineProfile? machine, string? error) = MachineProfileJson.Parse(Read("fresa-3mais2.maquina.json"));
        Assert.Null(error);
        int i = job.Operations.FindIndex(o => o.Enabled && CamStockStart.Applies(o.Kind));
        Assert.True(i >= 0);
        JobBuild typed = CamJobBuilder.Build(job, machine);
        job.Operations[i] = job.Operations[i] with { TopFromStock = true };
        JobBuild start = CamJobBuilder.Build(job, machine);
        Assert.Equal(typed.Program, start.Program);
        Assert.Contains(start.Warnings, w => w.Contains("voxel"));
    }

    [Fact]
    public void Programa_inteiro_simulado_igual_ao_bruto_em_sequencia()
    {
        // a operação seguinte usa o bruto da anterior já ajustada: o programa gerado, simulado de uma vez, dá o mesmo bruto
        CamJob job = FacingThenPocket(true);
        CamStockTimeline t = CamStockTimeline.Compute(job, null, Flat());
        Assert.NotNull(t.States[1].Start);
        Assert.True(t.States[1].Start!.Adjusted);
        Assert.Equal(0, t.States[1].Start!.FromOperation);
        Assert.True(t.ShowAfter(1));
        HeightMap chained = t.Map!;

        JobBuild build = CamJobBuilder.Build(job, null, Flat());
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(build.Program, job.Dialect == CamDialect.Siemens ? Dialect.Siemens : Dialect.Fanuc);
        var whole = new HeightMap(chained.OriginX, chained.OriginY, chained.Columns, chained.Rows, chained.CellSize, chained.TopZ, chained.BottomZ);
        new MaterialSimulator(whole, analysis.ToolPath.Moves, SimulationFactory.MillProfiles(null, analysis.ToolDiameterOf)).RunToEnd();
        int different = 0;
        for (int k = 0; k < whole.Heights.Length; k++)
        {
            different += Math.Abs(whole.Heights[k] - chained.Heights[k]) > 1e-4 ? 1 : 0;
        }

        Assert.Equal(0, different);
    }

    [Fact]
    public void Arquivo_versao_9_so_com_a_opcao()
    {
        CamJob off = Load("fresa-bloco-siemens");
        string text = CamJobJson.ToJson(off);
        Assert.Contains("\"version\": 1", text);
        Assert.DoesNotContain("topFromStock", text);

        off.Operations[2] = off.Operations[2] with { TopFromStock = true };
        string on = CamJobJson.ToJson(off);
        Assert.Contains("\"version\": 9", on);
        Assert.Contains("\"topFromStock\": true", on);
        JobLoadResult back = CamJobJson.FromJson(on);
        Assert.True(back.Ok, back.Error);
        Assert.True(back.Job!.Operations[2].TopFromStock);
        Assert.False(back.Job.Operations[0].TopFromStock);
        Assert.Equal(on, CamJobJson.ToJson(back.Job));
    }
}
