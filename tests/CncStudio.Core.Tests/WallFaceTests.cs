using CncStudio.Core.Cam;
using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>CAM-FACE-02: parede clicada no 3D vira contorno (pino, furo, parede reta) ou chanfro/contorno pela base (inclinada).</summary>
public class WallFaceTests
{
    private static MeshFace Wall(MeshFeatures f, Func<MeshFace, bool> where) =>
        f.Faces.Where(x => !(x.Kind == FaceKind.Plane && Math.Abs(x.Normal.Z) > 0.99)).Where(where).OrderByDescending(x => x.Area).First();

    /// <summary>Cilindro Ø44 de Z0 a Z−16 com tampa em cima (o pino do flange).</summary>
    private static TriangleMesh Pin(int n = 96)
    {
        var t = new List<(Point3, Point3, Point3)>();
        var c = new Point3(50, 50, 0);
        for (int k = 0; k < n; k++)
        {
            double a0 = 2 * Math.PI * k / n, a1 = 2 * Math.PI * (k + 1) / n;
            var p0 = new Point3(50 + (22 * Math.Cos(a0)), 50 + (22 * Math.Sin(a0)), 0);
            var p1 = new Point3(50 + (22 * Math.Cos(a1)), 50 + (22 * Math.Sin(a1)), 0);
            var q0 = p0 with { Z = -16 };
            var q1 = p1 with { Z = -16 };
            t.Add((q0, q1, p1));
            t.Add((q0, p1, p0));
            t.Add((c, p0, p1));
        }

        return new TriangleMesh(t);
    }

    [Fact]
    public void Parede_do_pino_vira_contorno_por_fora_em_circulo_com_as_alturas()
    {
        MeshFeatures f = MeshFeatures.Analyze(Pin());
        MeshFace wall = Wall(f, x => x.Kind == FaceKind.Boss);
        (WallContour? w, string? why) = WallSection.Of(f, wall);
        Assert.Null(why);
        Assert.Equal(ContourSide.Outside, w!.Side);
        Assert.False(w.Inclined);
        Assert.Equal(0, w.TopZ, 3);
        Assert.Equal(-16, w.BottomZ, 3);
        (double x0, double y0, double x1, double y1) = w.Contour.BoundsXY();
        Assert.Equal(44, x1 - x0, 0);
        Assert.Equal(50, (x0 + x1) / 2, 1);
        Assert.Equal(50, (y0 + y1) / 2, 1);

        (string header, IReadOnlyList<JobFaceItem> items) = FaceMenuRules.ForJob(f, wall, 0);
        Assert.StartsWith("Parede de pino", header);
        Assert.Null(CamJobFromGeometry.Unavailable(JobOpKind.Contour, f, [wall], [], 0, ContourSide.Outside));
        Assert.Contains("por fora", CamJobFromGeometry.Unavailable(JobOpKind.Contour, f, [wall], [], 0, ContourSide.Inside));
        Assert.Contains(items, i => i.Id == "contorno-fora");
    }

    [Fact]
    public void Parede_reta_do_cubo_e_por_fora_e_a_do_furo_por_dentro()
    {
        MeshFeatures f = MeshFeatures.Analyze(LoopSideTests.Flange());
        MeshFace hub = Wall(f, x => Math.Abs(x.Center.X - 20) < 0.01 && x.Center.Z > 5);
        (WallContour? h, _) = WallSection.Of(f, hub);
        Assert.Equal(ContourSide.Outside, h!.Side);
        Assert.Equal(15, h.TopZ, 3);
        Assert.Equal(5, h.BottomZ, 3);
        (double x0, double y0, double x1, double y1) = h.Contour.BoundsXY();
        Assert.Equal((20, 20, 40, 40), (Math.Round(x0, 3), Math.Round(y0, 3), Math.Round(x1, 3), Math.Round(y1, 3)));
        Assert.StartsWith("Parede reta", FaceMenuRules.ForJob(f, hub, 15).Header);

        MeshFace hole = Wall(f, x => Math.Abs(x.Center.X - 4) < 0.01 && x.Center.Z < 5);
        (WallContour? o, _) = WallSection.Of(f, hole);
        Assert.Equal(ContourSide.Inside, o!.Side);
        Assert.Equal(5, o.TopZ, 3);
        Assert.Equal(0, o.BottomZ, 3);
    }

