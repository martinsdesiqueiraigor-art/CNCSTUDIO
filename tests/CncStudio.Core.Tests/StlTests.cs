using System.Globalization;
using System.Text;
using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;

namespace CncStudio.Core.Tests;

public class StlTests
{
    // Cubo 10 × 20 × 30 com 12 triângulos orientados para fora
    private static List<(Point3, Point3, Point3)> Box(double x, double y, double z)
    {
        Point3 p000 = new(0, 0, 0), p100 = new(x, 0, 0), p110 = new(x, y, 0), p010 = new(0, y, 0);
        Point3 p001 = new(0, 0, z), p101 = new(x, 0, z), p111 = new(x, y, z), p011 = new(0, y, z);
        return
        [
            (p000, p010, p110), (p000, p110, p100), // baixo
            (p001, p101, p111), (p001, p111, p011), // cima
            (p000, p100, p101), (p000, p101, p001), // frente
            (p010, p011, p111), (p010, p111, p110), // trás
            (p000, p001, p011), (p000, p011, p010), // esquerda
            (p100, p110, p111), (p100, p111, p101), // direita
        ];
    }

    private static byte[] Binary(List<(Point3, Point3, Point3)> tris)
    {
        var bytes = new List<byte>(new byte[80]);
        bytes.AddRange(BitConverter.GetBytes((uint)tris.Count));
        foreach ((Point3 a, Point3 b, Point3 c) in tris)
        {
            bytes.AddRange(new byte[12]);
            foreach (Point3 p in new[] { a, b, c })
            {
                bytes.AddRange(BitConverter.GetBytes((float)p.X));
                bytes.AddRange(BitConverter.GetBytes((float)p.Y));
                bytes.AddRange(BitConverter.GetBytes((float)p.Z));
            }

            bytes.AddRange(new byte[2]);
        }

        return [.. bytes];
    }

    private static byte[] Ascii(List<(Point3, Point3, Point3)> tris)
    {
        var sb = new StringBuilder("solid teste\n");
        foreach ((Point3 a, Point3 b, Point3 c) in tris)
        {
            sb.Append("facet normal 0 0 0\nouter loop\n");
            foreach (Point3 p in new[] { a, b, c })
            {
                sb.Append(string.Create(CultureInfo.InvariantCulture, $"vertex {p.X} {p.Y} {p.Z}\n"));
            }

            sb.Append("endloop\nendfacet\n");
        }

        sb.Append("endsolid teste\n");
        return Encoding.UTF8.GetBytes(sb.ToString());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Le_cubo_binario_e_texto(bool binary)
    {
        List<(Point3, Point3, Point3)> box = Box(10, 20, 30);
        StlResult r = StlReader.Read(binary ? Binary(box) : Ascii(box));
        Assert.True(r.Ok, r.Error);
        Assert.Equal(binary, r.Binary);
        TriangleMesh mesh = r.Mesh!;
        Assert.Equal(12, mesh.TriangleCount);
        Assert.Equal(10, mesh.Size.X, 6);
        Assert.Equal(20, mesh.Size.Y, 6);
        Assert.Equal(30, mesh.Size.Z, 6);
        Assert.Equal(6000, mesh.Volume, 3);
        Assert.Equal(2 * ((10 * 20) + (10 * 30) + (20 * 30)), mesh.SurfaceArea, 3);
        Assert.True(mesh.IsClosed);
    }

    [Fact]
    public void Malha_aberta_nao_e_fechada()
    {
        List<(Point3, Point3, Point3)> box = Box(10, 10, 10);
        box.RemoveAt(0);
        Assert.False(new TriangleMesh(box).IsClosed);
    }

    [Fact]
    public void Escala_e_posicao()
    {
        TriangleMesh mesh = new TriangleMesh(Box(1, 2, 3)).Scaled(25.4);
        Assert.Equal(25.4, mesh.Size.X, 6);
        TriangleMesh placed = new TriangleMesh(Box(10, 10, 10)).Translated(5, 7, 3).PlacedTopAtZero();
        Assert.Equal(0, placed.Bounds.Min.X, 6);
        Assert.Equal(0, placed.Bounds.Min.Y, 6);
        Assert.Equal(0, placed.Bounds.Max.Z, 6);
        Assert.Equal(-10, placed.Bounds.Min.Z, 6);
    }

    [Fact]
    public void Descarta_triangulo_degenerado()
    {
        List<(Point3, Point3, Point3)> box = Box(10, 10, 10);
        box.Add((new Point3(1, 1, 1), new Point3(1, 1, 1), new Point3(2, 2, 2)));
        StlResult r = StlReader.Read(Binary(box));
        Assert.True(r.Ok);
        Assert.Equal(12, r.Mesh!.TriangleCount);
        Assert.Equal(1, r.Skipped);
    }

    [Fact]
    public void Erros_em_portugues()
    {
        Assert.False(StlReader.Read([1, 2, 3]).Ok);
        StlResult lixo = StlReader.Read(new byte[200]);
        Assert.False(lixo.Ok);
        Assert.Contains("STL", lixo.Error);
        StlResult ruim = StlReader.Read(Encoding.ASCII.GetBytes("solid x\nfacet normal 0 0 0\nouter loop\nvertex a b c\nendloop\nendfacet\n"));
        Assert.False(ruim.Ok);
    }

    [Fact]
    public void Malha_vira_cena_3D()
    {
        var mesh = new TriangleMesh(Box(10, 20, 30));
        var snapshot = mesh.ToSnapshot();
        Assert.Single(snapshot.Objects);
        Assert.Equal(12, snapshot.Objects[0].Mesh.TriangleCount);
        Assert.Equal(30, snapshot.Bounds.Max.Z, 6);
    }
}
