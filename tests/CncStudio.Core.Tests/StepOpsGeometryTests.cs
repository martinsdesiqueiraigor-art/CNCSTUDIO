using CncStudio.Core.Cam;
using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>STEP-OPS S0: a operação do 3D guarda a geometria, acha de novo no STEP e se edita (trocar, filtro de laços).</summary>
public class StepOpsGeometryTests
{
    private static Point3 P(double x, double y, double z) => new(x, y, z);

    private static void Quad(List<(Point3, Point3, Point3)> t, Point3 a, Point3 b, Point3 c, Point3 d)
    {
        t.Add((a, b, c));
        t.Add((a, c, d));
    }

    /// <summary>Bloco 40×40×10 com bolsão 10..30 de fundo Z=5 e um pino 18..22 no meio do fundo, até Z=10 (a ilha).</summary>
    private static TriangleMesh PocketWithIsland()
    {
        var t = new List<(Point3, Point3, Point3)>();
        Point3[] o = [P(0, 0, 10), P(40, 0, 10), P(40, 40, 10), P(0, 40, 10)];
        Point3[] i = [P(10, 10, 10), P(30, 10, 10), P(30, 30, 10), P(10, 30, 10)];
        Point3[] f = [P(10, 10, 5), P(30, 10, 5), P(30, 30, 5), P(10, 30, 5)];
        Point3[] p5 = [P(18, 18, 5), P(22, 18, 5), P(22, 22, 5), P(18, 22, 5)];
        Point3[] p10 = [P(18, 18, 10), P(22, 18, 10), P(22, 22, 10), P(18, 22, 10)];
        for (int k = 0; k < 4; k++)
        {
            int n = (k + 1) % 4;
            Quad(t, o[k], o[n], i[n], i[k]);
            Quad(t, i[k], i[n], f[n], f[k]);
            Quad(t, o[k], P(o[k].X, o[k].Y, 0), P(o[n].X, o[n].Y, 0), o[n]);
            Quad(t, f[k], f[n], p5[n], p5[k]); // fundo em volta do pino
            Quad(t, p5[k], p5[n], p10[n], p10[k]); // parede do pino
        }

        Quad(t, p10[0], p10[1], p10[2], p10[3]);
        Quad(t, P(0, 0, 0), P(0, 40, 0), P(40, 40, 0), P(40, 0, 0));
        return new TriangleMesh(t);
    }

    /// <summary>Arruela raio 20, furo raio 5, altura 8.</summary>
    private static TriangleMesh Washer(int n = 48)
    {
        var t = new List<(Point3, Point3, Point3)>();
        for (int i = 0; i < n; i++)
        {
            double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
            Point3 o0b = P(20 * Math.Cos(a0), 20 * Math.Sin(a0), 0), o1b = P(20 * Math.Cos(a1), 20 * Math.Sin(a1), 0);
            Point3 o0t = P(o0b.X, o0b.Y, 8), o1t = P(o1b.X, o1b.Y, 8);
            Point3 i0b = P(5 * Math.Cos(a0), 5 * Math.Sin(a0), 0), i1b = P(5 * Math.Cos(a1), 5 * Math.Sin(a1), 0);
            Point3 i0t = P(i0b.X, i0b.Y, 8), i1t = P(i1b.X, i1b.Y, 8);
            Quad(t, o0b, o1b, o1t, o0t);
            Quad(t, i0b, i0t, i1t, i1b);
            Quad(t, o0t, o1t, i1t, i0t);
            Quad(t, o0b, i0b, i1b, o1b);
        }

        return new TriangleMesh(t);
    }

    private static CamJob Job(TriangleMesh mesh)
    {
        var job = new CamJob { Name = "PECA3D", ProgramNumber = 1001, WorkOffset = 54, Zero = JobZero.Legacy };
        job.PartStep = CamJobPart.Import("C:/pecas/peca.step", mesh, [], false);
        return job;
    }

