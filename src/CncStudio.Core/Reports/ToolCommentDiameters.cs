using System.Globalization;
using System.Text.RegularExpressions;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Parsing;

namespace CncStudio.Core.Reports;

/// <summary>
/// Diâmetro da ferramenta escrito no comentário da troca: "T8 M06 (FRESA DE TOPO D10)",
/// "T=\"BROCA\" ; BROCA Ø6,8". Serve quando não há tabela de ferramentas (versão web),
/// para a compensação G41/G42 ser desenhada.
/// </summary>
public static partial class ToolCommentDiameters
{
    public static Dictionary<int, double> Read(string text, ParseResult parse, Dialect dialect)
    {
        var diameters = new Dictionary<int, double>();
        foreach ((int tool, ToolNote note) in SetupSheetBuilder.ReadToolComments(text, parse, dialect, MachineKind.Mill))
        {
            if ((FromComment(note.Operation ?? "") ?? FromComment(note.Code ?? "")) is { } diameter)
            {
                diameters[tool] = diameter;
            }
        }

        return diameters;
    }

    /// <summary>"FRESA D10" → 10; "BROCA Ø6,8" → 6,8; "MACHO M8" → null.</summary>
    public static double? FromComment(string comment)
    {
        Match match = DiameterPattern().Match(comment);
        if (!match.Success)
        {
            return null;
        }

        double value = double.Parse(match.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        return value > 0 ? value : null;
    }

    [GeneratedRegex(@"(?:^|[^A-Z0-9])(?:D|Ø|DIAM\.?)\s*(\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex DiameterPattern();
}
