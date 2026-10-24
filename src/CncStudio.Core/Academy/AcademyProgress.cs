using System.Text.Json.Nodes;

namespace CncStudio.Core.Academy;

/// <summary>Progresso do aluno guardado no aparelho (sem login no piloto): aulas concluídas e melhor nota de cada quiz.</summary>
public sealed class AcademyProgress
{
    private readonly HashSet<string> _done = [];
    private readonly Dictionary<string, int> _best = [];
    private readonly SortedSet<string> _days = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> CompletedLessons => _done;

    /// <summary>Dias (aaaa-mm-dd) em que a pessoa estudou.</summary>
    public IReadOnlyCollection<string> StudyDays => _days;

    /// <summary>Registra o dia de estudo (a tela manda a data do aparelho; texto fora do formato é ignorado).</summary>
    public void RecordStudyDay(string? day)
    {
        if (DateOnly.TryParseExact(day, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out _))
        {
            _days.Add(day!);
        }
    }

    /// <summary>Dias seguidos estudando, contando até <paramref name="today"/> (ou até ontem, se hoje ainda não estudou).</summary>
    public int Streak(string? today)
    {
        if (!DateOnly.TryParseExact(today, "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out DateOnly day))
        {
            return 0;
        }

        if (!_days.Contains(day.ToString("yyyy-MM-dd")))
        {
            day = day.AddDays(-1);
        }

        int count = 0;
        while (_days.Contains(day.ToString("yyyy-MM-dd")))
        {
            count++;
            day = day.AddDays(-1);
        }

        return count;
    }

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
            done.Add(JsonValue.Create(id));
        }

        var days = new JsonArray();
        foreach (string d in _days)
        {
            days.Add(JsonValue.Create(d));
        }

        return new JsonObject { ["done"] = done, ["scores"] = scores, ["days"] = days }.ToJsonString();
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

                if (o["days"] is JsonArray days)
                {
                    foreach (JsonNode? n in days)
                    {
                        progress.RecordStudyDay(n is JsonValue v && v.TryGetValue(out string? d) ? d : null);
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
