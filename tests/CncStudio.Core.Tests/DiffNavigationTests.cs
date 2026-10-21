using CncStudio.Core.Dialects;
using CncStudio.Core.Editing;

namespace CncStudio.Core.Tests;

/// <summary>CODE-07: navegação da comparação de programas (blocos, resumo, contexto, linha do editor).</summary>
public class DiffNavigationTests
{
    private static List<DiffRow> Compare(string left, string right) =>
        ProgramDiff.Compare(left, right, new DiffOptions(), Dialect.Fanuc);

    private const string Base = "G21\nG90\nG00 X0\nG01 X10 F100\nX20\nX30\nX40\nX50\nG00 Z5\nM30";

    [Fact]
    public void ProgramasIguaisNaoTemBlocos()
    {
        List<DiffRow> rows = Compare(Base, Base);

        Assert.Empty(DiffNavigation.BlockStarts(rows));
        Assert.Equal(new DiffSummary(0, 0, 0, 0), DiffNavigation.Summarize(rows));
    }

    [Fact]
    public void CadaTrechoDiferenteViraUmBloco()
    {
        string right = Base.Replace("X20", "X22").Replace("M30", "M30\nM99");
        List<DiffRow> rows = Compare(Base, right);

        IReadOnlyList<int> starts = DiffNavigation.BlockStarts(rows);
        DiffSummary summary = DiffNavigation.Summarize(rows);

        Assert.Equal(2, starts.Count);
        Assert.Equal(DiffKind.Changed, rows[starts[0]].Kind);
        Assert.Equal(DiffKind.Added, rows[starts[1]].Kind);
        Assert.Equal(new DiffSummary(2, 1, 1, 0), summary);
        Assert.Equal(2, summary.Total);
    }

    [Fact]
    public void LinhasDiferentesSeguidasSaoUmBlocoSo()
    {
        List<DiffRow> rows = Compare(Base, Base.Replace("X20\nX30", "Y1\nY2"));

        Assert.Single(DiffNavigation.BlockStarts(rows));
        Assert.Equal(2, DiffNavigation.Summarize(rows).Changed);
    }

    [Fact]
    public void ContextoMostraSoVizinhasDasDiferencasEContaAsEscondidas()
    {
        // diferença na linha 5 de 10: com contexto 1 ficam as linhas 4, 5 e 6
        List<DiffRow> rows = Compare(Base, Base.Replace("X20", "X22"));

        IReadOnlyList<DiffEntry> entries = DiffNavigation.Collapse(rows, context: 1);

        Assert.Equal([3, 4, 5], entries.Select(e => e.RowIndex));
        Assert.Equal(3, entries[0].HiddenBefore); // G21, G90, G00 X0 escondidas acima
        Assert.Equal(0, entries[1].HiddenBefore);
    }

    [Fact]
    public void ContextoNegativoMostraTudoSemEscondidas()
    {
        List<DiffRow> rows = Compare(Base, Base.Replace("X20", "X22"));

        IReadOnlyList<DiffEntry> entries = DiffNavigation.Collapse(rows, context: -1);

        Assert.Equal(rows.Count, entries.Count);
        Assert.All(entries, e => Assert.Equal(0, e.HiddenBefore));
    }

    [Fact]
    public void SemDiferencaOContextoNaoMostraNada()
    {
        Assert.Empty(DiffNavigation.Collapse(Compare(Base, Base), context: 2));
    }

    [Fact]
    public void LinhaDoEditorParaLinhaIncluidaVoltaParaALinhaAnterior()
    {
        List<DiffRow> rows = Compare("A\nB\nC", "A\nB\nNOVA\nC");
        int added = rows.FindIndex(r => r.Kind == DiffKind.Added);

        Assert.Null(rows[added].LeftLine);
        Assert.Equal(2, DiffNavigation.EditorLine(rows, added)); // depois da linha 2 da esquerda
        Assert.Equal(1, DiffNavigation.EditorLine(rows, 0));
        Assert.Equal(1, DiffNavigation.EditorLine([], 0));
    }
}
