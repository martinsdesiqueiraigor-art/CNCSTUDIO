using CncStudio.Core.Cam;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

public class LatheCamTests
{
    private static readonly LatheTool Tool = new(1, "DESBASTE CNMG");
    private static readonly MachineSettings LatheSettings = new() { Machine = MachineKind.Lathe };

    private static LatheProfile Shaft()
    {
        // (Z, raio): chanfro, cilindro Ø20, ombro, cilindro Ø28, filete convexo, cilindro Ø34
        var chain = new List<Segment2>
        {
            Segment2.Line(new Point2(0, 8), new Point2(-2, 10)),
            Segment2.Line(new Point2(-2, 10), new Point2(-25, 10)),
            Segment2.Line(new Point2(-25, 10), new Point2(-25, 14)),
            Segment2.Line(new Point2(-25, 14), new Point2(-35, 14)),
            Segment2.Arc(new Point2(-35, 14), new Point2(-38, 17), new Point2(-35, 17), ccw: false),
            Segment2.Line(new Point2(-38, 17), new Point2(-45, 17)),
        };
        return LatheProfile.From(chain, out _)!;
    }

    private static LatheProfile Bore()
    {
        var chain = new List<Segment2>
        {
            Segment2.Line(new Point2(0, 10), new Point2(-10, 10)),
            Segment2.Line(new Point2(-10, 10), new Point2(-10, 7)),
            Segment2.Line(new Point2(-10, 7), new Point2(-20, 7)),
        };
        return LatheProfile.From(chain, out _)!;
    }

    private static ToolPath Simulate(string program, CamDialect dialect) =>
        MachineExecutor.Execute(GCodeParser.Parse(program, dialect == CamDialect.Fanuc ? Dialect.Fanuc : Dialect.Siemens), LatheSettings);

