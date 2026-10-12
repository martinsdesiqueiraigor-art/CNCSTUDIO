using System.Text;
using CncStudio.Core.Dialects;
using CncStudio.Core.Lexing;

namespace CncStudio.Core.Editing;

/// <summary>Como uma linha da comparação ficou.</summary>
public enum DiffKind
{
    /// <summary>Igual nos dois programas.</summary>
    Equal,

    /// <summary>Existe nos dois, mas mudou.</summary>
    Changed,

    /// <summary>Só existe no programa da direita (incluída).</summary>
    Added,

    /// <summary>Só existe no programa da esquerda (removida).</summary>
    Removed,
}

/// <summary>Uma linha da comparação lado a lado. LeftLine/RightLine (base 1) são null no lado vazio.</summary>
public readonly record struct DiffRow(DiffKind Kind, int? LeftLine, int? RightLine);

/// <summary>O que não conta como diferença.</summary>
/// <param name="IgnoreWhitespace">Espaços (N10 G01 X10 = N10G01X10).</param>
/// <param name="IgnoreCase">Maiúsculas e minúsculas.</param>
/// <param name="IgnoreBlockNumbers">Números de bloco N (útil depois de renumerar).</param>
public sealed record DiffOptions(bool IgnoreWhitespace = true, bool IgnoreCase = false, bool IgnoreBlockNumbers = false);

/// <summary>
/// Compara dois programas linha a linha (algoritmo de Myers: menor quantidade de linhas
/// incluídas/removidas). Linhas removidas seguidas de incluídas no mesmo ponto viram "alteradas",
/// lado a lado.
/// </summary>
public static class ProgramDiff
{
    /// <summary>Acima disso (programas muito diferentes), o miolo é marcado como alterado sem buscar o mínimo.</summary>
    private const int MaxEditDistance = 4000;

    public static List<DiffRow> Compare(string left, string right, DiffOptions options, Dialect dialect)
    {
        string[] a = SplitLines(left);
        string[] b = SplitLines(right);

        // Linhas viram números (mesmo texto normalizado = mesmo número): comparar int é rápido
        var ids = new Dictionary<string, int>();
        int[] x = a.Select(line => Id(Normalize(line, options, dialect))).ToArray();
        int[] y = b.Select(line => Id(Normalize(line, options, dialect))).ToArray();

        // Começo e fim iguais saem do cálculo (o caso comum: poucas linhas mudaram)
        int prefix = 0;
        while (prefix < x.Length && prefix < y.Length && x[prefix] == y[prefix])
        {
            prefix++;
        }

        int suffix = 0;
        while (suffix < x.Length - prefix && suffix < y.Length - prefix && x[^(suffix + 1)] == y[^(suffix + 1)])
        {
            suffix++;
        }

        var script = new List<(char Op, int Count)>();   // '=' igual, '-' removida, '+' incluída
        if (prefix > 0)
        {
            script.Add(('=', prefix));
        }

        script.AddRange(Myers(x[prefix..^suffix], y[prefix..^suffix]));
        if (suffix > 0)
        {
            script.Add(('=', suffix));
        }

        return BuildRows(script);

        int Id(string text)
        {
            if (!ids.TryGetValue(text, out int id))
            {
                id = ids.Count;
                ids[text] = id;
            }

            return id;
        }
    }

    /// <summary>Texto da linha como é comparado.</summary>
    public static string Normalize(string line, DiffOptions options, Dialect dialect)
    {
        string text = line.TrimEnd('\r');
        if (options.IgnoreBlockNumbers)
        {
            var tokens = new List<Token>();
            GCodeLexer.TokenizeLine(text, dialect, tokens);
            Token? number = tokens.FirstOrDefault(t => t.Kind == TokenKind.BlockNumber) is { Length: > 0 } n ? n : null;
            if (number is { } t)
            {
                text = text.Remove(t.Column, t.Length);
            }
        }

        if (options.IgnoreWhitespace)
        {
            var builder = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (!char.IsWhiteSpace(c))
                {
                    builder.Append(c);
                }
            }

            text = builder.ToString();
        }

