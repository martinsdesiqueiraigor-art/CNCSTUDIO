using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Statistics;

/// <summary>Números de uma ferramenta dentro do programa.</summary>
/// <param name="Tool">Número da ferramenta (0 = movimentos antes de qualquer T).</param>
/// <param name="FirstLine">Primeira linha em que a ferramenta se move.</param>
/// <param name="MoveCount">Quantidade de movimentos (rápidos e de corte).</param>
/// <param name="CuttingDistance">Distância percorrida em G01/G02/G03 (mm).</param>
/// <param name="RapidDistance">Distância percorrida em G00 (mm).</param>
/// <param name="CuttingTime">Tempo de corte estimado.</param>
/// <param name="RapidTime">Tempo de rápido estimado.</param>
/// <param name="DwellTime">Tempo de espera (G04, G82...).</param>
/// <param name="MinFeed">Menor avanço de corte usado (mm/min); 0 se não houve corte com avanço.</param>
/// <param name="MaxFeed">Maior avanço de corte usado (mm/min).</param>
/// <param name="MinZ">Menor Z atingido em corte (profundidade máxima); null se não houve corte.</param>
public sealed record ToolStatistics(
    int Tool,
    int FirstLine,
    int MoveCount,
    double CuttingDistance,
    double RapidDistance,
    TimeSpan CuttingTime,
    TimeSpan RapidTime,
    TimeSpan DwellTime,
    double MinFeed,
    double MaxFeed,
    double? MinZ)
{
    public TimeSpan TotalTime => CuttingTime + RapidTime + DwellTime;
}

/// <summary>
/// Estatísticas do programa: tempo estimado, distâncias e tempo por ferramenta.
///
/// O tempo é uma estimativa simples, sem aceleração: corte = comprimento / avanço,
/// rápido = comprimento / velocidade de rápido, mais as esperas. Em programas com muitos
/// movimentos curtos o tempo real é maior (a máquina não chega ao avanço programado).
/// </summary>
public sealed record ProgramStatistics(
    TimeSpan CuttingTime,
    TimeSpan RapidTime,
    TimeSpan DwellTime,
    double CuttingDistance,
    double RapidDistance,
    int MoveCount,
    int ToolChanges,
    int MovesWithoutFeed,
    double RapidRate,
    IReadOnlyList<ToolStatistics> Tools)
{
    public static ProgramStatistics Empty { get; } = new(
        TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, 0, 0, 0, 0, 0, MachineSettings.Default.RapidRate, []);

    public TimeSpan TotalTime => CuttingTime + RapidTime + DwellTime;

    /// <summary>
    /// Calcula as estatísticas da trajetória.
    /// "rapidRate" é a velocidade de rápido em mm/min (deve ser positiva).
    /// </summary>
    public static ProgramStatistics Compute(ToolPath path, double rapidRate)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!(rapidRate > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(rapidRate), "A velocidade de rápido deve ser maior que zero.");
        }

        // Acumuladores por ferramenta, na ordem em que aparecem no programa
        var byTool = new Dictionary<int, Accumulator>();
        var order = new List<Accumulator>();
        Accumulator For(int tool, int line)
        {
            if (!byTool.TryGetValue(tool, out Accumulator? accumulator))
            {
                accumulator = new Accumulator(tool, line);
                byTool.Add(tool, accumulator);
                order.Add(accumulator);
            }

            return accumulator;
        }

        int toolChanges = 0;
        int movesWithoutFeed = 0;
        int previousTool = 0;

        foreach (Move move in path.Moves)
        {
            if (move.Tool != 0 && previousTool != 0 && move.Tool != previousTool)
            {
                toolChanges++;
            }

            if (move.Tool != 0)
            {
                previousTool = move.Tool;
            }

            Accumulator tool = For(move.Tool, move.LineNumber);
            tool.MoveCount++;
            double length = move.Length;

            if (move.IsRapid)
            {
                tool.RapidDistance += length;
                tool.RapidMinutes += length / rapidRate;
                continue;
            }

            tool.CuttingDistance += length;
            // Arco em G18/G19 pode descer abaixo do início e do fim: usa o envelope
            double lowest = move.IsArc ? move.IncludeIn(Bounds3.Empty).Min.Z : Math.Min(move.Start.Z, move.End.Z);
            tool.MinZ = Math.Min(tool.MinZ ?? double.MaxValue, lowest);

            if (move.Feed > 0)
            {
                tool.CuttingMinutes += length / move.Feed;
                tool.MinFeed = Math.Min(tool.MinFeed, move.Feed);
                tool.MaxFeed = Math.Max(tool.MaxFeed, move.Feed);
            }
            else if (length > 0)
            {
                movesWithoutFeed++; // sem avanço não dá para estimar o tempo (o executor já avisou)
            }
        }

        foreach (ProgramDwell dwell in path.Dwells)
        {
            For(dwell.Tool, dwell.LineNumber).DwellSeconds += dwell.Seconds;
        }

        List<ToolStatistics> tools = order.Select(a => a.ToStatistics()).ToList();
        return new ProgramStatistics(
            Sum(tools, t => t.CuttingTime),
            Sum(tools, t => t.RapidTime),
            Sum(tools, t => t.DwellTime),
            tools.Sum(t => t.CuttingDistance),
            tools.Sum(t => t.RapidDistance),
            path.Moves.Count,
            toolChanges,
            movesWithoutFeed,
            rapidRate,
            tools);
    }

    private static TimeSpan Sum(IEnumerable<ToolStatistics> tools, Func<ToolStatistics, TimeSpan> selector) =>
        tools.Aggregate(TimeSpan.Zero, (total, t) => total + selector(t));

    private sealed class Accumulator(int tool, int firstLine)
    {
        public int MoveCount;
        public double CuttingDistance;
        public double RapidDistance;
        public double CuttingMinutes;
        public double RapidMinutes;
        public double DwellSeconds;
        public double MinFeed = double.MaxValue;
        public double MaxFeed;
        public double? MinZ;

        public ToolStatistics ToStatistics() => new(
            tool,
            firstLine,
            MoveCount,
            CuttingDistance,
            RapidDistance,
            ToTime(CuttingMinutes * 60),
            ToTime(RapidMinutes * 60),
            ToTime(DwellSeconds),
            MaxFeed > 0 ? MinFeed : 0,
            MaxFeed,
            MinZ);

        // Limita a ~100 anos para programas absurdos não estourarem o TimeSpan
        private static TimeSpan ToTime(double seconds) => TimeSpan.FromSeconds(Math.Min(seconds, 3.15e9));
    }
}
