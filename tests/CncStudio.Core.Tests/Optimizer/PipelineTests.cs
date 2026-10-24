using System.Diagnostics;
using CncStudio.Core.Optimizer;
using Xunit;
using static CncStudio.Core.Tests.Optimizer.Fixtures;

namespace CncStudio.Core.Tests.Optimizer;

public class PipelineTests
{
    private static Proposal Single(ConversionResult r) => Assert.Single(r.Proposals);

    [Fact]
    public void ZLevelsAfterRealCamPreambleAreConverted()
    {
        var r = Run(ZLevels(10));
        var p = Single(r);
        Assert.True(p.Accepted, string.Join(" | ", p.Blockers));
        Assert.Equal("NIVEIS Z", p.Kind);
        Assert.Equal((7, 10), (p.Period, p.Count));
        Assert.Contains("WHILE [#100 LT 10] DO1", p.Fragment);
        Assert.Contains("G90 G01 Z[-1.0+#100*-1.0] F100.", p.Fragment);
        Assert.DoesNotContain("#101", p.Fragment);
        Assert.Equal(70, p.Expanded.Count);
        Assert.True(r.SavedLines > 50);
        Assert.False(r.MachineReady);
        Assert.Equal("2D Contour1", r.Operations.Last().Label);
        Assert.Equal(1, r.Operations.Last().Tool);
    }

    [Fact]
    public void ResidualLastPassStaysLiteral()
    {
        var r = Run(ZLevels(5, tail: Pass(-5.5)));
        var p = Single(r);
        Assert.True(p.Accepted);
        Assert.Equal(5, p.Count);
        Assert.Contains("G90 G01 Z-5.5 F100.", r.Document);
    }

    [Fact]
    public void TranslatedCopiesUseXYOffsets()
    {
        var lines = Preamble.Concat(Enumerable.Range(0, 4).SelectMany(k => Pass(-2, ox: 40 * k, oy: 5 * k)));
        var p = Single(Run(lines));
        Assert.True(p.Accepted, string.Join(" | ", p.Blockers));
        Assert.Equal("TRANSLACAO XY", p.Kind);
        Assert.Contains("X[10.0+#100*40.0] F400.", p.Fragment);
        Assert.Contains("Y[10.0+#100*5.0]", p.Fragment);
        Assert.Contains("G90 G01 Z-2.0 F100.", p.Fragment);
    }

    [Fact]
    public void TranslatedCopiesWithArcsKeepIncrementalCenters()
    {
        string[] Hole(double x) => new[]
        {
            $"G00 X{N(x + 5)} Y0.", "G01 Z-2. F100.", $"G03 X{N(x + 5)} Y0. I-5. J0. F300.", "G00 Z5."
        };
        var p = Single(Run(Preamble.Concat(Enumerable.Range(0, 6).SelectMany(k => Hole(20 * k)))));
        Assert.True(p.Accepted, string.Join(" | ", p.Blockers));
        Assert.Contains("I-5. J0.", p.Fragment);
        Assert.Contains("X[5.0+#100*20.0]", p.Fragment);
    }

    [Fact]
    public void IncrementalIdenticalPassesFromFixture()
    {
        var p = Single(Run(File.ReadAllLines("otimizador-gerais/cam-g91-conversao-simulador-sintetico.nc")));
        Assert.True(p.Accepted);
        Assert.Equal("REPETICAO IDENTICA", p.Kind);
        Assert.DoesNotContain("#101", p.Fragment);
        Assert.DoesNotContain("[", p.Fragment!.Replace("WHILE [", ""));
    }

    [Fact]
    public void MixedFixtureKeepsHelixAndBlocksNothingSilently()
    {
        var lines = File.ReadAllLines("otimizador-gerais/cam-misto-g90-g91-helice-sintetico.nc");
        var r = Run(lines);
        Assert.Equal(3, r.Operations.Count);
        Assert.All(r.Proposals, p => Assert.True(p.EndLine < 42));
        Assert.Contains("G03 X10 Y0 I-10 J0 Z-3 F200", r.Document);
        Assert.StartsWith("GCODEOPTIMIZER", r.Document);
        Assert.Contains("NAO EXECUTAVEL", r.Document.Split('\n')[1]);
    }

