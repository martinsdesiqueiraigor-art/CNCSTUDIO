using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;

namespace CncStudio.Core.Tests;

/// <summary>UX-03b: menu da face no Modelo 3D mostra só o que serve, e "Ver todas" diz o porquê de cada item apagado.</summary>
public class FaceMenuTests
{
    private const double Top = 30;

    private static MeshFace Face(FaceKind kind, Point3 normal, double z, double diameter = 0, Point3? axis = null) =>
        new(1, kind, [0], 100, normal, new Point3(5, 5, z), new Bounds3(new Point3(0, 0, z - 10), new Point3(10, 10, z)), diameter, axis);

    private static readonly Point3 Up = new(0, 0, 1);

    private static string[] Fits(FaceMenu m) => [.. m.Fits.Select(i => i.Item.Id)];

    private static string? Why(FaceMenu m, string id) => m.All.Single(i => i.Item.Id == id).Unavailable;

    [Fact]
    public void Fundo_de_bolsao_com_um_contorno_oferece_so_bolsao()
    {
        FaceMenu m = FaceMenuRules.For(Face(FaceKind.Plane, Up, 12), 1, Top);
        Assert.Equal("Face plana · Z = 12,0 mm", m.Header);
        Assert.Equal(["bolsao"], Fits(m));
        Assert.Contains("Faceamento a partir do 3D ainda não existe", Why(m, "faceamento"));
    }

    [Fact]
    public void Bolsao_com_ilhas_fica_apagado_com_motivo()
    {
        FaceMenu m = FaceMenuRules.For(Face(FaceKind.Plane, Up, 12), 2, Top);
        Assert.Empty(m.Fits);
        Assert.Equal("Bolsão com ilhas ainda não sai do 3D.", Why(m, "bolsao"));
    }

    [Fact]
    public void Topo_plano_nao_oferece_nada_e_faceamento_diz_que_nao_existe()
    {
        FaceMenu m = FaceMenuRules.For(Face(FaceKind.Plane, Up, Top), 1, Top);
        Assert.Equal("Face plana de topo · Z = 30,0 mm", m.Header);
        Assert.Empty(m.Fits);
        Assert.Contains("topo do modelo", Why(m, "bolsao"));
        Assert.StartsWith("Faceamento a partir do 3D ainda não existe", Why(m, "faceamento"));
    }

    [Fact]
    public void Furo_vertical_oferece_so_furacao()
    {
        FaceMenu m = FaceMenuRules.For(Face(FaceKind.Hole, Up, 30, 8, new Point3(0, 0, -1)), 0, Top);
        Assert.StartsWith("Furo Ø 8,0 mm", m.Header);
        Assert.Equal(["furacao"], Fits(m));
        Assert.Equal("Isto é um furo: use Furação.", Why(m, "bolsao"));
    }

    [Theory]
    [InlineData(FaceKind.Hole)]
    [InlineData(FaceKind.Boss)]
    [InlineData(FaceKind.ConcaveCurve)]
    [InlineData(FaceKind.ConvexCurve)]
    [InlineData(FaceKind.Free)]
    public void Furo_inclinado_pino_curva_e_livre_nao_servem_mas_ver_todas_traz_tudo_com_motivo(FaceKind kind)
    {
        MeshFace f = Face(kind, new Point3(1, 0, 0), 15, 6, kind == FaceKind.Hole ? new Point3(1, 0, 0) : null);
        FaceMenu m = FaceMenuRules.For(f, 0, Top);
        Assert.Empty(m.Fits);
        Assert.False(string.IsNullOrWhiteSpace(m.Header));
        Assert.NotEmpty(m.All);
        Assert.All(m.All, i => Assert.False(string.IsNullOrWhiteSpace(i.Unavailable)));
    }

    [Fact]
    public void Furo_inclinado_diz_por_que_nao_fura()
    {
        FaceMenu m = FaceMenuRules.For(Face(FaceKind.Hole, Up, 15, 6, new Point3(1, 0, 0)), 0, Top);
        Assert.StartsWith("Furo inclinado", m.Header);
        Assert.Equal("Furo inclinado: sem furação automática (3 eixos).", Why(m, "furacao"));
    }

    [Theory]
    [InlineData(EdgeKind.Line, "Aresta reta")]
    [InlineData(EdgeKind.Circle, "Aresta circular")]
    [InlineData(EdgeKind.Arc, "Arco")]
    [InlineData(EdgeKind.Curve, "Aresta curva")]
    public void Aresta_mostra_so_cabecalho_e_tudo_apagado(EdgeKind kind, string header)
    {
        var e = new MeshEdge(1, kind, [new Point3(0, 0, 0), new Point3(10, 0, 0)], kind == EdgeKind.Circle, 10, new Point3(5, 0, 0), 4);
        FaceMenu m = FaceMenuRules.For(e);
        Assert.StartsWith(header, m.Header);
        Assert.Empty(m.Fits);
        Assert.All(m.All, i => Assert.False(string.IsNullOrWhiteSpace(i.Unavailable)));
    }

    [Fact]
    public void Catalogo_inteiro_entra_em_ver_todas_inclusive_o_grupo_3d()
    {
        FaceMenu m = FaceMenuRules.For(Face(FaceKind.Plane, Up, 12), 1, Top);
        string[] ids = [.. m.All.Select(i => i.Item.Id)];
        Assert.Contains("varredura", ids);
        Assert.Contains("desbaste-niveis", ids);
        Assert.Contains("texto-cilindro", ids);
    }

    [Fact]
    public void Caixa_real_topo_e_lateral_pela_malha()
    {
        Point3 p000 = new(0, 0, 0), p100 = new(10, 0, 0), p110 = new(10, 10, 0), p010 = new(0, 10, 0);
        Point3 p001 = new(0, 0, 10), p101 = new(10, 0, 10), p111 = new(10, 10, 10), p011 = new(0, 10, 10);
        var mesh = new TriangleMesh(
        [
            (p000, p010, p110), (p000, p110, p100), (p001, p101, p111), (p001, p111, p011),
            (p000, p100, p101), (p000, p101, p001), (p010, p011, p111), (p010, p111, p110),
            (p000, p001, p011), (p000, p011, p010), (p100, p110, p111), (p100, p111, p101),
        ]);
        MeshFeatures features = MeshFeatures.Analyze(mesh);
        MeshFace top = features.Faces.Single(f => f.Kind == FaceKind.Plane && f.Normal.Z > 0.99);
        MeshFace side = features.Faces.First(f => f.Kind == FaceKind.Plane && Math.Abs(f.Normal.Z) < 0.01);

        FaceMenu topMenu = FaceMenuRules.For(features, top, mesh.Bounds.Max.Z);
        Assert.Equal("Face plana de topo · Z = 10,0 mm", topMenu.Header);
        Assert.Empty(topMenu.Fits);

        FaceMenu sideMenu = FaceMenuRules.For(features, side, mesh.Bounds.Max.Z);
        Assert.Equal("Face plana vertical", sideMenu.Header);
        Assert.Empty(sideMenu.Fits);
    }
}
