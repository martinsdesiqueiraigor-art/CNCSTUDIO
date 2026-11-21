using CncStudio.Core.Cam;
using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>CAM-ILHAS-01 F2: bolsão do 3D no filtro automático (ilha só onde sobe, furo ignorado, borda aberta pelo bruto).</summary>
public class CamIslandsAutoTests
{
    private static Point3 P(double x, double y, double z) => new(x, y, z);

    private static void Quad(List<(Point3, Point3, Point3)> t, Point3 a, Point3 b, Point3 c, Point3 d)
    {
        t.Add((a, b, c));
        t.Add((a, c, d));
    }

    private static Point3[] Sq(double x0, double y0, double x1, double y1, double z) => [P(x0, y0, z), P(x1, y0, z), P(x1, y1, z), P(x0, y1, z)];

    private static void Wall(List<(Point3, Point3, Point3)> t, Point3[] a, Point3[] b)
    {
        for (int k = 0; k < 4; k++)
        {
            int n = (k + 1) % 4;
            Quad(t, a[k], a[n], b[n], b[k]);
        }
    }

    private static void Ring(List<(Point3, Point3, Point3)> t, Point3[] o, Point3[] i)
    {
        for (int k = 0; k < 4; k++)
        {
            int n = (k + 1) % 4;
            Quad(t, o[k], o[n], i[n], i[k]);
        }
    }

    /// <summary>Flange com topo do cubo em Z0: placa em Z−10, fundo Z−15.</summary>
    private static TriangleMesh Flange() => LoopSideTests.Shift(LoopSideTests.Flange(), -15);

    /// <summary>Bloco 40×40 de topo Z0, bolsão fechado 10..30 de fundo Z−5 com pino 18..22 (até Z0) e furo vazado 12..14 no fundo.</summary>
    private static TriangleMesh ClosedWithPinAndHole()
    {
        var t = new List<(Point3, Point3, Point3)>();
        Point3[] o = Sq(0, 0, 40, 40, 0), i = Sq(10, 10, 30, 30, 0);
        Ring(t, o, i);
        Wall(t, Sq(0, 0, 40, 40, -10), o);
        // fundo em grade conforme: x/y em 10,12,14,18,22,30; células do pino e do furo ficam de fora
        double[] g = [10, 12, 14, 18, 22, 30];
        var edge = new List<(double X, double Y)>();
        foreach (double x in g[..^1]) edge.Add((x, 10));
        foreach (double y in g[..^1]) edge.Add((30, y));
        foreach (double x in g.Reverse().ToArray()[..^1]) edge.Add((x, 30));
        foreach (double y in g.Reverse().ToArray()[..^1]) edge.Add((10, y));
        for (int k = 0; k < edge.Count; k++)
        {
            // parede do bolsão, um trecho por vão da grade do fundo (aresta comum com o fundo)
            (double x0, double y0) = edge[k];
            (double x1, double y1) = edge[(k + 1) % edge.Count];
            Quad(t, P(x0, y0, -5), P(x1, y1, -5), P(x1, y1, 0), P(x0, y0, 0));
        }

        for (int a = 0; a < 5; a++)
        {
            for (int b = 0; b < 5; b++)
            {
                if ((a == 1 && b == 1) || (a == 3 && b == 3))
                {
                    continue;
                }

                Point3[] c = Sq(g[a], g[b], g[a + 1], g[b + 1], -5);
                Quad(t, c[0], c[1], c[2], c[3]);
            }
        }

        Point3[] pin5 = Sq(18, 18, 22, 22, -5), pin0 = Sq(18, 18, 22, 22, 0);
        Wall(t, pin5, pin0);
        Quad(t, pin0[0], pin0[1], pin0[2], pin0[3]);
        Wall(t, Sq(12, 12, 14, 14, -5), Sq(12, 12, 14, 14, -10));
        return new TriangleMesh(t);
    }

