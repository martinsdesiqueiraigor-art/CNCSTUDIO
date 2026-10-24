using CncStudio.Core.Optimizer;
using Xunit;
using static CncStudio.Core.Tests.Optimizer.Fixtures;

namespace CncStudio.Core.Tests.Optimizer;

public class Phase4Tests
{
    private static string[] Corpus(string name) => File.ReadAllLines(Path.Combine("otimizador-fixtures", name));

    [Theory]
    [InlineData("A-fusion360-flange.nc", "Autodesk Fusion / HSM")]
    [InlineData("B-mastercam-suporte.nc", "Mastercam")]
    [InlineData("C-pos-compacto-placa.nc", CamSignature.Unknown)]
    [InlineData("K-sem-comentarios.nc", CamSignature.Unknown)]
    public void CamIsRecognizedOnlyFromStrongSignatures(string name, string cam) =>
        Assert.Equal(cam, Run(Corpus(name)).Cam);

    [Theory]
    [InlineData("A-fusion360-flange.nc")]
    [InlineData("B-mastercam-suporte.nc")]
    [InlineData("G-bolsoes-concentricos.nc")]
    [InlineData("J-subprogramas-placa.nc")]
    public void AnonymizedCopyHasNoCommentTextAndTheSameAnalysis(string name)
    {
        var lines = Corpus(name);
        var copy = NcAnonymizer.Anonymize(lines);
        Assert.Equal(lines.Length, copy.Count);
        var comments = NcTokenizer.Tokenize(lines).Where(b => b.Comment is { Length: > 3 }).Select(b => b.Comment!).Distinct();
        Assert.All(comments, c => Assert.DoesNotContain(copy, l => l.Contains(c)));
        var a = Run(lines); var b = Run(copy);
        Assert.Equal(a.Proposals.Select(p => (p.Kind, p.StartLine, p.EndLine, p.Count, p.Status)), b.Proposals.Select(p => (p.Kind, p.StartLine, p.EndLine, p.Count, p.Status)));
        Assert.Equal(a.Operations.Select(o => (o.StartLine, o.Source)), b.Operations.Select(o => (o.StartLine, o.Source)));
        Assert.Equal(a.ConvertedLines, b.ConvertedLines);
    }

    [Fact]
    public void ReferenceReturnIsModeledAndOnlyLosesTheNamedAxis()
    {
        var s = NcMachine.Run(Calc, NcState.Unknown, NcTokenizer.Tokenize(new[] { "G21 G90 G17 G0 X5. Y6. Z7.", "G91 G28 Z0.", "G90" }));
        Assert.All(s, x => Assert.False(x.Unmodeled));
        var after = s[^1].After;
        Assert.Equal((5.0, 6.0, PosKind.Unknown), (after.X.Value, after.Y.Value, after.Z.Kind));
        Assert.False(after.Tainted);
    }

    [Fact]
    public void PositioningBeforeLengthOffsetDoesNotSplitAnOperation()
    {
        var lines = new[] { "G21 G90 G17", "T1 M6", "G54 G0 X0. Y0.", "G43 H1 Z20.", "G1 Z-1. F100.", "X10.",
            "T2 M6", "G0 X0. Y0.", "G43 H2 Z20.", "G1 Z-1. F100.", "X10.", "G55 G0 X0. Y0.", "G1 Z-1. F100.", "X10." };
        var r = Run(lines);
        Assert.Equal(new[] { ("INICIO", 1), ("TROCA DE FERRAMENTA", 7), ("ORIGEM", 12) }, r.Operations.Select(o => (o.Source, o.StartLine)));
    }

    [Fact]
    public void HeaderAndFooterArePreservedInTheSimulationProgram()
    {
        var lines = Corpus("A-fusion360-flange.nc");
        var sim = Run(lines).SimulationProgram!.TrimEnd('\n').Split('\n');
        Assert.Equal(lines.Take(2), sim.Take(2));                      // % and O-number
        Assert.Equal(lines.Skip(2).Take(7), sim.Skip(6).Take(7));     // tool table and preamble after the 4 header comments
        Assert.Equal(lines.TakeLast(20), sim.TakeLast(20));           // drilling operation and footer untouched
    }
}
