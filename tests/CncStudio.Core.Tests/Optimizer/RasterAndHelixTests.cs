using CncStudio.Core.Optimizer;
using Xunit;

namespace CncStudio.Core.Tests.Optimizer;

/// <summary>Raster and helix behaviour through the single pipeline (former harness cases ported).</summary>
public class RasterAndHelixTests
{
    private static Proposal? Raster(params string[] lines) =>
        ConversionPipeline.Run(lines, NcProfile.Fanuc0iMfCalculator).Proposals.SingleOrDefault();

    private static string[] Passes(double z, string between, int count, int step = 2) =>
        new[] { "G90 G21", "G00 X0 Y0 Z" + z }.Concat(Enumerable.Range(0, count).SelectMany(k => new[]
            { "G01 X20 F100", between.Replace("{y}", (step * (k + 1)).ToString()) })).ToArray();

    [Fact]
    public void RasterWithRapidAtCuttingHeightIsBlocked()
    {
        var p = Raster(Passes(-1, "G00 X0 Y{y}", 12))!;
        Assert.False(p.Accepted);
        Assert.Contains(p.Blockers, b => b.Contains("altura de corte"));
        Assert.Null(ConversionPipeline.Run(Passes(-1, "G00 X0 Y{y}", 12), NcProfile.Fanuc0iMfCalculator).SimulationProgram);
    }

    [Fact]
    public void RasterWithZRetractIsAccepted()
    {
        var lines = new[] { "G90 G21", "G00 X0 Y0 Z2", "G01 Z-1 F100" }.Concat(Enumerable.Range(0, 10).SelectMany(k => new[]
            { "G01 X20 F300", "G00 Z2", "X0 Y" + 2 * (k + 1), "G01 Z-1 F100" })).ToArray();
        var p = Raster(lines)!;
        Assert.True(p.Accepted, string.Join(" | ", p.Blockers));
        Assert.Equal("TRANSLACAO XY", p.Kind);
    }

    [Fact]
    public void IrregularRasterIsNotLooped()
    {
        var lines = Passes(-1, "G00 X0 Y{y}", 12).ToList();
        lines[10] = "G00 X0 Y11";
        Assert.All(ConversionPipeline.Run(lines, NcProfile.Fanuc0iMfCalculator).Proposals, p => Assert.True(p.EndLine < 11 || p.StartLine > 11));
    }

    // Helix: 12 full G03 turns of radius 10 descending 0.5 mm per turn, after a real CAM header.
    private static string[] HelixProgram(Func<int, string?>? turn = null) =>
        Fixtures.Preamble.Concat(new[] { "G0 X10. Y0.", "G1 Z0. F100." })
            .Concat(Enumerable.Range(1, 12).Select(k => turn?.Invoke(k) ?? $"G3 X10. Y0. I-10. J0. Z{Fixtures.N(-0.5 * k)} F200."))
            .Append("G0 Z5.").ToArray();

    [Fact]
    public void HelixAfterRealPreambleBecomesSimulatorLoop()
    {
        var r = ConversionPipeline.Run(HelixProgram(), NcProfile.Fanuc0iMfCalculator);
        var p = Assert.Single(r.Proposals);
        Assert.True(p.Accepted, string.Join(" | ", p.Blockers));
        Assert.Equal(("HELICE", 1, 12), (p.Kind, p.Period, p.Count));
        Assert.Contains("G3 X10. Y0. I-10. J0. Z[-0.5+#100*-0.5] F200.", p.Fragment);
        Assert.Contains(p.Warnings, w => w.Contains("helicoidal"));
        Assert.NotNull(r.SimulationProgram);
    }

