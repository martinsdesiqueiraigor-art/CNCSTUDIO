using System.Globalization;

namespace CncStudio.Core.Statistics;

/// <summary>Formata durações do jeito que se lê na oficina: "4,2 s", "12min 05s", "1h 02min 03s".</summary>
public static class DurationFormat
{
    public static string Format(TimeSpan duration, CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        double seconds = Math.Max(0, duration.TotalSeconds);

        if (seconds < 59.95)
        {
            return seconds.ToString("0.0", culture) + " s";
        }

        long total = (long)Math.Round(seconds);
        long hours = total / 3600;
        long minutes = total % 3600 / 60;
        long rest = total % 60;

        return hours > 0
            ? $"{hours}h {minutes:00}min {rest:00}s"
            : $"{minutes}min {rest:00}s";
    }
}