    [Fact]
    public void Contorno_criado_da_parede_leva_topo_e_fundo_e_gera_por_fora_do_cubo()
    {
        MeshFeatures f = MeshFeatures.Analyze(LoopSideTests.Flange());
        MeshFace hub = Wall(f, x => Math.Abs(x.Center.X - 20) < 0.01 && x.Center.Z > 5);
        var job = new CamJob();
        (IReadOnlyList<int> made, string? error) = CamJobFromGeometry.Add(job, JobOpKind.Contour, f, [hub], [], 15, ContourSide.Outside);
        Assert.Null(error);
        CamOperationSpec s = job.Operations[made[0]];
        Assert.Equal(15, s.Levels.TopZ, 3);
        Assert.Equal(5, s.Levels.BottomZ, 3);
        Assert.Equal("Contorno da parede", s.Label);
        Assert.NotNull(s.Geometry);

        // com ferramenta, o contorno anda por fora do cubo (eixo a pelo menos o raio da parede)
        Contour part = job.Contours[s.ContourIndex];
        OperationResult r = CamOperations.Contour(part, new CamTool(1, 6, 3000, 500, 200), s.Levels with { StepDown = 5 }, s.ContourParams!);
        Assert.True(r.Ok, r.Error);
        Assert.All(r.Operation!.Moves.Where(m => m.Kind == MoveKind.Feed && m.Z < 15), m =>
            Assert.True(m.X <= 20 - 3 + 1e-6 || m.X >= 40 + 3 - 1e-6 || m.Y <= 20 - 3 + 1e-6 || m.Y >= 40 + 3 - 1e-6, $"({m.X:0.###},{m.Y:0.###}) dentro do cubo"));
    }

    [Fact]
    public void Furo_escareado_da_chanfro_com_largura_e_angulo_e_contorno_pela_base()
    {
        MeshFeatures f = MeshFeatures.Analyze(LoopSideTests.Flange(chamferHole: true));
        MeshFace cham = Wall(f, x => x.Center.Z > 4 && x.Center.Z < 5 && x.Center.X < 10 && x.Center.Y < 10);
        (WallContour? w, string? why) = WallSection.Of(f, cham);
        Assert.Null(why);
        Assert.True(w!.Inclined);
        Assert.Equal(ContourSide.Inside, w.Side);
        Assert.Equal(1, w.ChamferWidth, 2);
        Assert.Equal(90, w.ChamferTipAngle, 0);
        Assert.Equal(5, w.TopZ, 3);
        Assert.Equal(4, w.BottomZ, 3);
        (double x0, _, double x1, _) = w.Contour.BoundsXY();
        Assert.Equal(4, x0, 2); // pé da inclinação = o furo (aresta do chanfro)
        Assert.Equal(8, x1, 2);

        (string header, IReadOnlyList<JobFaceItem> items) = FaceMenuRules.ForJob(f, cham, 5);
        Assert.StartsWith("Parede inclinada", header);
        Assert.Contains(items, i => i.Kind == JobOpKind.Chamfer);
        Assert.Contains(items, i => i.Label.StartsWith("Contorno pela base", StringComparison.Ordinal));

        var job = new CamJob();
        (IReadOnlyList<int> made, string? error) = CamJobFromGeometry.Add(job, JobOpKind.Chamfer, f, [cham], [], 5);
        Assert.Null(error);
        CamOperationSpec s = job.Operations[made[0]];
        Assert.Equal(1, s.Chamfer!.Width, 2);
        Assert.Equal(90, s.Chamfer.TipAngle, 0);
        Assert.Equal(ContourSide.Inside, s.Chamfer.Side);
        Assert.DoesNotContain(CamJobInfo.ChamferWidthField, s.MissingFields);
        Assert.Contains(CamJobInfo.ChamferDepthField, s.MissingFields);
        Assert.Equal(5, s.Levels.TopZ, 3);

        (IReadOnlyList<int> base1, string? baseErr) = CamJobFromGeometry.Add(job, JobOpKind.Contour, f, [cham], [], 5, ContourSide.Inside);
        Assert.Null(baseErr);
        Assert.Equal("Contorno pela base", job.Operations[base1[0]].Label);
    }

    [Fact]
    public void Parede_volta_a_ser_achada_ao_reabrir()
    {
        MeshFeatures f = MeshFeatures.Analyze(Pin());
        MeshFace wall = Wall(f, x => x.Kind == FaceKind.Boss);
        var job = new CamJob();
        CamJobFromGeometry.Add(job, JobOpKind.Contour, f, [wall], [], 0, ContourSide.Outside);
        CamJob again = CamJobJson.FromJson(CamJobJson.ToJson(job)).Job!;
        Assert.Equal(0, CamJobFromGeometry.Reconcile(again, MeshFeatures.Analyze(Pin())));
    }
}
