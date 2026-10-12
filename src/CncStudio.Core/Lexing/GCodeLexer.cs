using System.Globalization;
using CncStudio.Core.Dialects;

namespace CncStudio.Core.Lexing;

/// <summary>
/// Quebra uma linha de G-code em tokens.
/// É usado pelo parser e também pelo syntax highlighting do editor — por isso
/// trabalha linha a linha e cada token guarda coluna e tamanho.
/// </summary>
public static class GCodeLexer
{
    /// <summary>Tokeniza uma linha e retorna uma lista nova.</summary>
    public static List<Token> TokenizeLine(string line, Dialect dialect)
    {
        var tokens = new List<Token>();
        TokenizeLine(line, dialect, tokens);
        return tokens;
    }

    /// <summary>
    /// Tokeniza uma linha reaproveitando a lista "output" (que é limpa antes).
    /// Evita alocar uma lista por linha ao processar arquivos grandes.
    /// </summary>
    public static void TokenizeLine(string line, Dialect dialect, List<Token> output)
    {
        output.Clear();
        int i = 0;
        int n = line.Length;

        while (i < n)
        {
            char c = line[i];

            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            // "%" sozinho (Fanuc) ou "%_N_NOME_MPF" (Siemens): marcador de programa até o fim da linha
            if (c == '%')
            {
                output.Add(new Token(TokenKind.ProgramMarker, i, n - i, '\0', null));
                return;
            }

            if (c == ';')
            {
                if (dialect == Dialect.Siemens)
                {
                    // Siemens: comentário até o fim da linha
                    output.Add(new Token(TokenKind.Comment, i, n - i, '\0', null));
                    return;
                }

                // Fanuc: ";" é o fim de bloco (EOB), opcional no arquivo — apenas ignoramos
                i++;
                continue;
            }

            if (c == '(' && dialect == Dialect.Fanuc)
            {
                // Fanuc: comentário entre parênteses (se não fechar, vai até o fim da linha)
                int close = line.IndexOf(')', i + 1);
                int end = close < 0 ? n : close + 1;
                output.Add(new Token(TokenKind.Comment, i, end - i, '\0', null));
                i = end;
                continue;
            }

            if (c == '/')
            {
                // "/" no início do bloco = bloco opcional (block delete). No protótipo o bloco é executado.
                i++;
                continue;
            }

            if (char.IsAsciiLetter(c))
            {
                // Conta quantas letras seguidas existem para decidir entre endereço (G01) e palavra-chave (CYCLE81)
                int j = i + 1;
                while (j < n && char.IsAsciiLetter(line[j]))
                {
                    j++;
                }

                i = (j - i == 1)
                    ? ReadAddressWord(line, i, dialect, output)
                    : ReadKeyword(line, i, dialect, output);
                continue;
            }

            output.Add(new Token(TokenKind.Unknown, i, 1, '\0', null));
            i++;
        }
    }

    /// <summary>Lê uma palavra de endereço (letra + número). Retorna a posição após o token.</summary>
    private static int ReadAddressWord(string line, int start, Dialect dialect, List<Token> output)
    {
        char letter = char.ToUpperInvariant(line[start]);
        int k = start + 1;

        // Siemens aceita "X=10" além de "X10"
        if (dialect == Dialect.Siemens && k < line.Length && line[k] == '=')
        {
            k++;
        }

        if (TryReadNumber(line, k, out double value, out int end))
        {
            TokenKind kind = letter switch
            {
                'N' => TokenKind.BlockNumber,
                'O' when dialect == Dialect.Fanuc => TokenKind.ProgramMarker,
                _ => TokenKind.Word,
            };
            output.Add(new Token(kind, start, end - start, letter, value));
            return end;
        }

        // Letra sem número válido depois dela
        output.Add(new Token(TokenKind.Unknown, start, k - start, '\0', null));
        return k;
    }

    /// <summary>
    /// Lê um identificador de várias letras: "CYCLE81(10,0,2,-5)", "CR=5", "DIAMOF".
    /// Retorna a posição após o token.
    /// </summary>
    private static int ReadKeyword(string line, int start, Dialect dialect, List<Token> output)
    {
        int n = line.Length;
        int j = start;
        while (j < n && (char.IsAsciiLetterOrDigit(line[j]) || line[j] == '_'))
        {
            j++;
        }

        // Atribuição numérica: CR=5, AP=90...
        if (dialect == Dialect.Siemens && j < n && line[j] == '='
            && TryReadNumber(line, j + 1, out double value, out int numberEnd))
        {
            output.Add(new Token(TokenKind.Keyword, start, numberEnd - start, '\0', value));
            return numberEnd;
        }

        // Chamada com argumentos: CYCLE81(...), MSG("...") — inclui até o ")" correspondente
        if (j < n && line[j] == '(')
        {
            int depth = 0;
            int k = j;
            while (k < n)
            {
                if (line[k] == '(') depth++;
                else if (line[k] == ')') depth--;
                k++;
                if (depth == 0) break;
            }

            output.Add(new Token(TokenKind.Keyword, start, k - start, '\0', null));
            return k;
        }

        output.Add(new Token(TokenKind.Keyword, start, j - start, '\0', null));
        return j;
    }

    /// <summary>
    /// Lê um número no formato G-code: sinal opcional, dígitos e ponto decimal opcional.
    /// Aceita "10", "10.", ".5", "-.5", "+3.25". Precisa de pelo menos um dígito.
    /// </summary>
    internal static bool TryReadNumber(string s, int start, out double value, out int end)
    {
        int n = s.Length;
        int i = start;
        bool anyDigit = false;

        if (i < n && (s[i] == '+' || s[i] == '-'))
        {
            i++;
        }

        while (i < n && char.IsAsciiDigit(s[i]))
        {
            i++;
            anyDigit = true;
        }

        if (i < n && s[i] == '.')
        {
            i++;
            while (i < n && char.IsAsciiDigit(s[i]))
            {
                i++;
                anyDigit = true;
            }
        }

        if (!anyDigit)
        {
            value = 0;
            end = start;
            return false;
        }

        // Observação: no Fanuc real, "X10" sem ponto pode significar 0,010 mm (depende de parâmetro).
        // No protótipo, todo número é interpretado em milímetros.
        value = double.Parse(s.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        end = i;
        return true;
    }
}