    [Fact]
    public void CommentBetweenPassesSplitsRuns()
    {
        var lines = Preamble.Concat(Enumerable.Range(1, 5).SelectMany(k => Pass(-k)))
            .Append("(2D Contour2)").Concat(Enumerable.Range(1, 5).SelectMany(k => Pass(-k, ox: 100)));
        var r = Run(lines);
        Assert.Equal(2, r.Proposals.Count);
        Assert.All(r.Proposals, p => Assert.True(p.Accepted));
        Assert.NotEqual(r.Proposals[0].Operation, r.Proposals[1].Operation);
    }

    [Fact]
    public void SequenceNumbersAndCompactCodeAreAccepted()
    {
        int n = 100;
        var lines = Preamble.Concat(Enumerable.Range(1, 6).SelectMany(k => Pass(-k)).Select(l => "N" + (n += 10) + l.Replace(" ", "")));
        var p = Single(Run(lines));
        Assert.True(p.Accepted, string.Join(" | ", p.Blockers));
        Assert.DoesNotContain("N1", p.Fragment);
        Assert.Contains(p.Warnings, w => w.Contains("Numeros N"));
    }

    [Theory]
    [InlineData(-3, "altura de corte")]
    [InlineData(-0.5, "Z <= 0")]
    public void DangerousOriginalRapidsAreBlocked(double retract, string reason)
    {
        var lines = Preamble.Concat(Enumerable.Range(1, 6).SelectMany(k => Pass(-k, retract: retract)));
        var p = Single(Run(lines));
        Assert.False(p.Accepted);
        Assert.Contains(p.Blockers, b => b.Contains(reason));
        Assert.Null(Run(lines).Document.Split('\n').FirstOrDefault(l => l.StartsWith("WHILE")));
    }

    [Fact]
    public void ConventionalDecimalInputEmitsEffectiveValues()
    {
        var lines = new[] { "G21 G90 G17", "G00 X0 Y0 Z5000" }
            .Concat(Enumerable.Range(1, 6).SelectMany(k => new[] { $"G01 Z-{k}000 F100", "X10000", "G00 Z8000" }));
        var conv = Single(Run(lines, Conv));
        Assert.True(conv.Accepted, string.Join(" | ", conv.Blockers));
        Assert.Contains("Z[-1.0+#100*-1.0]", conv.Fragment);
        var calc = Single(Run(lines, Calc));
        Assert.Contains("Z[-1000.0+#100*-1000.0]", calc.Fragment);
    }

    [Fact]
    public void ConventionalWithoutUnitsIsBlocked()
    {
        var lines = new[] { "G90", "G00 X0 Y0 Z5000" }
            .Concat(Enumerable.Range(1, 6).SelectMany(k => new[] { $"G01 Z-{k}000 F100", "X10000", "G00 Z8000" }));
        var p = Single(Run(lines, Conv));
        Assert.False(p.Accepted);
        Assert.Contains(p.Blockers, b => b.Contains("G20/G21"));
    }

    [Fact]
    public void ToolChangeSpindleAndCommentsNeverEnterALoop()
    {
        var pass = new[] { "T1 M6", "S1000 M3", "G00 X0. Y0. Z5.", "G01 Z-1. F100.", "X10.", "G00 Z5." };
        var r = Run(new[] { "G21 G90 G54" }.Concat(Enumerable.Repeat(pass, 6).SelectMany(x => x)));
        Assert.All(r.Proposals, p => Assert.DoesNotContain("M6", p.Fragment ?? ""));
        Assert.All(r.Proposals, p => Assert.DoesNotContain("S1000", p.Fragment ?? ""));
    }

