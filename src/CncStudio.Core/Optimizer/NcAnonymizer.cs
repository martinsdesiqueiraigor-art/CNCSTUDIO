using System.Text.RegularExpressions;

namespace CncStudio.Core.Optimizer;

/// <summary>Copy of a program that can be shared for testing: every comment text becomes (C1), (C2)...
/// (equal texts keep equal codes, so operation boundaries survive). Codes, coordinates, O numbers and
/// line count are unchanged, so the analysis of the copy matches the original.</summary>
public static class NcAnonymizer
{
    private static readonly Regex Paren = new(@"\([^)]*\)", RegexOptions.Compiled);

    public static IReadOnlyList<string> Anonymize(IReadOnlyList<string> lines)
    {
        var codes = new Dictionary<string, string>();
        string Code(string text) =>
            codes.TryGetValue(text, out var c) ? c : codes[text] = "C" + (codes.Count + 1);
        return lines.Select(line =>
        {
            var text = Paren.Replace(line, m => "(" + Code(m.Value[1..^1].Trim()) + ")");
            var semicolon = text.IndexOf(';');
            if (semicolon >= 0 && text[(semicolon + 1)..].Trim().Length > 0)
                text = text[..(semicolon + 1)] + " " + Code(text[(semicolon + 1)..].Trim());
            return text;
        }).ToArray();
    }
}
