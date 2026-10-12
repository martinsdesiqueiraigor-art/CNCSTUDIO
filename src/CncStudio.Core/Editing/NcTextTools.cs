using System.Text;
using CncStudio.Core.Dialects;
using CncStudio.Core.Lexing;

namespace CncStudio.Core.Editing;

/// <summary>Como a renumeração trata os blocos.</summary>
public enum RenumberMode
{
    /// <summary>Numera todos os blocos com conteúdo (cria N onde não existe).</summary>
    AllBlocks,

    /// <summary>Renumera só os blocos que já têm N.</summary>
    ExistingOnly,

    /// <summary>Remove todos os números de bloco.</summary>
    Remove,
}

/// <summary>Parâmetros da renumeração.</summary>
public sealed record RenumberOptions(int Start = 10, int Increment = 10, RenumberMode Mode = RenumberMode.AllBlocks);

/// <summary>
/// Ferramentas de edição de programas NC (renumerar, maiúsculas...).
/// Trabalham sobre o texto inteiro e devolvem o texto novo, preservando
/// a quebra de linha original (\r\n ou \n). Usam o lexer, então respeitam comentários.
/// </summary>
public static class NcTextTools
{
    /// <summary>
    /// Renumera os blocos. Linhas vazias, só de comentário ou marcadores de programa
    /// (%, O1001, %_N_..._MPF) nunca recebem número.
    /// </summary>
    public static string RenumberBlocks(string text, Dialect dialect, RenumberOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Start, 0);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.Increment, 1);

        var tokens = new List<Token>();
        int next = options.Start;

        return TransformLines(text, line =>
        {
            GCodeLexer.TokenizeLine(line, dialect, tokens);
            int blockIndex = tokens.FindIndex(t => t.Kind == TokenKind.BlockNumber);
            bool hasContent = tokens.Exists(t => t.Kind is TokenKind.Word or TokenKind.Keyword or TokenKind.Expression or TokenKind.Label);

            switch (options.Mode)
            {
                case RenumberMode.Remove:
                    return blockIndex < 0 ? line : RemoveToken(line, tokens[blockIndex]);

                case RenumberMode.ExistingOnly:
                    if (blockIndex < 0)
                    {
                        return line;
                    }

                    return ReplaceToken(line, tokens[blockIndex], "N" + Take(ref next, options.Increment));

                default: // AllBlocks
                    if (blockIndex >= 0)
                    {
                        return ReplaceToken(line, tokens[blockIndex], "N" + Take(ref next, options.Increment));
                    }

                    if (!hasContent)
                    {
                        return line;
                    }

                    return InsertBlockNumber(line, "N" + Take(ref next, options.Increment));
            }
        });
    }

    /// <summary>Converte para maiúsculas tudo o que não é comentário.</summary>
    public static string ToUpperOutsideComments(string text, Dialect dialect)
    {
        var tokens = new List<Token>();
        return TransformLines(text, line =>
        {
            GCodeLexer.TokenizeLine(line, dialect, tokens);
            char[] chars = line.ToUpperInvariant().ToCharArray();

            // Restaura o texto original dos comentários
            foreach (Token token in tokens)
            {
                if (token.Kind == TokenKind.Comment)
                {
                    line.CopyTo(token.Column, chars, token.Column, token.Length);
                }
            }

            return new string(chars);
        });
    }

    /// <summary>
    /// Remove espaços entre palavras de endereço ("N10 G01 X10" → "N10G01X10") e os espaços
    /// no começo e no fim das linhas. Comentários, textos e palavras-chave (IF, GOTO, DEF...)
    /// ficam como estão, porque ali o espaço separa partes da instrução.
    /// </summary>
    public static string RemoveSpaces(string text, Dialect dialect)
    {
        var tokens = new List<Token>();
        return TransformLines(text, line =>
        {
            GCodeLexer.TokenizeLine(line, dialect, tokens);
            var result = new StringBuilder(line.Length);
            int position = 0;
            for (int i = 0; i < tokens.Count; i++)
            {
                Token token = tokens[i];
                string gap = line[position..token.Column];
                bool removable = i > 0 && IsAddress(tokens[i - 1]) && IsAddress(token) && gap.Trim().Length == 0;
                if (i > 0 && !removable)
                {
                    result.Append(gap.Length > 0 && gap.Trim().Length == 0 ? " " : gap); // vários espaços viram um
                }

                result.Append(line, token.Column, token.Length);
                position = token.Column + token.Length;
            }

            if (tokens.Count == 0)
            {
                return line.Trim();
            }

            result.Append(line[position..].TrimEnd());
            return result.ToString().TrimEnd();
        });
    }

    /// <summary>Insere um espaço entre palavras de endereço coladas ("N10G01X10" → "N10 G01 X10").</summary>
    public static string InsertSpaces(string text, Dialect dialect)
    {
        var tokens = new List<Token>();
        return TransformLines(text, line =>
        {
            GCodeLexer.TokenizeLine(line, dialect, tokens);
            var result = new StringBuilder(line.Length + tokens.Count);
            int position = 0;
            for (int i = 0; i < tokens.Count; i++)
            {
                Token token = tokens[i];
                result.Append(line, position, token.Column - position);
                bool glued = i > 0 && token.Column == position
                             && (IsAddress(tokens[i - 1]) || IsAddress(token))
                             && line[token.Column] != ',';           // canto Fanuc ",R" fica colado
                if (glued)
                {
                    result.Append(' ');
                }

                result.Append(line, token.Column, token.Length);
                position = token.Column + token.Length;
            }

            result.Append(line, position, line.Length - position);
            return result.ToString();
        });
    }

    /// <summary>
    /// Remove os comentários. Linhas que só tinham comentário somem; linhas que já
    /// eram vazias ficam (não muda a estrutura do arquivo à toa).
    /// </summary>
    public static string RemoveComments(string text, Dialect dialect)
    {
        var tokens = new List<Token>();
        return TransformLines(text, line =>
        {
            GCodeLexer.TokenizeLine(line, dialect, tokens);
            if (!tokens.Exists(t => t.Kind == TokenKind.Comment))
            {
                return line;
            }

            var result = new StringBuilder(line.Length);
            int position = 0;
            foreach (Token token in tokens)
            {
                if (token.Kind != TokenKind.Comment)
                {
                    continue;
                }

                result.Append(line, position, token.Column - position);
                position = token.Column + token.Length;
            }

            result.Append(line, position, line.Length - position);
            string cleaned = result.ToString().TrimEnd();
            return cleaned.Trim().Length == 0 ? null : cleaned;
        });
    }

    /// <summary>Palavra de endereço (X10, G01, N10) ou expressão de endereço do Fanuc (X#1).</summary>
    private static bool IsAddress(Token token) =>
        token.Kind is TokenKind.Word or TokenKind.BlockNumber;

    private static int Take(ref int next, int increment)
    {
        int value = next;
        next += increment;
        return value;
    }

    private static string ReplaceToken(string line, Token token, string replacement) =>
        string.Concat(line.AsSpan(0, token.Column), replacement, line.AsSpan(token.Column + token.Length));

    /// <summary>Remove o token e um espaço logo depois dele, se houver.</summary>
    private static string RemoveToken(string line, Token token)
    {
        int end = token.Column + token.Length;
        if (end < line.Length && line[end] == ' ')
        {
            end++;
        }

        return string.Concat(line.AsSpan(0, token.Column), line.AsSpan(end));
    }

    /// <summary>Insere "N.. " no começo do bloco (depois de espaços iniciais e do "/" de bloco opcional).</summary>
    private static string InsertBlockNumber(string line, string blockNumber)
    {
        int position = 0;
        while (position < line.Length && char.IsWhiteSpace(line[position]))
        {
            position++;
        }

        if (position < line.Length && line[position] == '/')
        {
            position++;
        }

        return string.Concat(line.AsSpan(0, position), blockNumber, " ", line.AsSpan(position));
    }

    /// <summary>
    /// Aplica uma transformação a cada linha, preservando a quebra de linha original.
    /// A transformação pode devolver null para apagar a linha.
    /// </summary>
    private static string TransformLines(string text, Func<string, string?> transform)
    {
        string newline = text.Contains("\r\n") ? "\r\n" : "\n";
        string[] lines = text.Split('\n');
        var kept = new List<string>(lines.Length);

        foreach (string raw in lines)
        {
            if (transform(raw.TrimEnd('\r')) is { } line)
            {
                kept.Add(line);
            }
        }

        return string.Join(newline, kept);
    }
}
