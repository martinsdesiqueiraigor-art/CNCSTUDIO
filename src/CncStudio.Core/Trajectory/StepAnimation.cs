namespace CncStudio.Core.Trajectory;

/// <summary>
/// Onde a ferramenta está dentro de um passo da simulação: quantos movimentos já terminaram, qual está em
/// andamento e quanto dele já foi percorrido (0 a 1).
/// </summary>
/// <param name="Completed">Movimentos do passo já terminados (0 a quantidade do passo).</param>
/// <param name="MoveIndex">Índice (na trajetória) do movimento em andamento; no fim do passo, o último dele.</param>
/// <param name="Fraction">Quanto do movimento em andamento foi percorrido.</param>
public readonly record struct StepPose(int Completed, int MoveIndex, double Fraction);

/// <summary>
/// Anima a ferramenta pelo percurso de um passo em vez de saltar para o fim dele: o tempo do passo é
/// dividido entre os movimentos pelo comprimento, e os rápidos (G00, inclusive os retornos dos ciclos)
/// passam mais depressa que os cortes.
/// </summary>
public static class StepAnimation
{
    /// <summary>Quantas vezes o rápido é mais veloz que o corte na animação.</summary>
    public const double RapidSpeedup = 4;

    /// <param name="startMove">Primeiro movimento do passo (índice na trajetória).</param>
    /// <param name="endMove">Fim do passo, exclusivo (== início do próximo).</param>
    /// <param name="progress">Andamento do passo, de 0 (início) a 1 (fim).</param>
    public static StepPose At(IReadOnlyList<Move> moves, int startMove, int endMove, double progress)
    {
        int count = Math.Max(0, endMove - startMove);
        if (count == 0)
        {
            return new StepPose(0, Math.Max(0, startMove - 1), 1);
        }

        progress = Math.Clamp(progress, 0, 1);
        if (progress >= 1)
        {
            return new StepPose(count, endMove - 1, 1);
        }

        // Tempo de cada movimento ∝ comprimento (rápido: dividido por RapidSpeedup); mínimo para ponto parado
        double total = 0;
        for (int i = startMove; i < endMove; i++)
        {
            total += Weight(moves[i]);
        }

        double target = progress * total;
        double acc = 0;
        for (int i = startMove; i < endMove; i++)
        {
            double w = Weight(moves[i]);
            if (acc + w > target)
            {
                return new StepPose(i - startMove, i, Math.Clamp((target - acc) / w, 0, 1));
            }

            acc += w;
        }

        return new StepPose(count, endMove - 1, 1);
    }

    private static double Weight(Move move) =>
        Math.Max(move.Length, 0.05) / (move.IsRapid ? RapidSpeedup : 1);
}
