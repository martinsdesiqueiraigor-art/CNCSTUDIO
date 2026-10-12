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
            bool hasContent = tokens.Exists(t => t.Kind is TokenKind.Word or TokenKind.Keyword);

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

    /// <summary>Aplica uma transformação a cada linha, preservando a quebra de linha original.</summary>
    private static string TransformLines(string text, Func<string, string> transform)
    {
        string newline = text.Contains("\r\n") ? "\r\n" : "\n";
        string[] lines = text.Split('\n');
        var result = new StringBuilder(text.Length + lines.Length * 4);

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            result.Append(transform(line));
            if (i < lines.Length - 1)
            {
                result.Append(newline);
            }
        }

        return result.ToString();
    }
}
