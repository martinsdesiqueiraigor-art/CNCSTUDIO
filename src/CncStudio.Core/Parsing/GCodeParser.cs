using CncStudio.Core.Dialects;
using CncStudio.Core.Lexing;

namespace CncStudio.Core.Parsing;

/// <summary>
/// Converte o texto do programa em blocos (<see cref="NcBlock"/>), só com a sintaxe.
/// Erros aqui são de escrita (texto não reconhecido). O significado — modal, posições,
/// ciclos, comandos não suportados — fica com o <c>MachineExecutor</c>.
/// </summary>
public static class GCodeParser
{
    /// <summary>Faz o parse do programa completo.</summary>
    public static ParseResult Parse(string text, Dialect dialect)
    {
        var blocks = new List<NcBlock>();
        var diagnostics = new List<ParseDiagnostic>();
        var tokens = new List<Token>();

        using var reader = new StringReader(text);
        int lineNumber = 0;
        string? line;

        while ((line = reader.ReadLine()) != null)
        {
            lineNumber++;
            GCodeLexer.TokenizeLine(line, dialect, tokens);
            if (tokens.Count == 0)
            {
                continue;
            }

            NcBlock? block = ParseLine(line, lineNumber, tokens, diagnostics);
            if (block is not null)
            {
                blocks.Add(block);
            }
        }

        return new ParseResult(dialect, blocks, diagnostics);
    }

    /// <summary>Monta o bloco de uma linha. Retorna null se a linha só tem comentários/marcadores.</summary>
    private static NcBlock? ParseLine(string line, int lineNumber, List<Token> tokens, List<ParseDiagnostic> diagnostics)
    {
        int? blockNumber = null;
        List<AddressWord>? words = null;
        List<Statement>? statements = null;

        foreach (Token token in tokens)
        {
            switch (token.Kind)
            {
                case TokenKind.Comment:
                case TokenKind.ProgramMarker:
                    break;

                case TokenKind.BlockNumber:
                    blockNumber = (int)token.Value!.Value;
                    break;

                case TokenKind.Word:
                    (words ??= []).Add(new AddressWord(token.Letter, token.Value!.Value, token.HasDecimalPoint, token.Column, token.Length));
                    break;

                case TokenKind.Keyword:
                case TokenKind.Expression:
                case TokenKind.Label:
                    (statements ??= []).Add(ToStatement(line, token));
                    break;

                case TokenKind.Unknown:
                    diagnostics.Add(new ParseDiagnostic(lineNumber, token.Column, token.Length, DiagnosticSeverity.Error,
                        $"Texto não reconhecido: '{token.GetText(line)}'."));
                    break;
            }
        }

        if (words is null && statements is null)
        {
            return null;
        }

        return new NcBlock
        {
            LineNumber = lineNumber,
            BlockNumber = blockNumber,
            Words = (IReadOnlyList<AddressWord>?)words ?? [],
            Statements = (IReadOnlyList<Statement>?)statements ?? [],
        };
    }

    private static Statement ToStatement(string line, Token token)
    {
        string text = token.GetText(line);
        switch (token.Kind)
        {
            case TokenKind.Label:
                return new Statement(StatementKind.Label, text.TrimEnd(':').ToUpperInvariant(), null, null, text, token.Column, token.Length);

            case TokenKind.Expression:
                // Canto Fanuc com expressão (",R[#8]") mantém o nome curto
                string expressionName = text.StartsWith(',') ? "," + token.Letter : text;
                return new Statement(StatementKind.Expression, expressionName, null, null, text, token.Column, token.Length);

            default:
                // Palavra-chave: separa nome e argumentos ("CYCLE81(10,0)" → "CYCLE81" + "10,0")
                int open = text.IndexOfAny(['(', '[']);
                int equals = text.IndexOf('=');
                int nameEnd = open >= 0 ? open : equals >= 0 ? equals : text.Length;
                string name = text[..nameEnd].Trim().ToUpperInvariant();
                if (token.Letter != '\0')
                {
                    name = "," + token.Letter; // canto Fanuc ",R" / ",C"
                }

                string? arguments = null;
                if (open >= 0)
                {
                    int close = text.LastIndexOfAny([')', ']']);
                    arguments = close > open ? text[(open + 1)..close] : text[(open + 1)..];
                }

                return new Statement(StatementKind.Keyword, name, token.Value, arguments, text, token.Column, token.Length);
        }
    }
}
