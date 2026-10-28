using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;
using Xunit;

namespace CncStudio.Core.Tests;

public class MeshFeaturesTests
{
    private static Point3 P(double x, double y, double z) => new(x, y, z);

    private static void Quad(List<(Point3, Point3, Point3)> t, Point3 a, Point3 b, Point3 c, Point3 d)
    {
        t.Add((a, b, c));
        t.Add((a, c, d));
    }

    /// <summary>Caixa 20×10×5, de (0,0,0) a (20,10,5), normais para fora.</summary>
    private static TriangleMesh Box()
    {
        var t = new List<(Point3, Point3, Point3)>();
        Quad(t, P(0, 0, 0), P(0, 10, 0), P(20, 10, 0), P(20, 0, 0)); // baixo
        Quad(t, P(0, 0, 5), P(20, 0, 5), P(20, 10, 5), P(0, 10, 5)); // cima
        Quad(t, P(0, 0, 0), P(20, 0, 0), P(20, 0, 5), P(0, 0, 5));   // frente
        Quad(t, P(0, 10, 0), P(0, 10, 5), P(20, 10, 5), P(20, 10, 0)); // trás
        Quad(t, P(0, 0, 0), P(0, 0, 5), P(0, 10, 5), P(0, 10, 0));   // esquerda
        Quad(t, P(20, 0, 0), P(20, 10, 0), P(20, 10, 5), P(20, 0, 5)); // direita
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
            Quad(t, o0b, o1b, o1t, o0t); // externa (para fora)
            Quad(t, i0b, i0t, i1t, i1b); // furo (para o eixo)
            Quad(t, o0t, o1t, i1t, i0t); // topo
            Quad(t, o0b, i0b, i1b, o1b); // base
        }

        return new TriangleMesh(t);
    }

    [Fact]
    public void Caixa_tem_6_faces_planas_e_12_arestas_retas()
    {
        MeshFeatures f = MeshFeatures.Analyze(Box());
        Assert.Equal(6, f.Faces.Count);
        Assert.All(f.Faces, x => Assert.Equal(FaceKind.Plane, x.Kind));
        Assert.Equal(12, f.Edges.Count);
        Assert.All(f.Edges, e => Assert.Equal(EdgeKind.Line, e.Kind));
        Assert.Equal(200, f.Faces.Max(x => x.Area), 6);
    }

    [Fact]
    public void Arruela_acha_furo_pino_e_dois_circulos_em_cada_face()
    {
        MeshFeatures f = MeshFeatures.Analyze(Washer());
        MeshFace hole = Assert.Single(f.Faces, x => x.Kind == FaceKind.Hole);
        MeshFace boss = Assert.Single(f.Faces, x => x.Kind == FaceKind.Boss);
        Assert.Equal(10, hole.Diameter, 2);
        Assert.Equal(40, boss.Diameter, 2);
        Assert.Equal(8, hole.Depth, 6);
        Assert.Equal(2, f.Faces.Count(x => x.Kind == FaceKind.Plane));
        Assert.Equal(4, f.Edges.Count(e => e.Kind == EdgeKind.Circle));
        Assert.Contains(f.Edges, e => e.Kind == EdgeKind.Circle && Math.Abs(e.Diameter - 10) < 0.05);
    }

    [Fact]
    public void Raio_que_desce_pelo_furo_acerta_a_parede_do_furo()
    {
        MeshFeatures f = MeshFeatures.Analyze(Washer());
        // do alto, inclinado: entra pelo furo e bate na parede do lado oposto
        (MeshFace? face, double d) = f.PickFace(P(-2, 0, 30), P(0.25, 0, -1));
        Assert.NotNull(face);
        Assert.Equal(FaceKind.Hole, face!.Kind);
        Assert.True(d > 0);
    }

    [Fact]
    public void Raio_vertical_no_topo_acerta_o_plano()
    {
        MeshFeatures f = MeshFeatures.Analyze(Washer());
        (MeshFace? face, _) = f.PickFace(P(12, 0, 30), P(0, 0, -1));
        Assert.Equal(FaceKind.Plane, face!.Kind);
        Assert.Equal(8, face.Center.Z, 6);
    }

    [Fact]
    public void Clique_perto_da_aresta_escolhe_a_aresta_e_longe_escolhe_a_face()
    {
        MeshFeatures f = MeshFeatures.Analyze(Box());
        // vista de cima, na quina (20,10,5)
        (MeshFace? face, MeshEdge? edge) = f.Pick(P(19.95, 5, 50), P(0, 0, -1), 0.002);
        var pe = f.PickEdge(P(19.95, 5, 50), P(0, 0, -1), 0.002);
        Assert.True(pe.Edge is not null, "PickEdge falhou; arestas: " + string.Join(" | ", f.Edges.Select(e => $"{e.Points[0]}->{e.Points[^1]}")));
        Assert.Null(face);
        Assert.NotNull(edge);
        Assert.Equal(EdgeKind.Line, edge!.Kind);
        Assert.Equal(10, edge.Length, 6);

        (MeshFace? face2, MeshEdge? edge2) = f.Pick(P(10, 5, 50), P(0, 0, -1), 0.002);
        Assert.NotNull(face2);
        Assert.Null(edge2);
    }

    [Fact]
    public void Raio_que_erra_tudo_nao_acerta_nada()
    {
        MeshFeatures f = MeshFeatures.Analyze(Box());
        (MeshFace? face, MeshEdge? edge) = f.Pick(P(100, 100, 50), P(0, 0, -1), 0.002);
        Assert.Null(face);
        Assert.Null(edge);
    }

    [Fact]
    public void Malha_vazia_nao_quebra()
    {
        MeshFeatures f = MeshFeatures.Analyze(new TriangleMesh([]));
        Assert.Empty(f.Faces);
        Assert.Empty(f.Edges);
    }
}
