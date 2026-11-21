using CncStudio.Core.Geometry;
using CncStudio.Core.Simulation;

namespace CncStudio.Core.Cam;

/// <summary>
/// Resultado de "começar no material" numa operação (CAM-AR-01). <see cref="Adjusted"/> = alturas mudadas; senão <see cref="Reason"/>
/// diz por que a operação gera pelo topo digitado (null quando o material já está no topo digitado e nada muda).
/// </summary>
public sealed record StockStart(bool Adjusted, CamLevels Levels, double? MaterialTop, double Clearance, int PassesBefore, int PassesAfter, string? Reason)
{
    /// <summary>Índice da operação cujo bruto foi usado (−1 = bruto inteiro). Só com <see cref="Adjusted"/>.</summary>
    public int FromOperation { get; init; } = -1;
}

/// <summary>
/// "Começar no material" (CAM-AR-01, referência: Fusion "Top: Stock top" + "Feed height", com o bruto em processo): o topo da
/// operação desce até o maior Z do material que sobrou dentro da área que a ferramenta percorre; a altura de avanço fica logo
/// acima dele; os passes em Z são contados dali. Nunca sobe acima do digitado; o fundo e a altura segura não mudam.
/// Simulação é verificação, nunca garantia: por isso a folga e o aviso.
/// </summary>
public static class CamStockStart
{
    /// <summary>Folga fixa acima do material (mm), somada ao tamanho da célula do bruto.</summary>
    public const double BaseClearance = 1;

    /// <summary>Operações que podem começar no material (o Chanfro conta o fundo a partir do topo; a Furação tem ciclo próprio).</summary>
    public static bool Applies(JobOpKind kind) => kind is JobOpKind.Facing or JobOpKind.Contour or JobOpKind.Pocket;

    /// <summary>Folga usada com esta grade: 1 mm + a célula.</summary>
    public static double ClearanceFor(HeightMap map) => BaseClearance + map.CellSize;

    /// <summary>
    /// Maior Z do material (no referencial do trabalho) na área varrida pelos movimentos de corte de <paramref name="op"/>, cada um
    /// alargado pelo raio da ferramenta (duas vezes com compensação no comando). Entram também os rápidos abaixo da altura segura
    /// (<paramref name="safeZ"/>): com o topo ajustado eles passam a andar logo acima do material. Null sem movimento ou sem material na
    /// área. <paramref name="zero"/>: o mesmo zero que desloca as operações para a grade.
    /// </summary>
    public static double? MaterialTop(HeightMap map, CamOperation op, Point3 zero, double safeZ)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(op);
        bool[] seen = new bool[map.Columns * map.Rows];
        double best = double.NegativeInfinity;
        double step = Math.Max(map.CellSize, 1e-3);
        ToolMove? previous = null;
        foreach (ToolMove m in op.Moves)
        {
            if (previous is { } from && (m.Kind != MoveKind.Rapid || Math.Min(from.Z, m.Z) < safeZ - 1e-6))
            {
                double reach = op.Tool.Radius * (m.Comp is CutterComp.Left or CutterComp.Right ? 2 : 1);
                foreach (Point2 p in Samples(from, m, step))
                {
                    Mark(map, seen, p.X - zero.X, p.Y - zero.Y, reach, ref best);
                }
            }

            previous = m;
        }