    [Fact]
    public void TamperedFragmentFailsIndependentCheck()
    {
        var lines = ZLevels(6);
        var r = Run(lines);
        var p = Single(r);
        var blocks = NcTokenizer.Tokenize(lines);
        var source = blocks.Where(b => b.Line >= p.StartLine && b.Line <= p.EndLine).ToArray();
        var entry = NcMachine.Run(Calc, NcState.Unknown, blocks.Where(b => b.Line < p.StartLine))[^1].After;
        Assert.True(EquivalenceChecker.Check(Calc, entry, source, p.Fragment!).Match);
        foreach (var (from, to) in new[] { ("LT 6", "LT 7"), ("#100*-1.0]", "#100*-1.01]"), ("F400.", "F450."), ("DO1", "DO4"), ("G00 Z8.", "G01 Z8."), ("#100 = #100 + 1", "#104 = #100 + 1") })
        {
            var tampered = p.Fragment!.Replace(from, to);
            Assert.NotEqual(p.Fragment, tampered);
            Assert.False(EquivalenceChecker.Check(Calc, entry, source, tampered).Match, from + " -> " + to);
        }
    }

    [Fact]
    public void LargeProgramsStayFast()
    {
        var many = new List<string>(Preamble);
        for (int k = 0; many.Count < 49000; k++) many.AddRange(Pass(-(k % 50 + 1), ox: k % 7));
        var rnd = new Random(1);
        var surface = Preamble.Concat(Enumerable.Range(0, 49000).Select(_ =>
            $"G01 X{rnd.Next(100)}.{rnd.Next(1000)} Y{rnd.Next(100)}.{rnd.Next(1000)} Z-{rnd.Next(5)}.{rnd.Next(100)} F1000.")).ToArray();
        var watch = Stopwatch.StartNew();
        Run(many);
        Run(surface);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), "Pipeline levou " + watch.Elapsed);
    }

    [Fact]
    public void RealProgramIsSegmentedByHeaderCommentsAndToolChanges()
    {
        var lines = ZLevels(4).SkipLast(2)
            .Append("(Contour2)").Append("T2 M6").Append("S6000 M3").Append("G43 H2 Z50.")
            .Concat(Enumerable.Range(1, 4).SelectMany(k => Pass(-k))).Append("T3 M6").Append("G00 X0. Y0. Z5.").Append("M30");
        var r = Run(lines);
        Assert.Equal(new[] { "T1 D=10. CR=0. - FLAT END MILL", "2D Contour1", "Contour2", "Ferramenta T3" }, r.Operations.Select(o => o.Label));
        Assert.Equal(new[] { 1, 1, 2, 3 }, r.Operations.Select(o => o.Tool));
        Assert.Equal(new[] { 2, 3 }, r.Proposals.Select(p => p.Operation));
    }

    [Fact]
    public void ProposalsNeverOverlapAndDocumentPreservesEverythingElse()
    {
        foreach (var file in Directory.GetFiles("otimizador-gerais", "*.nc"))
        {
            var lines = File.ReadAllLines(file);
            var r = Run(lines);
            var ordered = r.Proposals.OrderBy(p => p.StartLine).ToArray();
            for (int i = 1; i < ordered.Length; i++) Assert.True(ordered[i].StartLine > ordered[i - 1].EndLine, file);
            var covered = r.Proposals.Where(p => p.Accepted).SelectMany(p => Enumerable.Range(p.StartLine, p.EndLine - p.StartLine + 1)).ToHashSet();
            var docLines = r.Document.Split('\n');
            foreach (var (line, index) in lines.Select((l, i) => (l, i + 1)).Where(x => !covered.Contains(x.Item2)))
                Assert.Contains(line, docLines);
            Assert.False(r.MachineReady);
        }
    }

    [Fact]
    public void TwoPassesOfSevenBlocksAreNotWorthALoop() =>
        Assert.Empty(Run(File.ReadAllLines("otimizador-gerais/cam-g90-conversao-simulador-sintetico.nc")).Proposals);
}
