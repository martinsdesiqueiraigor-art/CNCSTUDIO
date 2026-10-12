using System.Globalization;
using CncStudio.Core.Dialects;

namespace CncStudio.Core.Lexing;

/// <summary>
/// Quebra uma linha de G-code em tokens.
/// É usado pelo parser e também pelo syntax highlighting do editor — por isso
/// trabalha linha a linha e cada token guarda coluna e tamanho.
///
/// Reconhece (sem avaliar) a sintaxe de macros e parâmetros — #1=..., X[#1+2], IF [...],
/// R1=..., Z=R1, rótulos "VOLTA:" — para que programas paramétricos não virem uma
/// avalanche de erros. A avaliação é feita pelo interpretador.
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
        bool fanuc = dialect == Dialect.Fanuc;

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
                if (!fanuc)
                {
                    // Siemens: comentário até o fim da linha
                    output.Add(new Token(TokenKind.Comment, i, n - i, '\0', null));
                    return;
                }

                // Fanuc: ";" é o fim de bloco (EOB), opcional no arquivo — apenas ignoramos
                i++;
                continue;
            }

            if (c == '(' && fanuc)
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
                // "/" no início do bloco = bloco opcional (block delete). Ele é executado.
                i++;
                continue;
            }

            if (fanuc && c == '#')
            {
                // Fanuc: atribuição de variável "#1=48" (até o fim do bloco)
                int end = ReadUntilStatementEnd(line, i);
                output.Add(new Token(TokenKind.Expression, i, end - i, '\0', null));
                i = end;
                continue;
            }

            if (fanuc && c == '[')
            {
                int end = ReadGroup(line, i, '[', ']');
                output.Add(new Token(TokenKind.Expression, i, end - i, '\0', null));
                i = end;
                continue;
            }

            if (fanuc && c == ',' && i + 1 < n && char.IsAsciiLetter(line[i + 1]))
            {
                // Fanuc: canto arredondado ",R5." ou chanfro ",C2."
                i = ReadCornerWord(line, i, output);
                continue;
            }

            if (char.IsAsciiLetter(c))
            {
                // Conta as letras seguidas para decidir entre endereço (G01) e palavra-chave (CYCLE81)
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

    /// <summary>Lê uma palavra de endereço (letra + número, ou letra + expressão). Retorna a posição após o token.</summary>
    private static int ReadAddressWord(string line, int start, Dialect dialect, List<Token> output)
    {
        int n = line.Length;
        char letter = char.ToUpperInvariant(line[start]);
        int k = start + 1;
        bool siemens = dialect == Dialect.Siemens;

        // Siemens aceita "X=10" além de "X10"
        bool hasEquals = siemens && k < n && line[k] == '=';
        if (hasEquals)
        {
            k++;
        }

        if (TryReadNumber(line, k, out double value, out int end, out bool hasPoint))
        {
            // Siemens: "R1=..." é atribuição de parâmetro, não palavra de endereço
            if (siemens && !hasEquals && end < n && line[end] == '=')
            {
                int expressionEnd = ReadUntilWhitespace(line, end + 1);
                output.Add(new Token(TokenKind.Expression, start, expressionEnd - start, letter, null));
                return expressionEnd;
            }

            TokenKind kind = letter switch
            {
                'N' => TokenKind.BlockNumber,
                'O' when !siemens => TokenKind.ProgramMarker,
                _ => TokenKind.Word,
            };
            output.Add(new Token(kind, start, end - start, letter, value, hasPoint));
            return end;
        }

        // Endereço com expressão: "X#7", "X-[#9+#14]" (Fanuc) ou "Z=R1", "X=IC(5)" (Siemens)
        int p = k;
        if (!siemens && p < n && (line[p] == '-' || line[p] == '+'))
        {
            p++;
        }

        if (!siemens && p < n && (line[p] == '#' || line[p] == '['))
        {
            int expressionEnd = line[p] == '['
                ? ReadGroup(line, p, '[', ']')
                : ReadVariableReference(line, p);
            output.Add(new Token(TokenKind.Expression, start, expressionEnd - start, letter, null));
            return expressionEnd;
        }

        if (hasEquals && k < n && !char.IsWhiteSpace(line[k]))
        {
            int expressionEnd = ReadUntilWhitespace(line, k);
            output.Add(new Token(TokenKind.Expression, start, expressionEnd - start, letter, null));
            return expressionEnd;
        }

        // Letra sem número válido depois dela
        output.Add(new Token(TokenKind.Unknown, start, k - start, '\0', null));
        return k;
    }

    /// <summary>
    /// Lê um identificador de várias letras: "CYCLE81(10,0,2,-5)", "CR=5", "DIAMOF", "IF [...]",
    /// "VOLTA:" (rótulo Siemens). Retorna a posição após o token.
    /// </summary>
    private static int ReadKeyword(string line, int start, Dialect dialect, List<Token> output)
    {
        int n = line.Length;
        int j = start;
        while (j < n && (char.IsAsciiLetterOrDigit(line[j]) || line[j] == '_'))
        {
            j++;
        }

        bool siemens = dialect == Dialect.Siemens;
        string word = line[start..j].ToUpperInvariant();

        // Siemens: condição sem parênteses obrigatórios ("IF R1>R2 GOTOF FIM", "FOR R1=0 TO 10",
        // "DEF REAL A=1, B"): a palavra-chave leva o resto da instrução junto
        if (siemens && word is "IF" or "ELSEIF" or "WHILE" or "UNTIL" or "FOR" or "DEF")
        {
            int end = ReadSiemensCondition(line, j, stopAtGoto: word == "IF");
            output.Add(new Token(TokenKind.Keyword, start, end - start, '\0', null));
            return end;
        }

        // Fanuc: "GOTO 260", "GOTO#10", "GOTO[#1+10]" (com espaço ou expressão depois)
        if (!siemens && word == "GOTO")
        {
            int p0 = j;
            while (p0 < n && line[p0] == ' ')
            {
                p0++;
            }

            int end = p0;
            if (p0 < n && char.IsAsciiDigit(line[p0]))
            {
                while (end < n && char.IsAsciiDigit(line[end])) end++;
            }
            else if (p0 < n && line[p0] == '#')
            {
                end = ReadVariableReference(line, p0);
            }
            else if (p0 < n && line[p0] == '[')
            {
                end = ReadGroup(line, p0, '[', ']');
            }

            if (end > p0)
            {
                output.Add(new Token(TokenKind.Keyword, start, end - start, '\0', null));
                return end;
            }
        }

        // Rótulo Siemens: "VOLTA:"
        if (siemens && j < n && line[j] == ':')
        {
            output.Add(new Token(TokenKind.Label, start, j + 1 - start, '\0', null));
            return j + 1;
        }

        // Atribuição: CR=5 (número) ou T="FRESA", AP=R2 (expressão)
        if (siemens && j < n && line[j] == '=')
        {
            if (TryReadNumber(line, j + 1, out double value, out int numberEnd, out _))
            {
                output.Add(new Token(TokenKind.Keyword, start, numberEnd - start, '\0', value));
                return numberEnd;
            }

            int expressionEnd = ReadUntilWhitespace(line, j + 1);
            output.Add(new Token(TokenKind.Expression, start, expressionEnd - start, '\0', null));
            return expressionEnd;
        }

        // Argumentos entre parênteses (Siemens) ou colchetes (Fanuc), com ou sem espaço antes:
        // CYCLE81(...), MSG("..."), IF (R1<=R2), IF [#20 GT #12], WHILE [#1 GE 0]
        char open = siemens ? '(' : '[';
        char close = siemens ? ')' : ']';
        int p = j;
        while (p < n && line[p] == ' ')
        {
            p++;
        }

        if (p < n && line[p] == open)
        {
            int end = ReadGroup(line, p, open, close);
            output.Add(new Token(TokenKind.Keyword, start, end - start, '\0', null));
            return end;
        }

        output.Add(new Token(TokenKind.Keyword, start, j - start, '\0', null));
        return j;
    }

    /// <summary>
    /// Fim de uma condição Siemens: até ";" (comentário), fim da linha ou — no IF de salto — até
    /// a palavra GOTO/GOTOF/GOTOB/GOTOC (fora de parênteses).
    /// </summary>
    private static int ReadSiemensCondition(string line, int start, bool stopAtGoto)
    {
        int depth = 0;
        int k = start;
        while (k < line.Length)
        {
            char c = line[k];
            if (c == '"')
            {
                int quoteEnd = line.IndexOf('"', k + 1);
                k = quoteEnd < 0 ? line.Length : quoteEnd + 1;
                continue;
            }

            if (c is '(' or '[') depth++;
            else if (c is ')' or ']') depth--;
            else if (depth <= 0 && c == ';') break;
            else if (depth <= 0 && stopAtGoto && (c is 'G' or 'g')
                     && (k == 0 || !char.IsAsciiLetterOrDigit(line[k - 1]))
                     && string.Compare(line, k, "GOTO", 0, 4, StringComparison.OrdinalIgnoreCase) == 0)
            {
                break;
            }

            k++;
        }

        while (k > start && char.IsWhiteSpace(line[k - 1]))
        {
            k--;
        }

        return k;
    }

    /// <summary>Fanuc ",R5." / ",C[#5]": canto arredondado ou chanfro.</summary>
    private static int ReadCornerWord(string line, int start, List<Token> output)
    {
        int k = start + 2;
        if (TryReadNumber(line, k, out double value, out int end, out _))
        {
            output.Add(new Token(TokenKind.Keyword, start, end - start, char.ToUpperInvariant(line[start + 1]), value));
            return end;
        }

        if (k < line.Length && (line[k] == '[' || line[k] == '#'))
        {
            int expressionEnd = line[k] == '[' ? ReadGroup(line, k, '[', ']') : ReadVariableReference(line, k);
            output.Add(new Token(TokenKind.Expression, start, expressionEnd - start, char.ToUpperInvariant(line[start + 1]), null));
            return expressionEnd;
        }

        output.Add(new Token(TokenKind.Unknown, start, 2, '\0', null));
        return start + 2;
    }

    /// <summary>A partir de um caractere de abertura, vai até o fechamento correspondente (respeitando aspas).</summary>
    private static int ReadGroup(string line, int start, char open, char close)
    {
        int n = line.Length;
        int depth = 0;
        int k = start;
        while (k < n)
        {
            char c = line[k];
            if (c == '"')
            {
                int quoteEnd = line.IndexOf('"', k + 1);
                k = quoteEnd < 0 ? n : quoteEnd + 1;
                continue;
            }

            if (c == open) depth++;
            else if (c == close) depth--;
            k++;
            if (depth == 0) break;
        }

        return k;
    }

    /// <summary>"#12" ou "#[...]".</summary>
    private static int ReadVariableReference(string line, int start)
    {
        int k = start + 1;
        if (k < line.Length && line[k] == '[')
        {
            return ReadGroup(line, k, '[', ']');
        }

        while (k < line.Length && char.IsAsciiDigit(line[k]))
        {
            k++;
        }

        return k;
    }

    /// <summary>Fim de uma instrução de macro Fanuc: ";" ou início de comentário, fora de colchetes.</summary>
    private static int ReadUntilStatementEnd(string line, int start)
    {
        int depth = 0;
        int k = start;
        while (k < line.Length)
        {
            char c = line[k];
            if (c == '[') depth++;
            else if (c == ']') depth--;
            else if (depth == 0 && (c == ';' || c == '(')) break;
            k++;
        }

        // Não inclui espaços finais
        while (k > start && char.IsWhiteSpace(line[k - 1]))
        {
            k--;
        }

        return k;
    }

    /// <summary>Até o próximo espaço fora de parênteses/colchetes (expressões Siemens: "R1=R1-(R1-R2)").</summary>
    private static int ReadUntilWhitespace(string line, int start)
    {
        int depth = 0;
        int k = start;
        while (k < line.Length)
        {
            char c = line[k];
            if (c == '"')
            {
                int quoteEnd = line.IndexOf('"', k + 1);
                k = quoteEnd < 0 ? line.Length : quoteEnd + 1;
                continue;
            }

            if (c is '(' or '[') depth++;
            else if (c is ')' or ']') depth--;
            else if (depth <= 0 && (char.IsWhiteSpace(c) || c == ';')) break;
            k++;
        }

        return k;
    }

    /// <summary>
    /// Lê um número no formato G-code: sinal opcional, dígitos e ponto decimal opcional.
    /// Aceita "10", "10.", ".5", "-.5", "+3.25". Precisa de pelo menos um dígito.
    /// </summary>
    internal static bool TryReadNumber(string s, int start, out double value, out int end) =>
        TryReadNumber(s, start, out value, out end, out _);

    internal static bool TryReadNumber(string s, int start, out double value, out int end, out bool hasPoint)
    {
        int n = s.Length;
        int i = start;
        bool anyDigit = false;
        hasPoint = false;

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
            hasPoint = true;
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
            hasPoint = false;
            return false;
        }

        value = double.Parse(s.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
        end = i;
        return true;
    }
}
