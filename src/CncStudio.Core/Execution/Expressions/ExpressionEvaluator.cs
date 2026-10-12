using System.Globalization;
using CncStudio.Core.Dialects;

namespace CncStudio.Core.Execution.Expressions;

/// <summary>Erro de sintaxe ou de cálculo numa expressão.</summary>
public sealed class ExpressionException(string message) : Exception(message);

/// <summary>Onde o avaliador lê e grava variáveis ("#1", "R12", "CONTADOR").</summary>
public interface IVariableScope
{
    /// <summary>Valor da variável, ou null se vazia/indefinida.</summary>
    double? Get(string name);

    void Set(string name, double? value);
}

/// <summary>
/// Avalia expressões de macro dos dois dialetos:
/// <list type="bullet">
/// <item>Fanuc: <c>#1</c>, <c>#[#2+1]</c>, <c>[ ]</c>, <c>+ - * / MOD</c>, <c>EQ NE GT GE LT LE</c>,
/// <c>AND OR XOR</c>, funções com colchetes (<c>SIN[30]</c>, <c>ATAN[#1]/[#2]</c>).</item>
/// <item>Siemens: <c>R1</c>, variáveis DEF, <c>( )</c>, <c>+ - * / DIV MOD</c>, <c>== &lt;&gt; &gt; &gt;= &lt; &lt;=</c>,
/// <c>AND OR XOR NOT</c>, funções com parênteses (<c>SIN(30)</c>, <c>ATAN2(a,b)</c>, <c>POT(x)</c>).</item>
/// </list>
/// Ângulos em graus. Comparações e lógica devolvem 1 (verdadeiro) ou 0 (falso).
/// Variável vazia vale 0 em contas (e gera aviso pelo callback).
/// </summary>
public sealed class ExpressionEvaluator
{
    private enum Kind
    {
        Number,
        Identifier,   // nomes, R-parâmetros (R12), palavras como EQ, AND, SIN
        Variable,     // Fanuc #12 (nome já resolvido)
        VariableIndirect, // Fanuc #[...]
        Operator,     // + - * / , ( ) [ ] == <> <= >= < > =
        End,
    }

    private readonly record struct Tok(Kind Kind, string Text, double Number = 0);

    private readonly IVariableScope _variables;
    private readonly Action<string>? _warn;
    private readonly List<Tok> _tokens;
    private int _position;

    // O dialeto é aceito na assinatura pública para futuras diferenças; hoje a sintaxe é unificada
    private ExpressionEvaluator(string text, IVariableScope variables, Action<string>? warn)
    {
        _variables = variables;
        _warn = warn;
        _tokens = Tokenize(text);
    }

    /// <summary>Avalia a expressão inteira. Lança <see cref="ExpressionException"/> se for inválida.</summary>
    public static double Evaluate(string text, Dialect dialect, IVariableScope variables, Action<string>? warn = null)
    {
        _ = dialect;
        var evaluator = new ExpressionEvaluator(text, variables, warn);
        double value = evaluator.ParseOr();
        if (evaluator.Peek.Kind != Kind.End)
        {
            throw new ExpressionException($"Texto inesperado na expressão: '{evaluator.Peek.Text}'.");
        }

        return value;
    }

    /// <summary>Condição: verdadeira se o resultado for diferente de zero.</summary>
    public static bool EvaluateCondition(string text, Dialect dialect, IVariableScope variables, Action<string>? warn = null) =>
        Evaluate(text, dialect, variables, warn) != 0;

