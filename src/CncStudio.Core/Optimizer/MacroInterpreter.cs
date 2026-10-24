using System.Globalization;
using System.Text;

namespace CncStudio.Core.Optimizer;

public sealed record MacroExpansion(IReadOnlyList<NcBlock> Blocks, IReadOnlyList<string> Text,
    IReadOnlyDictionary<int, double> Variables, string? Error)
{
    public bool Ok => Error is null;
}

/// <summary>Independent interpreter for the FANUC Macro B subset the generator may emit:
/// #n = expr, WHILE [cond] DOm / ENDm (m = 1..3, nesting up to 3) and NC words whose value is
/// a literal, #n or [expr]; functions SIN/COS (degrees). IF, GOTO, M98/M99, G65 and system variables
/// are rejected. Like the control, a computed dimension is rounded to the least input increment
/// (0.001 mm in G21, 0.0001 inch in G20). It knows nothing about the generator: it only executes the text.</summary>
public static class MacroInterpreter
{
    private const int MaxBlocks = 500_000;

    /// <param name="units">G20/G21 in effect where the fragment starts (rounding of computed values).</param>
    public static MacroExpansion Expand(string fragment, int units = 21)
    {
        var blocks = new List<NcBlock>();
        var text = new List<string>();
        var vars = new Dictionary<int, double>();
        var lines = fragment.Split('\n');
        var statements = new List<(int Line, string Code)>();
        for (int i = 0; i < lines.Length; i++)
        {
            var code = StripComments(lines[i], out var commentError);
            if (commentError) return Fail("Linha " + (i + 1) + ": comentario sem fechamento");
            code = new string(code.Where(c => !char.IsWhiteSpace(c)).ToArray()).ToUpperInvariant();
            if (code.Length > 0) statements.Add((i + 1, code));
        }
        // Pre-compute WHILE/END pairs; FANUC accepts only identifiers 1..3.
        var match = new Dictionary<int, int>();
        var open = new Stack<(int Index, int Id)>();
        for (int k = 0; k < statements.Count; k++)
        {
            var c = statements[k].Code;
            if (c.StartsWith("WHILE", StringComparison.Ordinal))
            {
                var at = c.LastIndexOf("]DO", StringComparison.Ordinal);
                if (at < 0 || !int.TryParse(c[(at + 3)..], NumberStyles.None, CultureInfo.InvariantCulture, out var id) || id is < 1 or > 3)
                    return Fail("Linha " + statements[k].Line + ": WHILE exige DO1, DO2 ou DO3");
                if (open.Any(o => o.Id == id) || open.Count >= 3)
                    return Fail("Linha " + statements[k].Line + ": aninhamento invalido de DO" + id);
                open.Push((k, id));
            }
            else if (IsEnd(c, out var endId))
            {
                if (open.Count == 0 || open.Peek().Id != endId)
                    return Fail("Linha " + statements[k].Line + ": END" + endId + " sem WHILE correspondente");
                var w = open.Pop();
                match[w.Index] = k; match[k] = w.Index;
            }
        }
        if (open.Count > 0) return Fail("WHILE sem END correspondente");

        for (int pc = 0; pc < statements.Count; pc++)
        {
            var (line, code) = statements[pc];
            try
            {
                if (code.StartsWith("WHILE", StringComparison.Ordinal))
                {
                    var cond = code[5..code.LastIndexOf("DO", StringComparison.Ordinal)];
                    if (!Condition(cond, vars)) pc = match[pc];
                }
                else if (IsEnd(code, out _)) pc = match[pc] - 1;
                else if (code[0] == '#')
                {
                    var eq = code.IndexOf('=');
                    if (eq < 0) return Fail("Linha " + line + ": atribuicao invalida");
                    var target = Variable(code[1..eq], line);
                    vars[target] = Evaluate(code[(eq + 1)..], vars);
                }
                else if (code.StartsWith("IF", StringComparison.Ordinal) || code.StartsWith("GOTO", StringComparison.Ordinal))
                    return Fail("Linha " + line + ": IF/GOTO nao permitido");
                else
                {
                    var (block, expanded) = NcLine(line, code, vars, units);
                    foreach (var g in block.Words.Where(w => w.Letter == 'G' && w.Value is 20 or 21)) units = (int)g.Value;
                    if (block.Unsupported is not null) return Fail("Linha " + line + ": " + block.Unsupported);
                    // M98 is resolved by NcMachine against subprograms of the same file; M99/G65/G66 are not allowed here.
                    if (block.Words.Any(w => (w.Letter == 'M' && w.Value == 99) || (w.Letter == 'G' && w.Value is 65 or 66 or 67)))
                        return Fail("Linha " + line + ": M99/G65/G66 nao permitido no fragmento");
                    blocks.Add(block with { Line = blocks.Count + 1 });
                    text.Add(expanded);
                    if (blocks.Count > MaxBlocks) return Fail("Expansao excede " + MaxBlocks + " blocos");
                }
            }
            catch (MacroException e) { return Fail("Linha " + line + ": " + e.Message); }
        }
        return new(blocks, text, vars, null);

        MacroExpansion Fail(string error) => new(blocks, text, vars, error);
    }

    private static bool IsEnd(string code, out int id)
    {
        id = 0;
        return code.StartsWith("END", StringComparison.Ordinal) && code.Length == 4 &&
               int.TryParse(code[3..], NumberStyles.None, CultureInfo.InvariantCulture, out id) && id is >= 1 and <= 3;
    }

