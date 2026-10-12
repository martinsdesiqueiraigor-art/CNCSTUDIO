using CncStudio.Core.Dialects;
using CncStudio.Core.Editing;

namespace CncStudio.Core.Tests;

public class ProgramDiffTests
{
    private static List<DiffRow> Diff(string a, string b, DiffOptions? options = null) =>
        ProgramDiff.Compare(a, b, options ?? new DiffOptions(), Dialect.Fanuc);

    [Fact]
    public void ProgramasIguaisSoTemLinhasIguais()
    {
        List<DiffRow> rows = Diff("G00 X0\nG01 X10\n", "G00 X0\nG01 X10\n");
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(DiffKind.Equal, r.Kind));
    }

    [Fact]
    public void LinhaAlteradaIncluidaERemovida()
    {
        string a = "N10 G00 X0\nN20 G01 X10 F100\nN30 Y10\nN40 X0\nN50 M30";
        string b = "N10 G00 X0\nN20 G01 X10 F150\nN30 Y10\nN35 Z-1\nN50 M30";
        List<DiffRow> rows = Diff(a, b);

        Assert.Equal(DiffKind.Changed, rows[1].Kind);       // F100 → F150
        Assert.Equal((2, 2), (rows[1].LeftLine, rows[1].RightLine));
        Assert.Equal(DiffKind.Changed, rows[3].Kind);       // N40 X0 ↔ N35 Z-1 (mesmo ponto)
        Assert.Equal(DiffKind.Equal, rows[^1].Kind);
        Assert.Equal(5, rows.Count);
    }

    [Fact]
    public void SoInclusaoNoMeio()
    {
        List<DiffRow> rows = Diff("A\nB\nC", "A\nB\nX\nY\nC");
        Assert.Equal([DiffKind.Equal, DiffKind.Equal, DiffKind.Added, DiffKind.Added, DiffKind.Equal], rows.Select(r => r.Kind));
        Assert.Equal([1, 2, null, null, 3], rows.Select(r => r.LeftLine));
        Assert.Equal(5, rows[^1].RightLine);
    }

    [Fact]
    public void SoRemocaoNoFim()
    {
        List<DiffRow> rows = Diff("A\nB\nC\nD", "A\nB");
        Assert.Equal([DiffKind.Equal, DiffKind.Equal, DiffKind.Removed, DiffKind.Removed], rows.Select(r => r.Kind));
        Assert.Equal(4, rows[^1].LeftLine);
        Assert.Null(rows[^1].RightLine);
    }

    [Fact]
    public void OpcoesIgnoramEspacosMaiusculasENumerosDeBloco()
    {
        Assert.All(Diff("N10 G01 X10", "N10G01X10"), r => Assert.Equal(DiffKind.Equal, r.Kind));
        Assert.Equal(DiffKind.Changed, Diff("N10 g01 x10", "N10 G01 X10").Single().Kind);
        Assert.Equal(DiffKind.Equal, Diff("N10 g01 x10", "N10 G01 X10", new DiffOptions(IgnoreCase: true)).Single().Kind);

        // Depois de renumerar, com "ignorar N" os blocos continuam iguais
        string before = "N10 G00 X0\nN20 G01 X10\nN30 Y10";
        string after = "N100 G00 X0\nN110 G01 X10\nN120 Y10";
        Assert.All(Diff(before, after, new DiffOptions(IgnoreBlockNumbers: true)), r => Assert.Equal(DiffKind.Equal, r.Kind));
        Assert.All(Diff(before, after), r => Assert.Equal(DiffKind.Changed, r.Kind));
    }

    [Fact]
    public void ArquivoGrandeComPoucasMudancasEhRapido()
    {
        var lines = Enumerable.Range(1, 20_000).Select(i => $"N{i} G01 X{i % 100} Y{i % 37}").ToList();
        string a = string.Join("\n", lines);
        lines[5_000] = "N5001 G01 X999";
        lines.Insert(12_000, "M00");
        string b = string.Join("\n", lines);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        List<DiffRow> rows = Diff(a, b);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2));
        Assert.Single(rows, r => r.Kind == DiffKind.Changed);
        Assert.Single(rows, r => r.Kind == DiffKind.Added);
        Assert.Equal(20_001, rows.Count);
    }
}
