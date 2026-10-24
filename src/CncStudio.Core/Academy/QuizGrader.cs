namespace CncStudio.Core.Academy;

/// <summary>Resultado de uma rodada de quiz.</summary>
public sealed record QuizResult(int Correct, int Total, int Percent, bool Passed, IReadOnlyList<int> WrongIndexes);

/// <summary>Correção de quizzes: a resposta é o índice da alternativa (0–3) ou -1 quando em branco.</summary>
public static class QuizGrader
{
    public const int DefaultPassPercent = 70;

    public static QuizResult Grade(IReadOnlyList<QuizQuestion> questions, IReadOnlyList<int> answers, int passPercent = DefaultPassPercent)
    {
        List<int> wrong = [];
        int correct = 0;
        for (int i = 0; i < questions.Count; i++)
        {
            int given = i < answers.Count ? answers[i] : -1;
            if (given == questions[i].CorrectIndex)
            {
                correct++;
            }
            else
            {
                wrong.Add(i);
            }
        }

        int percent = questions.Count == 0 ? 0 : (int)Math.Round(100.0 * correct / questions.Count);
        return new QuizResult(correct, questions.Count, percent, percent >= passPercent, wrong);
    }

    /// <summary>Sorteia (de forma repetível pela semente) <paramref name="count"/> perguntas, mantendo todas as de leitura de programa.</summary>
    public static List<QuizQuestion> Draw(IReadOnlyList<QuizQuestion> pool, int count, int seed)
    {
        Random rng = new(seed);
        List<QuizQuestion> reading = [.. pool.Where(q => q.Kind.Contains("leitura", StringComparison.OrdinalIgnoreCase))];
        List<QuizQuestion> others = [.. pool.Except(reading).OrderBy(_ => rng.Next())];
        List<QuizQuestion> picked = [.. reading, .. others.Take(Math.Max(0, count - reading.Count))];
        return [.. picked.OrderBy(_ => rng.Next())];
    }
}
