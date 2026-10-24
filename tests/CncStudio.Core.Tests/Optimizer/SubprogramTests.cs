using CncStudio.Core.Optimizer;
using Xunit;
using static CncStudio.Core.Tests.Optimizer.Fixtures;

namespace CncStudio.Core.Tests.Optimizer;

public class SubprogramTests
{
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<NcBlock>> Subs = new Dictionary<int, IReadOnlyList<NcBlock>>
    {
        [1001] = NcTokenizer.Tokenize(new[] { "G91 G1 X10. F100.", "G90", "M99" }),
        [1002] = NcTokenizer.Tokenize(new[] { "M98 P1001", "G91 G1 Y5.", "G90 M99" }),
        [1003] = NcTokenizer.Tokenize(new[] { "M98 P1003", "M99" })
    };

    private static IReadOnlyList<NcStep> Run(params string[] lines) =>
        NcMachine.Run(Calc, NcState.Unknown, NcTokenizer.Tokenize(new[] { "G21 G90 G17 G0 X0. Y0. Z5." }.Concat(lines)), Subs);

    [Theory]
    [InlineData("M98 P1001 L3")]
    [InlineData("M98 P31001")]
    public void RepeatCountByLOrByPrefix(string call)
    {
        var steps = Run(call);
        Assert.Equal(30, steps[^1].After.X.Value, 6);
        Assert.False(steps[^1].After.Tainted);
        Assert.Equal(1 + 1 + 3 * 2, steps.Count); // preamble, call, 3 x (move, G90)
    }

    [Fact]
    public void NestedCallsAndM99WithOtherWords()
    {
        var s = Run("M98 P1002")[^1].After;
        Assert.Equal((10.0, 5.0, 90), (s.X.Value, s.Y.Value, s.Distance));
        Assert.False(s.Tainted);
    }

    [Theory]
    [InlineData("M98 P1003")]   // recursion
    [InlineData("M98 P2000")]   // not in the file
    [InlineData("M99")]         // M99 in the main program
    public void UnresolvableCallsLosePositions(string call)
    {
        var s = Run(call)[^1].After;
        Assert.True(s.Tainted);
        Assert.Equal(PosKind.Unknown, s.X.Kind);
    }

    [Fact]
    public void LoopOverCallsToMissingSubprogramIsBlocked()
    {
        var lines = new[] { "G21 G90 G17", "G0 X0. Y0. Z25." }
            .Concat(Enumerable.Range(0, 8).SelectMany(k => new[] { $"G0 X{N(30 + 20 * k)} Y50.", "Z2.", "M98 P7777", "G0 Z25." }));
        var p = Assert.Single(Fixtures.Run(lines).Proposals);
        Assert.False(p.Accepted);
        Assert.Contains(p.Blockers, b => b.Contains("não modelado"));
    }

    [Fact]
    public void LoopOverSubprogramCallsIsVerifiedThroughTheSubprogram()
    {
        var lines = File.ReadAllLines("otimizador-fixtures/J-subprogramas-placa.nc");
        var r = Fixtures.Run(lines);
        var p = Assert.Single(r.Proposals);
        Assert.True(p.Accepted, string.Join(" | ", p.Blockers));
        Assert.Contains("M98 P1002", p.Fragment);
        Assert.NotNull(r.SimulationProgram);
        Assert.Contains("O1002 (FURO INTERPOLADO D4)", r.SimulationProgram);

        // Calling the other subprogram inside the loop changes the motion: the check must see it.
        var blocks = NcTokenizer.Tokenize(lines);
        var main = blocks.TakeWhile(b => b.Get('O') is null || b.Line < 3).ToArray();
        var source = blocks.Where(b => b.Line >= p.StartLine && b.Line <= p.EndLine).ToArray();
        var subs = new Dictionary<int, IReadOnlyList<NcBlock>>
        {
            [1001] = blocks.SkipWhile(b => b.Get('O')?.Value != 1001).Skip(1).TakeWhile(b => b.Get('O') is null).ToArray(),
            [1002] = blocks.SkipWhile(b => b.Get('O')?.Value != 1002).Skip(1).TakeWhile(b => b.Get('O') is null && !b.IsDelimiter).ToArray()
        };
        var entry = NcMachine.Run(Calc, NcState.Unknown, blocks.Where(b => b.Line < p.StartLine), subs)[^1].After;
        Assert.True(EquivalenceChecker.Check(Calc, entry, source, p.Fragment!, subs).Match);
        Assert.False(EquivalenceChecker.Check(Calc, entry, source, p.Fragment!.Replace("M98 P1002", "M98 P1001"), subs).Match);
    }
}
