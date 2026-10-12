using System.Globalization;
using CncStudio.Core.Dialects;
using CncStudio.Core.Lexing;

namespace CncStudio.Core.Parsing;

/// <summary>
/// Converte o texto do programa em blocos com estado modal resolvido.
///
/// Escopo do protótipo: G00–G03, M03/M04/M05, M30/M02, F, S, eixos X/Y/Z e centro I/J.
/// Condições assumidas: milímetros, plano XY, coordenadas absolutas, sem compensação de raio.
/// Tudo o que estiver fora disso gera aviso ou erro, mas o parse continua.
/// </summary>
public sealed class GCodeParser
{
    // G-codes aceitos sem aviso: não alteram a trajetória nas condições assumidas
    private static readonly HashSet<int> SilentG = [17, 21, 40, 49, 54, 55, 56, 57, 58, 59, 64, 80, 90, 94];

    // Específicos do Siemens: G71 = métrico, G500 = cancela frame, G60 = parada exata
    private static readonly HashSet<int> SilentSiemensG = [60, 71, 500];

    // M-codes aceitos sem aviso: troca de ferramenta e refrigeração não afetam a trajetória
    private static readonly HashSet<int> SilentM = [6, 7, 8, 9];

    private readonly Dialect _dialect;
    private readonly List<Block> _blocks = [];
    private readonly List<ParseDiagnostic> _diagnostics = [];

    // Estado modal (vale de uma linha para as seguintes)
    private MotionMode _motion = MotionMode.Rapid;
    private double _feed;
    private double _spindleSpeed;
    private SpindleState _spindle = SpindleState.Off;

    private GCodeParser(Dialect dialect)
    {
        _dialect = dialect;
    }

    /// <summary>Faz o parse do programa completo.</summary>
    public static ParseResult Parse(string text, Dialect dialect)
    {
        var parser = new GCodeParser(dialect);
        parser.ParseAll(text);
        return new ParseResult(dialect, parser._blocks, parser._diagnostics);
    }

    private void ParseAll(string text)
    {
        var tokens = new List<Token>();
        bool programEnded = false;
        bool warnedAfterEnd = false;

        using var reader = new StringReader(text);
        int lineNumber = 0;
        string? line;

        while ((line = reader.ReadLine()) != null)
        {
            lineNumber++;
            GCodeLexer.TokenizeLine(line, _dialect, tokens);
            if (tokens.Count == 0)
            {
                continue;
            }

            Block? block = ParseLine(line, lineNumber, tokens);
            if (block is null)
            {
                continue;
            }

            if (programEnded)
            {
                if (!warnedAfterEnd)
                {
                    Warning(lineNumber, 0, 0, "Blocos após o fim do programa (M30/M02) são ignorados.");
                    warnedAfterEnd = true;
                }

                continue;
            }

            _blocks.Add(block);
            programEnded = block.EndsProgram;
        }
    }

