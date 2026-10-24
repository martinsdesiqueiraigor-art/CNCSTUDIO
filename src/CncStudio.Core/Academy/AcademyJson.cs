using System.Text.Json;
using System.Text.Json.Nodes;

namespace CncStudio.Core.Academy;

/// <summary>Ponte em JSON entre a Academy (Core) e a tela em HTML da web. O progresso vem e volta como texto (guardado no aparelho).</summary>
public static class AcademyJson
{
    private static AcademyLibrary? _library;

    private static AcademyLibrary Library => _library ??= AcademyLibrary.FromEmbedded();

    /// <summary>Troca a biblioteca (testes).</summary>
    internal static void UseLibrary(AcademyLibrary? library) => _library = library;

    /// <summary>
    /// Trilhas e níveis: {"levels":[…],"tracks":[{"name","levels":[{"level","module":null|{id,title,lessons:[{id,title,done}],hasFinal,finalBest,draft}}]}]}.
    /// Módulo sem conteúdo vem com <c>soon:true</c> (aparece como "em breve").
    /// </summary>
    public static string Catalog(string? progressJson)
    {
        AcademyProgress progress = AcademyProgress.FromJson(progressJson);
        var tracks = new JsonArray();
        foreach (string track in AcademyLibrary.Tracks)
        {
            var levels = new JsonArray();
            foreach (string level in AcademyLibrary.Levels)
            {
                PlannedModule? plan = AcademyLibrary.Plan.FirstOrDefault(p => p.Track == track && p.Level == level);
                if (plan is null)
                {
                    levels.Add(new JsonObject { ["level"] = level, ["module"] = null });
                    continue;
                }

                AcademyModule? m = Library.Module(plan.Id);
                var module = new JsonObject { ["id"] = plan.Id, ["title"] = plan.Title, ["soon"] = m is null || m.Lessons.Count == 0 };
                if (m is not null)
                {
                    var lessons = new JsonArray();
                    foreach (Lesson l in m.Lessons)
                    {
                        lessons.Add(new JsonObject { ["id"] = l.Id, ["title"] = l.Title, ["done"] = progress.IsDone(l.Id) });
                    }

                    module["lessons"] = lessons;
                    module["hasFinal"] = m.Final is not null;
                    module["finalBest"] = m.Final is null ? -1 : progress.BestScore(m.Final.Id);
                    module["draft"] = m.Lessons.Any(l => l.Status != "revisado");
                }

                levels.Add(new JsonObject { ["level"] = level, ["module"] = module });
            }

            tracks.Add(new JsonObject { ["name"] = track, ["levels"] = levels });
        }

        var levelNames = new JsonArray();
        foreach (string l in AcademyLibrary.Levels)
        {
            levelNames.Add(JsonValue.Create(l));
        }

        return new JsonObject { ["levels"] = levelNames, ["tracks"] = tracks }.ToJsonString();
    }

    /// <summary>Uma aula completa para ler: texto, programa de exemplo, exercício e quiz (com a resposta e a explicação, para corrigir na hora).</summary>
    public static string Lesson(string lessonId)
    {
        Lesson? l = Library.FindLesson(lessonId);
        if (l is null)
        {
            return new JsonObject { ["ok"] = false }.ToJsonString();
        }

        return new JsonObject
        {
            ["ok"] = true, ["id"] = l.Id, ["module"] = l.ModuleId, ["title"] = l.Title, ["level"] = l.Level, ["track"] = l.Track,
            ["draft"] = l.Status != "revisado", ["goal"] = l.Goal, ["text"] = l.Text, ["commonMistake"] = l.CommonMistake,
            ["warning"] = l.Warning,
            ["programName"] = Path.GetFileName(l.ProgramPath),
            ["program"] = Library.Programs.GetValueOrDefault(l.ProgramPath, ""),
            ["walk"] = Walk(Library.Programs.GetValueOrDefault(l.ProgramPath, "")),
            ["exercise"] = l.Exercise is null ? null : new JsonObject
            {
                ["prompt"] = l.Exercise.Prompt, ["answer"] = l.Exercise.Answer,
                ["editor"] = l.Exercise.Editor is null ? null : new JsonObject { ["prefix"] = l.Exercise.Editor.Prefix, ["initial"] = l.Exercise.Editor.Initial },
            },
            ["diagrams"] = Strings(l.Diagrams ?? []),
            ["video"] = l.Video is null ? null : new JsonObject { ["title"] = l.Video.Title, ["url"] = l.Video.Url },
            ["quizId"] = l.Id + "-quiz",
            ["quiz"] = Questions(l.Quiz),
        }.ToJsonString();
    }