    private static CamJob Job(TriangleMesh mesh, bool stock = true)
    {
        var job = new CamJob { Name = "ILHAS", ProgramNumber = 1001, WorkOffset = 54, Zero = JobZero.Legacy };
        job.PartStep = CamJobPart.Import("C:/pecas/peca.step", mesh, [], false);
        if (stock)
        {
            job.SetStock(new JobStock(-5, -5, 70, 70, 15), null);
        }

        return job;
    }

    private static void Fill(CamJob job, double diameter)
    {
        CamOperationSpec s = job.Operations[0];
        job.Operations[0] = s with { Tool = new CamTool(1, diameter, 3000, 400, 150, "T1"), MissingFields = [], InvalidFields = [] };
    }

    private static MeshFace FaceAt(MeshFeatures f, double z) =>
        f.Faces.Where(x => x.Kind == FaceKind.Plane && x.Normal.Z > 0.99 && Math.Abs(x.Center.Z - z) < 0.5).OrderByDescending(x => x.Area).First();

    private static CamJob FlangePocket(bool stock = true)
    {
        TriangleMesh mesh = Flange();
        CamJob job = Job(mesh, stock);
        MeshFeatures f = MeshFeatures.Analyze(mesh);
        (IReadOnlyList<int> made, string? error) = CamJobFromGeometry.Add(job, JobOpKind.Pocket, f, [FaceAt(f, -10)], [], 0);
        Assert.Null(error);
        Assert.Single(made);
        Fill(job, 10);
        return job;
    }

    [Fact]
    public void Flange_cubo_e_ilha_furo_fica_de_fora_e_borda_e_aberta()
    {
        CamJob job = FlangePocket();
        CamOperationSpec op = job.Operations[0];
        Assert.Equal(LoopFilter.Auto, op.Geometry!.Loops);
        Assert.Equal(PocketBorder.Open, op.Geometry.Border);
        Assert.Equal(2, op.Geometry.Owned.Count); // borda + cubo; o furo não entra
        Assert.True(op.Islands);
        Assert.Equal(-10, op.Levels.BottomZ, 6);

        JobBuild build = CamJobBuilder.Build(job, null);
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        IReadOnlyList<ToolMove> moves = build.Operations[0].Moves;
        // não entra no cubo (20..40) abaixo do topo dele
        Assert.DoesNotContain(moves, m => m.Z < -0.5 && m.X > 20.5 && m.X < 39.5 && m.Y > 20.5 && m.Y < 39.5);
        // passa para fora do contorno da peça (borda aberta vai até o bruto)
        Assert.Contains(moves, m => Math.Abs(m.Z + 10) < 0.01 && (m.X > 60 || m.X < 0));
        // o centro da fresa não passa do bruto + raio + folga
        Assert.All(moves.Where(m => m.Z < -0.5), m => Assert.InRange(m.X, -5 - 6 - 1e-6, 65 + 6 + 1e-6));
    }

    [Fact]
    public void Borda_aberta_sem_bruto_explica()
    {
        CamJob job = FlangePocket(stock: false);
        JobBuild build = CamJobBuilder.Build(job, null);
        Assert.Contains(build.Errors, e => e.Contains(CamJobBuilder.OpenBorderStockError, StringComparison.Ordinal));
    }

