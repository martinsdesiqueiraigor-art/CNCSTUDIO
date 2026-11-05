using System.Text;
using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;

namespace CncStudio.Core.Tests;

/// <summary>
/// Triângulos colapsados (dois vértices no mesmo ponto), como os que o OpenCASCADE gera ao triangular STEP reais
/// (diagnóstico CAM-08-T2): não podem fazer uma malha fechada parecer aberta nem em pedaços.
/// </summary>
public class MeshCollapsedTests
{
    private static List<(Point3, Point3, Point3)> Box(double d)
    {
        Point3 p000 = new(0, 0, 0), p100 = new(d, 0, 0), p110 = new(d, d, 0), p010 = new(0, d, 0);
        Point3 p001 = new(0, 0, d), p101 = new(d, 0, d), p111 = new(d, d, d), p011 = new(0, d, d);
        return
        [
            (p000, p010, p110), (p000, p110, p100), (p001, p101, p111), (p001, p111, p011),
            (p000, p100, p101), (p000, p101, p001), (p010, p011, p111), (p010, p111, p110),
            (p000, p001, p011), (p000, p011, p010), (p100, p110, p111), (p100, p111, p101),
        ];
    }

    // Colapsado sobre uma aresta que já existe (0,0,0)-(10,0,0): o caso que virava aresta não-manifold
    private static (Point3, Point3, Point3) CollapsedOnEdge => (new(0, 0, 0), new(10, 0, 0), new(10, 0, 0));

    [Fact]
    public void Malha_fechada_com_um_colapsado_continua_fechada_e_em_um_pedaco()
    {
        List<(Point3, Point3, Point3)> t = Box(10);
        t.Add(CollapsedOnEdge);
        var mesh = new TriangleMesh(t);

        Assert.True(mesh.IsCollapsed(12));
        Assert.True(mesh.IsClosed);

        MeshCheck check = MeshCheck.Of(mesh);
        Assert.True(check.IsClosed);
        Assert.Equal(1, check.Pieces);
        Assert.Equal(1, check.Collapsed);
        Assert.Equal(0, check.BorderEdges);
        Assert.Equal(0, check.NonManifoldEdges);
    }

    [Fact]
    public void Colapsado_nao_vira_nao_manifold_nem_pedaco_isolado()
    {
        List<(Point3, Point3, Point3)> t = Box(10);
        t.Add(CollapsedOnEdge);
        t.Add((new(0, 0, 10), new(0, 0, 10), new(0, 0, 10))); // os três no mesmo ponto
        MeshOrientationResult r = MeshOrientation.MakeConsistent(new TriangleMesh(t));

        Assert.Equal(1, r.Components);
        Assert.Equal(0, r.NonManifoldEdges);
        Assert.Equal(0, r.Conflicts);
        Assert.Equal(14, r.Mesh.TriangleCount); // os colapsados voltam como estavam
        Assert.Equal(1000, MeshOrientation.SignedVolume(r.Mesh), 6);
    }

    [Fact]
    public void Malha_aberta_de_verdade_continua_aberta_com_os_numeros()
    {
        List<(Point3, Point3, Point3)> t = Box(10);
        t.RemoveRange(0, 2); // tira a face de baixo (10 × 10)
        t.Add(CollapsedOnEdge);
        var mesh = new TriangleMesh(t);

        Assert.False(mesh.IsClosed);
        MeshCheck check = MeshCheck.Of(mesh);
        Assert.False(check.IsClosed);
        Assert.Equal(4, check.BorderEdges);
        Assert.Equal(40, check.BorderLength, 9);
        Assert.Equal(0, check.NonManifoldEdges);
        Assert.Equal(1, check.Pieces);
    }

    [Fact]
    public void Aresta_com_tres_triangulos_e_contada_e_a_malha_nao_e_fechada()
    {
        List<(Point3, Point3, Point3)> t = Box(10);
        t.Add((new(0, 0, 0), new(10, 0, 0), new(5, -5, 0))); // aba presa numa aresta da caixa
        MeshCheck check = MeshCheck.Of(new TriangleMesh(t));

        Assert.False(check.IsClosed);
        Assert.Equal(1, check.NonManifoldEdges);
        Assert.Equal(2, check.BorderEdges);
    }

    [Fact]
    public void Tirar_os_colapsados_nao_muda_area_nem_volume()
    {
        List<(Point3, Point3, Point3)> t = Box(10);
        t.Add(CollapsedOnEdge);
        var mesh = new TriangleMesh(t);
        TriangleMesh clean = mesh.WithoutCollapsed(out int removed);

        Assert.Equal(1, removed);
        Assert.Equal(12, clean.TriangleCount);
        Assert.Equal(mesh.SurfaceArea, clean.SurfaceArea, 9);
        Assert.Equal(mesh.Volume, clean.Volume, 9);
        Assert.Same(clean, clean.WithoutCollapsed(out int none));
        Assert.Equal(0, none);
    }

    [Fact]
    public void Triangulo_alinhado_com_tres_pontos_diferentes_nao_e_colapsado()
    {
        var mesh = new TriangleMesh([(new(0, 0, 0), new(5, 0, 0), new(10, 0, 0))]);
        Assert.False(mesh.IsCollapsed(0));
    }

    [Fact]
    public void Malha_so_de_colapsados_nao_e_fechada()
    {
        var mesh = new TriangleMesh([CollapsedOnEdge]);
        Assert.False(mesh.IsClosed);
        Assert.False(MeshCheck.Of(mesh).IsClosed);
        Assert.Equal(0, MeshCheck.Of(mesh).Pieces);
    }

    [Fact]
    public void STL_com_triangulo_colapsado_le_fechado_e_em_um_pedaco()
    {
        List<(Point3, Point3, Point3)> t = Box(10);
        t.Add(CollapsedOnEdge);
        var stl = new StringBuilder("solid caixa\n");
        foreach ((Point3 a, Point3 b, Point3 c) in t)
        {
            stl.Append("facet normal 0 0 0\nouter loop\n");
            foreach (Point3 p in new[] { a, b, c })
            {
                stl.Append($"vertex {p.X} {p.Y} {p.Z}\n");
            }

            stl.Append("endloop\nendfacet\n");
        }

        stl.Append("endsolid caixa\n");
        StlResult r = StlReader.Read(Encoding.ASCII.GetBytes(stl.ToString()));

        Assert.True(r.Ok);
        Assert.Equal(1, r.Skipped); // o leitor de STL já descarta triângulo sem área
        Assert.True(r.Mesh!.IsClosed);
        Assert.Equal(1, MeshCheck.Of(r.Mesh).Pieces);
        Assert.Equal(1, MeshOrientation.MakeConsistent(r.Mesh).Components);
    }
}