    private static void AssertNoErrors(ToolPath path, string program) =>
        Assert.True(path.Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error),
            string.Join("\n", path.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)) + "\n" + program);

    private static List<Point2> ProfilePoints(LatheProfile p) => p.Segments.SelectMany(s => s.Sample(0.01)).ToList();

    /// <summary>Raio do perfil em Z (no ombro vertical vale o menor, ou o maior se pedido).</summary>
    private static double RadiusAt(List<Point2> pts, double z, bool max)
    {
        var candidates = new List<double>();
        for (int i = 1; i < pts.Count; i++)
        {
            Point2 a = pts[i - 1], b = pts[i];
            double lo = Math.Min(a.X, b.X), hi = Math.Max(a.X, b.X);
            if (z < lo - 1e-6 || z > hi + 1e-6)
            {
                continue;
            }

            candidates.Add(hi - lo < 1e-9 ? (max ? Math.Max(a.Y, b.Y) : Math.Min(a.Y, b.Y)) : a.Y + ((b.Y - a.Y) * (z - a.X) / (b.X - a.X)));
        }

        return candidates.Count == 0 ? double.NaN : (max ? candidates.Max() : candidates.Min());
    }

    private static double DistToPolyline(Point2 p, List<Point2> line)
    {
        double best = double.MaxValue;
        for (int i = 1; i < line.Count; i++)
        {
            Point2 d = line[i] - line[i - 1];
            double len2 = d.Dot(d);
            double t = len2 < 1e-18 ? 0 : Math.Clamp((p - line[i - 1]).Dot(d) / len2, 0, 1);
            best = Math.Min(best, p.DistanceTo(line[i - 1] + (d * t)));
        }

        return best;
    }

    /// <summary>Pontos (Z, raio) de todo movimento de corte simulado.</summary>
    private static List<List<Point2>> CutLines(ToolPath path)
    {
        var lines = new List<List<Point2>>();
        foreach (Move m in path.Moves.Where(m => !m.IsRapid))
        {
            var pts = new List<Geometry.Point3> { m.Start };
            m.AppendPolyline(pts, 0.005);
            pts.Add(m.End);

            lines.Add(pts.Select(p => new Point2(p.Z, p.X)).ToList());
        }

        return lines;
    }

    [Theory]
    [InlineData(CamDialect.Fanuc)]
    [InlineData(CamDialect.Siemens)]
    public void TorneamentoExternoNaoEntraNoPerfilEAcabaNele(CamDialect dialect)
    {
        LatheProfile profile = Shaft();
        LatheOperation op = LatheOperations.Turning(profile, Tool, safeZ: 50, safeR: 40, new TurningParams(StockDiameter: 40)).Operation!;
        string program = LathePost.Generate([op], new LathePostOptions(dialect));
        ToolPath path = Simulate(program, dialect);
        AssertNoErrors(path, program);

        List<Point2> pts = ProfilePoints(profile);
        List<List<Point2>> cuts = CutLines(path);
        Assert.NotEmpty(cuts);
        foreach (Point2 p in cuts.SelectMany(l => l))
        {
            Assert.True(p.X >= -45 - 1e-3, $"cortou além do fim do perfil: Z{p.X}");
            double r = RadiusAt(pts, p.X, max: false);
            if (!double.IsNaN(r) && p.Y < r - 0.02)
            {
                Assert.True(DistToPolyline(p, pts) < 0.03, $"entrou no perfil em Z{p.X:0.###}: raio {p.Y:0.###} < {r:0.###}");
            }
        }

        // acabamento: todo ponto do perfil tem corte a menos de 0,05 mm
        List<Point2> all = cuts.SelectMany(l => l).ToList();
        foreach (Point2 p in pts.Where((_, i) => i % 5 == 0))
        {
            Assert.True(cuts.Min(l => DistToPolyline(p, l)) < 0.05, $"perfil não usinado perto de Z{p.X:0.###} R{p.Y:0.###}");
        }

        Assert.Equal(8, all.Min(p => p.Y), 2); // chega no raio 8 (chanfro)
    }

    [Theory]
    [InlineData(CamDialect.Fanuc)]
    [InlineData(CamDialect.Siemens)]
    public void TorneamentoInternoFicaDentroDoFuro(CamDialect dialect)
    {
        LatheProfile profile = Bore();
        LatheOperation op = LatheOperations.Turning(profile, Tool, 50, 40, new TurningParams(StockDiameter: 10, Internal: true, DepthOfCut: 1)).Operation!;
        string program = LathePost.Generate([op], new LathePostOptions(dialect));
        ToolPath path = Simulate(program, dialect);
        AssertNoErrors(path, program);

        List<Point2> pts = ProfilePoints(profile);
        foreach (Point2 p in CutLines(path).SelectMany(l => l))
        {
            Assert.True(p.X >= -20 - 1e-3);
            double r = RadiusAt(pts, p.X, max: true);
            if (!double.IsNaN(r) && p.Y > r + 0.02)
            {
                Assert.True(DistToPolyline(p, pts) < 0.03, $"cortou o material em Z{p.X:0.###}: raio {p.Y:0.###} > {r:0.###}");
            }
        }

        if (dialect == CamDialect.Fanuc)
        {
            Assert.Contains("U-", program); // sobremetal interno negativo
        }
    }

    [Fact]
    public void SoDesbasteNaoFazAcabamento()
    {
        LatheOperation op = LatheOperations.Turning(Shaft(), Tool, 50, 40, new TurningParams(40, Finish: false)).Operation!;
        string fanuc = LathePost.Generate([op], new LathePostOptions(CamDialect.Fanuc));
        Assert.DoesNotContain("G70", fanuc);
        string siemens = LathePost.Generate([op], new LathePostOptions(CamDialect.Siemens));
        Assert.Contains(",1,,,", siemens.Replace(" ", string.Empty).Replace("0.5)", ")"), StringComparison.Ordinal);
        AssertNoErrors(Simulate(fanuc, CamDialect.Fanuc), fanuc);
        AssertNoErrors(Simulate(siemens, CamDialect.Siemens), siemens);
    }

    [Theory]
    [InlineData(CamDialect.Fanuc)]
    [InlineData(CamDialect.Siemens)]
    public void SoAcabamentoSegueOPerfilSemDesbaste(CamDialect dialect)
    {
        LatheOperation op = LatheOperations.Turning(Shaft(), Tool, 50, 40, new TurningParams(40, Rough: false)).Operation!;
        string program = LathePost.Generate([op], new LathePostOptions(dialect));
        Assert.DoesNotContain("G71 U", program);
        Assert.DoesNotContain("CYCLE95", program);
        ToolPath path = Simulate(program, dialect);
        AssertNoErrors(path, program);
        List<Point2> pts = ProfilePoints(Shaft());
        foreach (Point2 p in pts.Where((_, i) => i % 5 == 0))
        {
            Assert.True(CutLines(path).Min(l => DistToPolyline(p, l)) < 0.05, $"perfil não usinado perto de Z{p.X:0.###} R{p.Y:0.###}\n{program}");
        }
    }

    [Theory]
    [InlineData(CamDialect.Fanuc)]
    [InlineData(CamDialect.Siemens)]
    public void FaceamentoChegaNaFaceEOCentro(CamDialect dialect)
    {
        LatheOperation op = LatheOperations.Facing(Tool, 50, 40, stockDiameter: 40, stockFaceZ: 2, finalFaceZ: 0, depthOfCut: 1, feed: 0.15).Operation!;
        string program = LathePost.Generate([op], new LathePostOptions(dialect));
        ToolPath path = Simulate(program, dialect);
        AssertNoErrors(path, program);
        List<Point2> cut = CutLines(path).SelectMany(l => l).ToList();
        Assert.Equal(0, cut.Min(p => p.X), 3); // face pronta
        Assert.Equal(0, cut.Min(p => p.Y), 3); // até o centro
        Assert.True(cut.Max(p => p.X) <= 2 + 1e-3);
    }

    [Theory]
    [InlineData(CamDialect.Fanuc)]
    [InlineData(CamDialect.Siemens)]
    public void FuracaoAxialChegaNoFundo(CamDialect dialect)
    {
        var drill = new LatheTool(2, "BROCA 10", Rpm: 1200);
        LatheOperation op = LatheOperations.Drilling(drill, 50, 40, topZ: 0, bottomZ: -25, peck: 8, feed: 0.12).Operation!;
        string program = LathePost.Generate([op], new LathePostOptions(dialect));
        Assert.Contains("G97 S1200", program);
        ToolPath path = Simulate(program, dialect);
        AssertNoErrors(path, program);
        List<Point2> cut = CutLines(path).SelectMany(l => l).ToList();
        Assert.Equal(-25, cut.Min(p => p.X), 3);
        Assert.All(cut, p => Assert.Equal(0, p.Y, 3));
    }

    [Fact]
    public void ProgramaComVariasOperacoesTrocaFerramenta()
    {
        LatheOperation face = LatheOperations.Facing(Tool, 50, 40, 40, 2, 0, 1, 0.15).Operation!;
        LatheOperation turn = LatheOperations.Turning(Shaft(), Tool, 50, 40, new TurningParams(40)).Operation!;
        LatheOperation drill = LatheOperations.Drilling(new LatheTool(2, "BROCA", Rpm: 1000), 50, 40, 0, -20, 6, 0.1).Operation!;
        foreach (CamDialect dialect in new[] { CamDialect.Fanuc, CamDialect.Siemens })
        {
            string program = LathePost.Generate([face, turn, drill], new LathePostOptions(dialect));
            AssertNoErrors(Simulate(program, dialect), program);
            Assert.Equal(2, program.Split('\n').Count(l => l.StartsWith("T", StringComparison.Ordinal))); // 2 trocas; faceamento e torneamento usam T1
        }
    }

    private static LatheProfile Grooved()
    {
        // chanfro, Ø20, canal Ø14 entre Z-10 e Z-20 (rebaixo), Ø20, ombro para Ø28
        var chain = new List<Segment2>
        {
            Segment2.Line(new Point2(0, 8), new Point2(-2, 10)),
            Segment2.Line(new Point2(-2, 10), new Point2(-10, 10)),
            Segment2.Line(new Point2(-10, 10), new Point2(-10, 7)),
            Segment2.Line(new Point2(-10, 7), new Point2(-20, 7)),
            Segment2.Line(new Point2(-20, 7), new Point2(-20, 10)),
            Segment2.Line(new Point2(-20, 10), new Point2(-30, 10)),
            Segment2.Line(new Point2(-30, 10), new Point2(-30, 14)),
            Segment2.Line(new Point2(-30, 14), new Point2(-40, 14)),
        };
        return LatheProfile.From(chain, out _)!;
    }

    [Theory]
    [InlineData(CamDialect.Fanuc)]
    [InlineData(CamDialect.Siemens)]
    public void PerfilComRebaixoMergulhaNoCanalSemInvadirAPeca(CamDialect dialect)
    {
        LatheResult r = LatheOperations.Turning(Grooved(), Tool, 50, 40, new TurningParams(30));
        Assert.True(r.Ok, r.Error);
        Assert.Contains(r.Operation!.Warnings, w => w.Contains("rebaixo"));
        string text = LathePost.Generate([r.Operation!], new LathePostOptions(dialect));
        ToolPath path = Simulate(text, dialect);
        AssertNoErrors(path, text);
        List<Point2> profile = ProfilePoints(Grooved());

        // nenhum ponto do caminho (corte ou rápido) fica dentro da peça
        foreach (Move m in path.Moves.Skip(1)) // o 1º movimento sai da origem da máquina (fora do programa)
        {
            for (int k = 0; k <= 20; k++)
            {
                var pt = m.PointAt(k / 20.0);
                if (pt.Z < -1e-6 && pt.Z >= -40)
                {
                    Assert.True(pt.X >= RadiusAt(profile, pt.Z, max: false) - 1e-6, $"X{pt.X:0.###} Z{pt.Z:0.###} dentro da peça (linha {m.LineNumber} {(m.IsRapid ? "rápido" : "corte")} {m.Start.X:0.###},{m.Start.Z:0.###} -> {m.End.X:0.###},{m.End.Z:0.###})\n{text}");
                }
            }
        }

        // desbasta dentro do canal e o acabamento chega ao fundo (raio 7)
        var cuts = path.Moves.Where(m => !m.IsRapid).ToList();
        var inGroove = cuts.Where(m => Math.Max(m.Start.Z, m.End.Z) <= -10 + 1e-6 && Math.Min(m.Start.Z, m.End.Z) >= -20 - 1e-6).ToList();
        Assert.NotEmpty(inGroove);
        Assert.Equal(7, inGroove.Min(m => Math.Min(m.Start.X, m.End.X)), 3);
        Assert.Contains(inGroove, m => m.Start.X < 8 && m.End.X < 8 && Math.Abs(m.Start.Z - m.End.Z) > 5); // desbaste ao longo do fundo
    }

    [Fact]
    public void PostFanucDoRebaixoUsaPrimeiroBlocoComXeZ()
    {
        LatheResult r = LatheOperations.Turning(Grooved(), Tool, 50, 40, new TurningParams(30));
        string text = LathePost.Generate([r.Operation!], new LathePostOptions(CamDialect.Fanuc));
        Assert.Matches(@"N\d+ G00 X16\. Z0\.", text);
    }

    [Fact]
    public void PerfilQueVoltaEmZContinuaRecusado()
    {
        var chain = new List<Segment2>
        {
            Segment2.Line(new Point2(0, 10), new Point2(-10, 10)),
            Segment2.Line(new Point2(-10, 10), new Point2(-5, 12)),
        };
        // a cadeia é orientada (Z diminui no total) mas volta no meio
        chain.Add(Segment2.Line(new Point2(-5, 12), new Point2(-30, 12)));
        LatheResult r = LatheOperations.Turning(LatheProfile.From(chain, out _)!, Tool, 50, 40, new TurningParams(30));
        Assert.False(r.Ok);
    }

    [Fact]
    public void BrutoMenorQueOPerfilEPrefuroMaiorSaoRecusados()
    {
        Assert.False(LatheOperations.Turning(Shaft(), Tool, 50, 40, new TurningParams(StockDiameter: 30)).Ok);
        Assert.False(LatheOperations.Turning(Bore(), Tool, 50, 40, new TurningParams(StockDiameter: 16, Internal: true)).Ok);
    }

    [Fact]
    public void ParametrosInvalidosSaoRecusados()
    {
        Assert.False(LatheOperations.Turning(Shaft(), Tool, 50, 40, new TurningParams(40, Rough: false, Finish: false)).Ok);
        Assert.False(LatheOperations.Turning(Shaft(), Tool, 50, 40, new TurningParams(40, DepthOfCut: 0)).Ok);
        Assert.False(LatheOperations.Turning(Shaft(), new LatheTool(0), 50, 40, new TurningParams(40)).Ok);
        Assert.False(LatheOperations.Facing(Tool, 50, 40, 40, 0, 1, 1, 0.1).Ok);
        Assert.False(LatheOperations.Drilling(Tool, 50, 40, 0, 5, 3, 0.1).Ok);
    }

    [Fact]
    public void PerfilEmOrdemInvertidaEOrientado()
    {
        var chain = Shaft().Segments.Reverse().Select(s => s.IsArc ? Segment2.Arc(s.End, s.Start, s.Center, !s.Ccw) : Segment2.Line(s.End, s.Start)).ToList();
        LatheProfile? p = LatheProfile.From(chain, out string? error);
        Assert.Null(error);
        Assert.Equal(0, p!.Start.X, 6);
        Assert.Equal(-45, p.End.X, 6);
    }

    [Fact]
    public void PerfilInterrompidoErro()
    {
        var chain = new List<Segment2> { Segment2.Line(new Point2(0, 5), new Point2(-5, 5)), Segment2.Line(new Point2(-8, 5), new Point2(-10, 5)) };
        Assert.Null(LatheProfile.From(chain, out string? error));
        Assert.NotNull(error);
    }
}
