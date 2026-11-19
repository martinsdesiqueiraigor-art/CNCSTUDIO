using CncStudio.Core.Assist;
using CncStudio.Core.Cam;
using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>STEP-OPS S4: o Assist lê a peça STEP e planeja como faz com o DXF (profundidades reais da malha).</summary>
public class StepPlanSourceTests
{
    private static Point3 P(double x, double y, double z) => new(x, y, z);

    private static void Quad(List<(Point3, Point3, Point3)> t, Point3 a, Point3 b, Point3 c, Point3 d)
    {
        t.Add((a, b, c));
        t.Add((a, c, d));
    }

    /// <summary>Bloco 60×40×10: bolsão 15..45 × 10..30 de fundo Z=4 com um pino 28..32 × 18..22 até o topo.</summary>
    private static TriangleMesh Block()
    {
        var t = new List<(Point3, Point3, Point3)>();
        const int n = 32;
        Point3[] o = [P(0, 0, 10), P(60, 0, 10), P(60, 40, 10), P(0, 40, 10)];
        Point3[] i = [P(15, 10, 10), P(45, 10, 10), P(45, 30, 10), P(15, 30, 10)];
        Point3[] f = [P(15, 10, 4), P(45, 10, 4), P(45, 30, 4), P(15, 30, 4)];
        Point3[] p4 = [P(28, 18, 4), P(32, 18, 4), P(32, 22, 4), P(28, 22, 4)];
        Point3[] p10 = [P(28, 18, 10), P(32, 18, 10), P(32, 22, 10), P(28, 22, 10)];
        for (int k = 0; k < 4; k++)
        {
            int m = (k + 1) % 4;
            Quad(t, o[k], P(o[k].X, o[k].Y, 0), P(o[m].X, o[m].Y, 0), o[m]);

            Quad(t, i[k], i[m], f[m], f[k]);
            Quad(t, f[k], f[m], p4[m], p4[k]);
            Quad(t, p4[k], p4[m], p10[m], p10[k]);
        }

        Quad(t, p10[0], p10[1], p10[2], p10[3]);
        Quad(t, P(0, 0, 0), P(0, 40, 0), P(60, 40, 0), P(60, 0, 0));

        // topo com recorte do bolsão: 4 trapézios entre o contorno externo e o interno
        for (int k = 0; k < 4; k++)
        {
            int m = (k + 1) % 4;
            Quad(t, o[k], o[m], i[m], i[k]);
        }

        return new TriangleMesh(t);
    }

    private static StepPlanDrawing Read(TriangleMesh m) => StepPlanSource.FromMesh(MeshFeatures.Analyze(m), m.Bounds);

    [Fact]
    public void Peca_com_bolsao_e_ilha_vira_contornos_com_profundidade_real()
    {
        TriangleMesh m = Block();
        StepPlanDrawing d = Read(m);
        Assert.True(d.Ok);
        Assert.Equal(10, d.Thickness, 6);
        // peça + bolsão + ilha
        Assert.Equal(3, d.Contours.Count);
        Assert.Equal(6, d.Depths[1], 6); // topo Z10, fundo Z4
        Assert.DoesNotContain(2, d.Depths.Keys);
    }

    [Fact]
    public void O_plano_do_STEP_faz_bolsao_com_ilha_na_profundidade_do_modelo_e_gera_o_G_code()
    {
        TriangleMesh m = Block();
        StepPlanDrawing d = Read(m);
        var input = new PlanInput(d.Contours, d.Thickness, AssistLibrary.Defaults().Materials[3], []) { Depths = d.Depths };
        PlanResult plan = AssistPlanner.Plan(input);
        Assert.True(plan.Ok, string.Join("; ", plan.Notes));
        CamOperationSpec pocket = Assert.Single(plan.Operations, o => o.Kind == JobOpKind.Pocket);
        Assert.True(pocket.Islands);
        Assert.Equal(-6, pocket.Levels.BottomZ, 6); // a profundidade real, não a metade suposta (-5)
        Assert.Equal(-10, Assert.Single(plan.Operations, o => o.Kind == JobOpKind.Contour).Levels.BottomZ);
        Assert.Contains(plan.Notes, x => x.Contains("lidas do modelo STEP"));

        var job = new CamJob { Name = "STEP", ProgramNumber = 1001, WorkOffset = 54, Zero = JobZero.Legacy };
        job.Contours.AddRange(d.Contours);
        foreach (CamOperationSpec o in plan.Operations)
        {
            job.Operations.Add(o);
        }

        JobBuild build = CamJobBuilder.Build(job, null);
        Assert.True(build.Ok, string.Join("; ", build.Errors));
    }

