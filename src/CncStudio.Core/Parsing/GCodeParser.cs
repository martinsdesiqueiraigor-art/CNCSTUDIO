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
        string? programId = null;
        List<AddressWord>? words = null;
        List<Statement>? statements = null;

        foreach (Token token in tokens)
        {
            switch (token.Kind)
            {
                case TokenKind.Comment:
                    break;

                case TokenKind.ProgramMarker:
                    programId = ProgramIdOf(token, line);
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

        if (words is null && statements is null && programId is null)
        {
            return null;
        }

        return new NcBlock
        {
            LineNumber = lineNumber,
            BlockNumber = blockNumber,
            Words = (IReadOnlyList<AddressWord>?)words ?? [],
            Statements = (IReadOnlyList<Statement>?)statements ?? [],
            ProgramId = programId,
        };
    }

    /// <summary>"O1234" → "1234"; "%_N_FURO_SPF" → "FURO"; "%" → null.</summary>
    private static string? ProgramIdOf(Token token, string line)
    {
        if (token.Letter == 'O' && token.Value is { } number)
        {
            return ((int)number).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        string text = token.GetText(line).Trim();
        if (text.StartsWith("%_N_", StringComparison.OrdinalIgnoreCase))
        {
            string name = text[4..];
            int suffix = name.LastIndexOf('_');
            if (suffix > 0)
            {
                name = name[..suffix]; // remove _MPF / _SPF
            }

            return name.ToUpperInvariant();
        }

        return null;
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
                // Palavra-chave: nome (identificador inicial) + argumentos (o resto, sem o grupo externo)
                int nameEnd = 0;
                while (nameEnd < text.Length && (char.IsAsciiLetterOrDigit(text[nameEnd]) || text[nameEnd] == '_'))
                {
                    nameEnd++;
                }

                string name = text[..nameEnd].ToUpperInvariant();
                double? value = token.Value;
                if (token.Letter != '\0')
                {
                    name = "," + token.Letter; // canto Fanuc ",R" / ",C"
                }
                else if (SplitNumberedKeyword(name) is { } numbered)
                {
                    // Fanuc "GOTO260", "DO2", "END2" → nome + número
                    (name, value) = numbered;
                }

                string? arguments = null;
                string rest = nameEnd < text.Length ? text[nameEnd..].Trim() : "";
                if (rest.StartsWith('='))
                {
                    rest = ""; // "CR=5": o valor já está em Value
                }

                if (rest.Length > 0)
                {
                    arguments = StripOuterGroup(rest);
                }

                return new Statement(StatementKind.Keyword, name, value, arguments, text, token.Column, token.Length);
        }
    }

    /// <summary>"GOTO260" → ("GOTO", 260); "DO2" → ("DO", 2); "END2" → ("END", 2). Outros → null.</summary>
    private static (string Name, double Value)? SplitNumberedKeyword(string name)
    {
        foreach (string prefix in (string[])["GOTO", "DO", "END"])
        {
            if (name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.Ordinal)
                && name.AsSpan(prefix.Length).IndexOfAnyExceptInRange('0', '9') < 0)
            {
                return (prefix, int.Parse(name.AsSpan(prefix.Length), System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        return null;
    }

    /// <summary>"(R1<=R2)" → "R1<=R2"; "[#1 GE 0]" → "#1 GE 0"; "(a)+(b)" fica como está.</summary>
    private static string StripOuterGroup(string text)
    {
        if (text.Length < 2 || !(text[0] is '(' or '['))
        {
            return text;
        }

        char open = text[0];
        char close = open == '(' ? ')' : ']';
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '"')
            {
                int quoteEnd = text.IndexOf('"', i + 1);
                if (quoteEnd < 0) break;
                i = quoteEnd;
                continue;
            }

            if (text[i] == open) depth++;
            else if (text[i] == close) depth--;

            if (depth == 0)
            {
                return i == text.Length - 1 ? text[1..^1] : text;
            }
        }

        return text;
    }
}