    [Fact]
    public void QuadrantSplitHelixIsRecognized()
    {
        // CAM posts often split each turn into four quarter arcs.
        var quarter = new[] { ("X0.", "Y10.", "I-10.", "J0."), ("X-10.", "Y0.", "I0.", "J-10."), ("X0.", "Y-10.", "I10.", "J0."), ("X10.", "Y0.", "I0.", "J10.") };
        var lines = Fixtures.Preamble.Concat(new[] { "G0 X10. Y0.", "G1 Z0. F100." })
            .Concat(Enumerable.Range(0, 32).Select(k => { var q = quarter[k % 4]; return $"G3 {q.Item1} {q.Item2} {q.Item3} {q.Item4} Z{Fixtures.N(-0.125 * (k + 1))} F200."; }))
            .Append("G0 Z5.");
        var p = Assert.Single(ConversionPipeline.Run(lines.ToArray(), NcProfile.Fanuc0iMfCalculator).Proposals);
        Assert.True(p.Accepted, string.Join(" | ", p.Blockers));
        Assert.Equal(("HELICE", 4, 8), (p.Kind, p.Period, p.Count));
    }

    [Theory]
    [InlineData("G3 X10. Y0. I-9. J0. Z-3. F200.")]      // centro
    [InlineData("G3 X10. Y0. I-10. J0. Z-3. F250.")]     // avanco
    [InlineData("G3 X10. Y0. I-10. J0. Z-3.2 F200.")]    // passo
    [InlineData("G2 X10. Y0. I-10. J0. Z-3. F200.")]     // sentido
    [InlineData("G3 X10. Y0. R10. Z-3. F200.")]          // R
    [InlineData("G91 G3 X0. Y0. I-10. J0. Z-0.5 F200.")] // incremental
    [InlineData("G20 G3 X10. Y0. I-10. J0. Z-3. F200.")] // polegadas
    [InlineData("G18 G3 X10. Y0. I-10. K0. Z-3. F200.")] // plano
    [InlineData("G3 X9. Y1. I-10. J0. Z-3. F200.")]      // XY
    [InlineData("G55 G3 X10. Y0. I-10. J0. Z-3. F200.")] // origem
    [InlineData("G3 X10. Y0. I-10. J0. Z-3. Q1. F200.")] // palavra extra
    public void AnyDeviatingTurnStaysOutsideTheLoop(string changed)
    {
        var lines = HelixProgram(k => k == 6 ? changed : null);
        var r = ConversionPipeline.Run(lines, NcProfile.Fanuc0iMfCalculator);
        int changedLine = Array.IndexOf(lines, changed) + 1;
        Assert.DoesNotContain(r.Proposals, p => p.Accepted && p.StartLine <= changedLine && p.EndLine >= changedLine);
    }

    [Fact]
    public void HelixWithInconsistentRadiusIsBlocked()
    {
        // Every turn ends off the circle: the control would alarm; the loop is equivalent but must not be offered.
        var lines = HelixProgram(k => $"G3 X10.01 Y0. I-10. J0. Z{Fixtures.N(-0.5 * k)} F200.");
        var p = Assert.Single(ConversionPipeline.Run(lines, NcProfile.Fanuc0iMfCalculator).Proposals);
        Assert.False(p.Accepted);
        Assert.Contains(p.Blockers, b => b.Contains("diferem"));
    }

    [Fact]
    public void HelixWithRadiusWordIsBlocked()
    {
        var lines = Fixtures.Preamble.Concat(new[] { "G0 X10. Y0.", "G1 Z0. F100." })
            .Concat(Enumerable.Range(1, 12).SelectMany(k => new[] { $"G3 X-10. Y0. R10. Z{Fixtures.N(-0.5 * k + 0.25)} F200.", $"X10. Y0. R10. Z{Fixtures.N(-0.5 * k)}" }))
            .Append("G0 Z5.").ToArray();
        var p = Assert.Single(ConversionPipeline.Run(lines, NcProfile.Fanuc0iMfCalculator).Proposals);
        Assert.False(p.Accepted);
        Assert.Contains(p.Blockers, b => b.Contains("helice com R"));
    }
}