        return options.IgnoreCase ? text.ToUpperInvariant() : text;
    }

    private static string[] SplitLines(string text)
    {
        string[] lines = text.Split('\n');
        return lines.Length > 0 && lines[^1].Length == 0 ? lines[..^1] : lines; // "\n" final não é linha a mais
    }

    /// <summary>
    /// Myers O((N+M)·D): guarda o vetor V de cada passo para refazer o caminho.
    /// Se as diferenças passam de MaxEditDistance, marca tudo como removido + incluído.
    /// </summary>
    private static List<(char Op, int Count)> Myers(int[] a, int[] b)
    {
        int n = a.Length, m = b.Length;
        var result = new List<(char, int)>();
        if (n == 0 && m == 0)
        {
            return result;
        }

        if (n == 0 || m == 0)
        {
            if (n > 0) result.Add(('-', n));
            if (m > 0) result.Add(('+', m));
            return result;
        }

        int max = Math.Min(n + m, MaxEditDistance);
        int offset = max;
        var trace = new List<int[]>();
        int[] v = new int[2 * max + 2];
        int found = -1;

        for (int d = 0; d <= max && found < 0; d++)
        {
            trace.Add((int[])v.Clone());
            for (int k = -d; k <= d; k += 2)
            {
                int xPos = k == -d || (k != d && v[offset + k - 1] < v[offset + k + 1])
                    ? v[offset + k + 1]          // desce: inclusão
                    : v[offset + k - 1] + 1;     // vai à direita: remoção
                int yPos = xPos - k;
                while (xPos < n && yPos < m && a[xPos] == b[yPos])
                {
                    xPos++;
                    yPos++;
                }

                v[offset + k] = xPos;
                if (xPos >= n && yPos >= m)
                {
                    found = d;
                    break;
                }
            }
        }

        if (found < 0)
        {
            return [('-', n), ('+', m)]; // diferentes demais: sem alinhamento fino
        }

        // Refaz o caminho de trás para frente
        var ops = new List<char>(n + m);
        int x = n, y = m;
        for (int d = found; d > 0; d--)
        {
            int[] previous = trace[d];
            int k = x - y;
            bool down = k == -d || (k != d && previous[offset + k - 1] < previous[offset + k + 1]);
            int prevK = down ? k + 1 : k - 1;
            int prevX = previous[offset + prevK];
            int prevY = prevX - prevK;

            while (x > prevX && y > prevY)
            {
                ops.Add('=');
                x--;
                y--;
            }

            ops.Add(down ? '+' : '-');
            x = prevX;
            y = prevY;
        }

        while (x > 0 && y > 0)
        {
            ops.Add('=');
            x--;
            y--;
        }

        ops.Reverse();
        foreach (char op in ops)
        {
            if (result.Count > 0 && result[^1].Item1 == op)
            {
                result[^1] = (op, result[^1].Item2 + 1);
            }
            else
            {
                result.Add((op, 1));
            }
        }

        return result;
    }

    /// <summary>Monta as linhas lado a lado: removidas + incluídas no mesmo ponto viram alteradas.</summary>
    private static List<DiffRow> BuildRows(List<(char Op, int Count)> script)
    {
        var rows = new List<DiffRow>();
        int left = 1, right = 1;
        int i = 0;
        while (i < script.Count)
        {
            (char op, int count) = script[i];
            if (op == '=')
            {
                for (int k = 0; k < count; k++)
                {
                    rows.Add(new DiffRow(DiffKind.Equal, left++, right++));
                }

                i++;
                continue;
            }

            // Bloco de diferenças: soma removidas e incluídas até o próximo trecho igual
            int removed = 0, added = 0;
            while (i < script.Count && script[i].Op != '=')
            {
                if (script[i].Op == '-') removed += script[i].Count; else added += script[i].Count;
                i++;
            }

            int paired = Math.Min(removed, added);
            for (int k = 0; k < paired; k++)
            {
                rows.Add(new DiffRow(DiffKind.Changed, left++, right++));
            }

            for (int k = paired; k < removed; k++)
            {
                rows.Add(new DiffRow(DiffKind.Removed, left++, null));
            }

            for (int k = paired; k < added; k++)
            {
                rows.Add(new DiffRow(DiffKind.Added, null, right++));
            }
        }

        return rows;
    }
}