    [Fact]
    public void Limite_misto_explica()
    {
        // fundo Z−10 de 0..40; parede sobe só em Y=40; borda Y=0 desce
        var t = new List<(Point3, Point3, Point3)>();
        Quad(t, P(0, 0, -10), P(40, 0, -10), P(40, 40, -10), P(0, 40, -10));
        Quad(t, P(0, 40, -10), P(40, 40, -10), P(40, 40, 0), P(0, 40, 0));
        Quad(t, P(0, 40, 0), P(40, 40, 0), P(40, 60, 0), P(0, 60, 0));
        Quad(t, P(0, 0, -15), P(40, 0, -15), P(40, 0, -10), P(0, 0, -10));
        TriangleMesh mesh = new(t);
        CamJob job = Job(mesh);
        MeshFeatures f = MeshFeatures.Analyze(mesh);
        CamJobFromGeometry.Add(job, JobOpKind.Pocket, f, [FaceAt(f, -10)], [], 0);
        Fill(job, 10);
        Assert.Equal(PocketBorder.Mixed, job.Operations[0].Geometry!.Border);
        JobBuild build = CamJobBuilder.Build(job, null);
        Assert.Contains(build.Errors, e => e.Contains(CamJobBuilder.MixedBorderError, StringComparison.Ordinal));
    }

    [Fact]
    public void Bolsao_fechado_pino_e_ilha_e_furo_do_fundo_nao()
    {
        TriangleMesh mesh = ClosedWithPinAndHole();
        CamJob job = Job(mesh);
        MeshFeatures f = MeshFeatures.Analyze(mesh);
        CamJobFromGeometry.Add(job, JobOpKind.Pocket, f, [FaceAt(f, -5)], [], 0);
        Fill(job, 3);
        GeoSource geo = job.Operations[0].Geometry!;
        Assert.Equal(PocketBorder.Closed, geo.Border);
        Assert.Equal(2, geo.Owned.Count); // parede + pino
        JobBuild build = CamJobBuilder.Build(job, null);
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        IReadOnlyList<ToolMove> moves = build.Operations[0].Moves;
        Assert.DoesNotContain(moves, m => m.Z < -0.5 && m.X > 18.5 && m.X < 21.5 && m.Y > 18.5 && m.Y < 21.5);
        Assert.All(moves.Where(m => m.Z < -0.5), m => Assert.InRange(m.X, 10 + 1.5 - 1e-6, 30 - 1.5 + 1e-6));
    }

    [Fact]
    public void Automatico_vai_para_a_versao_8_e_volta_igual()
    {
        CamJob job = FlangePocket();
        string json = CamJobJson.ToJson(job);
        Assert.Contains("\"version\": 8", json);
        Assert.Contains("\"border\": \"Open\"", json);
        JobLoadResult back = CamJobJson.FromJson(json);
        Assert.NotNull(back.Job);
        GeoSource geo = back.Job!.Operations[0].Geometry!;
        Assert.Equal(LoopFilter.Auto, geo.Loops);
        Assert.Equal(PocketBorder.Open, geo.Border);
        Assert.Equal(json, CamJobJson.ToJson(back.Job));
        Assert.Equal(CamJobBuilder.Build(job, null).Program, CamJobBuilder.Build(back.Job, null).Program);
    }

    [Fact]
    public void Filtro_antigo_nao_ganha_borda_nem_versao_8()
    {
        CamJob job = FlangePocket();
        CamOperationSpec s = job.Operations[0];
        job.Operations[0] = s with { Geometry = s.Geometry! with { Loops = LoopFilter.All, Border = PocketBorder.Closed } };
        string json = CamJobJson.ToJson(job);
        Assert.DoesNotContain("\"border\"", json);
        Assert.DoesNotContain("\"version\": 8", json);
    }

    [Fact]
    public void Corte_do_bruto_bloco_e_cilindro()
    {
        var job = new CamJob { Name = "B" };
        job.SetStock(new JobStock(0, 0, 50, 30, 20), null);
        Contour box = Assert.IsType<Contour>(CamJobBuilder.StockSection(job, -5, 6));
        (double x0, double y0, double x1, double y1) = box.BoundsXY();
        Assert.Equal((-6, -6, 56, 36), (x0, y0, x1, y1));
        Assert.Null(CamJobBuilder.StockSection(job, -25, 6)); // abaixo do bruto
        Assert.Null(CamJobBuilder.StockSection(new CamJob { Name = "S" }, -5, 6)); // sem bruto
    }
}
