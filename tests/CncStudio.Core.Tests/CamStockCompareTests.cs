using CncStudio.Core.Cam;
using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;
using CncStudio.Core.Simulation;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>CAM-STOCK-03a: peça × bruto na grade de alturas (gouge, excesso, dentro), só no Core e nunca gravado.</summary>
public class CamStockCompareTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private static Point3 P(double x, double y, double z) => new(x, y, z);

    /// <summary>Grade 40 × 40 de células de 1 mm, topo em 0 e fundo em −10.</summary>
    private static HeightMap Grid() => new(0, 0, 40, 40, 1, 0, -10);

    private static HeightMap CutTo(double z)
    {
        HeightMap map = Grid();
        map.CutDisc(20, 20, 60, z); // o disco cobre a grade inteira
        return map;
    }

    /// <summary>Topo plano da peça em z, de (x0, y0) a (x1, y1).</summary>
    private static List<(Point3, Point3, Point3)> Top(double x0, double y0, double x1, double y1, double z) =>
    [
        (P(x0, y0, z), P(x1, y0, z), P(x1, y1, z)),
        (P(x0, y0, z), P(x1, y1, z), P(x0, y1, z)),
    ];

    private static CamStockCompareResult Run(HeightMap map, TriangleMesh part) =>
        CamStockCompare.Compare(map, CamStockCompare.PartHeights(map, part));

    [Fact]
    public void Bruto_igual_a_peca_nao_da_gouge_nem_excesso()
    {
        CamStockCompareResult r = Run(CutTo(-4), new TriangleMesh(Top(0, 0, 40, 40, -4)));
        Assert.Equal(0, r.GougeVolume);
        Assert.Equal(0, r.ExcessVolume);
        Assert.Equal(100, r.WithinPercent, 6);
        Assert.Empty(r.Regions);
        Assert.All(r.Cells, c => Assert.Equal(CompareCell.Within, c));
    }

    [Fact]
    public void Bruto_maior_que_a_peca_da_so_excesso_com_o_volume_esperado()
    {
        CamStockCompareResult r = Run(Grid(), new TriangleMesh(Top(0, 0, 40, 40, -4)));
        Assert.False(r.HasGouge);
        Assert.Equal(40 * 40 * 4, r.ExcessVolume, 3);
        CompareRegion region = Assert.Single(r.Regions);
        Assert.Equal(CompareCell.Excess, region.Kind);
        Assert.Equal(1600, region.Cells);
        Assert.Equal(4, region.MaxDepth, 6);
    }

    [Fact]
    public void Corte_alem_da_peca_da_gouge_com_o_volume_esperado()
    {
        CamStockCompareResult r = Run(CutTo(-6), new TriangleMesh(Top(0, 0, 40, 40, -4)));
        Assert.False(r.HasExcess);
        Assert.Equal(40 * 40 * 2, r.GougeVolume, 3);
        CompareRegion region = Assert.Single(r.Regions);
        Assert.Equal(CompareCell.Gouge, region.Kind);
        Assert.Equal(2, region.MaxDepth, 6);
    }

    [Fact]
    public void Diferenca_menor_que_a_tolerancia_nao_acusa()
    {
        // célula de 1 mm = tolerância: 0,5 mm a mais ou a menos ainda é "dentro"
        CamStockCompareResult below = Run(CutTo(-4.5), new TriangleMesh(Top(0, 0, 40, 40, -4)));
        CamStockCompareResult above = Run(CutTo(-3.5), new TriangleMesh(Top(0, 0, 40, 40, -4)));
        Assert.False(below.HasGouge);
        Assert.False(above.HasExcess);
        Assert.Equal(1, below.Tolerance, 6);
    }

    [Fact]
    public void Tolerancia_informada_vale_no_lugar_da_celula()
    {
        HeightMap map = CutTo(-4.5);
        TriangleMesh part = new(Top(0, 0, 40, 40, -4));
        CamStockCompareResult tight = CamStockCompare.Compare(map, CamStockCompare.PartHeights(map, part), 0.1);
        Assert.True(tight.HasGouge);
        Assert.Equal(0.1, tight.Tolerance, 6);
    }

    [Fact]
    public void Furo_da_peca_com_material_no_bruto_vira_excesso()
    {
        // peça em −4 com um furo de 18 a 22 (sem triângulos ali); o bruto foi cortado a −4 inclusive no furo
        var t = new List<(Point3, Point3, Point3)>();
        t.AddRange(Top(0, 0, 40, 18, -4));
        t.AddRange(Top(0, 22, 40, 40, -4));
        t.AddRange(Top(0, 18, 18, 22, -4));
        t.AddRange(Top(22, 18, 40, 22, -4));
        CamStockCompareResult r = Run(CutTo(-4), new TriangleMesh(t));
        Assert.False(r.HasGouge);
        Assert.True(r.HasExcess);
        CompareRegion region = Assert.Single(r.Regions);
        Assert.Equal(CompareCell.Excess, region.Kind);
        // só entra o miolo do furo (a borda de uma célula é tolerância lateral): 2 × 2 células de 6 mm
        Assert.Equal(4, region.Cells);
        Assert.Equal(4 * 6, r.ExcessVolume, 3);
        Assert.InRange(region.MinX, 18, 22);
        Assert.InRange(region.MaxX, 18, 22);
    }

    [Fact]
    public void Fora_da_peca_e_do_material_nao_entra_na_conta()
    {
        // peça só em 10 a 20; bruto no fundo fora dela: as células vazias de verdade não contam nem como "dentro"
        HeightMap map = CutTo(-10);
        CamStockCompareResult r = Run(map, new TriangleMesh(Top(10, 10, 20, 20, -4)));
        Assert.Equal(10 * 10, r.CountedCells);
        Assert.True(r.HasGouge);
        // CAM-ACHADOS-01: a borda de uma célula ao lado de célula sem peça é tolerância lateral (como em toda parede): só o miolo 8 × 8
        Assert.Equal(8 * 8 * 6, r.GougeVolume, 3);
    }

    [Fact]
    public void Mesma_entrada_duas_vezes_da_o_mesmo_resultado()
    {
        HeightMap map = CutTo(-6);
        var part = new TriangleMesh(Top(0, 0, 40, 40, -4));
        CamStockCompareResult a = Run(map, part), b = Run(map, part);
        Assert.Equal(a.GougeVolume, b.GougeVolume);
        Assert.Equal(a.ExcessVolume, b.ExcessVolume);
        Assert.Equal(a.Cells, b.Cells);
        Assert.Equal(a.Regions, b.Regions);
    }

    [Fact]
    public void Alturas_de_outra_grade_dao_erro()
    {
        Assert.Throws<ArgumentException>(() => CamStockCompare.Compare(Grid(), new float[10]));
    }

    // ---- com o trabalho (CamStockTimeline) ----

    private static CamJob FlangeJob(double? bottomZ = null)
    {
        TriangleMesh mesh = LoopSideTests.Shift(LoopSideTests.Flange(), -15);
        var job = new CamJob { Name = "COMPARA", ProgramNumber = 1001, WorkOffset = 54, Zero = JobZero.Legacy };
        job.PartStep = CamJobPart.Import("C:/pecas/peca.step", mesh, [], false);
        job.SetStock(new JobStock(-5, -5, 70, 70, 15), null);
        MeshFeatures f = MeshFeatures.Analyze(mesh);
        MeshFace face = f.Faces.Where(x => x.Kind == FaceKind.Plane && x.Normal.Z > 0.99 && Math.Abs(x.Center.Z + 10) < 0.5).OrderByDescending(x => x.Area).First();
        (IReadOnlyList<int> made, string? error) = CamJobFromGeometry.Add(job, JobOpKind.Pocket, f, [face], [], 0);
        Assert.Null(error);
        Assert.Single(made);
        CamOperationSpec s = job.Operations[0];
        CamLevels levels = bottomZ is { } z ? s.Levels with { BottomZ = z } : s.Levels;
        job.Operations[0] = s with { Tool = new CamTool(1, 10, 3000, 400, 150, "T1"), Levels = levels, MissingFields = [], InvalidFields = [] };
        return job;
    }

    private static TriangleMesh FlangeMesh(CamJob job) => CamJobPart.Place(LoopSideTests.Shift(LoopSideTests.Flange(), -15), job.PartStep!.Turns, job.PartStep.TopAtZero);

    [Fact]
    public void Flange_bolsao_do_3d_nao_da_gouge_e_deixa_excesso_no_furo()
    {
        CamJob job = FlangeJob();
        CamStockTimeline t = CamStockTimeline.Compute(job, null);
        Assert.Null(t.Problem);
        CamStockCompare? c = CamStockCompare.Create(t, job, FlangeMesh(job), out string? problem);
        Assert.Null(problem);
        CamStockCompareResult r = c!.After(0)!;
        Assert.False(r.HasGouge);
        Assert.True(r.HasExcess);
        // o furo vazado (4 a 8, em x e y) ainda tem material
        Assert.Contains(r.Regions, g => g.Kind == CompareCell.Excess && g.MinX >= 3 && g.MaxX <= 9 && g.MinY >= 3 && g.MaxY <= 9);
        Assert.Equal(CamStockVerdict.Excess, Assert.Single(c.Summary()).Verdict);
    }

    [Fact]
    public void Flange_bolsao_fundo_demais_da_gouge_no_piso()
    {
        CamJob job = FlangeJob(bottomZ: -12);
        CamStockTimeline t = CamStockTimeline.Compute(job, null);
        Assert.Null(t.Problem);
        CamStockCompare c = CamStockCompare.Create(t, job, FlangeMesh(job), out string? problem)!;
        Assert.Null(problem);
        CamStockCompareResult r = c.After(0)!;
        Assert.True(r.HasGouge);
        CompareRegion g = r.Regions.First(x => x.Kind == CompareCell.Gouge);
        Assert.InRange(g.MaxDepth, 1.7, 2.3); // piso em −10, cortado a −12
        Assert.Equal(CamStockVerdict.Gouge, Assert.Single(c.Summary()).Verdict);
    }

    [Fact]
    public void Contorno_por_fora_que_entra_na_flange_de_100_da_gouge_na_borda()
    {
        // o defeito do arquivo de teste do BOLTED_FLANGE: contorno por fora até Z−16 que corta a borda da flange Ø100 (aqui, contorno Ø88;
        // CAM-ACHADOS-01: a simulação usa o Ø20 da operação, então a parede do corte fica em r = 44, uma célula a menos de tolerância)
        var job = new CamJob { Name = "FLANGE", ProgramNumber = 1001, WorkOffset = 54, Zero = JobZero.Legacy };
        job.SetStock(new JobStock(0, 0, 120, 120, 28), null);
        job.Contours.Add(CamJobPart.Circle(new Point2(60, 60), 44));
        CamOperationSpec s = CamJobInfo.NewOperation(JobOpKind.Contour, ContourSide.Outside, 0);
        job.Operations.Add(s with
        {
            Tool = new CamTool(1, 20, 3000, 600, 200, "T1"),
            Levels = new CamLevels(10, 2, 0, -16, 4),
            MissingFields = [],
            InvalidFields = [],
        });

        // peça: disco Ø100 com o topo em Z −10 (a flange)
        var tri = new List<(Point3, Point3, Point3)>();
        const int n = 96;
        for (int i = 0; i < n; i++)
        {
            double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
            tri.Add((P(60, 60, -10), P(60 + (50 * Math.Cos(a0)), 60 + (50 * Math.Sin(a0)), -10), P(60 + (50 * Math.Cos(a1)), 60 + (50 * Math.Sin(a1)), -10)));
        }

        var mesh = new TriangleMesh(tri);
        CamStockTimeline t = CamStockTimeline.Compute(job, null);
        Assert.Null(t.Problem);
        CamStockCompare c = CamStockCompare.Create(t, job, mesh, out string? problem)!;
        Assert.Null(problem);
        CamStockCompareResult r = c.After(0)!;

        Assert.True(r.HasGouge);
        // anel na borda da flange (raio até 50), 6 mm abaixo dela (o topo em −10, o corte em −16)
        CompareRegion g = r.Regions.First(x => x.Kind == CompareCell.Gouge);
        Assert.InRange(g.MaxDepth, 5.5, 6.5);
        Assert.InRange(g.MinX, 9.5, 12);
        Assert.InRange(g.MaxX, 108, 110.5);
        Assert.InRange(r.GougeVolume, 1000, Math.PI * ((50 * 50) - (43.5 * 43.5)) * 6);
        Assert.Equal(CamStockVerdict.Gouge, Assert.Single(c.Summary()).Verdict);
    }

    [Fact]
    public void Sem_peca_3d_ou_sem_bruto_nao_compara_e_diz_por_que()
    {
        CamJob job = FlangeJob();
        CamStockTimeline t = CamStockTimeline.Compute(job, null);
        Assert.Null(CamStockCompare.Create(t, job, null, out string? p1));
        Assert.Equal(CamStockCompare.NoPart, p1);

        var empty = new CamJob { Name = "VAZIO", ProgramNumber = 1001, WorkOffset = 54, Zero = JobZero.Legacy };
        CamStockTimeline none = CamStockTimeline.Compute(empty, null);
        Assert.Null(CamStockCompare.Create(none, empty, FlangeMesh(job), out string? p2));
        Assert.False(string.IsNullOrEmpty(p2));
    }

    [Theory]
    [InlineData("fresa-bloco-siemens")]
    [InlineData("fresa-fixacao")]
    public void Comparar_todas_as_operacoes_e_rapido(string name)
    {
        // medida (mais do que prova): peça de 5000 triângulos sobre o bruto de um trabalho de referência, resumo de todas as operações
        string dir = Path.Combine(AppContext.BaseDirectory, "trabalhos-referencia");
        JobLoadResult loaded = CamJobJson.FromJson(File.ReadAllText(Path.Combine(dir, name + ".casillas-job")).Replace("\r\n", "\n"));
        Assert.True(loaded.Ok, loaded.Error);
        CamJob job = loaded.Job!;
        CamStockTimeline t = CamStockTimeline.Compute(job, null);
        Assert.Null(t.Problem);
        HeightMap map = t.Map!;
        var tri = new List<(Point3, Point3, Point3)>();
        double w = map.Width / 70, h = map.Height / 70;
        for (int i = 0; i < 70; i++)
        {
            for (int j = 0; j < 36; j++)
            {
                double x0 = map.OriginX + (i * w), y0 = map.OriginY + (j * h * 2);
                tri.Add((P(x0, y0, -2), P(x0 + w, y0, -2), P(x0 + w, y0 + (h * 2), -2)));
                tri.Add((P(x0, y0, -2), P(x0 + w, y0 + (h * 2), -2), P(x0, y0 + (h * 2), -2)));
            }
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        CamStockCompare? c = CamStockCompare.Create(t, job, new TriangleMesh(tri), out string? problem);
        Assert.Null(problem);
        IReadOnlyList<CamStockCompareSummary> summary = c!.Summary();
        watch.Stop();
        output.WriteLine($"MEDIDO {name}: grade {map.Columns}x{map.Rows}, {t.States.Count} operações, {tri.Count} triângulos: {watch.ElapsedMilliseconds} ms");
        Assert.Equal(t.States.Count, summary.Count);
        Assert.InRange(watch.ElapsedMilliseconds, 0, 5000);
    }
}