    /// <summary>Leitura linha a linha: [{n,text,explain,rapid,path:[[x,y,z],…]}] e o bruto {min,max}.</summary>
    private static JsonObject? Walk(string program)
    {
        if (program.Length == 0)
        {
            return null;
        }

        Walkthrough w = ProgramWalkthrough.Build(program);
        var lines = new JsonArray();
        foreach (WalkLine l in w.Lines)
        {
            var path = new JsonArray();
            foreach (var p in l.Path)
            {
                path.Add(new JsonArray(JsonValue.Create(Math.Round(p.X, 3)), JsonValue.Create(Math.Round(p.Y, 3)), JsonValue.Create(Math.Round(p.Z, 3))));
            }

            lines.Add(new JsonObject { ["n"] = l.Number, ["text"] = l.Text, ["explain"] = l.Explanation, ["rapid"] = l.Rapid, ["path"] = path });
        }

        return new JsonObject
        {
            ["lines"] = lines,
            ["min"] = new JsonArray(JsonValue.Create(w.StockMin.X), JsonValue.Create(w.StockMin.Y), JsonValue.Create(w.StockMin.Z)),
            ["max"] = new JsonArray(JsonValue.Create(w.StockMax.X), JsonValue.Create(w.StockMax.Y), JsonValue.Create(w.StockMax.Z)),
        };
    }

    /// <summary>Teste final do módulo: sorteia 10 perguntas do banco mais as de leitura de programa (semente repetível).</summary>
    public static string FinalTest(string moduleId, int seed)
    {
        FinalTest? f = Library.Module(moduleId)?.Final;
        if (f is null)
        {
            return new JsonObject { ["ok"] = false }.ToJsonString();
        }

        List<QuizQuestion> drawn = QuizGrader.Draw(f.Questions, Math.Min(f.Questions.Count, 12), seed);
        return new JsonObject
        {
            ["ok"] = true, ["quizId"] = f.Id, ["module"] = moduleId, ["draft"] = f.Status != "revisado",
            ["passPercent"] = f.PassPercent, ["canSkip"] = f.CanSkip, ["quiz"] = Questions(drawn),
        }.ToJsonString();
    }

    /// <summary>
    /// Corrige: <paramref name="answersJson"/> é uma lista de índices (0–3, ou null/-1 em branco), na ordem de <paramref name="questionIdsJson"/>.
    /// Guarda a nota e, em quiz de aula aprovado, marca a aula como feita. Devolve {percent,passed,correct,total,progress}.
    /// </summary>
    public static string Grade(string quizId, string questionIdsJson, string answersJson, string? progressJson)
    {
        AcademyProgress progress = AcademyProgress.FromJson(progressJson);
        List<string> ids = StringList(questionIdsJson);
        List<int> answers = IntList(answersJson);

        IEnumerable<QuizQuestion> pool = Library.FindLesson(quizId.EndsWith("-quiz", StringComparison.Ordinal) ? quizId[..^5] : "")?.Quiz
            ?? Library.Module(ModuleOfFinal(quizId))?.Final?.Questions
            ?? [];
        List<QuizQuestion> questions = [.. ids.Select(id => pool.FirstOrDefault(q => q.Id == id)).Where(q => q is not null).Select(q => q!)];
        if (questions.Count == 0 || questions.Count != ids.Count)
        {
            return new JsonObject { ["ok"] = false }.ToJsonString();
        }

        int pass = Library.Module(ModuleOfFinal(quizId))?.Final?.PassPercent ?? QuizGrader.DefaultPassPercent;
        QuizResult r = QuizGrader.Grade(questions, answers, pass);
        progress.RecordScore(quizId, r.Percent);
        if (r.Passed && quizId.EndsWith("-quiz", StringComparison.Ordinal))
        {
            progress.MarkDone(quizId[..^5]);
        }

        return new JsonObject
        {
            ["ok"] = true, ["percent"] = r.Percent, ["passed"] = r.Passed, ["correct"] = r.Correct, ["total"] = r.Total,
            ["progress"] = progress.ToJson(),
        }.ToJsonString();
    }

