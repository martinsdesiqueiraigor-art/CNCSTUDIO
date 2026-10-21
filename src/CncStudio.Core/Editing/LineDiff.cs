namespace CncStudio.Core.Editing;

/// <summary>Pedaço de uma linha; Changed = parte que difere da outra linha.</summary>
public readonly record struct DiffSegment(string Text, bool Changed);

/// <summary>
/// Diferença dentro de uma linha alterada: compara por "palavras" de G-code (X62., G71, (comentário)...)
/// e marca só o que mudou, para o olho achar de relance. A regra de espaços, maiúsculas e números N
/// é a mesma da comparação de programas (<see cref="DiffOptions"/>).
/// </summary>
public static class LineDiff
{
    /// <summary>Linhas com mais "palavras" que isso (produto acima do limite) viram uma marcação só.</summary>
    private const int MaxProduct = 40_000;

    public static (IReadOnlyList<DiffSegment> Left, IReadOnlyList<DiffSegment> Right) Segments(string left, string right, DiffOptions options)
    {
        List<Token> a = Tokenize(left, options);
        List<Token> b = Tokenize(right, options);

        // Só entram na busca as palavras que contam (espaços e N ignorados ficam de fora e nunca são marcados)
        List<int> ia = Indexes(a), ib = Indexes(b);
        bool[] changedA = new bool[a.Count], changedB = new bool[b.Count];
        foreach (int i in ia) changedA[i] = true;
        foreach (int i in ib) changedB[i] = true;

        if ((long)ia.Count * ib.Count <= MaxProduct)
        {
            int n = ia.Count, m = ib.Count;
            int[,] lcs = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
            {
                for (int j = m - 1; j >= 0; j--)
                {
                    lcs[i, j] = a[ia[i]].Key == b[ib[j]].Key ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
                }
            }

            for (int i = 0, j = 0; i < n && j < m;)
            {
                if (a[ia[i]].Key == b[ib[j]].Key)
                {
                    changedA[ia[i]] = false;
                    changedB[ib[j]] = false;
                    i++;
                    j++;
                }
                else if (lcs[i + 1, j] >= lcs[i, j + 1])
                {
                    i++;
                }
                else
                {
                    j++;
                }
            }
        }

        return (Merge(a, changedA), Merge(b, changedB));
    }

    private sealed record Token(string Text, string? Key);

    private static List<int> Indexes(List<Token> tokens)
    {
        var list = new List<int>();
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Key is not null)
            {
                list.Add(i);
            }
        }

        return list;
    }

    private static IReadOnlyList<DiffSegment> Merge(List<Token> tokens, bool[] changed)
    {
        var result = new List<DiffSegment>();
        for (int i = 0; i < tokens.Count; i++)
        {
            if (result.Count > 0 && result[^1].Changed == changed[i])
            {
                result[^1] = result[^1] with { Text = result[^1].Text + tokens[i].Text };
            }
            else
            {
                result.Add(new DiffSegment(tokens[i].Text, changed[i]));
            }
        }

        return result;
    }

    private static List<Token> Tokenize(string line, DiffOptions options)
    {
        var tokens = new List<Token>();
        int i = 0;
        while (i < line.Length)
        {
            int start = i;
            char c = line[i];
            if (char.IsWhiteSpace(c))
            {
                while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
                string text = line[start..i];
                tokens.Add(new Token(text, options.IgnoreWhitespace ? null : text));
                continue;
            }

            if (char.IsLetter(c) || c == '_')
            {
                // palavra: letras e, se vier número, o número junto (X-62., G71, F0.25)
                while (i < line.Length && (char.IsLetter(line[i]) || line[i] == '_')) i++;
                if (i < line.Length && (char.IsDigit(line[i]) || ((line[i] is '-' or '+' or '.') && i + 1 < line.Length && (char.IsDigit(line[i + 1]) || line[i + 1] == '.'))))
                {
                    i++;
                    while (i < line.Length && (char.IsDigit(line[i]) || line[i] == '.')) i++;
                }
            }
            else if (char.IsDigit(c) || c == '.')
            {
                while (i < line.Length && (char.IsDigit(line[i]) || line[i] == '.')) i++;
            }
            else
            {
                i++;
            }

            string word = line[start..i];
            string key = options.IgnoreCase ? word.ToUpperInvariant() : word;
            bool blockNumber = options.IgnoreBlockNumbers && tokens.All(t => t.Key is null && string.IsNullOrWhiteSpace(t.Text))
                && word.Length > 1 && (word[0] is 'N' or 'n') && word[1..].All(char.IsDigit);
            tokens.Add(new Token(word, blockNumber ? null : key));
        }

        return tokens;
    }
}