    private static void Fill(CamJob job, int index, double bottom, double diameter)
    {
        CamOperationSpec s = job.Operations[index];
        job.Operations[index] = s with { Tool = new CamTool(1, diameter, 3000, 400, 150, "T1"), Levels = s.Levels with { BottomZ = bottom }, MissingFields = [], InvalidFields = [] };
    }

    private static MeshFace Floor(MeshFeatures f) =>
        Assert.Single(f.Faces, x => x.Kind == FaceKind.Plane && x.Normal.Z > 0.99 && Math.Abs(x.Center.Z - 5) < 0.5 && x.Area > 100);

    [Fact]
    public void Bolsao_da_selecao_guarda_a_geometria_e_a_ilha_entra_sozinha()
    {
        TriangleMesh mesh = PocketWithIsland();
        CamJob job = Job(mesh);
        MeshFeatures f = MeshFeatures.Analyze(mesh);
        MeshFace floor = Floor(f);
        (IReadOnlyList<int> made, string? error) = CamJobFromGeometry.Add(job, JobOpKind.Pocket, f, [floor], [], mesh.Bounds.Max.Z);
        Assert.Null(error);
        CamOperationSpec op = job.Operations[Assert.Single(made)];
        Assert.True(op.Islands);
        Assert.Equal(2, job.Contours.Count); // contorno de fora + a ilha
        Assert.Equal(2, op.Geometry!.Owned.Count);
        Assert.Equal(LoopFilter.All, op.Geometry.Loops);
        Assert.Equal(5, op.Levels.BottomZ, 6);

        Fill(job, 0, 5, 4);
        JobBuild build = CamJobBuilder.Build(job, null);
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        // a fresa não entra no pino (18..22) na altura do fundo
        Assert.DoesNotContain(build.Operations[0].Moves, m => m.Z < 9 && m.X > 18.5 && m.X < 21.5 && m.Y > 18.5 && m.Y < 21.5);
    }

    [Fact]
    public void Filtro_de_lacos_externo_tira_a_ilha_e_mantem_o_que_foi_digitado()
    {
        TriangleMesh mesh = PocketWithIsland();
        CamJob job = Job(mesh);
        MeshFeatures f = MeshFeatures.Analyze(mesh);
        MeshFace floor = Floor(f);
        CamJobFromGeometry.Add(job, JobOpKind.Pocket, f, [floor], [], 10);
        Fill(job, 0, 4, 6);

        Assert.Null(CamJobFromGeometry.Update(job, 0, f, [floor], [], LoopFilter.Outer, 10));
        CamOperationSpec op = job.Operations[0];
        Assert.False(op.Islands);
        Assert.Equal(LoopFilter.Outer, op.Geometry!.Loops);
        Assert.Single(op.Geometry.Owned);
        Assert.Equal(6, op.Tool.Diameter, 6); // T, Ø e avanços digitados ficam
        Assert.Equal(4, op.Levels.BottomZ, 6); // o Z do fundo digitado não é trocado
        Assert.Single(job.Contours); // a ilha saiu da lista (era o último contorno)

        // volta para Todos: a ilha entra de novo
        Assert.Null(CamJobFromGeometry.Update(job, 0, f, [floor], [], LoopFilter.All, 10));
        Assert.True(job.Operations[0].Islands);
        Assert.Equal(2, job.Contours.Count);
    }

    [Fact]
    public void Geometria_vai_para_a_versao_6_e_volta_igual()
    {
        TriangleMesh mesh = PocketWithIsland();
        CamJob job = Job(mesh);
        MeshFeatures f = MeshFeatures.Analyze(mesh);
        CamJobFromGeometry.Add(job, JobOpKind.Pocket, f, [Floor(f)], [], 10);
        Fill(job, 0, 5, 4);
        string json = CamJobJson.ToJson(job);
        Assert.Contains("\"version\": 6", json);
        JobLoadResult back = CamJobJson.FromJson(json);
        Assert.NotNull(back.Job);
        GeoSource geo = back.Job!.Operations[0].Geometry!;
        Assert.Equal(job.Operations[0].Geometry!.Owned, geo.Owned);
        Assert.Equal(job.Operations[0].Geometry!.Refs[0].Shape, geo.Refs[0].Shape);
        Assert.Equal(CamJobJson.ToJson(job), CamJobJson.ToJson(back.Job));
    }

