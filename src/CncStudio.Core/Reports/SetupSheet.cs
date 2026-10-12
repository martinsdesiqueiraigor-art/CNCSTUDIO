using System.Text.RegularExpressions;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Lexing;
using CncStudio.Core.Parsing;
using CncStudio.Core.Statistics;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Reports;

/// <summary>Uma ferramenta na folha de setup.</summary>
/// <param name="Number">Número da ferramenta (0 = movimentos antes de qualquer T).</param>
/// <param name="Description">Comentário da troca de ferramenta no programa, ou a descrição da tabela.</param>
/// <param name="Diameter">Diâmetro da tabela de ferramentas (fresa); null se não informado.</param>
/// <param name="FirstLine">Primeira linha em que a ferramenta trabalha.</param>
/// <param name="Statistics">Tempos e distâncias da ferramenta.</param>
/// <param name="Share">Fração do tempo total (0 a 1).</param>
/// <param name="Reach">Fresa: menor Z de corte. Torno: menor diâmetro de corte. Null se não cortou.</param>
public sealed record SetupTool(int Number, string Description, double? Diameter, int FirstLine,
    ToolStatistics Statistics, double Share, double? Reach);

/// <summary>
/// Dados da folha de setup do programa atual: identificação, informações do programa,
/// ferramentas com o tempo de cada uma e o tempo total.
/// </summary>
public sealed record SetupSheet(
    string FileName,
    string? FilePath,
    string? ProgramNumber,
    IReadOnlyList<string> HeaderComments,
    Dialect Dialect,
    MachineKind Machine,
    DateTime Generated,
    int LineCount,
    int BlockCount,
    int MoveCount,
    IReadOnlyList<string> WorkOffsets,
    Bounds3 CuttingBounds,
    Bounds3 AllBounds,
    string? Stock,
    int ErrorCount,
    int WarningCount,
    ProgramStatistics Statistics,
    IReadOnlyList<SetupTool> Tools);

/// <summary>Monta a <see cref="SetupSheet"/> a partir do texto do programa e da análise já feita.</summary>
public static partial class SetupSheetBuilder
{
    private const int MaxHeaderComments = 12;

    /// <param name="text">Texto do programa.</param>
    /// <param name="analysis">Resultado da análise (trajetória, estatísticas, diagnósticos).</param>
    /// <param name="fileName">Nome do arquivo (ou "sem título").</param>
    /// <param name="filePath">Caminho completo, se salvo.</param>
    /// <param name="toolTable">Diâmetro e descrição de cada T na tabela da máquina (null = não cadastrada).</param>
    /// <param name="stock">Descrição do bruto, se informado.</param>
    /// <param name="now">Data/hora de geração (testes passam um valor fixo).</param>
    public static SetupSheet Build(string text, ProgramAnalysis analysis, string fileName, string? filePath,
        Func<int, (double Diameter, string Description)?> toolTable, string? stock = null, DateTime? now = null)
    {
        Dialect dialect = analysis.Dialect;
        MachineKind machine = analysis.ToolPath.Machine;
        ParseResult parse = GCodeParser.Parse(text, dialect);

        (string? programNumber, List<string> header) = ReadHeader(text, parse, dialect);
        Dictionary<int, string> toolComments = ReadToolComments(text, parse, dialect, machine);
        List<string> offsets = ReadWorkOffsets(parse, dialect);
        Dictionary<int, double> reach = ToolReach(analysis.ToolPath, machine);

        ProgramStatistics statistics = analysis.Statistics;
        double totalSeconds = statistics.TotalTime.TotalSeconds;
        var tools = statistics.Tools.Select(t =>
        {
            (double Diameter, string Description)? table = t.Tool == 0 ? null : toolTable(t.Tool);
            string description = toolComments.GetValueOrDefault(t.Tool) ?? table?.Description ?? "";
            double share = totalSeconds > 0 ? t.TotalTime.TotalSeconds / totalSeconds : 0;
            double? diameter = machine == MachineKind.Mill ? table?.Diameter : null;
            return new SetupTool(t.Tool, description, diameter, t.FirstLine, t, share, reach.TryGetValue(t.Tool, out double r) ? r : null);
        }).ToList();

        return new SetupSheet(
            fileName, filePath, programNumber, header, dialect, machine, now ?? DateTime.Now,
            text.Split('\n').Length, analysis.BlockCount, analysis.ToolPath.Moves.Count, offsets,
            analysis.ToolPath.CuttingBounds, analysis.ToolPath.Bounds, stock,
            analysis.ErrorCount, analysis.WarningCount, statistics, tools);
    }

