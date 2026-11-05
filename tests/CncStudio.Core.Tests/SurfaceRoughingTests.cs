using CncStudio.Core.Cam;
using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;

namespace CncStudio.Core.Tests;

public class SurfaceRoughingTests
{
    private static readonly CamTool Ball = new(1, 6, 8000, 1200, 400, "Bola D6");

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

    // Meia esfera (cúpula) de raio 15 com centro em (15, 15, 0), com base plana em Z = 0
    internal static TriangleMesh Dome()
    {
        const double radius = 15;
        const int segments = 24;
        var tris = new List<(Point3, Point3, Point3)>();
        Point3 P(int i, int j)
        {
            double theta = Math.PI / 2 * i / segments, phi = 2 * Math.PI * j / (segments * 2);
            return new Point3(15 + (radius * Math.Sin(theta) * Math.Cos(phi)), 15 + (radius * Math.Sin(theta) * Math.Sin(phi)), radius * Math.Cos(theta));
        }

        Point3 center = new(15, 15, 0);
        for (int i = 0; i < segments; i++)
        {
            for (int j = 0; j < segments * 2; j++)
            {
                Point3 a = P(i, j), b = P(i + 1, j), c = P(i + 1, j + 1), d = P(i, j + 1);
                if (i > 0)
                {
                    tris.Add((a, b, d));
                }

                tris.Add((b, c, d));
            }
        }

        // base (tampa em Z = 0)
        for (int j = 0; j < segments * 2; j++)
        {
            tris.Add((center, P(segments, j + 1), P(segments, j)));
        }

        return new TriangleMesh(tris);
    }

    private static double FeedLength(IEnumerable<ToolMove> moves, double z)
    {
        double total = 0;
        ToolMove? prev = null;
        foreach (ToolMove m in moves)
        {
            if (m.Kind == MoveKind.Feed && Math.Abs(m.Z - z) < 1e-6 && prev is { } p && Math.Abs(p.Z - z) < 1e-6)
            {
                total += Math.Abs(m.X - p.X);
            }

            prev = m;
        }

        return total;
    }

    [Fact]
    public void Cupula_corta_so_nos_niveis_e_nunca_abaixo_do_modelo_mais_a_sobra()
    {
        TriangleMesh dome = Dome();
        var levels = new CamLevels(40, 30, 15, 0, 5);
        var p = new RoughParams(3, AxialStock: 0.5, RadialStock: 0.5);
        OperationResult r = SurfaceRoughing.ByLevels(dome, Ball, levels, p);
        Assert.True(r.Ok, r.Error);

        // Só nos níveis 10, 5 e 0
        double[] allowed = [10, 5, 0];
        Assert.All(r.Operation!.Moves.Where(m => m.Kind == MoveKind.Feed), m => Assert.Contains(allowed, a => Math.Abs(a - m.Z) < 1e-6));

        // Nunca abaixo do modelo + sobra axial (esfera engrossada pela sobra radial)
        var cutter = new DropCutter(dome, Ball.Radius + p.RadialStock);
        double worst = 0;
        foreach (ToolMove m in r.Operation.Moves.Where(m => m.Kind == MoveKind.Feed))
        {
            double min = cutter.TipHeight(m.X, m.Y) is { } h ? Math.Max(h + p.AxialStock, levels.BottomZ) : levels.BottomZ;
            worst = Math.Max(worst, min - m.Z);
        }

        Assert.True(worst < 1e-6, $"corte abaixo do limite em {worst} mm");
    }

    [Fact]
    public void Niveis_mais_fundos_cortam_menos_na_cupula()
    {
        OperationResult r = SurfaceRoughing.ByLevels(Dome(), Ball, new CamLevels(40, 30, 15, 0, 5), new RoughParams(3, 0.3, 0.3));
        Assert.True(r.Ok, r.Error);
        double l10 = FeedLength(r.Operation!.Moves, 10), l5 = FeedLength(r.Operation.Moves, 5), l0 = FeedLength(r.Operation.Moves, 0);
        Assert.True(l10 > l5 && l5 > l0 && l0 > 0, $"{l10} / {l5} / {l0}");
        Assert.Contains(r.Operation.Warnings, w => w.Contains("3 níveis"));
    }

    [Fact]
    public void Mais_sobra_axial_corta_menos_no_fundo()
    {
        var levels = new CamLevels(40, 30, 15, 0, 5);
        double lenThin = FeedLength(SurfaceRoughing.ByLevels(Dome(), Ball, levels, new RoughParams(3, 0.0, 0.0)).Operation!.Moves, 5);
        double lenThick = FeedLength(SurfaceRoughing.ByLevels(Dome(), Ball, levels, new RoughParams(3, 2.0, 2.0)).Operation!.Moves, 5);
        Assert.True(lenThick < lenThin, $"{lenThick} deveria ser menor que {lenThin}");
    }

    [Fact]
    public void Bloco_que_ocupa_o_bruto_inteiro_nao_tem_o_que_remover()
    {
        OperationResult r = SurfaceRoughing.ByLevels(Box(20, 20, 10), Ball, new CamLevels(30, 25, 10, 0, 5), new RoughParams(3));
        Assert.False(r.Ok);
        Assert.Contains("Nenhuma parte", r.Error);
    }

