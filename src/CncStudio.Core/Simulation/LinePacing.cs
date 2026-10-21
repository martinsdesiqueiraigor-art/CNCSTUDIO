using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Simulation;

/// <summary>
/// Execução "linha a linha" suave: em vez de saltar cada linha do programa de uma vez, a ferramenta
/// percorre a linha (todos os movimentos dela, inclusive os retornos dos ciclos) levando 1/velocidade
/// segundos. O tempo da linha é dividido pelo comprimento e os rápidos (G00) passam mais depressa.
/// </summary>
public static class LinePacing
{
    /// <summary>Quantas vezes o rápido é mais veloz que o corte.</summary>
    public const double RapidSpeedup = 4;

    /// <summary>
    /// Avança a simulação "budgetLines" linhas (pode ser fração: meia linha anda metade do tempo dela).
    /// Para sempre no fim de uma linha quando o orçamento a completa e segue para a próxima.
    /// </summary>
    public static void Advance(IMaterialSimulation simulation, IReadOnlyList<Move> moves, double budgetLines)
    {
        int guard = 0;
        while (budgetLines > 1e-9 && !simulation.IsFinished && guard++ < 10_000)
        {
            int current = Math.Min(simulation.CurrentMoveIndex, moves.Count - 1);
            (int start, int end) = LineSpan(moves, current);
            double total = 0, done = 0;
            for (int i = start; i < end; i++)
            {
                double w = Weight(moves[i]);
                total += w;
                if (i < current)
                {
                    done += w;
                }
            }

            double remaining = Math.Max(total - done, total * 0.001);
            double cost = remaining / total; // fração da linha que falta, em "linhas"
            if (budgetLines >= cost)
            {
                simulation.AdvanceToward(end, double.PositiveInfinity);
                budgetLines -= cost;
                continue;
            }

            // Orçamento não completa a linha: anda só a parte dele, na velocidade (real) do movimento atual
            double weighted = budgetLines * total;
            double distance = weighted * (moves[current].IsRapid ? RapidSpeedup : 1);
            simulation.AdvanceToward(end, Math.Max(distance, 1e-6));
            return;
        }
    }

    /// <summary>Primeiro movimento da linha do movimento "index" e o seguinte ao último dela.</summary>
    private static (int Start, int End) LineSpan(IReadOnlyList<Move> moves, int index)
    {
        int line = moves[index].LineNumber;
        int start = index;
        while (start > 0 && moves[start - 1].LineNumber == line)
        {
            start--;
        }

        int end = index + 1;
        while (end < moves.Count && moves[end].LineNumber == line)
        {
            end++;
        }

        return (start, end);
    }

    private static double Weight(Move move) =>
        Math.Max(move.Length, 0.05) / (move.IsRapid ? RapidSpeedup : 1);
}
