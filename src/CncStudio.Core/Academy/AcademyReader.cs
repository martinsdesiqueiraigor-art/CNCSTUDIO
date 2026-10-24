using System.Text.Json.Nodes;

namespace CncStudio.Core.Academy;

/// <summary>Lê as aulas e o teste final (JSON) para os modelos. Tolerante: campo ausente vira texto vazio, e a conferência fica no <see cref="AcademyValidator"/>.</summary>
public static class AcademyReader
{
    public static Lesson? ReadLesson(string json, out string error)
    {
        error = "";
        if (!TryObject(json, out JsonObject? o, out error) || o is null)
        {
            return null;
        }

        JsonObject? ex = o["exercicio"] as JsonObject;
        return new Lesson(
            Str(o, "id"), Str(o, "modulo"), Str(o, "titulo"), Str(o, "nivel"), Str(o, "caminho"), Str(o, "status"),
            Str(o, "objetivo"), Str(o, "pre_requisito"), Str(o, "texto"), Str(o, "erro_comum"), Str(o, "programa"), Str(o, "aviso"),
            ex is null ? null : new LessonExercise(Str(ex, "tipo"), Str(ex, "enunciado"), Str(ex, "gabarito")),
            Questions(o["quiz"]), Str(o, "observacoes"));
    }

    public static FinalTest? ReadFinal(string json, out string error)
    {
        error = "";
        if (!TryObject(json, out JsonObject? o, out error) || o is null)
        {
            return null;
        }

        int pass = o["nota_minima_percentual"] is JsonValue v && v.TryGetValue(out double d) ? (int)Math.Round(d) : 70;
        bool skip = o["pode_pular"] is JsonValue s && s.TryGetValue(out bool b) && b;
        return new FinalTest(Str(o, "id"), Str(o, "modulo"), Str(o, "status"), pass, skip, Questions(o["questoes"]));
    }

    private static bool TryObject(string json, out JsonObject? obj, out string error)
    {
        obj = null;
        error = "";
        try
        {
            obj = JsonNode.Parse(json) as JsonObject;
            if (obj is null)
            {
                error = "O arquivo não é um objeto JSON.";
            }
        }
        catch (System.Text.Json.JsonException e)
        {
            error = "JSON inválido: " + e.Message;
        }

        return obj is not null;
    }

    private static string Str(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "";

    private static List<QuizQuestion> Questions(JsonNode? node)
    {
        List<QuizQuestion> list = [];
        if (node is not JsonArray array)
        {
            return list;
        }

        foreach (JsonNode? item in array)
        {
            if (item is not JsonObject q)
            {
                continue;
            }

            List<string> options = [];
            if (q["alternativas"] is JsonArray alts)
            {
                foreach (JsonNode? a in alts)
                {
                    options.Add(a is JsonValue av && av.TryGetValue(out string? t) ? t ?? "" : "");
                }
            }

            string letter = Str(q, "correta").Trim().ToUpperInvariant();
            int index = letter.Length == 1 && letter[0] is >= 'A' and <= 'Z' ? letter[0] - 'A' : -1;
            list.Add(new QuizQuestion(Str(q, "id"), Str(q, "enunciado"), options, index, Str(q, "explicacao"), Str(q, "dica"), Str(q, "tipo")));
        }

        return list;
    }
}
