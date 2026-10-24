using CncStudio.Core.Optimizer;
using Xunit;
using static CncStudio.Core.Tests.Optimizer.Fixtures;

namespace CncStudio.Core.Tests.Optimizer;

public class Phase2Tests
{
    [Fact]
    public void InterpreterHasDegreesTrigAndRoundsLikeTheControl()
    {
        var metric = MacroInterpreter.Expand("#1 = 30\nX[10.0*COS[#1]] Y[10.0*SIN[#1]] F[1/3]");
        Assert.True(metric.Ok, metric.Error);
        Assert.Equal("X8.66 Y5.0 F0.333333", metric.Text[0]);   // F is not a dimension: not rounded to 0.001
        var inch = MacroInterpreter.Expand("G20\nX[1/3]");
        Assert.Equal("X0.3333", inch.Text[1]);
        Assert.Equal("X0.333", MacroInterpreter.Expand("X[1/3]").Text[0]);
    }

    [Fact]
    public void NonUniformStepsNeedThreeCopies()
    {
        // Two rectangles of different size have the same shape but are not a repetition.
        string[] Rect(double s) => new[] { $"G1 X{N(s)} F500.", $"Y{N(s)}", $"X{N(-s)}", $"Y{N(-s)}", "X0.", "Y0.", "G0 Z5.", "G1 Z-1. F100.", "X1.", "Y1." };
        var two = Run(new[] { "G21 G90 G17", "G0 X0. Y0. Z5.", "G1 Z-1. F100." }.Concat(Rect(10)).Concat(Rect(20)));
        Assert.Empty(two.Proposals);
        var three = Run(new[] { "G21 G90 G17", "G0 X0. Y0. Z5.", "G1 Z-1. F100." }.Concat(Rect(10)).Concat(Rect(20)).Concat(Rect(30)));
        Assert.Equal("PASSOS LINEARES", Assert.Single(three.Proposals).Kind);
    }

    [Fact]
    public void FeedChangesAreNeverFolded()
    {
        var lines = new[] { "G21 G90 G17", "G0 X0. Y0. Z5." }
            .Concat(Enumerable.Range(1, 8).SelectMany(k => new[] { $"G1 Z-{k}. F{100 + 10 * k}.", "X10.", "Y10.", "X0.", "Y0.", "G0 Z5." }));
        Assert.Empty(Run(lines).Proposals);
    }

    [Theory]
    [InlineData("R-6.", "fundo Z")]
    [InlineData("R-1.", "plano R <= 0")]
    public void UnsafeCannedCyclesAreBlocked(string r, string reason)
    {
        var lines = new[] { "G21 G90 G17", "G0 X0. Y0. Z20.", $"G99 G81 X0. Y0. Z-5. {r} F100." }
            .Concat(Enumerable.Range(1, 12).Select(k => $"X{N(10 * k)}"));
        var p = Assert.Single(Run(lines).Proposals);
        Assert.False(p.Accepted);
        Assert.Contains(p.Blockers, b => b.Contains(reason));
    }

    [Fact]
    public void CannedCycleLoopKeepsTheCycleOutsideAndVerifiesReturnLevel()
    {
        var lines = new[] { "G21 G90 G17", "G0 X0. Y0. Z20.", "G98 G81 X0. Y0. Z-5. R3. F100." }
            .Concat(Enumerable.Range(1, 12).Select(k => $"X{N(10 * k)}")).Append("G80");
        var r = Run(lines);
        var p = Assert.Single(r.Proposals);
        Assert.True(p.Accepted, string.Join(" | ", p.Blockers));
        Assert.Equal("X[10.0+#100*10.0]", p.Fragment!.Split('\n')[3]);
        Assert.DoesNotContain("G81", p.Fragment);
        Assert.NotNull(r.SimulationProgram);
    }

    [Fact]
    public void BoltCircleNeedsMetricUnits()
    {
        string Hole(int k) => $"X{N(40 * Math.Cos(k * Math.PI / 6))} Y{N(40 * Math.Sin(k * Math.PI / 6))}";
        var holes = Enumerable.Range(1, 11).Select(Hole);
        var metric = Run(new[] { "G21 G90 G17", "G0 X40. Y0. Z20.", "G98 G81 X40. Y0. Z-5. R3. F100." }.Concat(holes));
        Assert.True(Assert.Single(metric.Proposals).Accepted);
        var noUnits = Run(new[] { "G90 G17", "G0 X40. Y0. Z20.", "G98 G81 X40. Y0. Z-5. R3. F100." }.Concat(holes));
        Assert.Contains("G21", Assert.Single(noUnits.Proposals).Blockers[0]);
    }

    [Fact]
    public void TamperedGridFailsIndependentCheck()
    {
        var lines = File.ReadAllLines("otimizador-fixtures/G-bolsoes-concentricos.nc");
        var p = Run(lines).Proposals[0];
        var blocks = NcTokenizer.Tokenize(lines);
        var source = blocks.Where(b => b.Line >= p.StartLine && b.Line <= p.EndLine).ToArray();
        var entry = NcMachine.Run(Calc, NcState.Unknown, blocks.Where(b => b.Line < p.StartLine))[^1].After;
        Assert.True(EquivalenceChecker.Check(Calc, entry, source, p.Fragment!).Match);
        foreach (var (from, to) in new[] { ("LT 5] DO2", "LT 6] DO2"), ("#101*-4.0]", "#101*-4.1]"), ("#101 = #101 + 1", "#101 = #101 + 2"), ("DO2", "DO1") })
            Assert.False(EquivalenceChecker.Check(Calc, entry, source, p.Fragment!.Replace(from, to)).Match, from);
    }
}
