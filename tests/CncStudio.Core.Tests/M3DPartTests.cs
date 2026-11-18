using CncStudio.Core.Cam;
using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>M3D: peça STEP no Trabalho (M3D-1) e face do 3D virando operação na árvore (M3D-2).</summary>
public class M3DPartTests
{
    private static Point3 P(double x, double y, double z) => new(x, y, z);

    private static void Quad(List<(Point3, Point3, Point3)> t, Point3 a, Point3 b, Point3 c, Point3 d)
    {
        t.Add((a, b, c));
        t.Add((a, c, d));
    }

    /// <summary>Bloco 40×40×10 com bolsão quadrado 10..30, fundo em Z=5 (o mesmo dos testes do Modelo 3D).</summary>
    private static TriangleMesh PocketBlock()
    {
        var t = new List<(Point3, Point3, Point3)>();
        Point3[] o = [P(0, 0, 10), P(40, 0, 10), P(40, 40, 10), P(0, 40, 10)];
        Point3[] i = [P(10, 10, 10), P(30, 10, 10), P(30, 30, 10), P(10, 30, 10)];
        Point3[] f = [P(10, 10, 5), P(30, 10, 5), P(30, 30, 5), P(10, 30, 5)];
        for (int k = 0; k < 4; k++)
        {
            int n = (k + 1) % 4;
            Quad(t, o[k], o[n], i[n], i[k]);
            Quad(t, i[k], i[n], f[n], f[k]);
            Quad(t, o[k], P(o[k].X, o[k].Y, 0), P(o[n].X, o[n].Y, 0), o[n]);
        }

        Quad(t, f[0], f[1], f[2], f[3]);
        Quad(t, P(0, 0, 0), P(0, 40, 0), P(40, 40, 0), P(40, 0, 0));
        return new TriangleMesh(t);
    }

    /// <summary>Arruela: raio externo 20, furo de raio 5, altura 8, centrada em (0,0).</summary>
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

    /// <summary>Preenche o que nasce vazio (T, Ø, rotação, avanços e o Z do fundo), como o usuário faria.</summary>
    private static void Fill(CamJob job, int index, double bottom, double diameter)
    {
        CamOperationSpec s = job.Operations[index];
        s = s with { Tool = new CamTool(1, diameter, 3000, 400, 150, "T1"), Levels = s.Levels with { BottomZ = bottom }, MissingFields = [], InvalidFields = [] };
        job.Operations[index] = s;
    }

    [Fact]
    public void Peca_girada_guarda_o_resumo_da_posicao_e_os_giros()
    {
        TriangleMesh raw = PocketBlock();
        JobPartStep p = CamJobPart.Import("C:/p.step", raw, [ModelTurn.Flip], true);
        Assert.Equal([ModelTurn.Flip], p.Turns);
        Assert.Equal(0, p.Summary.MaxZ, 6);
        Assert.Equal(-10, p.Summary.MinZ, 6);
        Assert.Equal(StepStockSummary.From(CamJobPart.Place(raw, [ModelTurn.Flip], true)), p.Summary);
    }

    [Fact]
    public void Arquivo_com_peca_step_sai_na_versao_4_e_volta_igual()
    {
        CamJob job = Job(PocketBlock());
        job.PartStep = job.PartStep! with { Turns = [ModelTurn.XPlus90, ModelTurn.Flip], TopAtZero = true };
        string json = CamJobJson.ToJson(job);
        Assert.Contains("\"version\": 4", json);
        Assert.Contains("\"partStep\"", json);
        Assert.DoesNotContain("triangles", json);
        JobLoadResult back = CamJobJson.FromJson(json);
        Assert.True(back.Ok, back.Error);
        Assert.True(job.PartStep.SameAs(back.Job!.PartStep));
        Assert.Equal(json, CamJobJson.ToJson(back.Job));
    }

