using CncStudio.Core.Cam;
using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>CAM-FURO-01: furos verticais da peça agrupados, e o que fica de fora com a razão.</summary>
public class HoleRecognizerTests
{
    private static Point3 P(double x, double y, double z) => new(x, y, z);

    private static void Quad(List<(Point3, Point3, Point3)> t, Point3 a, Point3 b, Point3 c, Point3 d)
    {
        t.Add((a, b, c));
        t.Add((a, c, d));
    }

    /// <summary>Parede de um furo vertical (normais para o eixo).</summary>
    private static void Wall(List<(Point3, Point3, Point3)> t, double cx, double cy, double r, double z0, double z1, int n = 48)
    {
        for (int i = 0; i < n; i++)
        {
            double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
            Point3 b0 = P(cx + (r * Math.Cos(a0)), cy + (r * Math.Sin(a0)), z0), b1 = P(cx + (r * Math.Cos(a1)), cy + (r * Math.Sin(a1)), z0);
            Quad(t, b0, P(b0.X, b0.Y, z1), P(b1.X, b1.Y, z1), b1);
        }
    }

    /// <summary>Fundo plano de furo cego (disco com normal para cima).</summary>
    private static void Floor(List<(Point3, Point3, Point3)> t, double cx, double cy, double r, double z, int n = 48)
    {
        for (int i = 0; i < n; i++)
        {
            double a0 = 2 * Math.PI * i / n, a1 = 2 * Math.PI * (i + 1) / n;
            t.Add((P(cx, cy, z), P(cx + (r * Math.Cos(a0)), cy + (r * Math.Sin(a0)), z), P(cx + (r * Math.Cos(a1)), cy + (r * Math.Sin(a1)), z)));
        }
    }

    /// <summary>Placa 100×100×20 (topo Z 20, fundo Z 0): só os planos grandes; os furos entram como paredes soltas.</summary>
    private static List<(Point3, Point3, Point3)> Plate()
    {
        var t = new List<(Point3, Point3, Point3)>();
        Quad(t, P(-50, -50, 0), P(-50, 50, 0), P(50, 50, 0), P(50, -50, 0));
        Quad(t, P(-50, -50, 20), P(50, -50, 20), P(50, 50, 20), P(-50, 50, 20));
        return t;
    }

    private static HoleReport Run(List<(Point3, Point3, Point3)> t)
    {
        var mesh = new TriangleMesh(t);
        return HoleRecognizer.Recognize(MeshFeatures.Analyze(mesh), mesh);
    }

    [Fact]
    public void Seis_furos_iguais_passantes_viram_um_grupo()
    {
        List<(Point3, Point3, Point3)> t = Plate();
        for (int i = 0; i < 6; i++)
        {
            double a = Math.PI / 3 * i;
            Wall(t, 30 * Math.Cos(a), 30 * Math.Sin(a), 4.5, 0, 20);
        }

        HoleReport r = Run(t);
        HoleGroup g = Assert.Single(r.Groups);
        Assert.Equal(6, g.Faces.Count);
        Assert.Equal(9, g.Diameter, 2);
        Assert.True(g.Through);
        Assert.Equal(20, g.Depth, 3);
        Assert.Empty(r.Rejected);
    }

    [Fact]
    public void Passante_e_cego_do_mesmo_diametro_sao_grupos_separados()
    {
        List<(Point3, Point3, Point3)> t = Plate();
        Wall(t, -20, 0, 4.5, 0, 20);
        Wall(t, 20, 0, 4.5, 8, 20);
        Floor(t, 20, 0, 4.5, 8);
        HoleReport r = Run(t);
        Assert.Equal(2, r.Groups.Count);
        Assert.Single(r.Groups, g => g.Through);
        HoleGroup blind = Assert.Single(r.Groups, g => !g.Through);
        Assert.Equal(12, blind.Depth, 3);
    }

    [Fact]
    public void Diametros_diferentes_ficam_do_maior_para_o_menor()
    {
        List<(Point3, Point3, Point3)> t = Plate();
        Wall(t, -20, 0, 3, 0, 20);
        Wall(t, 20, 0, 6, 0, 20);
        HoleReport r = Run(t);
        Assert.Equal([12.0, 6.0], r.Groups.Select(g => Math.Round(g.Diameter, 2)));
    }

