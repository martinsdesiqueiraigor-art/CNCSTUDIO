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

        if (VersionOf(o) >= 2)
        {
            return ReadLessonV2(o);
        }

        JsonObject? ex = o["exercicio"] as JsonObject;
        return new Lesson(
            Str(o, "id"), Str(o, "modulo"), Str(o, "titulo"), Str(o, "nivel"), Str(o, "caminho"), Str(o, "status"),
            Str(o, "objetivo"), Str(o, "pre_requisito"), Str(o, "texto"), Str(o, "erro_comum"), Str(o, "programa"), Str(o, "aviso"),
            ex is null ? null : new LessonExercise(Str(ex, "tipo"), Str(ex, "enunciado"), Str(ex, "gabarito"), Editor(ex["editor"] as JsonObject)),
            Questions(o["quiz"]), Str(o, "observacoes"), Strings(o["diagramas"]),
            o["video"] is JsonObject v && (Str(v, "url").Length > 0) ? new LessonVideo(Str(v, "titulo"), Str(v, "url")) : null,
            Strings(o["resumo"]), Str(o, "abertura"));
    }

    /// <summary>Versão do arquivo: o campo "versao"; sem ele, 2 se tiver "secoes", senão 1.</summary>
    public static int VersionOf(JsonObject o) =>
        o["versao"] is JsonValue v && v.TryGetValue(out int n) ? n : o["secoes"] is JsonObject ? 2 : 1;

    /// <summary>
    /// Aula no formato v2 (uma seção por aba). Os campos que a tela atual lê continuam preenchidos: texto ← visao.texto,
    /// programa ← exemplo.programa, erro comum ← cuidados.erros, aviso ← cuidados.alertas, resumo ← visao.conceitos,
    /// exercício e quiz ← praticar.
    /// </summary>
    private static Lesson ReadLessonV2(JsonObject o)
    {
        JsonObject secoes = o["secoes"] as JsonObject ?? [];
        JsonObject Sec(string name) => secoes[name] as JsonObject ?? [];
        JsonObject visao = Sec("visao"), trajetoria = Sec("trajetoria"), sintaxe = Sec("sintaxe"), exemplo = Sec("exemplo"), cuidados = Sec("cuidados"), praticar = Sec("praticar");

        var status = new Dictionary<string, string>();
        foreach (string name in LessonSections.Names)
        {
            status[name] = secoes[name] is JsonObject s ? Str(s, "estado") : "";
        }

        List<SyntaxParam> parameters = [];
        if (sintaxe["parametros"] is JsonArray ps)
        {
            foreach (JsonNode? n in ps)
            {
                if (n is JsonObject p)
                {
                    parameters.Add(new SyntaxParam(Str(p, "endereco"), Str(p, "conceito")));
                }
            }
        }

        List<string> concepts = Strings(visao["conceitos"]), errors = Strings(cuidados["erros"]), alerts = Strings(cuidados["alertas"]);
        var sections = new LessonSections(status, concepts, Str(visao, "imagem"), Str(trajetoria, "programa"), Strings(sintaxe["blocos"]), parameters,
            Strings(exemplo["etapas"]), errors, alerts);
        JsonObject? ex = praticar["exercicio"] as JsonObject;
        string dialect = Str(o, "dialeto").ToLowerInvariant(), machine = Str(o, "maquina").ToLowerInvariant();
        return new Lesson(
            Str(o, "id"), Str(o, "modulo"), Str(o, "titulo"), Str(o, "nivel"), Str(o, "caminho"), Str(o, "estado"),
            Str(o, "objetivo"), Str(o, "pre_requisito"), Str(visao, "texto"), string.Join("\n\n", errors), Str(exemplo, "programa"),
            string.Join("\n\n", alerts),
            ex is null ? null : new LessonExercise(Str(ex, "tipo"), Str(ex, "enunciado"), Str(ex, "gabarito"), Editor(ex["editor"] as JsonObject)),
            Questions(praticar["quiz"]), Str(o, "observacoes"), Strings(o["diagramas"]),
            o["video"] is JsonObject v && (Str(v, "url").Length > 0) ? new LessonVideo(Str(v, "titulo"), Str(v, "url")) : null,
            // dialeto e máquina ficam como vieram (vazio = padrão fanuc/fresa ao ler; o validador do v2 exige os dois)
            concepts, "", dialect, machine, VersionOf(o), sections, Stock(o["bruto"] as JsonObject));
    }

    private static LessonStock? Stock(JsonObject? b) => b is null
        ? null
        : new LessonStock(Str(b, "tipo"), Num(b, "x") ?? 0, Num(b, "y") ?? 0, Num(b, "z") ?? 0, Num(b, "diametro") ?? 0, Num(b, "comprimento") ?? 0);

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

    private static ExerciseEditor? Editor(JsonObject? e)
    {
        if (e is null)
        {
            return null;
        }

        List<ExerciseCheck> checks = [];
        if (e["checagens"] is JsonArray array)
        {
            foreach (JsonNode? n in array)
            {
                if (n is JsonObject c)
                {
                    checks.Add(new ExerciseCheck(Str(c, "tipo"), Str(c, "texto"), Num(c, "x"), Num(c, "y"), Num(c, "z"), Str(c, "dica")));
                }
            }
        }

        return new ExerciseEditor(Str(e, "prefixo"), Str(e, "inicial"), checks, Str(e, "solucao"));
    }

    private static double? Num(JsonObject o, string key) => o[key] is JsonValue v && v.TryGetValue(out double d) ? d : null;

    private static List<string> Strings(JsonNode? node)
    {
        List<string> list = [];
        if (node is JsonArray array)
        {
            foreach (JsonNode? n in array)
            {
                if (n is JsonValue v && v.TryGetValue(out string? s) && !string.IsNullOrEmpty(s))
                {
                    list.Add(s);
                }
            }
        }

        return list;
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