    [Fact]
    public void Programa_roda_nos_dois_comandos()
    {
        OperationResult r = SurfaceRoughing.ByLevels(Dome(), Ball, new CamLevels(40, 30, 15, 0, 5), new RoughParams(3));
        Assert.True(r.Ok, r.Error);
        foreach (CamDialect dialect in new[] { CamDialect.Fanuc, CamDialect.Siemens })
        {
            string program = CamPost.Generate([r.Operation!], new PostOptions(dialect));
            Assert.Contains("DESBASTE", program);
            var analysis = ProgramAnalyzer.Analyze(program, dialect == CamDialect.Fanuc ? Dialects.Dialect.Fanuc : Dialects.Dialect.Siemens);
            Assert.True(analysis.ToolPath.Moves.Count > 10);
        }
    }

    [Fact]
    public void Valida_parametros()
    {
        TriangleMesh dome = Dome();
        var levels = new CamLevels(40, 30, 15, 0, 5);
        Assert.False(SurfaceRoughing.ByLevels(dome, Ball, levels, new RoughParams(0)).Ok);
        Assert.False(SurfaceRoughing.ByLevels(dome, Ball, levels, new RoughParams(7)).Ok); // maior que o diâmetro
        Assert.False(SurfaceRoughing.ByLevels(dome, Ball, levels, new RoughParams(3, AxialStock: -1)).Ok);
        Assert.False(SurfaceRoughing.ByLevels(dome, Ball, levels, new RoughParams(3, SampleStep: 0)).Ok);
        Assert.False(SurfaceRoughing.ByLevels(dome, Ball, new CamLevels(40, 30, 10, 0, 5), new RoughParams(3)).Ok); // topo do bruto abaixo do modelo
        Assert.False(SurfaceRoughing.ByLevels(dome, Ball, new CamLevels(40, 30, 15, 0, 0), new RoughParams(3)).Ok); // passo em Z zero
        Assert.False(SurfaceRoughing.ByLevels(new TriangleMesh([]), Ball, levels, new RoughParams(3)).Ok);
    }
}

public class SurfaceLevelsTests
{
    // Caixa de 20 x 20 x 10 com base em Z = 0 e topo em Z = 10
    private static TriangleMesh Box()
    {
        Point3 p000 = new(0, 0, 0), p100 = new(20, 0, 0), p110 = new(20, 20, 0), p010 = new(0, 20, 0);
        Point3 p001 = new(0, 0, 10), p101 = new(20, 0, 10), p111 = new(20, 20, 10), p011 = new(0, 20, 10);
        return new TriangleMesh(new (Point3, Point3, Point3)[]
        {
            (p000, p010, p110), (p000, p110, p100), (p001, p101, p111), (p001, p111, p011),
            (p000, p100, p101), (p000, p101, p001), (p010, p011, p111), (p010, p111, p110),
            (p000, p001, p011), (p000, p011, p010), (p100, p110, p111), (p100, p111, p101),
        });
    }

    [Fact]
    public void UsaAsAlturasDigitadasSemSomarNada()
    {
        (CamLevels? l, string? e) = SurfaceLevels.Build(Box(), new ModelZ(50, 14), 2, 1.5);
        Assert.Null(e);
        Assert.Equal(50, l!.SafeZ);
        Assert.Equal(14, l.ClearanceZ);
        Assert.Equal(10, l.TopZ);
        Assert.Equal(2, l.BottomZ);
        Assert.Equal(1.5, l.StepDown);
    }

    [Theory]
    [InlineData(50, 8)]   // aproximação abaixo do topo
    [InlineData(10, 14)]  // seguro abaixo da aproximação
    [InlineData(14, 14)]  // seguro igual à aproximação
    [InlineData(double.NaN, 14)]
    [InlineData(50, double.PositiveInfinity)]
    public void AlturasIncoerentesBloqueiam(double safe, double clearance)
    {
        (CamLevels? l, string? e) = SurfaceLevels.Build(Box(), new ModelZ(safe, clearance), 2, 1);
        Assert.Null(l);
        Assert.False(string.IsNullOrEmpty(e));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(10)]      // no topo
    [InlineData(12)]      // acima do topo
    [InlineData(-3)]      // abaixo da base do modelo
    public void FundoInvalidoBloqueia(double bottom)
    {
        (CamLevels? l, string? e) = SurfaceLevels.Build(Box(), new ModelZ(50, 14), bottom, 1);
        Assert.Null(l);
        Assert.False(string.IsNullOrEmpty(e));
    }

    [Fact]
    public void FundoNaBaseDoModeloServe()
    {
        (CamLevels? l, string? e) = SurfaceLevels.Build(Box(), new ModelZ(50, 14), 0, 1);
        Assert.Null(e);
        Assert.Equal(0, l!.BottomZ);
    }

    [Fact]
    public void PassoEmZZeroBloqueia()
    {
        (CamLevels? l, string? e) = SurfaceLevels.Build(Box(), new ModelZ(50, 14), 2, 0);
        Assert.Null(l);
        Assert.NotNull(e);
    }

    [Fact]
    public void DesbasteUsaOsZDoUsuarioNosMovimentos()
    {
        (CamLevels? l, _) = SurfaceLevels.Build(SurfaceRoughingTests.Dome(), new ModelZ(55, 17), 2, 3);
        OperationResult r = SurfaceRoughing.ByLevels(SurfaceRoughingTests.Dome(), new CamTool(1, 6, 8000, 1200, 400, "Bola D6"), l!, new RoughParams(3, 0.3, 0.3));
        Assert.True(r.Ok, r.Error);
        var zs = r.Operation!.Moves.Where(m => m.Kind == MoveKind.Rapid).Select(m => m.Z).ToList();
        Assert.Contains(55, zs);
        Assert.Contains(17, zs);
        Assert.DoesNotContain(35, zs);
        Assert.DoesNotContain(15, zs);
    }
}
