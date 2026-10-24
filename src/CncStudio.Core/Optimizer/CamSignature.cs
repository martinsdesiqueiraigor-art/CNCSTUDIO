using System.Text.RegularExpressions;

namespace CncStudio.Core.Optimizer;

/// <summary>Recognizes the CAM system from typical comment signatures of its FANUC posts.
/// Only strong signatures are used; anything else is reported as unknown. A hint, not evidence.</summary>
public static class CamSignature
{
    private static readonly (string Cam, Regex Pattern)[] Signatures =
    {
        ("Autodesk Fusion / HSM", new(@"\bT\d+\s+D=[\d.]+\s+CR=|\bZMIN=", RegexOptions.IgnoreCase)),
        ("Mastercam", new(@"DATE=DD-MM-YY|MASTERCAM|MCAM|^\s*T\d+\s*\|", RegexOptions.IgnoreCase)),
        ("Siemens NX", new(@"\bSIEMENS\s+NX\b|\bNX\s+POST\b|\bUGII\b", RegexOptions.IgnoreCase)),
        ("Autodesk PowerMill", new(@"POWERMILL", RegexOptions.IgnoreCase)),
        ("hyperMILL", new(@"HYPERMILL", RegexOptions.IgnoreCase)),
        ("SolidCAM", new(@"SOLIDCAM", RegexOptions.IgnoreCase)),
        ("ESPRIT", new(@"\bESPRIT\b", RegexOptions.IgnoreCase))
    };

    public const string Unknown = "Desconhecido (sem assinatura de CAM nos comentarios)";

    public static string Detect(IReadOnlyList<NcBlock> blocks)
    {
        var comments = blocks.Where(b => b.Comment is not null).Take(400).Select(b => b.Comment!).ToArray();
        foreach (var (cam, pattern) in Signatures)
            if (comments.Any(pattern.IsMatch)) return cam;
        return Unknown;
    }
}
