using System.Text.Json.Nodes;

namespace CncStudio.Core.Academy;

/// <summary>Progresso do aluno guardado no aparelho (sem login no piloto): aulas concluídas e melhor nota de cada quiz.</summary>
public sealed class AcademyProgress
{
    private readonly HashSet<string> _done = [];
    private readonly Dictionary<string, int> _best = [];

    public IReadOnlyCollection<string> CompletedLessons => _done;

    public bool IsDone(string lessonId) => _done.Contains(lessonId);

    public int BestScore(string quizId) => _best.TryGetValue(quizId, out int p) ? p : -1;

    public void MarkDone(string lessonId) => _done.Add(lessonId);

    /// <summary>Guarda a nota se for a melhor até agora.</summary>
    public void RecordScore(string quizId, int percent)
    {
        if (percent > BestScore(quizId))
        {
            _best[quizId] = percent;
        }
    }

    public string ToJson()
    {
        var scores = new JsonObject();
        foreach ((string k, int v) in _best.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            scores[k] = v;
        }

        var done = new JsonArray();
        foreach (string id in _done.OrderBy(s => s, StringComparer.Ordinal))
        {
            done.Add(id);
        }

        return new JsonObject { ["done"] = done, ["scores"] = scores }.ToJsonString();
    }

    /// <summary>Lê o progresso salvo; texto vazio ou quebrado vira progresso vazio.</summary>
    public static AcademyProgress FromJson(string? json)
    {
        AcademyProgress progress = new();
        if (string.IsNullOrWhiteSpace(json))
        {
            return progress;
        }

        try
        {
            if (JsonNode.Parse(json) is JsonObject o)
            {
                if (o["done"] is JsonArray done)
                {
                    foreach (JsonNode? n in done)
                    {
                        if (n is JsonValue v && v.TryGetValue(out string? id) && !string.IsNullOrEmpty(id))
                        {
                            progress._done.Add(id);
                        }
                    }
                }

                if (o["scores"] is JsonObject scores)
                {
                    foreach ((string k, JsonNode? n) in scores)
                    {
                        if (n is JsonValue v && v.TryGetValue(out int p))
                        {
                            progress._best[k] = Math.Clamp(p, 0, 100);
                        }
                    }
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            return new AcademyProgress();
        }

        return progress;
    }
}
