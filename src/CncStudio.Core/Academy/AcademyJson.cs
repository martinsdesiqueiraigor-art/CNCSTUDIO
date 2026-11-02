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
    public static string Catalog(string? progressJson, string? today = null)
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
                var module = new JsonObject
                {
                    ["id"] = plan.Id, ["title"] = plan.Title, ["description"] = plan.Description,
                    ["soon"] = plan.Soon || m is null || m.Lessons.Count == 0,
                };
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
                    // selo: todas as aulas feitas e o teste final aprovado
                    module["badge"] = m.Lessons.Count > 0 && m.Lessons.All(l => progress.IsDone(l.Id))
                        && m.Final is not null && progress.BestScore(m.Final.Id) >= m.Final.PassPercent;
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

        return new JsonObject { ["levels"] = levelNames, ["tracks"] = tracks, ["streak"] = progress.Streak(today) }.ToJsonString();
    }

    /// <summary>
    /// Uma aula completa para ler: texto, programa de exemplo, exercício e quiz. <paramref name="hideAnswers"/> falso (padrão, tela atual):
    /// o quiz vem com a resposta e a explicação; verdadeiro: sem elas, e a tela corrige pelo Core (<c>answer</c> e <c>grade</c>).
    /// </summary>
    public static string Lesson(string lessonId, bool hideAnswers = false)
    {
        Lesson? l = Library.FindLesson(lessonId);
        if (l is null)
        {
            return new JsonObject { ["ok"] = false }.ToJsonString();
        }

        string program = Library.Programs.GetValueOrDefault(l.ProgramPath, "");
        return new JsonObject
        {
            ["version"] = l.Version, ["dialect"] = AcademyTarget.DialectOf(l.Dialect) == Dialects.Dialect.Siemens ? "siemens" : "fanuc",
            ["machine"] = l.Machine == "torno" ? "torno" : "fresa",
            ["sections"] = Sections(l),
            ["ok"] = true, ["id"] = l.Id, ["module"] = l.ModuleId, ["title"] = l.Title, ["level"] = l.Level, ["track"] = l.Track,
            ["draft"] = l.Status != "revisado", ["goal"] = l.Goal, ["text"] = l.Text, ["commonMistake"] = l.CommonMistake,
            ["warning"] = l.Warning,
            ["programName"] = Path.GetFileName(l.ProgramPath),
            ["program"] = program,
            ["walk"] = Walk(program, l.Dialect, l.Machine),
            ["opening"] = l.Opening,
            ["summary"] = Strings(l.Summary ?? []),
            ["exercise"] = l.Exercise is null ? null : new JsonObject
            {
                ["prompt"] = l.Exercise.Prompt, ["answer"] = l.Exercise.Answer,
                ["editor"] = l.Exercise.Editor is null ? null : new JsonObject { ["prefix"] = l.Exercise.Editor.Prefix, ["initial"] = l.Exercise.Editor.Initial },
            },
            ["diagrams"] = Strings(l.Diagrams ?? []),
            ["video"] = l.Video is null ? null : new JsonObject { ["title"] = l.Video.Title, ["url"] = l.Video.Url },
            ["quizId"] = l.Id + "-quiz",
            ["quiz"] = Questions(l.Quiz, hideAnswers),
        }.ToJsonString();
    }

    /// <summary>
    /// Dados das abas do v2 (null numa aula v1): estado de cada seção, conceitos, imagem, programa e leitura da trajetória,
    /// blocos e parâmetros da sintaxe, etapas do exemplo, erros e alertas. Só acréscimo: a tela atual não lê este campo.
    /// </summary>
    private static JsonObject? Sections(Lesson l)
    {
        if (l.Sections is not { } s)
        {
            return null;
        }

        var status = new JsonObject();
        foreach (string name in LessonSections.Names)
        {
            status[name] = s.Status.GetValueOrDefault(name, "");
        }

        var parameters = new JsonArray();
        foreach (SyntaxParam p in s.SyntaxParams)
        {
            parameters.Add(new JsonObject { ["address"] = p.Address, ["concept"] = p.Concept });
        }

        string trajectory = Library.Programs.GetValueOrDefault(s.TrajectoryPath, "");
        return new JsonObject
        {
            ["status"] = status, ["concepts"] = Strings(s.Concepts), ["image"] = s.Image,
            ["trajectoryName"] = Path.GetFileName(s.TrajectoryPath), ["trajectory"] = trajectory, ["trajectoryWalk"] = Walk(trajectory, l.Dialect, l.Machine),
            ["syntaxBlocks"] = Strings(s.SyntaxBlocks), ["syntaxParams"] = parameters, ["exampleSteps"] = Strings(s.ExampleSteps),
            ["errors"] = Strings(s.Errors), ["alerts"] = Strings(s.Alerts),
        };
    }

    /// <summary>Leitura linha a linha: [{n,text,explain,rapid,path:[[x,y,z],…]}] e o bruto {min,max}, no dialeto e na máquina da aula.</summary>
    private static JsonObject? Walk(string program, string dialect = "fanuc", string machine = "fresa")
    {
        if (program.Length == 0)
        {
            return null;
        }

        Walkthrough w = ProgramWalkthrough.Build(program, dialect, machine);
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
    public static string FinalTest(string moduleId, int seed, bool hideAnswers = false)
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
            ["passPercent"] = f.PassPercent, ["canSkip"] = f.CanSkip, ["quiz"] = Questions(drawn, hideAnswers),
        }.ToJsonString();
    }

    /// <summary>
    /// Corrige: <paramref name="answersJson"/> é uma lista de índices (0–3, ou null/-1 em branco), na ordem de <paramref name="questionIdsJson"/>.
    /// Guarda a nota e, em quiz de aula aprovado, marca a aula como feita. Devolve {percent,passed,correct,total,progress,results},
    /// com results[i] = {id, ok, right, explanation}: a resposta certa só sai do Core depois da correção.
    /// </summary>
    public static string Grade(string quizId, string questionIdsJson, string answersJson, string? progressJson, string? today = null)
    {
        AcademyProgress progress = AcademyProgress.FromJson(progressJson);
        List<string> ids = StringList(questionIdsJson);
        List<int> answers = IntList(answersJson);

        IEnumerable<QuizQuestion> pool = QuizPool(quizId);
        List<QuizQuestion> questions = [.. ids.Select(id => pool.FirstOrDefault(q => q.Id == id)).Where(q => q is not null).Select(q => q!)];
        if (questions.Count == 0 || questions.Count != ids.Count)
        {
            return new JsonObject { ["ok"] = false }.ToJsonString();
        }

        int pass = Library.Module(ModuleOfFinal(quizId))?.Final?.PassPercent ?? QuizGrader.DefaultPassPercent;
        QuizResult r = QuizGrader.Grade(questions, answers, pass);
        progress.RecordScore(quizId, r.Percent);
        progress.RecordStudyDay(today);
        if (r.Passed && quizId.EndsWith("-quiz", StringComparison.Ordinal))
        {
            progress.MarkDone(quizId[..^5]);
        }

        var results = new JsonArray();
        for (int i = 0; i < questions.Count; i++)
        {
            results.Add(new JsonObject
            {
                ["id"] = questions[i].Id, ["ok"] = !r.WrongIndexes.Contains(i), ["right"] = questions[i].CorrectIndex,
                ["explanation"] = questions[i].Explanation,
            });
        }

        return new JsonObject
        {
            ["ok"] = true, ["percent"] = r.Percent, ["passed"] = r.Passed, ["correct"] = r.Correct, ["total"] = r.Total,
            ["progress"] = progress.ToJson(), ["results"] = results,
        }.ToJsonString();
    }

    /// <summary>
    /// Corrige uma pergunta só (resposta na hora, sem guardar nota): {ok, correct, right, explanation}. É o que a tela usa
    /// quando o quiz chega sem gabarito (<c>hideAnswers</c>).
    /// </summary>
    public static string Answer(string quizId, string questionId, int choice)
    {
        QuizQuestion? q = QuizPool(quizId).FirstOrDefault(x => x.Id == questionId);
        return q is null
            ? new JsonObject { ["ok"] = false }.ToJsonString()
            : new JsonObject { ["ok"] = true, ["correct"] = choice == q.CorrectIndex, ["right"] = q.CorrectIndex, ["explanation"] = q.Explanation }.ToJsonString();
    }

    /// <summary>Perguntas do quiz da aula ("…-quiz") ou do teste final ("…-teste-final").</summary>
    private static IEnumerable<QuizQuestion> QuizPool(string quizId) =>
        Library.FindLesson(quizId.EndsWith("-quiz", StringComparison.Ordinal) ? quizId[..^5] : "")?.Quiz
            ?? Library.Module(ModuleOfFinal(quizId))?.Final?.Questions
            ?? [];

    /// <summary>
    /// Ponto único de entrada da tela: <paramref name="command"/> = catalog | lesson | final | grade | answer | check | done, com os
    /// argumentos em <paramref name="argsJson"/> ({progress, id, module, seed, quizId, ids, answers, qid, choice, hideAnswers}).
    /// Devolve o JSON do comando.
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
        bool hide = args["hideAnswers"] is JsonValue hv && hv.TryGetValue(out bool h) && h;
        return command switch
        {
            "catalog" => Catalog(progress, Text("today")),
            "lesson" => Lesson(Text("id"), hide),
            "final" => FinalTest(Text("module"), args["seed"] is JsonValue sv && sv.TryGetValue(out int seed) ? seed : 1, hide),
            "grade" => Grade(Text("quizId"), args["ids"]?.ToJsonString() ?? "[]", args["answers"]?.ToJsonString() ?? "[]", progress, Text("today")),
            "answer" => Answer(Text("quizId"), Text("qid"), args["choice"] is JsonValue cv && cv.TryGetValue(out int choice) ? choice : -1),
            "check" => CheckExercise(Text("id"), Text("text")),
            "done" => new JsonObject { ["ok"] = true, ["progress"] = MarkDone(Text("id"), progress, Text("today")) }.ToJsonString(),
            _ => new JsonObject { ["ok"] = false }.ToJsonString(),
        };
    }

    /// <summary>Marca a aula como feita (botão "Concluir aula").</summary>
    public static string MarkDone(string lessonId, string? progressJson, string? today = null)
    {
        AcademyProgress progress = AcademyProgress.FromJson(progressJson);
        if (Library.FindLesson(lessonId) is not null)
        {
            progress.MarkDone(lessonId);
            progress.RecordStudyDay(today);
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
        Lesson? lesson = Library.FindLesson(lessonId);
        ExerciseEditor? editor = lesson?.Exercise?.Editor;
        if (lesson is null || editor is null)
        {
            return new JsonObject { ["ok"] = false }.ToJsonString();
        }

        CheckResult r = ExerciseChecker.Check(editor, studentText, lesson.Dialect, lesson.Machine);
        return new JsonObject { ["ok"] = true, ["passed"] = r.Passed, ["messages"] = Strings(r.Messages) }.ToJsonString();
    }

    private static string ModuleOfFinal(string quizId) =>
        quizId.EndsWith("-teste-final", StringComparison.Ordinal) ? quizId[..^"-teste-final".Length] : "";

    private static JsonArray Questions(IEnumerable<QuizQuestion> questions, bool hideAnswers = false)
    {
        var array = new JsonArray();
        foreach (QuizQuestion q in questions)
        {
            var options = new JsonArray();
            foreach (string o in q.Options)
            {
                options.Add(JsonValue.Create(o));
            }

            var item = new JsonObject
            {
                ["id"] = q.Id, ["prompt"] = q.Prompt, ["options"] = options,
                ["hint"] = q.Hint, ["reading"] = q.Kind.Contains("leitura", StringComparison.OrdinalIgnoreCase),
            };
            if (!hideAnswers)
            {
                // compatível com a tela atual, que corrige na hora com a resposta em mãos
                item["correct"] = q.CorrectIndex;
                item["explanation"] = q.Explanation;
            }

            array.Add(item);
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
