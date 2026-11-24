using CncStudio.Core.Cam;
using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>CAM-ACHADOS-01: achados do teste completo do BOLTED_FLANGE (gouge falso no furo, motivo do "começar no material").</summary>
public class CamAchados01Tests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static Point3 P(double x, double y, double z) => new(x, y, z);

    /// <summary>Disco Ø36 no centro de (20, 20), topo Z0 e fundo Z−10, com furo vazado de raio r facetado em n lados (como a malha do STEP).</summary>
    internal static TriangleMesh DiscWithHole(double r, int n)
    {
        var t = new List<(Point3, Point3, Point3)>();
        for (int i = 0; i < n; i++)
        {
            double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
            Point3 I(double a, double z) => P(20 + (r * Math.Cos(a)), 20 + (r * Math.Sin(a)), z);
            Point3 O(double a, double z) => P(20 + (18 * Math.Cos(a)), 20 + (18 * Math.Sin(a)), z);
            t.Add((I(a0, 0), O(a0, 0), O(a1, 0)));
            t.Add((I(a0, 0), O(a1, 0), I(a1, 0)));
            t.Add((I(a0, -10), O(a1, -10), O(a0, -10)));
            t.Add((I(a0, -10), I(a1, -10), O(a1, -10)));
            t.Add((I(a0, 0), I(a1, 0), I(a1, -10)));
            t.Add((I(a0, 0), I(a1, -10), I(a0, -10)));
        }

        return new TriangleMesh(t);
    }

    private static CamJob DrillJob(double toolD)
    {
        var job = new CamJob { Name = "FURO", ProgramNumber = 1001, WorkOffset = 54, Zero = JobZero.Legacy };
        job.SetStock(new JobStock(0, 0, 40, 40, 10), null);
        job.Contours.Add(CamJobPart.Circle(new Point2(20, 20), 4.5));
        CamOperationSpec s = CamJobInfo.NewOperation(JobOpKind.Drilling, ContourSide.Outside, 0);
        job.Operations.Add(s with
        {
            Tool = new CamTool(4, toolD, 1000, 100, 80, "Broca"),
            Levels = new CamLevels(10, 2, 0, -13, 13),
            Drill = new DrillSpec(DrillCycleKind.Drill, 0, 0),
            MissingFields = [],
            InvalidFields = [],
        });
        return job;
    }

    [Theory]
    [InlineData(16)]
    [InlineData(48)]
    public void Furo_feito_com_a_broca_do_mesmo_diametro_nao_da_gouge(int sides)
    {
        CamJob job = DrillJob(9);
        CamStockTimeline t = CamStockTimeline.Compute(job, null);
        Assert.Null(t.Problem);
        CamStockCompare c = CamStockCompare.Create(t, job, DiscWithHole(4.5, sides), out string? problem)!;
        Assert.Null(problem);
        CamStockCompareResult r = c.After(0)!;
        output.WriteLine($"célula {r.CellSize}; gouge {r.GougeVolume:0.###} mm³; excesso {r.ExcessVolume:0.###}");
        Assert.False(r.HasGouge);
    }

    [Fact]
    public void Broca_maior_que_o_furo_ainda_da_gouge()
    {
        // a tolerância lateral não esconde o gouge de verdade: broca Ø10 no furo Ø9 da peça (anel de 0,5 mm)
        CamJob job = DrillJob(10);
        CamStockTimeline t = CamStockTimeline.Compute(job, null);
        CamStockCompareResult r = CamStockCompare.Create(t, job, DiscWithHole(4.5, 48), out _)!.After(0)!;
        output.WriteLine($"gouge {r.GougeVolume:0.###} mm³");
        Assert.True(r.HasGouge);
        Assert.InRange(r.GougeVolume, 50, Math.PI * ((5 * 5) - (4.5 * 4.5)) * 10);
    }

    private static CamJob Load(string name)
    {
        string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "trabalhos-referencia", name + ".casillas-job")).Replace("\r\n", "\n");
        JobLoadResult loaded = CamJobJson.FromJson(text);
        Assert.True(loaded.Ok, loaded.Error);
        return loaded.Job!;
    }

    private static ToolBank Flat() => new("fresa", "Fresa", ToolBankMachine.Mill)
    {
        Tools = [new BankTool(1) { Name = "Fresa D10", Diameter = 10, Type = BankToolType.EndMill }],
    };

    [Fact]
    public void Material_acima_do_topo_digitado_diz_o_motivo()
    {
        // bolsão digitado de Z−2 a Z−8 num bloco com topo em Z0: o material (Z0) está acima do topo digitado
        CamJob on = Load("fresa-bloco-siemens"), off = Load("fresa-bloco-siemens");
        CamOperationSpec pocket = on.Operations[2] with { Levels = new CamLevels(50, 2, -2, -8, 3) };
        foreach (CamJob j in new[] { on, off })
        {
            j.Operations.Clear();
        }

        on.Operations.Add(pocket with { TopFromStock = true });
        off.Operations.Add(pocket);
        JobBuild typed = CamJobBuilder.Build(off, null, Flat()), start = CamJobBuilder.Build(on, null, Flat());
        Assert.True(start.Ok, string.Join("; ", start.Errors));
        Assert.Equal(typed.Program, start.Program);
        string note = Assert.Single(start.Warnings, w => w.Contains("começar no material não aplicado"));
        output.WriteLine(note);
        Assert.Contains("acima do topo digitado Z-2", note);
        Assert.Contains("material em Z0", note);

        // a tela usa o mesmo texto (selo e aba Alturas)
        CamStockTimeline t = CamStockTimeline.Compute(on, null, Flat());
        Assert.Equal(note, t.States[0].StartNote);
    }
}