    /// <summary>
    /// Número/nome do programa (O1234, %_N_NOME_MPF) e os comentários do cabeçalho:
    /// os que aparecem antes do primeiro movimento (nome da peça, bruto, material, autor...).
    /// </summary>
    private static (string? ProgramNumber, List<string> Header) ReadHeader(string text, ParseResult parse, Dialect dialect)
    {
        string? number = parse.Blocks.FirstOrDefault(b => b.ProgramId is not null)?.ProgramId;
        string[] lines = text.Split('\n');
        var header = new List<string>();
        var tokens = new List<Token>();

        foreach (string raw in lines)
        {
            string line = raw.TrimEnd('\r');
            GCodeLexer.TokenizeLine(line, dialect, tokens);
            bool moves = tokens.Exists(t => t.Kind == TokenKind.Word && t.Letter is 'X' or 'Y' or 'Z' or 'U' or 'W');
            if (moves || header.Count >= MaxHeaderComments)
            {
                break;
            }

            // Só comentários de linhas sem comandos (ou da linha do número do programa):
            // "T1 M06 (FRESA)" é descrição da ferramenta, não cabeçalho
            bool onlyHeader = tokens.TrueForAll(t => t.Kind is TokenKind.Comment or TokenKind.ProgramMarker
                                                     || (t.Kind == TokenKind.Word && t.Letter == 'O'));
            if (!onlyHeader)
            {
                continue;
            }

            foreach (Token token in tokens.Where(t => t.Kind == TokenKind.Comment))
            {
                string comment = CleanComment(line.Substring(token.Column, token.Length));
                bool systemLine = comment.StartsWith("$PATH", StringComparison.OrdinalIgnoreCase); // linha do Siemens, não é descrição
                if (comment.Length > 0 && !systemLine && header.Count < MaxHeaderComments)
                {
                    header.Add(comment);
                }
            }
        }

        return (number, header);
    }

    /// <summary>Comentário escrito na linha da troca de ferramenta: "T0101 (DESBASTE EXTERNO)".</summary>
    private static Dictionary<int, string> ReadToolComments(string text, ParseResult parse, Dialect dialect, MachineKind machine)
    {
        var comments = new Dictionary<int, string>();
        string[] lines = text.Split('\n');
        var tokens = new List<Token>();

        foreach (NcBlock block in parse.Blocks)
        {
            AddressWord? tool = block.Find('T');
            if (tool is not { } t || block.LineNumber > lines.Length)
            {
                continue;
            }

            int number = machine == MachineKind.Lathe && dialect == Dialect.Fanuc && t.Value >= 100 ? (int)t.Value / 100 : (int)t.Value;
            if (comments.ContainsKey(number))
            {
                continue;
            }

            string line = lines[block.LineNumber - 1].TrimEnd('\r');
            GCodeLexer.TokenizeLine(line, dialect, tokens);
            string comment = string.Join(" ", tokens.Where(k => k.Kind == TokenKind.Comment)
                .Select(k => CleanComment(line.Substring(k.Column, k.Length))).Where(c => c.Length > 0));
            if (comment.Length > 0)
            {
                comments[number] = comment;
            }
        }

        return comments;
    }

    /// <summary>Zeros de peça usados, na ordem em que aparecem (G54, G55... / G500, G54...).</summary>
    private static List<string> ReadWorkOffsets(ParseResult parse, Dialect dialect)
    {
        var offsets = new List<string>();
        foreach (NcBlock block in parse.Blocks)
        {
            foreach (AddressWord word in block.Words)
            {
                if (word.Letter != 'G' || word.Value != Math.Floor(word.Value))
                {
                    continue;
                }

                int code = (int)word.Value;
                bool isOffset = code is >= 54 and <= 59 || (dialect == Dialect.Siemens && code is 500 or (>= 505 and <= 599));
                string name = "G" + code;
                if (isOffset && !offsets.Contains(name))
                {
                    offsets.Add(name);
                }
            }
        }

        return offsets;
    }

    /// <summary>Fresa: menor Z de corte por ferramenta. Torno: menor diâmetro de corte.</summary>
    private static Dictionary<int, double> ToolReach(ToolPath path, MachineKind machine)
    {
        var reach = new Dictionary<int, double>();
        foreach (Move move in path.Moves)
        {
            if (move.IsRapid)
            {
                continue;
            }

            Bounds3 b = move.IncludeIn(Bounds3.Empty);
            double value = machine == MachineKind.Lathe ? Math.Abs(b.Min.X) * 2 : b.Min.Z;
            reach[move.Tool] = reach.TryGetValue(move.Tool, out double current) ? Math.Min(current, value) : value;
        }

        return reach;
    }

    /// <summary>"(DESBASTE)" → "DESBASTE"; "; contorno" → "contorno".</summary>
    private static string CleanComment(string comment)
    {
        string text = comment.Trim();
        if (text.StartsWith(';'))
        {
            text = text[1..];
        }
        else if (text.StartsWith('(') )
        {
            text = text[1..];
            if (text.EndsWith(')'))
            {
                text = text[..^1];
            }
        }

        return Spaces().Replace(text, " ").Trim();
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
