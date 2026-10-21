using CncStudio.Core.Editing;

namespace CncStudio.Core.Tests;

/// <summary>CODE-07: destaque das palavras que mudaram dentro de uma linha.</summary>
public class LineDiffTests
{
    private static readonly DiffOptions Default = new();

    private static string[] Changed(IReadOnlyList<DiffSegment> segments) =>
        segments.Where(s => s.Changed).Select(s => s.Text).ToArray();

    [Fact]
    public void MarcaSoAPalavraQueMudou()
    {
        var (left, right) = LineDiff.Segments("N50 G00 X62. Z2. M08", "N50 G00 X52. Z2. M08", Default);

        Assert.Equal(["X62."], Changed(left));
        Assert.Equal(["X52."], Changed(right));
        Assert.Equal("N50 G00 X62. Z2. M08", string.Concat(left.Select(s => s.Text)));
    }

    [Fact]
    public void PalavraIncluidaOuRemovidaSoMarcaUmLado()
    {
        var (left, right) = LineDiff.Segments("N200 G70 P80 Q150 F.15", "N200 G70 P80 Q150", Default);

        Assert.Equal(["F.15"], Changed(left));
        Assert.Empty(Changed(right));
    }

    [Fact]
    public void MudancaVariasPalavrasMarcaCadaUma()
    {
        var (left, right) = LineDiff.Segments("N60 G71 U2.5 R1.0", "N60 G71 U1.5 R0.5", Default);

        Assert.Equal(["U2.5", "R1.0"], Changed(left));
        Assert.Equal(["U1.5", "R0.5"], Changed(right));
    }

    [Fact]
    public void ComentarioCompararPorPalavra()
    {
        var (left, right) = LineDiff.Segments("(PWLNL 2525M08 - WNMG 080412)", "(PWLNR 2525M08 - WNMG 080408)", Default);

        Assert.Contains("PWLNL", Changed(left));
        Assert.Contains("PWLNR", Changed(right));
        Assert.DoesNotContain("2525M08", Changed(left));
    }

    [Fact]
    public void IgnorarEspacosNaoMarcaEspaco()
    {
        var (left, right) = LineDiff.Segments("N10G01X10", "N10 G01 X10", Default);

        Assert.Empty(Changed(left));
        Assert.Empty(Changed(right));
    }

    [Fact]
    public void IgnorarMaiusculasENumerosDeBloco()
    {
        var options = new DiffOptions(IgnoreWhitespace: true, IgnoreCase: true, IgnoreBlockNumbers: true);

        var (left, right) = LineDiff.Segments("N10 g01 x10.", "N20 G01 X10.", options);

        Assert.Empty(Changed(left));
        Assert.Empty(Changed(right));
    }

    [Fact]
    public void LinhaVaziaDeUmLadoMarcaTudoDoOutro()
    {
        var (left, right) = LineDiff.Segments("G00 X10.", "", Default);

        Assert.Equal(["G00", "X10."], Changed(left));
        Assert.Empty(right);
    }
}