    [Fact]
    public void Trabalho_sem_peca_step_nao_ganha_chave_nem_versao_4()
    {
        var job = new CamJob { ProgramNumber = 1, WorkOffset = 54, Zero = JobZero.Legacy };
        string json = CamJobJson.ToJson(job);
        Assert.DoesNotContain("partStep", json);
        Assert.Contains("\"version\": 1", json);
    }

    [Fact]
    public void Peca_step_estragada_abre_sem_ela_com_aviso()
    {
        CamJob job = Job(PocketBlock());
        string json = CamJobJson.ToJson(job).Replace("\"volume\"", "\"vol\"");
        JobLoadResult back = CamJobJson.FromJson(json);
        Assert.True(back.Ok, back.Error);
        Assert.Null(back.Job!.PartStep);
        Assert.Contains(back.Warnings, w => w.Contains("Peça STEP"));
        Assert.Null(CamJobJson.FromJson(CamJobJson.ToJson(job).Replace("\"turns\": []", "\"turns\": [\"Giro\"]")).Job!.PartStep);
    }

    [Fact]
    public void Arquivo_sumido_marca_o_problema_sem_ler_o_step()
    {
        CamJob job = Job(PocketBlock());
        CamJobPart.VerifyFile(job, _ => false);
        Assert.Contains("não está mais", job.PartStep!.FileProblem);
        Assert.DoesNotContain("FileProblem", CamJobJson.ToJson(job));
        CamJobPart.VerifyFile(job, _ => true);
        Assert.Null(job.PartStep!.FileProblem);
        Assert.Equal("peca.step", CamJobPart.FileName(job));
    }

    [Fact]
    public void Uma_fonte_so_dxf_ou_step()
    {
        var dxf = new CamJob();
        dxf.Contours.Add(CamJobPart.Circle(new Point2(0, 0), 5));
        dxf.Operations.Add(CamJobInfo.NewOperation(JobOpKind.Drilling, ContourSide.Outside, -1));
        Assert.NotNull(CamJobPart.CannotSwitch(dxf, toStep: true));
        Assert.Null(CamJobPart.CannotSwitch(new CamJob(), toStep: true));

        CamJob step = Job(PocketBlock());
        step.Contours.Add(CamJobPart.Circle(new Point2(0, 0), 5));
        step.Operations.Add(CamJobInfo.NewOperation(JobOpKind.Drilling, ContourSide.Outside, -1));
        Assert.NotNull(CamJobPart.CannotSwitch(step, toStep: false));
        Assert.Null(CamJobPart.CannotSwitch(step, toStep: true));
    }

    [Fact]
    public void Fundo_do_rebaixo_vira_bolsao_na_arvore_vazio_ate_digitar()
    {
        TriangleMesh mesh = PocketBlock();
        CamJob job = Job(mesh);
        MeshFeatures f = MeshFeatures.Analyze(mesh);
        MeshFace floor = Assert.Single(f.Faces, x => x.Kind == FaceKind.Plane && x.Normal.Z > 0.99 && Math.Abs(x.Center.Z - 5) < 1e-6);
        (int index, string? error) = CamJobPart.AddPocket(job, f, floor, mesh.Bounds.Max.Z);
        Assert.Null(error);
        CamOperationSpec op = job.Operations[index];
        Assert.Equal(JobOpKind.Pocket, op.Kind);
        Assert.Equal(CamJobPart.PocketLabel, op.Label);
        Assert.Equal(10, op.Levels.TopZ, 6);
        Assert.Contains(CamJobPart.BottomField, op.MissingFields);
        Assert.Contains(CamJobInfo.ToolNumberField, op.MissingFields);
        Assert.Single(job.Contours);
        Assert.Equal(20, job.Contours[0].BoundsXY().MaxX - job.Contours[0].BoundsXY().MinX, 6);

        // vazio: não gera
        Assert.False(CamJobBuilder.Build(job, null).Ok);
        Fill(job, index, 5, 6);
        JobBuild build = CamJobBuilder.Build(job, null);
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        Assert.Contains("BOLSAO 3D", build.Program);
        Assert.Contains(build.Operations[0].Moves, m => Math.Abs(m.Z - 5) < 1e-6);
        Assert.DoesNotContain(build.Operations[0].Moves, m => m.Z < 5 - 1e-6);
    }