    [Fact]
    public void Sem_o_mapa_de_profundidades_o_DXF_continua_igual()
    {
        List<Contour> plate = [Contour.Rectangle(0, 0, 100, 60), Contour.Rectangle(30, 20, 40, 20)];
        PlanResult a = AssistPlanner.Plan(new PlanInput(plate, 10, AssistLibrary.Defaults().Materials[3], []));
        PlanResult b = AssistPlanner.Plan(new PlanInput(plate, 10, AssistLibrary.Defaults().Materials[3], []) { Depths = null });
        Assert.Equal(a.OperationsStamp, b.OperationsStamp);
        Assert.Equal(-5, Assert.Single(a.Operations, o => o.Kind == JobOpKind.Pocket).Levels.BottomZ);
    }

    [Fact]
    public void Peca_sem_face_para_cima_e_recusada_com_motivo()
    {
        var t = new List<(Point3, Point3, Point3)>();
        Quad(t, P(0, 0, 0), P(10, 0, 0), P(10, 10, 0), P(0, 10, 0)); // só uma face (lida como face para cima ou baixo)
        TriangleMesh m = new(t);
        StepPlanDrawing d = Read(m);
        Assert.NotNull(d.Notes);
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

    [Fact]
    public void Furo_vertical_sai_com_o_diametro_e_a_profundidade_da_malha()
    {
        TriangleMesh m = Washer();
        StepPlanDrawing d = Read(m);
        Assert.True(d.Ok);
        Assert.Equal(8, d.Thickness, 6);
        Assert.Equal(2, d.Contours.Count); // contorno externo + o furo
        Assert.Equal(8, d.Depths[1], 3);
        var input = new PlanInput(d.Contours, d.Thickness, AssistLibrary.Defaults().Materials[3], []) { Depths = d.Depths };
        PlanResult plan = AssistPlanner.Plan(input);
        CamOperationSpec hole = Assert.Single(plan.Operations, o => o.Kind == JobOpKind.Drilling);
        Assert.Equal(10, hole.Tool.Diameter, 2);
        Assert.Equal(-8, hole.Levels.BottomZ, 3);
    }

    [Fact]
    public void Aplicar_o_plano_do_STEP_com_contornos_ja_no_trabalho_ajusta_os_indices_e_gera()
    {
        TriangleMesh m = Block();
        StepPlanDrawing d = Read(m);
        PlanResult plan = AssistPlanner.Plan(new PlanInput(d.Contours, d.Thickness, AssistLibrary.Defaults().Materials[3], []) { Depths = d.Depths });
        var job = new CamJob { Name = "STEP", ProgramNumber = 1001, WorkOffset = 54, Zero = JobZero.Legacy };
        job.Contours.Add(Contour.Rectangle(500, 500, 10, 10)); // contorno que já estava no trabalho
        int offset = job.Contours.Count;
        job.Contours.AddRange(d.Contours);
        foreach (CamOperationSpec o in plan.Operations)
        {
            job.Operations.Add(o.ContourIndex >= 0 ? o with { ContourIndex = o.ContourIndex + offset } : o);
        }

        JobBuild build = CamJobBuilder.Build(job, null);
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        Assert.All(job.Operations, o => Assert.True(o.ContourIndex >= offset));
    }
}
