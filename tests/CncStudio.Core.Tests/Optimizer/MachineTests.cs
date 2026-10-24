using CncStudio.Core.Optimizer;
using Xunit;
using static CncStudio.Core.Tests.Optimizer.Fixtures;

namespace CncStudio.Core.Tests.Optimizer;

public class MachineTests
{
    private static NcState After(NcProfile p, params string[] lines) =>
        NcMachine.Run(p, NcState.Unknown, NcTokenizer.Tokenize(lines))[^1].After;

    [Fact]
    public void DecimalInputFollowsProfile()
    {
        Assert.Equal(-1, After(Calc, "G21 G90 G01 Z-1 F100").Z.Value, 9);
        Assert.Equal(-0.001, After(Conv, "G21 G90 G01 Z-1 F100").Z.Value, 9);
        Assert.Equal(-1, After(Conv, "G21 G90 G01 Z-1. F100").Z.Value, 9);
        Assert.Equal(-25.4, After(Calc, "G20 G90 G01 Z-1 F10").Z.Value, 9);
    }

    [Fact]
    public void IncrementalFromUnknownEntryStaysComparable()
    {
        var s = After(Calc, "G91 G01 X10. F100", "Y-5.");
        Assert.Equal(PosKind.RelativeToEntry, s.X.Kind);
        Assert.Equal(10, s.X.Value, 9);
        Assert.Equal(-5, s.Y.Value, 9);
    }

    [Fact]
    public void RealPreambleDoesNotPoisonTheState()
    {
        var s = After(Calc, Preamble);
        Assert.False(s.Tainted);
        Assert.Equal(1, s.Tool);
        Assert.Equal("G54", s.Wcs);
        Assert.Equal(43, s.ToolLength);
        Assert.Equal(1, s.H);
        Assert.Equal(3, s.SpindleDirection);
        Assert.True(s.Z.IsAbsolute);
        Assert.Equal(5, s.Z.Value, 9);
    }

    [Fact]
    public void ToolIsActiveOnlyAfterM6()
    {
        var s = After(Calc, "T1 M6", "T2");
        Assert.Equal(1, s.Tool);
        Assert.Equal(2, s.NextTool);
    }

    [Theory]
    [InlineData("G68 X0. Y0. R45.")]
    [InlineData("G91 G81 X1. Y1. Z-5. R2. F100.")]
    [InlineData("M98 P1000")]
    [InlineData("#1=2")]
    public void UnmodeledCommandsLosePositions(string line)
    {
        var s = After(Calc, "G21 G90 G00 X0. Y0. Z5.", line);
        Assert.True(s.Tainted);
        Assert.Equal(PosKind.Unknown, s.Z.Kind);
    }

    [Fact]
    public void CannedCyclesAreModeledInG90()
    {
        var steps = NcMachine.Run(Calc, NcState.Unknown, NcTokenizer.Tokenize(new[]
        {
            "G21 G90 G17 G00 X0. Y0. Z20.", "G98 G83 X1. Y1. Z-5. R2. Q1.5 F100.", "X5.", "G99 X6. Z-6.", "G80", "G00 X0. Y0."
        }));
        Assert.Equal(83, steps[1].Motion);
        Assert.Equal((1.0, 20.0), (steps[1].After.X.Value, steps[1].After.Z.Value)); // G98: back to the initial level
        Assert.Equal(new CannedCycle(83, 2, -5, 1.5, double.NaN, new Pos(20, PosKind.Absolute)), steps[1].After.Canned);
        Assert.Equal(83, steps[2].Motion);
        Assert.Equal(5, steps[2].After.X.Value);
        Assert.Equal(2, steps[3].After.Z.Value);                                      // G99: back to R
        Assert.Equal(-6, steps[3].After.Canned!.Z);
        Assert.Null(steps[4].After.Canned);
        Assert.Equal(0, steps[5].Motion);
        Assert.All(steps, s => Assert.False(s.After.Tainted));
    }

    [Fact]
    public void CannedCycleWithoutReturnModeHasUnknownZ()
    {
        var s = After(Calc, "G21 G90 G17 G00 X0. Y0. Z20.", "G81 X1. Y1. Z-5. R2. F100.");
        Assert.Equal(PosKind.Unknown, s.Z.Kind);
        Assert.True(s.X.IsAbsolute);
    }
}