    /// <summary>Interpreta uma linha. Retorna null se a linha não tem conteúdo executável.</summary>
    private Block? ParseLine(string line, int lineNumber, List<Token> tokens)
    {
        int? blockNumber = null;
        MotionMode? motionInLine = null;
        double? x = null, y = null, z = null, i = null, j = null;
        List<int>? mCodes = null;
        bool hasContent = false;
        bool endsProgram = false;

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

                case TokenKind.Unknown:
                    Error(lineNumber, token, $"Texto não reconhecido: '{token.GetText(line)}'.");
                    break;

                case TokenKind.Keyword:
                    Warning(lineNumber, token, $"'{KeywordName(token.GetText(line))}' não é suportado no protótipo e foi ignorado.");
                    break;

                case TokenKind.Word:
                    hasContent = true;
                    double value = token.Value!.Value;
                    switch (token.Letter)
                    {
                        case 'G':
                            HandleG(lineNumber, token, value, ref motionInLine);
                            break;
                        case 'M':
                            HandleM(lineNumber, token, value, ref endsProgram);
                            (mCodes ??= []).Add((int)value);
                            break;
                        case 'X': SetAddress(lineNumber, token, value, ref x); break;
                        case 'Y': SetAddress(lineNumber, token, value, ref y); break;
                        case 'Z': SetAddress(lineNumber, token, value, ref z); break;
                        case 'I': SetAddress(lineNumber, token, value, ref i); break;
                        case 'J': SetAddress(lineNumber, token, value, ref j); break;
                        case 'F': _feed = value; break;
                        case 'S': _spindleSpeed = value; break;
                        case 'T':
                        case 'D':
                        case 'H':
                            // Ferramenta e corretores: fora do escopo do protótipo, sem aviso
                            break;
                        case 'R':
                            Warning(lineNumber, token, "Arco por raio (R) não é suportado no protótipo; use I/J.");
                            break;
                        default:
                            Warning(lineNumber, token, $"Endereço '{token.Letter}' ignorado no protótipo.");
                            break;
                    }

                    break;
            }
        }

        if (motionInLine.HasValue)
        {
            _motion = motionInLine.Value;
        }

        if (!hasContent)
        {
            return null;
        }

        bool isArc = _motion is MotionMode.ClockwiseArc or MotionMode.CounterClockwiseArc;
        bool hasAxis = x.HasValue || y.HasValue || z.HasValue;
        if (isArc && hasAxis && !i.HasValue && !j.HasValue)
        {
            Error(lineNumber, 0, 0, "Arco (G02/G03) sem centro I/J. CR= e R não são suportados no protótipo.");
        }

        return new Block
        {
            LineNumber = lineNumber,
            BlockNumber = blockNumber,
            Motion = _motion,
            X = x,
            Y = y,
            Z = z,
            I = i,
            J = j,
            Feed = _feed,
            SpindleSpeed = _spindleSpeed,
            Spindle = _spindle,
            MCodes = (IReadOnlyList<int>?)mCodes ?? [],
            EndsProgram = endsProgram,
        };
    }

    private void HandleG(int lineNumber, Token token, double value, ref MotionMode? motionInLine)
    {
        if (value != Math.Floor(value))
        {
            Warning(lineNumber, token, $"G{Format(value)} não é suportado no protótipo e foi ignorado.");
            return;
        }

        int code = (int)value;
        switch (code)
        {
            case >= 0 and <= 3:
                if (motionInLine.HasValue)
                {
                    Error(lineNumber, token, "Mais de um código de movimento (G00–G03) no mesmo bloco.");
                }

                motionInLine = (MotionMode)code;
                return;

            case 91:
                Error(lineNumber, token, "G91 (incremental) não é suportado no protótipo; as coordenadas serão tratadas como absolutas.");
                return;

            case 20:
                Error(lineNumber, token, "G20 (polegadas) não é suportado no protótipo.");
                return;

            case 70 when _dialect == Dialect.Siemens:
                Error(lineNumber, token, "G70 (polegadas) não é suportado no protótipo.");
                return;

            case 18 or 19:
                Error(lineNumber, token, "Somente o plano XY (G17) é suportado no protótipo.");
                return;
        }

        if (SilentG.Contains(code) || (_dialect == Dialect.Siemens && SilentSiemensG.Contains(code)))
        {
            return;
        }

        Warning(lineNumber, token, $"G{code} não é suportado no protótipo e foi ignorado.");
    }

    private void HandleM(int lineNumber, Token token, double value, ref bool endsProgram)
    {
        int code = (int)value;
        switch (code)
        {
            case 3: _spindle = SpindleState.Clockwise; return;
            case 4: _spindle = SpindleState.CounterClockwise; return;
            case 5: _spindle = SpindleState.Off; return;
            case 2 or 30: endsProgram = true; return;
        }

        if (!SilentM.Contains(code))
        {
            Warning(lineNumber, token, $"M{code} não é suportado no protótipo e foi ignorado.");
        }
    }

    private void SetAddress(int lineNumber, Token token, double value, ref double? target)
    {
        if (target.HasValue)
        {
            Error(lineNumber, token, $"Endereço {token.Letter} repetido no mesmo bloco.");
        }

        target = value;
    }

    /// <summary>"CYCLE81(10,0)" → "CYCLE81"; "CR=5" → "CR".</summary>
    private static string KeywordName(string text)
    {
        int cut = text.IndexOfAny(['=', '(']);
        return cut < 0 ? text : text[..cut];
    }

    private static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);

    private void Warning(int lineNumber, Token token, string message) =>
        _diagnostics.Add(new ParseDiagnostic(lineNumber, token.Column, token.Length, DiagnosticSeverity.Warning, message));

    private void Warning(int lineNumber, int column, int length, string message) =>
        _diagnostics.Add(new ParseDiagnostic(lineNumber, column, length, DiagnosticSeverity.Warning, message));

    private void Error(int lineNumber, Token token, string message) =>
        _diagnostics.Add(new ParseDiagnostic(lineNumber, token.Column, token.Length, DiagnosticSeverity.Error, message));

    private void Error(int lineNumber, int column, int length, string message) =>
        _diagnostics.Add(new ParseDiagnostic(lineNumber, column, length, DiagnosticSeverity.Error, message));
}
