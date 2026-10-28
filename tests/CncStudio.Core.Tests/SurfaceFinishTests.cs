using CncStudio.Core.Cam;
using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;

namespace CncStudio.Core.Tests;

public class SurfaceFinishTests
{
    private static TriangleMesh Box(double x, double y, double z)
    {
        Point3 p000 = new(0, 0, 0), p100 = new(x, 0, 0), p110 = new(x, y, 0), p010 = new(0, y, 0);
        Point3 p001 = new(0, 0, z), p101 = new(x, 0, z), p111 = new(x, y, z), p011 = new(0, y, z);
        return new TriangleMesh(new (Point3, Point3, Point3)[]
        {
            (p000, p010, p110), (p000, p110, p100), (p001, p101, p111), (p001, p111, p011),
            (p000, p100, p101), (p000, p101, p001), (p010, p011, p111), (p010, p111, p110),
            (p000, p001, p011), (p000, p011, p010), (p100, p110, p111), (p100, p111, p101),
        });
    }

    // Cunha: z sobe 1 por 1 em X (rampa de 45°) de x = 0 a 20; y de 0 a 20
    private static TriangleMesh Wedge()
    {
        Point3 a = new(0, 0, 0), b = new(20, 0, 20), c = new(20, 20, 20), d = new(0, 20, 0);
        Point3 a2 = new(0, 0, -5), b2 = new(20, 0, -5), c2 = new(20, 20, -5), d2 = new(0, 20, -5);
        return new TriangleMesh(new (Point3, Point3, Point3)[]
        {
            (a, b, c), (a, c, d), // rampa
            (a2, c2, b2), (a2, d2, c2), // base
            (a, a2, b2), (a, b2, b), (d, c, c2), (d, c2, d2),
            (a, d, d2), (a, d2, a2), (b, b2, c2), (b, c2, c),
        });
    }

    private static readonly CamTool Ball = new(1, 6, 8000, 1200, 400, "Bola D6");
    private static readonly CamLevels Levels = new(30, 25, 20, -5, 5);

    [Fact]
    public void Topo_plano_da_altura_do_topo_e_a_borda_levanta_a_esfera()
    {
        var cutter = new DropCutter(Box(20, 20, 10), 1);
        Assert.Equal(10, cutter.TipHeight(10, 10)!.Value, 6);
        // 0,5 mm fora da aresta: a esfera (R1) apoia na aresta; centro a 10 + √(1−0,25)
        Assert.Equal(10 + Math.Sqrt(0.75) - 1, cutter.TipHeight(-0.5, 10)!.Value, 6);
        Assert.Null(cutter.TipHeight(-5, 10)); // longe do modelo
    }

    [Fact]
    public void Rampa_de_45_graus_sobe_R_vezes_raiz2_menos_1()
    {
        const double r = 3;
        var cutter = new DropCutter(Wedge(), r);
        double expected = 10 + (r * (Math.Sqrt(2) - 1));
        Assert.Equal(expected, cutter.TipHeight(10, 10)!.Value, 6);
    }

    [Fact]
    public void Esfera_o_topo_e_o_polo()
    {
        TriangleMesh sphere = Sphere(15, 24);
        var cutter = new DropCutter(sphere, 2);
        double top = cutter.TipHeight(0, 0)!.Value;
        Assert.InRange(top, 14.8, 15.05);
    }

    [Fact]
    public void Varredura_nunca_fica_abaixo_do_modelo()
    {
        TriangleMesh sphere = Sphere(15, 24).Translated(15, 15, 0);
        var levels = new CamLevels(40, 30, 20, -1, 5);
        OperationResult r = SurfaceFinish.Raster(sphere, Ball, levels, new SurfaceParams(1.5));
        Assert.True(r.Ok, r.Error);
        var cutter = new DropCutter(sphere, Ball.Radius);
        double worst = 0;
        foreach (ToolMove m in r.Operation!.Moves.Where(m => m.Kind == MoveKind.Feed))
        {
            double min = cutter.TipHeight(m.X, m.Y) is { } h ? Math.Max(h, levels.BottomZ) : levels.BottomZ;
            worst = Math.Max(worst, min - m.Z);
        }

        Assert.True(worst < 0.03, $"corte abaixo do modelo em {worst} mm");
        Assert.Contains(r.Operation.Warnings, w => w.Contains("cúspide"));
    }

    [Fact]
    public void Topo_plano_vira_linhas_retas_e_o_programa_roda()
    {
        TriangleMesh box = Box(20, 12, 10);
        OperationResult r = SurfaceFinish.Raster(box, Ball, Levels, new SurfaceParams(3));
        Assert.True(r.Ok, r.Error);
        // linhas a 3 mm em Y e só 2 pontos de avanço por linha (reta)
        Assert.All(r.Operation!.Moves.Where(m => m.Kind == MoveKind.Feed && Math.Abs(m.Z - 10) < 0.5), m => Assert.InRange(m.Z, 9.5, 10.001));
        foreach (CamDialect dialect in new[] { CamDialect.Fanuc, CamDialect.Siemens })
        {
            string program = CamPost.Generate([r.Operation], new PostOptions(dialect));
            Assert.Contains("VARREDURA", program);
            var analysis = ProgramAnalyzer.Analyze(program, dialect == CamDialect.Fanuc ? Dialects.Dialect.Fanuc : Dialects.Dialect.Siemens);
            Assert.True(analysis.ToolPath.Moves.Count > 10);
        }
    }

    [Fact]
    public void Valida_parametros()
    {
        TriangleMesh box = Box(20, 12, 10);
        Assert.False(SurfaceFinish.Raster(box, Ball, Levels, new SurfaceParams(0)).Ok);
        Assert.False(SurfaceFinish.Raster(box, Ball, Levels, new SurfaceParams(7)).Ok); // maior que o diâmetro
        Assert.False(SurfaceFinish.Raster(box, Ball, new CamLevels(30, 8, 5, -5, 5), new SurfaceParams(2)).Ok); // abaixo do topo do modelo
        Assert.False(SurfaceFinish.Raster(new TriangleMesh([]), Ball, Levels, new SurfaceParams(2)).Ok);
    }

    private static TriangleMesh Sphere(double radius, int segments)
    {
        var tris = new List<(Point3, Point3, Point3)>();
        Point3 P(int i, int j)
        {
            double theta = Math.PI * i / segments, phi = 2 * Math.PI * j / (segments * 2);
            return new Point3(radius * Math.Sin(theta) * Math.Cos(phi), radius * Math.Sin(theta) * Math.Sin(phi), radius * Math.Cos(theta));
        }

        for (int i = 0; i < segments; i++)
        {
            for (int j = 0; j < segments * 2; j++)
            {
                Point3 a = P(i, j), b = P(i + 1, j), c = P(i + 1, j + 1), d = P(i, j + 1);
                if (i > 0)
                {
                    tris.Add((a, b, d));
                }

                if (i < segments - 1)
                {
                    tris.Add((b, c, d));
                }
            }
        }

        return new TriangleMesh(tris);
    }
}
