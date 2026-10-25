using CncStudio.Core.Dialects;
using CncStudio.Core.Generators;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>Furação em grade, faceamento, rasgo, bolsão circular, furo helicoidal e rosca helicoidal.</summary>
public class MoreGeneratorsTests
{
    public static IEnumerable<object[]> Ids() => Generators.Generators.All.Where(g => g.SupportsMacro).Select(g => new object[] { g.Id });

    public static IEnumerable<object[]> AllIds() => Generators.Generators.All.Select(g => new object[] { g.Id });

    private static IReadOnlyDictionary<string, double> With(ProgramGenerator g, params (string Id, double Value)[] changes)
    {
        var values = g.Defaults.ToDictionary(p => p.Key, p => p.Value);
        foreach (var (id, value) in changes)
        {
            values[id] = value;
        }

        return values;
    }

    private static List<Move> Run(ProgramGenerator g, IReadOnlyDictionary<string, double> values, GeneratorOutput output)
    {
        GeneratorResult result = g.Generate(values, output);
        Assert.True(result.Ok, string.Join("; ", result.Errors));
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(result.Program, Dialect.Fanuc);
        Assert.DoesNotContain(analysis.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return [.. analysis.ToolPath.Moves];
    }

    private static List<Move> Cuts(ProgramGenerator g, IReadOnlyDictionary<string, double> values, GeneratorOutput output = GeneratorOutput.Plain) =>
        [.. Run(g, values, output).Where(m => !m.IsRapid)];

    [Theory]
    [MemberData(nameof(Ids))]
    public void PlainAndMacroMakeTheSameCuttingMoves(string id)
    {
        ProgramGenerator g = Generators.Generators.ById(id)!;
        List<Move> plain = Cuts(g, g.Defaults), macro = Cuts(g, g.Defaults, GeneratorOutput.MacroCall);
        Assert.NotEmpty(plain);
        Assert.Equal(plain.Count, macro.Count);
        for (int i = 0; i < plain.Count; i++)
        {
            Assert.InRange(Math.Abs(plain[i].End.X - macro[i].End.X), 0, 0.01);
            Assert.InRange(Math.Abs(plain[i].End.Y - macro[i].End.Y), 0, 0.01);
            Assert.InRange(Math.Abs(plain[i].End.Z - macro[i].End.Z), 0, 0.01);
        }
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void InitialZ_PlainAndMacroMatch_AndNothingCutsAboveStock(string id)
    {
        ProgramGenerator g = Generators.Generators.ById(id)!;
        var values = With(g, ("W", 3));
        List<Move> plain = Cuts(g, values), macro = Cuts(g, values, GeneratorOutput.MacroCall);
        Assert.NotEmpty(plain);
        Assert.Equal(plain.Count, macro.Count);
        for (int i = 0; i < plain.Count; i++)
        {
            Assert.InRange(Math.Abs(plain[i].End.X - macro[i].End.X), 0, 0.01);
            Assert.InRange(Math.Abs(plain[i].End.Y - macro[i].End.Y), 0, 0.01);
            Assert.InRange(Math.Abs(plain[i].End.Z - macro[i].End.Z), 0, 0.01);
        }

        // o ciclo/avanço nunca começa acima do topo do bruto
        Assert.All(plain, m => Assert.True(m.End.Z <= 3 + 0.01 || m.IsRapid));
    }

    [Theory]
    [MemberData(nameof(Ids))]
    public void InitialZ_ApproachClearsTheStock(string id)
    {
        // a rosca entra pelo furo (ou por fora da peça) já na cota de início da hélice; os demais descem do topo
        if (id == "threadmill")
        {
            return;
        }

        ProgramGenerator g = Generators.Generators.ById(id)!;
        var values = With(g, ("W", 20));
        foreach (GeneratorOutput o in new[] { GeneratorOutput.Plain, GeneratorOutput.MacroCall })
        {
            List<Move> moves = Run(g, values, o);
            // a primeira descida em avanço parte de uma altura livre (>= topo)
            Move firstCut = moves.First(m => !m.IsRapid);
            Assert.True(firstCut.Start.Z >= 20 - 0.01, $"{id}/{o}: início do corte em Z{firstCut.Start.Z}");
        }
    }

    [Fact]
    public void InitialZ_MissingInSavedValuesFallsBackToZero()
    {
        ProgramGenerator g = Generators.Generators.ById("pocket")!;
        var values = g.Defaults.Where(p => p.Key != "W").ToDictionary(p => p.Key, p => p.Value);
        Assert.True(g.Generate(values, GeneratorOutput.Plain).Ok);
    }

    [Fact]
    public void InitialZ_DepthMustBeBelowIt()
    {
        ProgramGenerator g = Generators.Generators.ById("pocket")!;
        Assert.False(g.Generate(With(g, ("W", -5), ("Z", -3)), GeneratorOutput.Plain).Ok);
    }

    [Fact]
    public void InitialZ_PocketLayersStartAtStockTop()
    {
        ProgramGenerator g = Generators.Generators.ById("pocket")!;
        var zs = Cuts(g, With(g, ("W", 3), ("Z", -2), ("Q", 1))).Select(m => Math.Round(m.End.Z, 3)).Distinct().OrderByDescending(z => z).ToList();
        Assert.Equal([2.0, 1.0, 0.0, -1.0, -2.0], zs);
    }

    [Theory]
    [MemberData(nameof(AllIds))]
    public void EveryDefaultProgramReachesTheRequestedDepth(string id)
    {
        ProgramGenerator g = Generators.Generators.All.Single(x => x.Id == id);
        double depth = g.Defaults["Z"];
        double bottom = Run(g, g.Defaults, GeneratorOutput.Plain).Min(m => m.End.Z);
        // a rosca helicoidal desce até o fim do comprimento (a hélice pode terminar acima ou no fundo, nunca abaixo)
        Assert.InRange(bottom, depth - 1e-6, depth + 0.01);
    }

    [Fact]
    public void GridHasColumnsTimesRowsHolesInSerpentineOrder()
    {
        var g = new GridGenerator();
        var holes = Cuts(g, With(g, ("X", 10), ("Y", 5), ("I", 4), ("J", 3)))
            .Where(m => m.End.Z < -9.9).Select(m => (Math.Round(m.End.X, 2), Math.Round(m.End.Y, 2))).ToList();
        Assert.Equal(12, holes.Count);
        Assert.Equal((10.0, 5.0), holes[0]);
        Assert.Equal((70.0, 5.0), holes[3]);      // 4 colunas, 20 mm
        Assert.Equal((70.0, 20.0), holes[4]);     // linha 2 começa pelo fim (serpentina)
        Assert.Equal((70.0, 35.0), holes[11]);    // última linha (par) volta a começar na coluna 0
    }

    [Fact]
    public void GridRotatesAroundTheFirstHole()
    {
        var g = new GridGenerator();
        var holes = Cuts(g, With(g, ("X", 0), ("Y", 0), ("I", 3), ("J", 1), ("A", 90)))
            .Where(m => m.End.Z < -9.9).Select(m => (Math.Round(m.End.X, 2), Math.Round(m.End.Y, 2))).ToList();
        Assert.Equal([(0.0, 0.0), (0.0, 20.0), (0.0, 40.0)], holes);
    }

    [Fact]
    public void FacingCoversTheWholeRectangleAndLeavesThePartOnEachPass()
    {
        var g = new FacingGenerator();
        var v = With(g, ("X", 50), ("Y", 30), ("I", 100), ("J", 60), ("D", 40), ("E", 70), ("Z", -1), ("Q", 0.5));
        List<Move> cuts = Cuts(g, v);
        Assert.Equal(-1, cuts.Min(m => m.End.Z), 6);
        // centro da fresa: de Y−J/2+D/2 a Y+J/2−D/2 (a borda da fresa encosta no limite)
        Assert.Equal(20, cuts.Min(m => m.End.Y), 6);
        Assert.Equal(40, cuts.Max(m => m.End.Y), 6);
        // entra e sai por fora: X vai de (X−I/2−D/2−2) a (X+I/2+D/2+2)
        Assert.Equal(-22, cuts.Min(m => m.End.X), 6);
        Assert.Equal(122, cuts.Max(m => m.End.X), 6);
        // faixas de 28 mm (70 % de 40) cobrem os 60 mm: sem vão entre linhas
        var ys = cuts.Select(m => Math.Round(m.End.Y, 3)).Distinct().Order().ToList();
        for (int i = 1; i < ys.Count; i++)
        {
            Assert.InRange(ys[i] - ys[i - 1], 0, 28 + 1e-6);
        }
    }

    [Fact]
    public void FacingNarrowerThanTheToolUsesOneCenteredRow()
    {
        var g = new FacingGenerator();
        List<Move> cuts = Cuts(g, With(g, ("Y", 7), ("J", 20), ("D", 50)));
        Assert.Equal([7.0], cuts.Select(m => Math.Round(m.End.Y, 3)).Distinct().ToArray());
    }

    [Fact]
    public void SlotWithToolAsWideAsTheSlotMakesOneLineAlongTheAxis()
    {
        var g = new SlotGenerator();
        List<Move> cuts = Cuts(g, With(g, ("X", 20), ("Y", 10), ("I", 40), ("J", 10), ("D", 10), ("Z", -2), ("Q", 1)));
        Assert.Equal([10.0], cuts.Where(m => m.End.Z < -0.5).Select(m => Math.Round(m.End.Y, 3)).Distinct().ToArray());
        Assert.Equal(5, cuts.Min(m => m.End.X), 6);   // 20 − (40 − 10)/2
        Assert.Equal(35, cuts.Max(m => m.End.X), 6);
    }

    [Fact]
    public void SlotAtNinetyDegreesRunsAlongY_AndWiderSlotAddsLines()
    {
        var g = new SlotGenerator();
        List<Move> cuts = Cuts(g, With(g, ("X", 0), ("Y", 0), ("I", 40), ("J", 16), ("D", 8), ("A", 90), ("Z", -1), ("Q", 1)));
        Assert.Equal(-16, cuts.Min(m => m.End.Y), 6);  // (40 − 8)/2
        Assert.Equal(16, cuts.Max(m => m.End.Y), 6);
        Assert.Equal(-4, cuts.Min(m => m.End.X), 6);   // (16 − 8)/2 de cada lado
        Assert.Equal(4, cuts.Max(m => m.End.X), 6);
        Assert.True(cuts.Select(m => Math.Round(m.End.X, 3)).Distinct().Count() >= 3);
    }

    [Fact]
    public void CircularPocketRingsStayInsideTheWallStock()
    {
        var g = new CircularPocketGenerator();
        var v = With(g, ("X", 30), ("Y", 20), ("B", 40), ("D", 10), ("U", 0.2), ("Z", -3), ("Q", 1));
        List<Move> cuts = Cuts(g, v);
        double rmax = (40 - 10) / 2.0 - 0.2;
        Assert.Equal(rmax, cuts.Max(m => Math.Sqrt(Math.Pow(m.End.X - 30, 2) + Math.Pow(m.End.Y - 20, 2))), 6);
        Assert.Equal(-3, cuts.Min(m => m.End.Z), 6);
        Assert.Contains(cuts, m => m.Motion == MotionMode.CounterClockwiseArc);   // anéis concordantes (G03)
    }

    [Fact]
    public void HelicalHoleDescendsInTurnsAtTheToolPathRadius()
    {
        var g = new HelicalHoleGenerator();
        var v = With(g, ("X", 5), ("Y", -5), ("B", 14), ("D", 10), ("Z", -4), ("Q", 1));
        List<Move> arcs = Cuts(g, v).Where(m => m.Motion == MotionMode.CounterClockwiseArc).ToList();
        Assert.Equal(5, arcs.Count);                                  // 4 voltas descendo + 1 volta no fundo
        Assert.All(arcs, m => Assert.Equal(2, Math.Sqrt(Math.Pow(m.End.X - 5, 2) + Math.Pow(m.End.Y + 5, 2)), 6));
        Assert.Equal([-1.0, -2.0, -3.0, -4.0, -4.0], arcs.Select(m => Math.Round(m.End.Z, 3)).ToArray());
    }

    [Theory]
    [InlineData(0, 0, "G03", true)]   // interna direita: sobe em G03
    [InlineData(0, 1, "G03", false)]  // interna esquerda: desce em G03
    [InlineData(1, 0, "G02", false)]  // externa direita: desce em G02
    [InlineData(1, 1, "G02", true)]   // externa esquerda: sobe em G02
    public void ThreadMillGoesTheRightWayForEachHandAndKind(double kind, double hand, string arc, bool up)
    {
        var g = new ThreadMillGenerator();
        var v = With(g, ("B", 16), ("D", 10), ("K", 2), ("Z", -15), ("H", kind), ("E", hand));
        GeneratorResult result = g.Generate(v, GeneratorOutput.Plain);
        Assert.True(result.Ok, string.Join("; ", result.Errors));
        Assert.Contains(arc + " ", result.Program);
        Assert.DoesNotContain(arc == "G02" ? "G03 " : "G02 ", result.Program);

        List<Move> arcs = Cuts(g, v).Where(m => m.Motion is MotionMode.ClockwiseArc or MotionMode.CounterClockwiseArc).ToList();
        Assert.Equal(8, arcs.Count);                                  // 15 / 2 → 8 voltas
        double radius = kind == 0 ? 3 : 13;
        Assert.All(arcs, m => Assert.Equal(radius, Math.Sqrt(m.End.X * m.End.X + m.End.Y * m.End.Y), 6));
        var zs = arcs.Select(m => m.End.Z).ToList();
        for (int i = 1; i < zs.Count; i++)
        {
            Assert.Equal(up ? 2 : -2, zs[i] - zs[i - 1], 6);          // um passo por volta
        }

        // a hélice cobre do fundo (Z) até o topo ou acima: sobe de Z, ou desce até Z começando em cima
        if (up)
        {
            Assert.Equal(-15, zs[0] - 2, 6);
            Assert.True(zs[^1] >= 0);
        }
        else
        {
            Assert.Equal(-15, zs[^1], 6);
            Assert.True(zs[0] + 2 >= 0);
        }
    }

    [Fact]
    public void InvalidValuesGiveMessages()
    {
        var circ = new CircularPocketGenerator();
        Assert.Contains(circ.Generate(With(circ, ("B", 10), ("D", 10)), GeneratorOutput.Plain).Errors, e => e.StartsWith("Diâmetro do bolsão"));
        var helix = new HelicalHoleGenerator();
        Assert.Contains(helix.Generate(With(helix, ("B", 40), ("D", 10)), GeneratorOutput.Plain).Errors, e => e.Contains("núcleo"));
        var thread = new ThreadMillGenerator();
        Assert.Contains(thread.Generate(With(thread, ("D", 15)), GeneratorOutput.Plain).Errors, e => e.StartsWith("Fresa"));
        var slot = new SlotGenerator();
        Assert.Contains(slot.Generate(With(slot, ("D", 12), ("J", 10)), GeneratorOutput.Plain).Errors, e => e.StartsWith("Fresa"));
        var grid = new GridGenerator();
        Assert.Contains(grid.Generate(With(grid, ("I", 1), ("J", 1)), GeneratorOutput.Plain).Errors, e => e.StartsWith("Grade"));
    }
}