    /// <summary>
    /// Nome da variável referida pelo texto ("#5", "#[#1+2]", "R3", "CONTADOR"), para o lado
    /// esquerdo de uma atribuição.
    /// </summary>
    public static string ResolveTarget(string text, Dialect dialect, IVariableScope variables, Action<string>? warn = null)
    {
        string trimmed = text.Trim();
        if (trimmed.StartsWith("#[", StringComparison.Ordinal))
        {
            int index = (int)Math.Round(Evaluate(trimmed[1..], dialect, variables, warn));
            return "#" + index.ToString(CultureInfo.InvariantCulture);
        }

        if (trimmed.StartsWith('#'))
        {
            if (!int.TryParse(trimmed.AsSpan(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
            {
                throw new ExpressionException($"Variável inválida: '{trimmed}'.");
            }

            return "#" + number.ToString(CultureInfo.InvariantCulture);
        }

        if (trimmed.Length == 0 || !(char.IsAsciiLetter(trimmed[0]) || trimmed[0] == '_' || trimmed[0] == '$'))
        {
            throw new ExpressionException($"Destino de atribuição inválido: '{trimmed}'.");
        }

        return trimmed.ToUpperInvariant();
    }

    // ───────────────────────── Gramática ─────────────────────────

    private Tok Peek => _tokens[_position];

    private Tok Next() => _tokens[_position++];

    private bool IsWord(string word) => Peek.Kind == Kind.Identifier && Peek.Text == word;

    private bool IsOp(string op) => Peek.Kind == Kind.Operator && Peek.Text == op;

    private double ParseOr()
    {
        double left = ParseAnd();
        while (IsWord("OR") || IsWord("XOR") || IsWord("B_OR") || IsWord("B_XOR"))
        {
            string op = Next().Text;
            double right = ParseAnd();
            left = op switch
            {
                "OR" => Bool(left != 0 || right != 0),
                "XOR" => Bool((left != 0) ^ (right != 0)),
                "B_OR" => (long)left | (long)right,
                _ => (long)left ^ (long)right,
            };
        }

        return left;
    }

    private double ParseAnd()
    {
        double left = ParseComparison();
        while (IsWord("AND") || IsWord("B_AND"))
        {
            string op = Next().Text;
            double right = ParseComparison();
            left = op == "AND" ? Bool(left != 0 && right != 0) : (long)left & (long)right;
        }

        return left;
    }

    private double ParseComparison()
    {
        double left = ParseAdditive();
        string? op = Peek switch
        {
            { Kind: Kind.Identifier, Text: "EQ" or "NE" or "GT" or "GE" or "LT" or "LE" } t => t.Text,
            { Kind: Kind.Operator, Text: "==" or "<>" or ">" or ">=" or "<" or "<=" or "=" } t => t.Text,
            _ => null,
        };

        if (op is null)
        {
            return left;
        }

        Next();
        double right = ParseAdditive();
        const double tolerance = 1e-9;
        return op switch
        {
            "EQ" or "==" or "=" => Bool(Math.Abs(left - right) <= tolerance),
            "NE" or "<>" => Bool(Math.Abs(left - right) > tolerance),
            "GT" or ">" => Bool(left > right + tolerance),
            "GE" or ">=" => Bool(left >= right - tolerance),
            "LT" or "<" => Bool(left < right - tolerance),
            _ => Bool(left <= right + tolerance),
        };
    }

    private double ParseAdditive()
    {
        double left = ParseMultiplicative();
        while (IsOp("+") || IsOp("-"))
        {
            string op = Next().Text;
            double right = ParseMultiplicative();
            left = op == "+" ? left + right : left - right;
        }

        return left;
    }

    private double ParseMultiplicative()
    {
        double left = ParseUnary();
        while (IsOp("*") || IsOp("/") || IsWord("MOD") || IsWord("DIV"))
        {
            string op = Next().Text;
            double right = ParseUnary();
            left = op switch
            {
                "*" => left * right,
                "/" => right == 0 ? throw new ExpressionException("Divisão por zero.") : left / right,
                "MOD" => right == 0 ? throw new ExpressionException("MOD por zero.") : left - right * Math.Truncate(left / right),
                _ => right == 0 ? throw new ExpressionException("DIV por zero.") : Math.Truncate(left / right),
            };
        }

        return left;
    }

    private double ParseUnary()
    {
        if (IsOp("-"))
        {
            Next();
            return -ParseUnary();
        }

        if (IsOp("+"))
        {
            Next();
            return ParseUnary();
        }

        if (IsWord("NOT"))
        {
            Next();
            return Bool(ParseUnary() == 0);
        }

        return ParsePrimary();
    }

    private double ParsePrimary()
    {
        Tok token = Next();
        switch (token.Kind)
        {
            case Kind.Number:
                return token.Number;

            case Kind.Variable:
                return Read(token.Text);

            case Kind.VariableIndirect:
            {
                double index = ParseGroup(); // #[...]
                return Read("#" + ((int)Math.Round(index)).ToString(CultureInfo.InvariantCulture));
            }

            case Kind.Operator when token.Text is "(" or "[":
            {
                _position--;
                return ParseGroup();
            }

            case Kind.Identifier:
                if (IsOp("(") || IsOp("["))
                {
                    return CallFunction(token.Text);
                }

                return token.Text switch
                {
                    "TRUE" => 1,
                    "FALSE" => 0,
                    _ => Read(token.Text),
                };

            case Kind.End:
                throw new ExpressionException("Expressão incompleta.");

            default:
                throw new ExpressionException($"Texto inesperado na expressão: '{token.Text}'.");
        }
    }

    /// <summary>( expr ) ou [ expr ].</summary>
    private double ParseGroup()
    {
        Tok open = Next();
        string close = open.Text == "(" ? ")" : "]";
        if (open.Text is not ("(" or "["))
        {
            throw new ExpressionException("Esperado '(' ou '['.");
        }

        double value = ParseOr();
        Expect(close);
        return value;
    }

    private List<double> ParseArguments()
    {
        Tok open = Next();
        string close = open.Text == "(" ? ")" : "]";
        var arguments = new List<double>();
        if (IsOp(close))
        {
            Next();
            return arguments;
        }

        arguments.Add(ParseOr());
        while (IsOp(","))
        {
            Next();
            arguments.Add(ParseOr());
        }

        Expect(close);
        return arguments;
    }

    private double CallFunction(string name)
    {
        List<double> a = ParseArguments();

        // Fanuc: ATAN[y]/[x] (forma de dois argumentos)
        if (name == "ATAN" && a.Count == 1 && IsOp("/") && _tokens[_position + 1] is { Kind: Kind.Operator, Text: "[" })
        {
            Next();
            List<double> divisor = ParseArguments();
            a.Add(divisor.Count > 0 ? divisor[0] : 1);
        }

        double Arg(int i) => i < a.Count ? a[i] : throw new ExpressionException($"{name}: faltam argumentos.");
        const double toRadians = Math.PI / 180;
        const double toDegrees = 180 / Math.PI;

        return name switch
        {
            "SIN" => Math.Sin(Arg(0) * toRadians),
            "COS" => Math.Cos(Arg(0) * toRadians),
            "TAN" => Math.Tan(Arg(0) * toRadians),
            "ASIN" => Math.Asin(Arg(0)) * toDegrees,
            "ACOS" => Math.Acos(Arg(0)) * toDegrees,
            "ATAN" when a.Count >= 2 => Atan2Degrees(Arg(0), Arg(1)),
            "ATAN" => Math.Atan(Arg(0)) * toDegrees,
            "ATAN2" => Atan2Degrees(Arg(0), Arg(1)),
            "SQRT" or "SQR" => Arg(0) < 0 ? throw new ExpressionException("Raiz de número negativo.") : Math.Sqrt(Arg(0)),
            "ABS" => Math.Abs(Arg(0)),
            "ROUND" => Math.Round(Arg(0), MidpointRounding.AwayFromZero),
            "ROUNDUP" => Math.Ceiling(Arg(0)),
            "FIX" => Math.Floor(Arg(0)),
            "FUP" => Math.Ceiling(Arg(0)),
            "TRUNC" => Math.Truncate(Arg(0)),
            "LN" => Math.Log(Arg(0)),
            "EXP" => Math.Exp(Arg(0)),
            "POT" => a.Count >= 2 ? Math.Pow(Arg(0), Arg(1)) : Arg(0) * Arg(0),
            "POW" => Math.Pow(Arg(0), Arg(1)),
            "MINVAL" => Math.Min(Arg(0), Arg(1)),
            "MAXVAL" => Math.Max(Arg(0), Arg(1)),
            "BIN" or "BCD" => Arg(0),
            _ => throw new ExpressionException($"Função {name} não é suportada."),
        };
    }

    private static double Atan2Degrees(double y, double x)
    {
        double degrees = Math.Atan2(y, x) * 180 / Math.PI;
        return degrees < 0 ? degrees + 360 : degrees; // Fanuc devolve 0–360
    }

    private void Expect(string op)
    {
        if (!IsOp(op))
        {
            throw new ExpressionException($"Esperado '{op}' na expressão.");
        }

        Next();
    }

    private double Read(string name)
    {
        double? value = _variables.Get(name);
        if (value is null)
        {
            _warn?.Invoke($"Variável {name} sem valor (usado 0).");
            return 0;
        }

        return value.Value;
    }

    private static double Bool(bool value) => value ? 1 : 0;

    // ───────────────────────── Tokens ─────────────────────────

    private static List<Tok> Tokenize(string text)
    {
        var tokens = new List<Tok>();
        int i = 0;
        int n = text.Length;

        while (i < n)
        {
            char c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < n && char.IsAsciiDigit(text[i + 1])))
            {
                int start = i;
                while (i < n && (char.IsAsciiDigit(text[i]) || text[i] == '.'))
                {
                    i++;
                }

                string number = text[start..i];
                if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed))
                {
                    throw new ExpressionException($"Número inválido: '{number}'.");
                }

                tokens.Add(new Tok(Kind.Number, number, parsed));
                continue;
            }

            if (c == '#')
            {
                if (i + 1 < n && text[i + 1] == '[')
                {
                    tokens.Add(new Tok(Kind.VariableIndirect, "#"));
                    i++;
                    continue;
                }

                int start = ++i;
                while (i < n && char.IsAsciiDigit(text[i]))
                {
                    i++;
                }

                if (i == start)
                {
                    throw new ExpressionException("'#' sem número de variável.");
                }

                tokens.Add(new Tok(Kind.Variable, "#" + int.Parse(text.AsSpan(start, i - start), CultureInfo.InvariantCulture)));
                continue;
            }

            if (char.IsAsciiLetter(c) || c == '_' || c == '$')
            {
                int start = i;
                while (i < n && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_' || text[i] == '$'))
                {
                    i++;
                }

                tokens.Add(new Tok(Kind.Identifier, text[start..i].ToUpperInvariant()));
                continue;
            }

            // Operadores de dois caracteres primeiro
            if (i + 1 < n)
            {
                string two = text.Substring(i, 2);
                if (two is "==" or "<>" or "<=" or ">=")
                {
                    tokens.Add(new Tok(Kind.Operator, two));
                    i += 2;
                    continue;
                }
            }

            if ("+-*/,()[]<>=".Contains(c))
            {
                tokens.Add(new Tok(Kind.Operator, c.ToString()));
                i++;
                continue;
            }

            throw new ExpressionException($"Caractere inesperado na expressão: '{c}'.");
        }

        tokens.Add(new Tok(Kind.End, "fim"));
        return tokens;
    }
}