    [Fact]
    public void Topo_e_moldura_com_ilha_nao_viram_bolsao()
    {
        TriangleMesh mesh = PocketBlock();
        CamJob job = Job(mesh);
        MeshFeatures f = MeshFeatures.Analyze(mesh);
        MeshFace top = Assert.Single(f.Faces, x => x.Kind == FaceKind.Plane && x.Normal.Z > 0.99 && Math.Abs(x.Center.Z - 10) < 1e-6);
        Assert.NotNull(CamJobPart.AddPocket(job, f, top, 10).Error);
        Assert.Empty(job.Operations);
        Assert.Empty(job.Contours);
    }

    [Fact]
    public void Furos_escolhidos_viram_uma_furacao_com_lista_de_contornos()
    {
        TriangleMesh mesh = Washer();
        CamJob job = Job(mesh);
        MeshFeatures f = MeshFeatures.Analyze(mesh);
        MeshFace hole = Assert.Single(f.Faces, x => x.Kind == FaceKind.Hole);
        (int index, string? error) = CamJobPart.AddDrilling(job, [hole]);
        Assert.Null(error);
        CamOperationSpec op = job.Operations[index];
        Assert.Equal(JobOpKind.Drilling, op.Kind);
        Assert.Equal([0], op.ContourIndices);
        Assert.Equal(8, op.Levels.TopZ, 6);
        Assert.Contains(CamJobPart.BottomField, op.MissingFields);
        Assert.Single(CamOperations.HolesFromCircles(job.Contours));

        // um círculo do DXF a mais não entra: só os furos escolhidos
        job.Contours.Add(CamJobPart.Circle(new Point2(100, 100), 3));
        Fill(job, index, -2, 6);
        JobBuild build = CamJobBuilder.Build(job, null);
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        Assert.Contains("FUROS 3D", build.Program);
        Assert.DoesNotContain("X100.", build.Program);

        // lista de contornos vai e volta do arquivo (versão 4)
        string json = CamJobJson.ToJson(job);
        Assert.Contains("\"contours\": [", json);
        JobLoadResult back = CamJobJson.FromJson(json);
        Assert.True(back.Ok, back.Error);
        Assert.Equal([0], back.Job!.Operations[0].ContourIndices);
        Assert.Equal(build.Program, CamJobBuilder.Build(back.Job, null).Program);
    }

    [Fact]
    public void Furo_que_sumiu_do_desenho_trava_com_motivo()
    {
        CamJob job = Job(Washer());
        job.Contours.Add(CamJobPart.Circle(new Point2(0, 0), 5));
        job.Operations.Add(CamJobInfo.NewOperation(JobOpKind.Drilling, ContourSide.Outside, -1) with { ContourIndices = [0, 3] });
        Assert.Contains(job.Validate(), e => e.Contains("não existe mais"));
    }

    [Fact]
    public void Furacao_do_3d_recusa_lista_vazia_e_furo_inclinado()
    {
        CamJob job = Job(Washer());
        Assert.NotNull(CamJobPart.AddDrilling(job, []).Error);
        var slanted = new MeshFace(1, FaceKind.Hole, [0], 10, new Point3(1, 0, 0), new Point3(0, 0, 4), new Bounds3(new Point3(-1, -1, 0), new Point3(1, 1, 8)), 6, new Point3(1, 0, 0));
        Assert.NotNull(CamJobPart.AddDrilling(job, [slanted]).Error);
        Assert.Empty(job.Contours);
    }
}
