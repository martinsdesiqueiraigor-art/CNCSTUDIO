using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>CAM-ILHAS-01 F1: lado de cada laço da face de fundo (parede sobe = ilha/parede; desce = furo/borda).</summary>
public class LoopSideTests
{
    private static Point3 P(double x, double y, double z) => new(x, y, z);

    private static void Quad(List<(Point3, Point3, Point3)> t, Point3 a, Point3 b, Point3 c, Point3 d)
    {
        t.Add((a, b, c));
        t.Add((a, c, d));
    }

    private static Point3[] Square(double x0, double y0, double x1, double y1, double z) =>
        [P(x0, y0, z), P(x1, y0, z), P(x1, y1, z), P(x0, y1, z)];

    /// <summary>Anel plano entre o quadrado de fora e o de dentro (de baixo para cima, sentido anti-horário).</summary>
    private static void Ring(List<(Point3, Point3, Point3)> t, Point3[] outer, Point3[] inner)
    {
        for (int k = 0; k < 4; k++)
        {
            int n = (k + 1) % 4;
            Quad(t, outer[k], outer[n], inner[n], inner[k]);
        }
    }

    /// <summary>Parede vertical entre dois quadrados iguais em Z diferentes.</summary>
    private static void Wall(List<(Point3, Point3, Point3)> t, Point3[] a, Point3[] b)
    {
        for (int k = 0; k < 4; k++)
        {
            int n = (k + 1) % 4;
            Quad(t, a[k], a[n], b[n], b[k]);
        }
    }

    private static MeshFace Face(MeshFeatures f, double z) =>
        f.Faces.Where(x => x.Kind == FaceKind.Plane && x.Normal.Z > 0.99 && Math.Abs(x.Center.Z - z) < 0.5).OrderByDescending(x => x.Area).First();

