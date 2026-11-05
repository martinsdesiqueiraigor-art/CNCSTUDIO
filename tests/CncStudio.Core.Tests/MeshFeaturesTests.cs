using CncStudio.Core.Cam;
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

    /// <summary>Bloco 40×40×10 com bolsão quadrado 10..30, fundo em Z=5.</summary>
    private static TriangleMesh PocketBlock()
    {
        var t = new List<(Point3, Point3, Point3)>();
        Point3[] o = [P(0, 0, 10), P(40, 0, 10), P(40, 40, 10), P(0, 40, 10)];
        Point3[] i = [P(10, 10, 10), P(30, 10, 10), P(30, 30, 10), P(10, 30, 10)];
        Point3[] f = [P(10, 10, 5), P(30, 10, 5), P(30, 30, 5), P(10, 30, 5)];
        for (int k = 0; k < 4; k++)
        {
            int n = (k + 1) % 4;
            Quad(t, o[k], o[n], i[n], i[k]); // topo em moldura (normal +Z)
            Quad(t, i[k], i[n], f[n], f[k]); // parede do bolsão: normal para dentro do bolsão
            Quad(t, o[k], P(o[k].X, o[k].Y, 0), P(o[n].X, o[n].Y, 0), o[n]); // parede externa
        }

        Quad(t, f[0], f[1], f[2], f[3]); // fundo (+Z)
        Quad(t, P(0, 0, 0), P(0, 40, 0), P(40, 40, 0), P(40, 0, 0)); // base (-Z)
        return new TriangleMesh(t);
    }

    [Fact]
    public void Fundo_do_bolsao_tem_um_contorno_de_4_cantos()
    {
        MeshFeatures f = MeshFeatures.Analyze(PocketBlock());
        MeshFace floor = Assert.Single(f.Faces, x => x.Kind == FaceKind.Plane && x.Normal.Z > 0.99 && Math.Abs(x.Center.Z - 5) < 1e-6);
        IReadOnlyList<IReadOnlyList<Point3>> loops = f.BoundaryLoops(floor);
        Assert.Single(loops);
        Assert.Equal(4, loops[0].Count);
        Assert.Equal(20, loops[0].Max(p => p.X) - loops[0].Min(p => p.X), 6);
    }

    [Fact]
    public void Moldura_do_topo_tem_dois_contornos_e_nao_vira_bolsao()
    {
        MeshFeatures f = MeshFeatures.Analyze(PocketBlock());
        MeshFace top = Assert.Single(f.Faces, x => x.Kind == FaceKind.Plane && x.Normal.Z > 0.99 && Math.Abs(x.Center.Z - 10) < 1e-6);
        Assert.Equal(2, f.BoundaryLoops(top).Count);
        OperationResult r = FacePocket.Build(f, top, new CamTool(1, 6, 8000, 800, 300, "Topo"), new ModelZ(35, 15), 10, 10, 2, new PocketParams());
        Assert.False(r.Ok);
    }

    [Fact]
    public void Bolsao_a_partir_do_fundo_gera_percurso_ate_o_fundo()
    {
        MeshFeatures f = MeshFeatures.Analyze(PocketBlock());
        MeshFace floor = Assert.Single(f.Faces, x => x.Kind == FaceKind.Plane && x.Normal.Z > 0.99 && Math.Abs(x.Center.Z - 5) < 1e-6);
        OperationResult r = FacePocket.Build(f, floor, new CamTool(1, 6, 8000, 800, 300, "Topo"), new ModelZ(35, 15), 5, 10, 2.5, new PocketParams());
        Assert.True(r.Ok, r.Error);
        Assert.Contains(r.Operation!.Moves, m => Math.Abs(m.Z - 5) < 1e-6);
        Assert.DoesNotContain(r.Operation.Moves, m => m.Z < 5 - 1e-6);
    }

    // ---- R-05: alturas Z e fundo informados pelo usuário nas operações 3D

    private static readonly CamTool Drill6 = new(1, 6, 2500, 600, 150, "Broca 6");

    [Fact]
    public void Furacao_usa_as_alturas_e_o_fundo_do_usuario_e_nao_os_da_malha()
    {
        MeshFeatures f = MeshFeatures.Analyze(Washer());
        MeshFace hole = Assert.Single(f.Faces, x => x.Kind == FaceKind.Hole);
        Assert.Equal(0, FaceDrilling.MeshBottomZ([hole]), 6); // a malha mostra o furo passante (Z 0)
        OperationResult r = FaceDrilling.Build([hole], Drill6, new ModelZ(60, 12), 8, 3, 2);
        Assert.True(r.Ok, r.Error);
        DrillCycle cycle = r.Operation!.Drill!;
        Assert.Equal(60, cycle.SafeZ);       // Z seguro do usuário (não 8 + 25)
        Assert.Equal(12, cycle.RetractZ);    // plano R do usuário (não 8 + 2)
        Assert.Equal(8, cycle.TopZ);
        Assert.Equal(3, cycle.BottomZ);      // fundo do usuário (cego), não o da malha
    }

    [Theory]
    [InlineData(35, 8)]            // aproximação no topo do furo
    [InlineData(35, 5)]            // aproximação abaixo do topo
    [InlineData(12, 12)]           // seguro igual à aproximação
    [InlineData(10, 12)]           // seguro abaixo da aproximação
    [InlineData(double.NaN, 12)]
    [InlineData(35, double.PositiveInfinity)]
    public void Furacao_recusa_alturas_incoerentes(double safe, double clearance)
    {
        MeshFeatures f = MeshFeatures.Analyze(Washer());
        MeshFace hole = Assert.Single(f.Faces, x => x.Kind == FaceKind.Hole);
        OperationResult r = FaceDrilling.Build([hole], Drill6, new ModelZ(safe, clearance), 8, 0, 2);
        Assert.False(r.Ok);
        Assert.Contains("Alturas Z", r.Error);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(double.NaN)]
    public void Furacao_recusa_fundo_no_topo_acima_ou_invalido(double bottom)
    {
        MeshFeatures f = MeshFeatures.Analyze(Washer());
        MeshFace hole = Assert.Single(f.Faces, x => x.Kind == FaceKind.Hole);
        OperationResult r = FaceDrilling.Build([hole], Drill6, new ModelZ(35, 12), 8, bottom, 2);
        Assert.False(r.Ok);
        Assert.Contains("fundo do furo", r.Error);
    }

    [Fact]
    public void Furacao_recusa_broca_maior_que_o_furo_e_lista_vazia()
    {
        MeshFeatures f = MeshFeatures.Analyze(Washer());
        MeshFace hole = Assert.Single(f.Faces, x => x.Kind == FaceKind.Hole);
        Assert.Contains("maior que o menor furo", FaceDrilling.Build([hole], Drill6 with { Diameter = 12 }, new ModelZ(35, 12), 8, 0, 2).Error);
        Assert.False(FaceDrilling.Build([], Drill6, new ModelZ(35, 12), 8, 0, 2).Ok);
    }

    [Fact]
    public void Seguro_precisa_passar_acima_do_ponto_mais_alto_do_modelo()
    {
        Assert.Contains("ponto mais alto", new ModelZ(12, 6).Validate(modelTopZ: 20, workTopZ: 5));
        Assert.Null(new ModelZ(25, 6).Validate(modelTopZ: 20, workTopZ: 5));
    }

    [Fact]
    public void Bolsao_usa_alturas_e_fundo_do_usuario()
    {
        MeshFeatures f = MeshFeatures.Analyze(PocketBlock());
        MeshFace floor = Assert.Single(f.Faces, x => x.Kind == FaceKind.Plane && x.Normal.Z > 0.99 && Math.Abs(x.Center.Z - 5) < 1e-6);
        OperationResult r = FacePocket.Build(f, floor, new CamTool(1, 6, 8000, 800, 300, "Topo"), new ModelZ(80, 20), 6, 10, 2, new PocketParams());
        Assert.True(r.Ok, r.Error);
        Assert.Contains(r.Operation!.Moves, m => Math.Abs(m.Z - 80) < 1e-6);
        Assert.DoesNotContain(r.Operation.Moves, m => m.Z > 80 + 1e-6);
        Assert.Contains(r.Operation.Moves, m => Math.Abs(m.Z - 6) < 1e-6);        // fundo digitado (a malha diz 5)
        Assert.DoesNotContain(r.Operation.Moves, m => m.Z < 6 - 1e-6);
    }

    [Fact]
    public void Bolsao_recusa_alturas_incoerentes_e_fundo_no_topo()
    {
        MeshFeatures f = MeshFeatures.Analyze(PocketBlock());
        MeshFace floor = Assert.Single(f.Faces, x => x.Kind == FaceKind.Plane && x.Normal.Z > 0.99 && Math.Abs(x.Center.Z - 5) < 1e-6);
        var tool = new CamTool(1, 6, 8000, 800, 300, "Topo");
        Assert.Contains("Alturas Z", FacePocket.Build(f, floor, tool, new ModelZ(10, 10), 5, 10, 2, new PocketParams()).Error);
        Assert.Contains("Alturas Z", FacePocket.Build(f, floor, tool, new ModelZ(30, 10), 5, 10, 2, new PocketParams()).Error); // aproximação no topo do modelo
        Assert.Contains("fundo", FacePocket.Build(f, floor, tool, new ModelZ(35, 15), 10, 10, 2, new PocketParams()).Error);
        Assert.Contains("fundo", FacePocket.Build(f, floor, tool, new ModelZ(35, 15), double.NaN, 10, 2, new PocketParams()).Error);
    }
}

