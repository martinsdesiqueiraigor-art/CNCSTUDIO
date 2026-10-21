namespace CncStudio.Core.Editing;

/// <summary>Resumo da comparação: quantos blocos de diferença e quantas linhas de cada tipo.</summary>
public readonly record struct DiffSummary(int Blocks, int Changed, int Added, int Removed)
{
    public int Total => Changed + Added + Removed;
}

/// <summary>Linha a mostrar na lista; HiddenBefore = linhas iguais escondidas logo acima dela.</summary>
public readonly record struct DiffEntry(int RowIndex, int HiddenBefore);

/// <summary>
/// Apoio à tela de comparação (celular e web): onde começa cada diferença, resumo, lista só com as
/// diferenças e um pouco de contexto, e a linha do editor para onde um toque deve levar.
/// </summary>
public static class DiffNavigation
{
    /// <summary>Índice (na lista de linhas) onde começa cada bloco de linhas diferentes seguidas.</summary>
    public static IReadOnlyList<int> BlockStarts(IReadOnlyList<DiffRow> rows)
    {
        var starts = new List<int>();
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Kind != DiffKind.Equal && (i == 0 || rows[i - 1].Kind == DiffKind.Equal))
            {
                starts.Add(i);
            }
        }

        return starts;
    }

    public static DiffSummary Summarize(IReadOnlyList<DiffRow> rows)
    {
        int changed = 0, added = 0, removed = 0;
        foreach (DiffRow row in rows)
        {
            switch (row.Kind)
            {
                case DiffKind.Changed: changed++; break;
                case DiffKind.Added: added++; break;
                case DiffKind.Removed: removed++; break;
            }
        }

        return new DiffSummary(BlockStarts(rows).Count, changed, added, removed);
    }

    /// <summary>
    /// Só as linhas diferentes e "context" linhas iguais de cada lado. context &lt; 0 mostra tudo.
    /// </summary>
    public static IReadOnlyList<DiffEntry> Collapse(IReadOnlyList<DiffRow> rows, int context)
    {
        var entries = new List<DiffEntry>();
        if (context < 0)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                entries.Add(new DiffEntry(i, 0));
            }

            return entries;
        }

        var keep = new bool[rows.Count];
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Kind == DiffKind.Equal)
            {
                continue;
            }

            for (int k = Math.Max(0, i - context); k <= Math.Min(rows.Count - 1, i + context); k++)
            {
                keep[k] = true;
            }
        }

        int hidden = 0;
        for (int i = 0; i < rows.Count; i++)
        {
            if (!keep[i])
            {
                hidden++;
                continue;
            }

            entries.Add(new DiffEntry(i, hidden));
            hidden = 0;
        }

        return entries;
    }

    /// <summary>
    /// Linha do programa da esquerda (o do editor) para onde um toque leva: a da própria linha ou, se ela só
    /// existe na direita, a última linha da esquerda antes dela (1 se não houver).
    /// </summary>
    public static int EditorLine(IReadOnlyList<DiffRow> rows, int rowIndex)
    {
        for (int i = Math.Min(rowIndex, rows.Count - 1); i >= 0; i--)
        {
            if (rows[i].LeftLine is { } line)
            {
                return line;
            }
        }

        return 1;
    }
}