    /// <summary>
    /// Ponto único de entrada da tela: <paramref name="command"/> = catalog | lesson | final | grade | done, com os argumentos
    /// em <paramref name="argsJson"/> ({progress, id, module, seed, quizId, ids, answers}). Devolve o JSON do comando.
    /// </summary>
    public static string Dispatch(string command, string argsJson)
    {
        try
        {
            return DispatchCore(command, argsJson);
        }
        catch (Exception e)
        {
            // a tela mostra o motivo em vez de falhar em silêncio
            return new JsonObject { ["ok"] = false, ["error"] = e.GetType().Name + ": " + e.Message }.ToJsonString();
        }
    }

    private static string DispatchCore(string command, string argsJson)
    {
        JsonObject args;
        try
        {
            args = JsonNode.Parse(argsJson) as JsonObject ?? [];
        }
        catch (JsonException)
        {
            args = [];
        }

        string Text(string key) => args[key] is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "";
        string progress = Text("progress");
        return command switch
        {
            "catalog" => Catalog(progress),
            "lesson" => Lesson(Text("id")),
            "final" => FinalTest(Text("module"), args["seed"] is JsonValue sv && sv.TryGetValue(out int seed) ? seed : 1),
            "grade" => Grade(Text("quizId"), args["ids"]?.ToJsonString() ?? "[]", args["answers"]?.ToJsonString() ?? "[]", progress),
            "check" => CheckExercise(Text("id"), Text("text")),
            "done" => new JsonObject { ["ok"] = true, ["progress"] = MarkDone(Text("id"), progress) }.ToJsonString(),
            _ => new JsonObject { ["ok"] = false }.ToJsonString(),
        };
    }

    /// <summary>Marca a aula como feita (botão "Concluir aula").</summary>
    public static string MarkDone(string lessonId, string? progressJson)
    {
        AcademyProgress progress = AcademyProgress.FromJson(progressJson);
        if (Library.FindLesson(lessonId) is not null)
        {
            progress.MarkDone(lessonId);
        }

        return progress.ToJson();
    }

    private static JsonArray Strings(IEnumerable<string> items)
    {
        var array = new JsonArray();
        foreach (string s in items)
        {
            array.Add(JsonValue.Create(s));
        }

        return array;
    }

    /// <summary>Confere o programa digitado no exercício da aula: {ok, passed, messages[]}.</summary>
    public static string CheckExercise(string lessonId, string studentText)
    {
        ExerciseEditor? editor = Library.FindLesson(lessonId)?.Exercise?.Editor;
        if (editor is null)
        {
            return new JsonObject { ["ok"] = false }.ToJsonString();
        }

        CheckResult r = ExerciseChecker.Check(editor, studentText);
        return new JsonObject { ["ok"] = true, ["passed"] = r.Passed, ["messages"] = Strings(r.Messages) }.ToJsonString();
    }

    private static string ModuleOfFinal(string quizId) =>
        quizId.EndsWith("-teste-final", StringComparison.Ordinal) ? quizId[..^"-teste-final".Length] : "";

    private static JsonArray Questions(IEnumerable<QuizQuestion> questions)
    {
        var array = new JsonArray();
        foreach (QuizQuestion q in questions)
        {
            var options = new JsonArray();
            foreach (string o in q.Options)
            {
                options.Add(JsonValue.Create(o));
            }

            array.Add(new JsonObject
            {
                ["id"] = q.Id, ["prompt"] = q.Prompt, ["options"] = options, ["correct"] = q.CorrectIndex,
                ["explanation"] = q.Explanation, ["hint"] = q.Hint, ["reading"] = q.Kind.Contains("leitura", StringComparison.OrdinalIgnoreCase),
            });
        }

        return array;
    }

    private static List<string> StringList(string json)
    {
        try
        {
            return JsonNode.Parse(json) is JsonArray a ? [.. a.Select(n => n is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "")] : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static List<int> IntList(string json)
    {
        try
        {
            return JsonNode.Parse(json) is JsonArray a ? [.. a.Select(n => n is JsonValue v && v.TryGetValue(out int i) ? i : -1)] : [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
