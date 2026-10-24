using System.Globalization;
using CncStudio.Core.Optimizer;

namespace CncStudio.Core.Tests.Optimizer;

internal static class Fixtures
{
    public static readonly NcProfile Calc = NcProfile.Fanuc0iMfCalculator;
    public static readonly NcProfile Conv = NcProfile.Fanuc0iMfConventional;

    /// <summary>Typical FANUC CAM header: program number, tool change, WCS, spindle, length offset.</summary>
    public static readonly string[] Preamble =
    {
        "%", "O1000", "(T1 D=10. CR=0. - FLAT END MILL)", "N10 G21 G90 G17 G40 G49 G80",
        "N20 T1 M6", "N30 G54", "N40 S8000 M3", "N50 G43 H1 Z50. M8", "N60 G00 X0. Y0. Z5."
    };

    public static string N(double v) => v.ToString("0.0##", CultureInfo.InvariantCulture);

    /// <summary>One contour pass at depth z (7 blocks, square of side 10 at ox,oy).</summary>
    public static IEnumerable<string> Pass(double z, double ox = 0, double oy = 0, double retract = 8) => new[]
    {
        $"G90 G01 Z{N(z)} F100.", $"X{N(ox + 10)} F400.", $"Y{N(oy + 10)}", $"X{N(ox - 10)}", $"Y{N(oy - 10)}",
        $"G00 Z{N(retract)}", $"X{N(ox + 30)}"
    };

    public static string[] ZLevels(int count, double step = -1, IEnumerable<string>? tail = null) =>
        Preamble.Append("(2D Contour1)")
            .Concat(Enumerable.Range(1, count).SelectMany(k => Pass(k * step)))
            .Concat(tail ?? Array.Empty<string>()).Append("M30").Append("%").ToArray();

    public static ConversionResult Run(IEnumerable<string> lines, NcProfile? profile = null) =>
        ConversionPipeline.Run(lines.ToArray(), profile ?? Calc);
}