    [Fact]
    public void Operacao_sem_geometria_continua_na_versao_de_antes()
    {
        TriangleMesh mesh = Washer();
        CamJob job = Job(mesh);
        MeshFeatures f = MeshFeatures.Analyze(mesh);
        MeshFace hole = Assert.Single(f.Faces, x => x.Kind == FaceKind.Hole);
        (int index, string? error) = CamJobPart.AddDrilling(job, [hole]);
        Assert.Null(error);
        Assert.Null(job.Operations[index].Geometry);
        Fill(job, index, 0, 4);
        Assert.Contains("\"version\": 4", CamJobJson.ToJson(job));
    }

    [Fact]
    public void Face_guardada_e_achada_de_novo_e_peca_trocada_nao_acha()
    {
        TriangleMesh mesh = PocketWithIsland();
        MeshFeatures f = MeshFeatures.Analyze(mesh);
        GeoRef r = JobGeometry.Of(Floor(f));
        // leitura nova da mesma peça: ids podem mudar, a impressão acha a mesma face
        MeshFace again = Assert.IsType<MeshFace>(JobGeometry.Find(MeshFeatures.Analyze(mesh), r));
        Assert.Equal(5, again.Center.Z, 0);

        // peça trocada (arruela): a face do fundo não existe
        Assert.Null(JobGeometry.Find(MeshFeatures.Analyze(Washer()), r));
        var source = new GeoSource([r], LoopFilter.All, [0]);
        (IReadOnlyList<MeshFace> faces, _, IReadOnlyList<GeoRef> lost) = JobGeometry.Resolve(MeshFeatures.Analyze(Washer()), source);
        Assert.Empty(faces);
        Assert.Single(lost);
    }

    [Fact]
    public void Furacao_da_selecao_troca_os_furos_e_so_aceita_furo_vertical()
    {
        TriangleMesh mesh = Washer();
        CamJob job = Job(mesh);
        MeshFeatures f = MeshFeatures.Analyze(mesh);
        MeshFace hole = Assert.Single(f.Faces, x => x.Kind == FaceKind.Hole);
        (IReadOnlyList<int> made, string? error) = CamJobFromGeometry.Add(job, JobOpKind.Drilling, f, [hole], [], 8);
        Assert.Null(error);
        CamOperationSpec op = job.Operations[Assert.Single(made)];
        Assert.Equal(op.Geometry!.Owned, op.ContourIndices);
        Assert.StartsWith("Furo", JobGeometry.Describe(op.Geometry.Refs[0]));

        Assert.NotNull(CamJobFromGeometry.Update(job, 0, f, [], [], LoopFilter.All, 8));
        Assert.Single(job.Contours);

        // sem seleção válida nada muda
        CamJobFromGeometry.Add(job, JobOpKind.Pocket, f, [], [], 8);
        Assert.Single(job.Operations);
    }

    [Fact]
    public void Faceamento_e_contorno_ainda_nao_saem_ate_as_proximas_fatias()
    {
        TriangleMesh mesh = Washer();
        CamJob job = Job(mesh);
        MeshFeatures f = MeshFeatures.Analyze(mesh);
        (IReadOnlyList<int> made, string? error) = CamJobFromGeometry.Add(job, JobOpKind.Contour, f, [], [], 8);
        Assert.Empty(made);
        Assert.NotNull(error);
        Assert.Empty(job.Operations);
    }
}
