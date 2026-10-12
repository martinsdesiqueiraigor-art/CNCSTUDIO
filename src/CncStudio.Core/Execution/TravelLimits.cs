using System.Globalization;
using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Execution;

/// <summary>
/// Limites de curso em coordenadas do programa (zero da peça aplicado), por eixo e sentido.
/// Null = sem limite naquele sentido. No torno, X é em diâmetro (como no programa).
/// </summary>
public sealed record TravelLimits(
    double? MinX = null, double? MaxX = null,
    double? MinY = null, double? MaxY = null,
    double? MinZ = null, double? MaxZ = null)
{
    public bool IsEmpty => MinX is null && MaxX is null && MinY is null && MaxY is null && MinZ is null && MaxZ is null;

    /// <summary>
    /// Avisos para cada eixo/sentido em que algum movimento passa do limite: o primeiro
    /// movimento que passa (linha e valor) e quantos outros passam também.
    /// </summary>
    public List<ParseDiagnostic> Check(ToolPath path)
    {
        var diagnostics = new List<ParseDiagnostic>();
        if (IsEmpty)
        {
            return diagnostics;
        }

        double xFactor = path.Machine == MachineKind.Lathe ? 2 : 1;
        var checks = new (string Name, double? Limit, bool IsMax, Func<Bounds3, double> Value)[]
        {
            ("X−", MinX, false, b => b.Min.X * xFactor),
            ("X+", MaxX, true, b => b.Max.X * xFactor),
            ("Y−", MinY, false, b => b.Min.Y),
            ("Y+", MaxY, true, b => b.Max.Y),
            ("Z−", MinZ, false, b => b.Min.Z),
            ("Z+", MaxZ, true, b => b.Max.Z),
        };

        foreach ((string name, double? limit, bool isMax, Func<Bounds3, double> value) in checks)
        {
            if (limit is not { } l)
            {
                continue;
            }

            int count = 0;
            Move? first = null;
            double firstValue = 0;
            foreach (Move move in path.Moves)
            {
                double v = value(move.IncludeIn(Bounds3.Empty));
                bool outside = isMax ? v > l + 1e-6 : v < l - 1e-6;
                if (!outside)
                {
                    continue;
                }

                if (count++ == 0)
                {
                    first = move;
                    firstValue = v;
                }
            }

            if (first is { } m)
            {
                string axis = name[..1];
                string others = count > 1 ? $" (e mais {count - 1} movimento(s))" : "";
                diagnostics.Add(new ParseDiagnostic(m.LineNumber, 0, 0, DiagnosticSeverity.Warning, string.Create(CultureInfo.InvariantCulture,
                    $"Passa do limite de curso {name} ({(isMax ? "máx." : "mín.")} {l:0.###}): {axis} = {firstValue:0.###}{others}.")));
            }
        }

        return diagnostics;
    }
}