    [Fact]
    public void Furo_com_degrau_fica_de_fora_com_a_razao()
    {
        List<(Point3, Point3, Point3)> t = Plate();
        Wall(t, 0, 0, 22, 10, 20); // rebaixo Ø44 em cima
        Wall(t, 0, 0, 12, 0, 10);  // furo Ø24 embaixo, na mesma linha
        HoleReport r = Run(t);
        Assert.Empty(r.Groups);
        Assert.Equal(2, r.Rejected.Count);
        Assert.All(r.Rejected, x => Assert.Contains("degrau", x.Reason));
    }

    [Fact]
    public void Furo_inclinado_fica_de_fora()
    {
        List<(Point3, Point3, Point3)> t = Plate();
        for (int i = 0; i < 48; i++)
        {
            double a0 = 2 * Math.PI * i / 48, a1 = 2 * Math.PI * (i + 1) / 48;
            // cilindro de raio 5 com eixo a 45° (direção (1,0,1)/√2); seção reta: u = Y, v = (1,0,-1)/√2
            Point3 Tilt(double a, double s) => P((5 * Math.Sin(a) / Math.Sqrt(2)) + (s / Math.Sqrt(2)), 5 * Math.Cos(a), (-5 * Math.Sin(a) / Math.Sqrt(2)) + (s / Math.Sqrt(2)) + 10);
            Quad(t, Tilt(a0, 0), Tilt(a1, 0), Tilt(a1, 15), Tilt(a0, 15));
        }

        HoleReport r = Run(t);
        Assert.Empty(r.Groups);
        HoleRejected x = Assert.Single(r.Rejected);
        Assert.Contains("inclinado", x.Reason);
    }

    [Fact]
    public void Grupo_com_furacao_no_trabalho_aparece_como_ja_tem_operacao()
    {
        List<(Point3, Point3, Point3)> t = Plate();
        Wall(t, -20, 0, 4.5, 0, 20);
        Wall(t, 20, 0, 4.5, 0, 20);
        var mesh = new TriangleMesh(t);
        MeshFeatures f = MeshFeatures.Analyze(mesh);
        HoleGroup g = Assert.Single(HoleRecognizer.Recognize(f, mesh).Groups);
        var job = new CamJob();
        Assert.False(HoleRecognizer.IsCovered(job, g));

        (IReadOnlyList<int> made, string? error) = CamJobFromGeometry.Add(job, JobOpKind.Drilling, f, g.Faces, [], 20);
        Assert.Null(error);
        Assert.Single(made);
        Assert.True(HoleRecognizer.IsCovered(job, g));
    }

    [Fact]
    public void Criar_por_grupo_nasce_sem_ferramenta_com_os_furos_do_grupo()
    {
        List<(Point3, Point3, Point3)> t = Plate();
        Wall(t, -20, 0, 4.5, 0, 20);
        Wall(t, 20, 0, 4.5, 0, 20);
        var mesh = new TriangleMesh(t);
        MeshFeatures f = MeshFeatures.Analyze(mesh);
        HoleGroup g = Assert.Single(HoleRecognizer.Recognize(f, mesh).Groups);
        var job = new CamJob();
        CamJobFromGeometry.Add(job, JobOpKind.Drilling, f, g.Faces, [], 20);
        CamOperationSpec op = Assert.Single(job.Operations);
        Assert.Equal(JobOpKind.Drilling, op.Kind);
        Assert.Equal(2, op.ContourIndices!.Count);
        Assert.Equal(2, CamOperations.HolesFromCircles(op.ContourIndices.Select(i => job.Contours[i])).Count);
        Assert.Contains(CamJobInfo.DiameterField, CamJobInfo.MissingFields(op));
        Assert.Contains(CamJobInfo.ToolNumberField, CamJobInfo.MissingFields(op));
    }

    [Fact]
    public void Furo_cego_com_fundo_conico_fica_de_fora()
    {
        List<(Point3, Point3, Point3)> t = Plate();
        Wall(t, 0, 0, 5, 8, 20);
        for (int i = 0; i < 48; i++)
        {
            double a0 = 2 * Math.PI * i / 48, a1 = 2 * Math.PI * (i + 1) / 48;
            t.Add((P(0, 0, 5), P(5 * Math.Cos(a0), 5 * Math.Sin(a0), 8), P(5 * Math.Cos(a1), 5 * Math.Sin(a1), 8))); // ponta da broca
        }

        HoleReport r = Run(t);
        Assert.Empty(r.Groups);
        Assert.Contains("cônico", Assert.Single(r.Rejected).Reason);
    }
}
