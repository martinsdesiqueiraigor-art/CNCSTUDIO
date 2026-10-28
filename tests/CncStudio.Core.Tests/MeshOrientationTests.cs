using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;

namespace CncStudio.Core.Tests;

public class MeshOrientationTests
{
    /// <summary>Caixa com triângulos voltados para fora (ou para dentro, se <paramref name="inward"/>).</summary>
    private static List<(Point3, Point3, Point3)> Box(double x0, double y0, double z0, double dx, double dy, double dz, bool inward = false)
    {
        Point3 p000 = new(x0, y0, z0), p100 = new(x0 + dx, y0, z0), p110 = new(x0 + dx, y0 + dy, z0), p010 = new(x0, y0 + dy, z0);
        Point3 p001 = new(x0, y0, z0 + dz), p101 = new(x0 + dx, y0, z0 + dz), p111 = new(x0 + dx, y0 + dy, z0 + dz), p011 = new(x0, y0 + dy, z0 + dz);
        (Point3, Point3, Point3)[] t =
        [
            (p000, p010, p110), (p000, p110, p100), (p001, p101, p111), (p001, p111, p011),
            (p000, p100, p101), (p000, p101, p001), (p010, p011, p111), (p010, p111, p110),
            (p000, p001, p011), (p000, p011, p010), (p100, p110, p111), (p100, p111, p101),
        ];
        return inward ? [.. t.Select(q => (q.Item1, q.Item3, q.Item2))] : [.. t];
    }

    [Fact]
    public void Caixa_ja_correta_nao_muda()
    {
        var mesh = new TriangleMesh(Box(0, 0, 0, 10, 20, 30));
        MeshOrientationResult r = MeshOrientation.MakeConsistent(mesh);
        Assert.Equal(0, r.FlippedTriangles);
        Assert.Equal(1, r.Components);
        Assert.Equal(6000, MeshOrientation.SignedVolume(r.Mesh), 6);
    }

    [Fact]
    public void Triangulos_soltos_virados_voltam_ao_sentido_certo()
    {
        List<(Point3, Point3, Point3)> t = Box(0, 0, 0, 10, 20, 30);
        foreach (int i in new[] { 1, 4, 5, 9 })
        {
            t[i] = (t[i].Item1, t[i].Item3, t[i].Item2);
        }

        MeshOrientationResult r = MeshOrientation.MakeConsistent(new TriangleMesh(t));
        Assert.Equal(4, r.FlippedTriangles);
        Assert.Equal(0, r.Conflicts);
        Assert.Equal(6000, MeshOrientation.SignedVolume(r.Mesh), 6);
    }

    [Fact]
    public void Caixa_inteira_de_dentro_para_fora_e_virada()
    {
        MeshOrientationResult r = MeshOrientation.MakeConsistent(new TriangleMesh(Box(0, 0, 0, 10, 10, 10, inward: true)));
        Assert.Equal(12, r.FlippedTriangles);
        Assert.Equal(1000, MeshOrientation.SignedVolume(r.Mesh), 6);
    }

    [Fact]
    public void Furo_passante_com_paredes_viradas_como_no_STEP_e_corrigido()
    {
        // Placa com furo quadrado passante: as 4 paredes do furo compartilham arestas com as faces de cima e de baixo.
        // Monta como um sólido: 8 faces externas + 4 paredes do furo, com as paredes do furo VIRADAS (como no STEP).
        var t = new List<(Point3, Point3, Point3)>();
        const double W = 100, D = 60, H = 20, X0 = 40, X1 = 60, Y0 = 20, Y1 = 40;
        Point3 P(double x, double y, double z) => new(x, y, z);

        // Quadrilátero com buraco em baixo e em cima, dividido em 8 trapézios/retângulos de cada lado
        (double, double)[] outer = [(0, 0), (W, 0), (W, D), (0, D)];
        (double, double)[] inner = [(X0, Y0), (X1, Y0), (X1, Y1), (X0, Y1)];
        for (int i = 0; i < 4; i++)
        {
            (double ax, double ay) = outer[i];
            (double bx, double by) = outer[(i + 1) % 4];
            (double cx, double cy) = inner[(i + 1) % 4];
            (double dx, double dy) = inner[i];
            // topo (z = H), anti-horário visto de cima = para cima
            t.Add((P(ax, ay, H), P(bx, by, H), P(cx, cy, H)));
            t.Add((P(ax, ay, H), P(cx, cy, H), P(dx, dy, H)));
            // base (z = 0), ordem contrária = para baixo
            t.Add((P(ax, ay, 0), P(cx, cy, 0), P(bx, by, 0)));
            t.Add((P(ax, ay, 0), P(dx, dy, 0), P(cx, cy, 0)));
            // lado de fora
            t.Add((P(ax, ay, 0), P(bx, by, 0), P(bx, by, H)));
            t.Add((P(ax, ay, 0), P(bx, by, H), P(ax, ay, H)));
            // parede do furo: ERRADA de propósito (virada para fora do furo, isto é, para o lado do material... invertida)
            t.Add((P(dx, dy, 0), P(cx, cy, 0), P(cx, cy, H)));
            t.Add((P(dx, dy, 0), P(cx, cy, H), P(dx, dy, H)));
        }

        var bad = new TriangleMesh(t);
        double expected = (W * D * H) - ((X1 - X0) * (Y1 - Y0) * H);
        Assert.NotEqual(expected, MeshOrientation.SignedVolume(bad), 3);

        MeshOrientationResult r = MeshOrientation.MakeConsistent(bad);
        Assert.Equal(1, r.Components);
        Assert.Equal(0, r.Conflicts);
        Assert.Equal(expected, MeshOrientation.SignedVolume(r.Mesh), 6);
        Assert.True(r.Mesh.IsClosed);
        Assert.Equal(expected, r.Mesh.Volume, 6);
    }

    [Fact]
    public void Malha_aberta_nao_quebra_e_malha_vazia_volta_igual()
    {
        List<(Point3, Point3, Point3)> t = Box(0, 0, 0, 10, 10, 10);
        t.RemoveAt(0);
        t.RemoveAt(0); // tira a face de baixo
        MeshOrientationResult r = MeshOrientation.MakeConsistent(new TriangleMesh(t));
        Assert.Equal(1, r.Components);
        Assert.False(r.Mesh.IsClosed);

        MeshOrientationResult empty = MeshOrientation.MakeConsistent(new TriangleMesh([]));
        Assert.Equal(0, empty.Components);
        Assert.Equal(0, empty.Mesh.TriangleCount);
    }
}