    /// <summary>
    /// Flange: placa 0..60 de topo Z=5 (borda que desce até Z=0), cubo 20..40 até Z=15 e furo 4..8 vazado.
    /// Topo em grade conforme (sem vértice no meio de aresta) para a solda dos laços.
    /// </summary>
    public static TriangleMesh Flange(bool chamferHole = false)
    {
        var t = new List<(Point3, Point3, Point3)>();
        double[] g = [0, 10, 20, 40, 60];
        Point3[] holeTop = chamferHole ? Square(3, 3, 9, 9, 5) : Square(4, 4, 8, 8, 5);
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                if (i == 2 && j == 2)
                {
                    continue; // cubo
                }

                Point3[] cell = Square(g[i], g[j], g[i + 1], g[j + 1], 5);
                if (i == 0 && j == 0)
                {
                    Ring(t, cell, holeTop);
                }
                else
                {
                    Quad(t, cell[0], cell[1], cell[2], cell[3]);
                }
            }
        }

        if (chamferHole)
        {
            Point3[] c4 = Square(4, 4, 8, 8, 4);
            Wall(t, holeTop, c4);
            Wall(t, c4, Square(4, 4, 8, 8, 0));
        }
        else
        {
            Wall(t, holeTop, Square(4, 4, 8, 8, 0));
        }

        Point3[] hub15 = Square(20, 20, 40, 40, 15);
        Wall(t, Square(20, 20, 40, 40, 5), hub15);
        Quad(t, hub15[0], hub15[1], hub15[2], hub15[3]);

        // borda da placa, um trecho por vão da grade
        var edge = new List<(double X, double Y)>();
        foreach (double x in g[..^1]) edge.Add((x, 0));
        foreach (double y in g[..^1]) edge.Add((60, y));
        foreach (double x in g.Reverse().ToArray()[..^1]) edge.Add((x, 60));
        foreach (double y in g.Reverse().ToArray()[..^1]) edge.Add((0, y));
        for (int k = 0; k < edge.Count; k++)
        {
            (double x0, double y0) = edge[k];
            (double x1, double y1) = edge[(k + 1) % edge.Count];
            Quad(t, P(x0, y0, 0), P(x1, y1, 0), P(x1, y1, 5), P(x0, y0, 5));
        }

        return new TriangleMesh(t);
    }

    /// <summary>Malha deslocada em Z (para o topo da peça ficar em Z0, como o bruto bloco absoluto).</summary>
    public static TriangleMesh Shift(TriangleMesh m, double dz) =>
        new([.. Enumerable.Range(0, m.TriangleCount).Select(i =>
        {
            (Point3 a, Point3 b, Point3 c) = m.Triangle(i);
            return (new Point3(a.X, a.Y, a.Z + dz), new Point3(b.X, b.Y, b.Z + dz), new Point3(c.X, c.Y, c.Z + dz));
        })]);

    [Fact]
    public void Flange_cubo_sobe_furo_desce_e_borda_desce()
    {
        MeshFeatures f = MeshFeatures.Analyze(Flange());
        IReadOnlyList<SidedLoop> loops = f.ClassifiedLoops(Face(f, 5));
        Assert.Equal(3, loops.Count);
        Assert.Equal(LoopSide.Down, loops[0].Side); // borda de fora da placa
        Assert.Equal(LoopSide.Up, loops[1].Side); // cubo (ilha)
        Assert.Equal(LoopSide.Down, loops[2].Side); // furo
    }

    [Fact]
    public void Furo_escareado_continua_descendo()
    {
        MeshFeatures f = MeshFeatures.Analyze(Flange(chamferHole: true));
        IReadOnlyList<SidedLoop> loops = f.ClassifiedLoops(Face(f, 5));
        Assert.Equal(LoopSide.Down, loops[^1].Side);
        Assert.Equal(LoopSide.Up, loops[1].Side);
    }

    [Fact]
    public void Bolsao_fechado_parede_sobe()
    {
        // bloco 40×40×10, bolsão 10..30 de fundo Z=5
        var t = new List<(Point3, Point3, Point3)>();
        Point3[] o = Square(0, 0, 40, 40, 10), i = Square(10, 10, 30, 30, 10), fl = Square(10, 10, 30, 30, 5);
        Ring(t, o, i);
        Wall(t, i, fl);
        Quad(t, fl[0], fl[1], fl[2], fl[3]);
        Wall(t, Square(0, 0, 40, 40, 0), o);
        MeshFeatures f = MeshFeatures.Analyze(new TriangleMesh(t));
        SidedLoop loop = Assert.Single(f.ClassifiedLoops(Face(f, 5)));
        Assert.Equal(LoopSide.Up, loop.Side);
    }

    [Fact]
    public void Ilha_com_raio_na_base_sobe()
    {
        // fundo Z=5 com um pino que começa num chanfro tangente 18..22 → 19..21 e sobe até Z=10
        var t = new List<(Point3, Point3, Point3)>();
        Point3[] o = Square(0, 0, 40, 40, 5), b = Square(18, 18, 22, 22, 5), m = Square(19, 19, 21, 21, 6), top = Square(19, 19, 21, 21, 10);
        Ring(t, o, b);
        Wall(t, b, m);
        Wall(t, m, top);
        Quad(t, top[0], top[1], top[2], top[3]);
        Wall(t, Square(0, 0, 40, 40, 0), o);
        MeshFeatures f = MeshFeatures.Analyze(new TriangleMesh(t));
        IReadOnlyList<SidedLoop> loops = f.ClassifiedLoops(Face(f, 5));
        Assert.Equal(2, loops.Count);
        Assert.Equal(LoopSide.Down, loops[0].Side);
        Assert.Equal(LoopSide.Up, loops[1].Side);
    }

    [Fact]
    public void Degrau_parte_sobe_parte_desce_e_misto()
    {
        // fundo Z=5 de 0..40; parede subindo só em Y=40 (até Z=10); as outras bordas descem
        var t = new List<(Point3, Point3, Point3)>();
        Quad(t, P(0, 0, 5), P(40, 0, 5), P(40, 40, 5), P(0, 40, 5));
        Quad(t, P(0, 40, 5), P(40, 40, 5), P(40, 40, 10), P(0, 40, 10)); // parede sobe
        Quad(t, P(0, 40, 10), P(40, 40, 10), P(40, 60, 10), P(0, 60, 10)); // topo alto
        Quad(t, P(0, 0, 0), P(40, 0, 0), P(40, 0, 5), P(0, 0, 5)); // borda desce
        MeshFeatures f = MeshFeatures.Analyze(new TriangleMesh(t));
        SidedLoop loop = Assert.Single(f.ClassifiedLoops(Face(f, 5)));
        Assert.Equal(LoopSide.Mixed, loop.Side);
    }

    [Fact]
    public void Lacos_classificados_tem_os_mesmos_pontos_que_BoundaryLoops()
    {
        MeshFeatures f = MeshFeatures.Analyze(Flange());
        MeshFace face = Face(f, 5);
        IReadOnlyList<IReadOnlyList<Point3>> plain = f.BoundaryLoops(face);
        IReadOnlyList<SidedLoop> sided = f.ClassifiedLoops(face);
        Assert.Equal(plain.Count, sided.Count);
        for (int i = 0; i < plain.Count; i++)
        {
            Assert.Equal(plain[i], sided[i].Points);
        }
    }

    [Fact]
    public void Laco_sem_vizinha_fica_subindo_o_lado_seguro()
    {
        var t = new List<(Point3, Point3, Point3)>();
        Quad(t, P(0, 0, 5), P(40, 0, 5), P(40, 40, 5), P(0, 40, 5));
        MeshFeatures f = MeshFeatures.Analyze(new TriangleMesh(t));
        Assert.Equal(LoopSide.Up, Assert.Single(f.ClassifiedLoops(Face(f, 5))).Side);
    }
}