    private static string StripComments(string line, out bool error)
    {
        var sb = new StringBuilder(); error = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '(')
            {
                var close = line.IndexOf(')', i + 1);
                if (close < 0) { error = true; return ""; }
                i = close;
            }
            else sb.Append(line[i]);
        }
        return sb.ToString();
    }

    private static (NcBlock, string) NcLine(int line, string code, Dictionary<int, double> vars, int units)
    {
        if (!code.Contains('#') && !code.Contains('['))
        {
            var plain = NcTokenizer.Tokenize(line, code);
            return (plain, string.Join(" ", plain.Words.Select(w => w.Text)));
        }
        var words = new List<NcWord>();
        int i = 0;
        while (i < code.Length)
        {
            var letter = code[i];
            if (letter < 'A' || letter > 'Z') throw new MacroException("caractere inesperado " + letter);
            i++;
            if (i < code.Length && (code[i] == '#' || code[i] == '[' ||
                ((code[i] == '-' || code[i] == '+') && i + 1 < code.Length && (code[i + 1] == '#' || code[i + 1] == '['))))
            {
                var p = new Parser(code, i, vars);
                var value = p.Unary();
                if ("XYZIJKRQ".Contains(letter))
                    value = Math.Round(value, units == 20 ? 4 : 3, MidpointRounding.AwayFromZero);
                i = p.Position;
                words.Add(new(letter, value, true, letter + NcTokenizer.Decimal(value)));
            }
            else
            {
                int start = i;
                if (i < code.Length && (code[i] == '-' || code[i] == '+')) i++;
                while (i < code.Length && (char.IsDigit(code[i]) || code[i] == '.')) i++;
                var literal = NcTokenizer.Tokenize(line, letter + code[start..i]);
                if (literal.Unsupported is not null || literal.Words.Count != 1) throw new MacroException("palavra invalida " + letter);
                words.Add(literal.Words[0]);
            }
        }
        var text = string.Join(" ", words.Select(w => w.Text));
        var block = NcTokenizer.Tokenize(line, text);
        return (block, text);
    }

    private static int Variable(string text, int line)
    {
        if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var n) || n < 1 || n > 999)
            throw new MacroException("variavel invalida #" + text + " (somente #1-#999)");
        return n;
    }

    private static double Evaluate(string expression, Dictionary<int, double> vars)
    {
        var p = new Parser(expression, 0, vars);
        var value = p.Expression();
        if (p.Position != expression.Length) throw new MacroException("expressao invalida: " + expression);
        return value;
    }

    private static bool Condition(string text, Dictionary<int, double> vars)
    {
        if (text.Length < 2 || text[0] != '[' || text[^1] != ']') throw new MacroException("condicao deve estar entre colchetes");
        var inner = text[1..^1];
        foreach (var op in new[] { "EQ", "NE", "LT", "LE", "GT", "GE" })
        {
            var at = inner.IndexOf(op, StringComparison.Ordinal);
            if (at <= 0) continue;
            var a = Evaluate(inner[..at], vars); var b = Evaluate(inner[(at + 2)..], vars);
            return op switch { "EQ" => a == b, "NE" => a != b, "LT" => a < b, "LE" => a <= b, "GT" => a > b, _ => a >= b };
        }
        throw new MacroException("operador de comparacao ausente");
    }

    private sealed class MacroException(string message) : Exception(message);

    private sealed class Parser(string text, int position, Dictionary<int, double> vars)
    {
        private static readonly (string Name, Func<double, double> Function)[] Functions =
        {
            ("SIN[", a => Math.Sin(a * Math.PI / 180)),
            ("COS[", a => Math.Cos(a * Math.PI / 180))
        };

        public int Position { get; private set; } = position;

        public double Expression()
        {
            var value = Term();
            while (Position < text.Length && (text[Position] == '+' || text[Position] == '-'))
            {
                var op = text[Position++];
                var right = Term();
                value = op == '+' ? value + right : value - right;
            }
            return value;
        }

        private double Term()
        {
            var value = Unary();
            while (Position < text.Length && (text[Position] == '*' || text[Position] == '/'))
            {
                var op = text[Position++];
                var right = Unary();
                if (op == '/' && right == 0) throw new MacroException("divisao por zero");
                value = op == '*' ? value * right : value / right;
            }
            return value;
        }

        public double Unary()
        {
            if (Position < text.Length && text[Position] == '-') { Position++; return -Unary(); }
            if (Position < text.Length && text[Position] == '+') { Position++; return Unary(); }
            if (Position >= text.Length) throw new MacroException("expressao incompleta");
            foreach (var (name, function) in Functions)
                if (string.CompareOrdinal(text, Position, name, 0, name.Length) == 0)
                {
                    Position += name.Length - 1; // keep '[' for the bracket rule below
                    return function(Unary());
                }
            if (text[Position] == '[')
            {
                Position++;
                var value = Expression();
                if (Position >= text.Length || text[Position] != ']') throw new MacroException("colchete sem fechamento");
                Position++;
                return value;
            }
            if (text[Position] == '#')
            {
                Position++;
                int start = Position;
                while (Position < text.Length && char.IsDigit(text[Position])) Position++;
                var n = Variable(text[start..Position], 0);
                if (!vars.TryGetValue(n, out var v)) throw new MacroException("variavel #" + n + " vazia");
                return v;
            }
            int s = Position;
            while (Position < text.Length && (char.IsDigit(text[Position]) || text[Position] == '.')) Position++;
            if (Position == s || !double.TryParse(text[s..Position], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                throw new MacroException("numero esperado");
            return number;
        }
    }
}
