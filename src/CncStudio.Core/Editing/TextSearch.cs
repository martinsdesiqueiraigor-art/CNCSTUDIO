using System.Text.RegularExpressions;

namespace CncStudio.Core.Editing;

/// <summary>Opções de busca.</summary>
/// <param name="MatchCase">Diferenciar maiúsculas de minúsculas.</param>
/// <param name="WholeWord">Palavra inteira no sentido do G-code: "X10" não acha "X100" nem "X10.5".</param>
/// <param name="UseRegex">O texto procurado é uma expressão regular.</param>
public sealed record SearchOptions(bool MatchCase = false, bool WholeWord = false, bool UseRegex = false);

/// <summary>Um trecho encontrado no texto.</summary>
public readonly record struct SearchMatch(int Offset, int Length);

/// <summary>
/// Procurar e substituir para o editor. Tudo vira uma Regex (texto simples é escapado),
/// com limite de tempo para uma expressão mal escrita não travar o programa.
/// </summary>
public static class TextSearch
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(2);

    /// <summary>Monta a Regex da busca. Retorna null (com a mensagem em "error") se a expressão for inválida.</summary>
    public static Regex? BuildRegex(string pattern, SearchOptions options, out string? error)
    {
        error = null;
        if (string.IsNullOrEmpty(pattern))
        {
            error = "Digite o texto a procurar.";
            return null;
        }

        string body = options.UseRegex ? pattern : Regex.Escape(pattern);
        if (options.WholeWord)
        {
            // Limites próprios do G-code: o que vem antes/depois depende de como o texto começa/termina
            char first = pattern[0];
            char last = pattern[^1];
            string before = char.IsAsciiLetter(first) ? "(?<![A-Za-z])" : char.IsAsciiDigit(first) || first is '.' or '-' ? "(?<![0-9.])" : "";
            string after = char.IsAsciiDigit(last) || last == '.' ? "(?![0-9.])" : char.IsAsciiLetter(last) ? "(?![A-Za-z])" : "";
            body = before + "(?:" + body + ")" + after;
        }

        RegexOptions regexOptions = RegexOptions.Multiline | RegexOptions.CultureInvariant;
        if (!options.MatchCase)
        {
            regexOptions |= RegexOptions.IgnoreCase;
        }

        try
        {
            return new Regex(body, regexOptions, Timeout);
        }
        catch (ArgumentException ex)
        {
            error = "Expressão regular inválida: " + ex.Message;
            return null;
        }
    }

    /// <summary>Todos os trechos encontrados (vazio se o padrão for inválido).</summary>
    public static List<SearchMatch> FindAll(string text, string pattern, SearchOptions options)
    {
        Regex? regex = BuildRegex(pattern, options, out _);
        if (regex is null)
        {
            return [];
        }

        try
        {
            return regex.Matches(text).Where(m => m.Length > 0).Select(m => new SearchMatch(m.Index, m.Length)).ToList();
        }
        catch (RegexMatchTimeoutException)
        {
            return [];
        }
    }

    /// <summary>
    /// Próximo trecho a partir de "from" (ou anterior, para trás), dando a volta no texto.
    /// Null se não houver nenhum.
    /// </summary>
    public static SearchMatch? FindNext(string text, string pattern, SearchOptions options, int from, bool backwards = false)
    {
        List<SearchMatch> matches = FindAll(text, pattern, options);
        if (matches.Count == 0)
        {
            return null;
        }

        if (backwards)
        {
            for (int i = matches.Count - 1; i >= 0; i--)
            {
                if (matches[i].Offset < from)
                {
                    return matches[i];
                }
            }

            return matches[^1];
        }

        foreach (SearchMatch match in matches)
        {
            if (match.Offset >= from)
            {
                return match;
            }
        }

        return matches[0];
    }

    /// <summary>
    /// Substitui todas as ocorrências. Com expressão regular, a substituição aceita $1, $2...
    /// Retorna o texto novo e quantas substituições foram feitas.
    /// </summary>
    public static (string Text, int Count) ReplaceAll(string text, string pattern, string replacement, SearchOptions options)
    {
        Regex? regex = BuildRegex(pattern, options, out _);
        if (regex is null)
        {
            return (text, 0);
        }

        int count = 0;
        string result = regex.Replace(text, match =>
        {
            count++;
            return options.UseRegex ? match.Result(replacement) : replacement;
        });
        return (result, count);
    }

    /// <summary>Texto que substitui um trecho (com $1, $2... quando é expressão regular).</summary>
    public static string ReplacementFor(string matchedText, string pattern, string replacement, SearchOptions options)
    {
        if (!options.UseRegex)
        {
            return replacement;
        }

        Regex? regex = BuildRegex(pattern, options, out _);
        Match? match = regex?.Match(matchedText);
        return match is { Success: true } ? match.Result(replacement) : replacement;
    }
}