        return double.IsNegativeInfinity(best) ? null : best + zero.Z;
    }

    /// <summary>
    /// Alturas novas com o material em <paramref name="materialTop"/> (referencial do trabalho). Material no topo digitado ou acima:
    /// nada muda. Material abaixo do fundo (ou nenhum): nada muda, com o motivo.
    /// </summary>
    public static StockStart Adjust(CamLevels levels, double? materialTop, double clearance)
    {
        ArgumentNullException.ThrowIfNull(levels);
        int before = levels.Depths().Count;
        if (materialTop is not { } top || top <= levels.BottomZ + 1e-9)
        {
            return new StockStart(false, levels, materialTop, clearance, before, before, "sem material na área da operação");
        }

        if (top >= levels.TopZ - 1e-9)
        {
            return new StockStart(false, levels, top, clearance, before, before, null);
        }

        double offset = levels.ClearanceZ - levels.TopZ;
        double feed = Math.Min(levels.ClearanceZ, top + Math.Max(clearance, offset));
        CamLevels adjusted = levels with { TopZ = top, ClearanceZ = feed };
        return new StockStart(true, adjusted, top, clearance, before, adjusted.Depths().Count, null);
    }

    /// <summary>Texto do aviso Conferir de uma operação ajustada (número da operação a partir de 1).</summary>
    public static string AdjustedNote(int index, StockStart s) =>
        $"Operação {index + 1} parte em Z{s.Levels.TopZ:0.###} pelo bruto " +
        (s.FromOperation < 0 ? "inicial" : $"da operação {s.FromOperation + 1}") +
        $" (altura de avanço Z{s.Levels.ClearanceZ:0.###}, passes em Z {s.PassesBefore} → {s.PassesAfter}). O G-code muda: confira na simulação.";

    /// <summary>Texto do aviso Conferir de uma operação que pediu "começar no material" e gera pelo topo digitado.</summary>
    public static string KeptNote(int index, string reason) =>
        $"Operação {index + 1}: começar no material não aplicado ({reason}); gera pelo topo digitado. Conferir.";

    private static void Mark(HeightMap map, bool[] seen, double x, double y, double reach, ref double best)
    {
        double half = map.CellSize / 2;
        double limit = reach + (half * Math.Sqrt(2));
        int c0 = (int)Math.Floor((x - reach - map.OriginX) / map.CellSize), c1 = (int)Math.Floor((x + reach - map.OriginX) / map.CellSize);
        int r0 = (int)Math.Floor((y - reach - map.OriginY) / map.CellSize), r1 = (int)Math.Floor((y + reach - map.OriginY) / map.CellSize);
        c0 = Math.Max(c0, 0);
        r0 = Math.Max(r0, 0);
        c1 = Math.Min(c1, map.Columns - 1);
        r1 = Math.Min(r1, map.Rows - 1);
        for (int r = r0; r <= r1; r++)
        {
            double dy = map.CellCenterY(r) - y;
            for (int c = c0; c <= c1; c++)
            {
                int k = (r * map.Columns) + c;
                if (seen[k])
                {
                    continue;
                }

                double dx = map.CellCenterX(c) - x;
                if ((dx * dx) + (dy * dy) > limit * limit)
                {
                    continue;
                }

                seen[k] = true;
                if (!map.IsEmptyCell(c, r) && map[c, r] > best)
                {
                    best = map[c, r];
                }
            }
        }
    }

    // pontos ao longo do movimento (reta ou arco), com passo no máximo do tamanho da célula; inclui as duas pontas
    private static IEnumerable<Point2> Samples(ToolMove from, ToolMove to, double step)
    {
        if (!to.IsArc)
        {
            double len = Math.Sqrt(((to.X - from.X) * (to.X - from.X)) + ((to.Y - from.Y) * (to.Y - from.Y)));
            int n = Math.Max(1, (int)Math.Ceiling(len / step));
            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n;
                yield return new Point2(from.X + ((to.X - from.X) * t), from.Y + ((to.Y - from.Y) * t));
            }

            yield break;
        }

        double cx = to.Center.X, cy = to.Center.Y;
        double radius = Math.Sqrt(((from.X - cx) * (from.X - cx)) + ((from.Y - cy) * (from.Y - cy)));
        double a0 = Math.Atan2(from.Y - cy, from.X - cx), a1 = Math.Atan2(to.Y - cy, to.X - cx);
        double sweep = a1 - a0;
        if (to.Kind == MoveKind.ArcCcw)
        {
            while (sweep <= 1e-12)
            {
                sweep += 2 * Math.PI;
            }
        }
        else
        {
            while (sweep >= -1e-12)
            {
                sweep -= 2 * Math.PI;
            }
        }

        int count = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) * radius / step));
        for (int i = 0; i <= count; i++)
        {
            double a = a0 + (sweep * i / count);
            yield return new Point2(cx + (radius * Math.Cos(a)), cy + (radius * Math.Sin(a)));
        }
    }
}
