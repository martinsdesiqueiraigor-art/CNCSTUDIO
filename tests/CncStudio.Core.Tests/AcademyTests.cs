using System.Text.Json.Nodes;
using CncStudio.Core.Academy;
using Xunit;

namespace CncStudio.Core.Tests;

public class AcademyTests
{
    private const string Program = "%\nO1001\n(SIMULE ANTES DE RODAR NA MAQUINA)\nG21 G90 G17\nG54\nG00 X0.0 Y0.0\nG01 X10.0 F100.\nM30\n%\n";

    private static JsonObject Question(string id, int correct, string explanation = "A correta mostra o conceito; B troca os eixos; C confunde zero máquina com zero peça; D ignora a unidade.", string hint = "")
    {
        return new JsonObject
        {
            ["id"] = id, ["enunciado"] = "Pergunta " + id,
            ["alternativas"] = new JsonArray("um", "dois", "três", "quatro"),
            ["correta"] = "ABCD"[correct].ToString(), ["explicacao"] = explanation, ["dica"] = hint.Length > 0 ? hint : "Dica " + id,
        };
    }

    private static string LessonJson(string module, int n, Func<string, int, JsonObject>? question = null)
    {
        question ??= (id, i) => Question(id, i % 4);
        string id = $"{module}-a{n:00}";
        var quiz = new JsonArray();
        for (int i = 0; i < 4; i++)
        {
            quiz.Add(question($"{id}-q{i + 1:00}", i));
        }

        return new JsonObject
        {
            ["id"] = id, ["modulo"] = module, ["titulo"] = "Aula " + n, ["nivel"] = "Introdução", ["caminho"] = "Comum", ["status"] = "rascunho",
            ["objetivo"] = "Entender o conceito.", ["pre_requisito"] = "", ["texto"] = string.Join(' ', Enumerable.Repeat("palavra", 320)),
            ["erro_comum"] = "Esquecer o zero.", ["programa"] = $"samples/academy/{id}.nc", ["aviso"] = "Simule antes de rodar na máquina",
            ["exercicio"] = new JsonObject { ["tipo"] = "complete", ["enunciado"] = "Complete ____", ["gabarito"] = "G54" },
            ["quiz"] = quiz,
            ["resumo"] = new JsonArray("ponto um", "ponto dois", "ponto três"),
        }.ToJsonString();
    }

    private static string FinalJson(string module, bool balanced = true)
    {
        var qs = new JsonArray();
        for (int i = 0; i < 12; i++)
        {
            JsonObject q = Question($"{module}-final-p{i + 1:00}", balanced ? i % 4 : 0);
            q["tipo"] = i >= 10 ? "leitura_programa" : "conceito";
            qs.Add(q);
        }

        return new JsonObject
        {
            ["id"] = module + "-teste-final", ["modulo"] = module, ["status"] = "rascunho", ["nota_minima_percentual"] = 70,
            ["pode_pular"] = true, ["questoes"] = qs,
        }.ToJsonString();
    }

    private static List<(string, string)> Files(string module = "acad-m1", Func<string, int, JsonObject>? question = null, bool balanced = true)
    {
        List<(string, string)> files = [];
        for (int n = 1; n <= 5; n++)
        {
            files.Add(($"{module}-a{n:00}.json", LessonJson(module, n, question)));
            files.Add(($"{module}-a{n:00}.nc", Program));
        }

        files.Add(($"{module}-teste-final.json", FinalJson(module, balanced)));
        return files;
    }

    [Fact]
    public void WellFormedModuleHasNoErrors()
    {
        AcademyLibrary lib = AcademyLibrary.FromFiles(Files());
        List<ContentIssue> issues = lib.Validate();
        Assert.DoesNotContain(issues, i => i.IsError);
        AcademyModule m = lib.Module("acad-m1")!;
        Assert.Equal(5, m.Lessons.Count);
        Assert.Equal(12, m.Final!.Questions.Count);
        Assert.Equal(70, m.Final.PassPercent);
    }

    [Fact]
    public void GenericExplanationsAndRepeatedHintsAreRejected()
    {
        var files = Files(question: (id, i) => Question(id, i, "A: correta. B: não atende ao enunciado. C: não atende ao enunciado.", "Leia o trecho"));
        List<ContentIssue> issues = AcademyLibrary.FromFiles(files).Validate();
        Assert.Contains(issues, i => i.IsError && i.Message.Contains("genérica"));
        Assert.Contains(issues, i => i.IsError && i.Message.Contains("dicas são iguais"));
    }

    [Fact]
    public void UnbalancedFinalTestIsRejected()
    {
        List<ContentIssue> issues = AcademyLibrary.FromFiles(Files(balanced: false)).Validate();
        Assert.Contains(issues, i => i.IsError && i.Message.Contains("desbalanceadas"));
    }

    [Fact]
    public void BrokenFilesAndMissingProgramAreReported()
    {
        var files = Files().Where(f => f.Item1 != "acad-m1-a02.nc").ToList();
        files.Add(("acad-m1-a06.json", "{ nao e json"));
        List<ContentIssue> issues = AcademyLibrary.FromFiles(files).Validate();
        Assert.Contains(issues, i => i.IsError && i.Message.Contains("Programa de exemplo não encontrado"));
        Assert.Contains(issues, i => i.IsError && i.Message.Contains("JSON inválido"));
    }

    [Fact]
    public void ModuleOutsideThePlanIsReported()
    {
        List<ContentIssue> issues = AcademyLibrary.FromFiles(Files("acad-m9")).Validate();
        Assert.Contains(issues, i => i.Message.Contains("fora do plano"));
    }

    [Fact]
    public void GraderCountsAnswersAndPassMark()
    {
        var qs = new[] { 0, 1, 2, 3 }.Select(i => new QuizQuestion("q" + i, "p", ["a", "b", "c", "d"], i, "x", "")).ToList();
        QuizResult all = QuizGrader.Grade(qs, [0, 1, 2, 3]);
        Assert.True(all.Passed);
        Assert.Equal(100, all.Percent);
        QuizResult some = QuizGrader.Grade(qs, [0, 1, 0, -1]);
        Assert.Equal(50, some.Percent);
        Assert.False(some.Passed);
        Assert.Equal([2, 3], some.WrongIndexes);
        Assert.Equal(0, QuizGrader.Grade(qs, []).Percent);
        Assert.True(QuizGrader.Grade(qs, [0, 1, 2, 0], 70).Passed);   // 75%
        Assert.False(QuizGrader.Grade(qs, [0, 1, 2, 0], 80).Passed);
    }

    [Fact]
    public void DrawKeepsReadingQuestionsAndIsRepeatable()
    {
        AcademyModule m = AcademyLibrary.FromFiles(Files()).Module("acad-m1")!;
        List<QuizQuestion> a = QuizGrader.Draw(m.Final!.Questions, 12, 7), b = QuizGrader.Draw(m.Final.Questions, 12, 7);
        Assert.Equal(a.Select(q => q.Id), b.Select(q => q.Id));
        Assert.Equal(2, QuizGrader.Draw(m.Final.Questions, 6, 3).Count(q => q.Kind.Contains("leitura")));
        Assert.Equal(6, QuizGrader.Draw(m.Final.Questions, 6, 3).Count);
    }

    [Fact]
    public void ProgressKeepsBestScoreAndSurvivesBadJson()
    {
        AcademyProgress p = new();
        p.MarkDone("acad-m1-a01");
        p.RecordScore("quiz", 50);
        p.RecordScore("quiz", 25);
        p.RecordScore("quiz", 100);
        AcademyProgress back = AcademyProgress.FromJson(p.ToJson());
        Assert.True(back.IsDone("acad-m1-a01"));
        Assert.Equal(100, back.BestScore("quiz"));
        Assert.Equal(-1, back.BestScore("nada"));
        Assert.Empty(AcademyProgress.FromJson("{ quebrado").CompletedLessons);
        Assert.Empty(AcademyProgress.FromJson(null).CompletedLessons);
    }

    [Fact]
    public void CatalogShowsPlanWithSoonAndTheLoadedModule()
    {
        AcademyJson.UseLibrary(AcademyLibrary.FromFiles(Files()));
        try
        {
            JsonNode cat = JsonNode.Parse(AcademyJson.Catalog(null))!;
            JsonArray tracks = cat["tracks"]!.AsArray();
            Assert.Equal(["Comum", "Fresa", "Torno"], tracks.Select(t => (string)t!["name"]!));
            JsonObject comum = tracks[0]!["levels"]!.AsArray()[0]!["module"]!.AsObject();
            Assert.False((bool)comum["soon"]!);
            Assert.Equal(5, comum["lessons"]!.AsArray().Count);
            JsonObject fresaIniciante = tracks[1]!["levels"]!.AsArray()[1]!["module"]!.AsObject();
            Assert.True((bool)fresaIniciante["soon"]!);
            // nível sem módulo no piloto
            Assert.Null(tracks[2]!["levels"]!.AsArray()[2]!["module"]);
        }
        finally
        {
            AcademyJson.UseLibrary(null);
        }
    }

    [Fact]
    public void GradeLessonQuizMarksLessonDoneOnlyWhenPassed()
    {
        AcademyJson.UseLibrary(AcademyLibrary.FromFiles(Files()));
        try
        {
            JsonNode lesson = JsonNode.Parse(AcademyJson.Lesson("acad-m1-a01"))!;
            Assert.True((bool)lesson["ok"]!);
            Assert.Contains("G54", (string)lesson["program"]!);
            string quizId = (string)lesson["quizId"]!;
            JsonArray quiz = lesson["quiz"]!.AsArray();
            string ids = new JsonArray(quiz.Select(q => (JsonNode)(string)q!["id"]!).ToArray()).ToJsonString();

            JsonNode bad = JsonNode.Parse(AcademyJson.Grade(quizId, ids, "[3,3,3,3]", null))!;
            Assert.False((bool)bad["passed"]!);
            Assert.False(AcademyProgress.FromJson((string)bad["progress"]!).IsDone("acad-m1-a01"));

            string right = new JsonArray(quiz.Select(q => (JsonNode)(int)q!["correct"]!).ToArray()).ToJsonString();
            JsonNode good = JsonNode.Parse(AcademyJson.Grade(quizId, ids, right, (string)bad["progress"]!))!;
            Assert.True((bool)good["passed"]!);
            Assert.True(AcademyProgress.FromJson((string)good["progress"]!).IsDone("acad-m1-a01"));
            Assert.Equal(100, AcademyProgress.FromJson((string)good["progress"]!).BestScore(quizId));

            Assert.False((bool)JsonNode.Parse(AcademyJson.Lesson("nao-existe"))!["ok"]!);
            Assert.False((bool)JsonNode.Parse(AcademyJson.Grade(quizId, "[\"x\"]", "[0]", null))!["ok"]!);
        }
        finally
        {
            AcademyJson.UseLibrary(null);
        }
    }

    [Fact]
    public void DispatchRoutesCommandsAndSurvivesBadInput()
    {
        AcademyJson.UseLibrary(AcademyLibrary.FromFiles(Files()));
        try
        {
            Assert.True((bool)JsonNode.Parse(AcademyJson.Dispatch("lesson", "{\"id\":\"acad-m1-a02\"}"))!["ok"]!);
            Assert.NotNull(JsonNode.Parse(AcademyJson.Dispatch("catalog", "{}"))!["tracks"]);
            Assert.True((bool)JsonNode.Parse(AcademyJson.Dispatch("final", "{\"module\":\"acad-m1\",\"seed\":3}"))!["ok"]!);
            JsonNode done = JsonNode.Parse(AcademyJson.Dispatch("done", "{\"id\":\"acad-m1-a02\"}"))!;
            Assert.True(AcademyProgress.FromJson((string)done["progress"]!).IsDone("acad-m1-a02"));
            Assert.False((bool)JsonNode.Parse(AcademyJson.Dispatch("xyz", "{}"))!["ok"]!);
            Assert.False((bool)JsonNode.Parse(AcademyJson.Dispatch("lesson", "isto não é json"))!["ok"]!);
        }
        finally
        {
            AcademyJson.UseLibrary(null);
        }
    }

    [Fact]
    public void FinalTestUsesModulePassMark()
    {
        AcademyJson.UseLibrary(AcademyLibrary.FromFiles(Files()));
        try
        {
            JsonNode t = JsonNode.Parse(AcademyJson.FinalTest("acad-m1", 1))!;
            Assert.True((bool)t["ok"]!);
            Assert.Equal(70, (int)t["passPercent"]!);
            JsonArray quiz = t["quiz"]!.AsArray();
            string ids = new JsonArray(quiz.Select(q => (JsonNode)(string)q!["id"]!).ToArray()).ToJsonString();
            string right = new JsonArray(quiz.Select(q => (JsonNode)(int)q!["correct"]!).ToArray()).ToJsonString();
            JsonNode r = JsonNode.Parse(AcademyJson.Grade((string)t["quizId"]!, ids, right, null))!;
            Assert.True((bool)r["passed"]!);
            Assert.Equal(100, AcademyProgress.FromJson((string)r["progress"]!).BestScore("acad-m1-teste-final"));
        }
        finally
        {
            AcademyJson.UseLibrary(null);
        }
    }

    [Fact]
    public void EmbeddedContentInTheRepositoryIsValid()
    {
        // vale para o que estiver em content/academy e samples/academy; sem conteúdo, nada a conferir
        List<ContentIssue> errors = [.. AcademyLibrary.FromEmbedded().Validate().Where(i => i.IsError)];
        Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => e.Where + ": " + e.Message)));
    }
}

public class AcademyEmbeddedTests
{
    [Fact]
    public void EmbeddedCatalogAndLessonWork()
    {
        AcademyJson.UseLibrary(null);
        JsonNode cat = JsonNode.Parse(AcademyJson.Dispatch("catalog", "{\"progress\":\"\"}"))!;
        JsonNode first = cat["tracks"]![0]!["levels"]![0]!["module"]!;
        Assert.False((bool)first["soon"]!);
        Assert.Equal(5, first["lessons"]!.AsArray().Count);
        Assert.True((bool)JsonNode.Parse(AcademyJson.Dispatch("lesson", "{\"id\":\"acad-m1-a01\"}"))!["ok"]!);
        Assert.True((bool)JsonNode.Parse(AcademyJson.Dispatch("final", "{\"module\":\"acad-m1\",\"seed\":5}"))!["ok"]!);
    }
}

public class WalkthroughTests
{
    private const string Prog = "%\nO1002\n(BRUTO: BLOCO 80 X 60 X 20 MM)\nG21 G90 G17 G94\nG54\nG00 Z20.0\nG00 X10.0 Y10.0\nG01 X40.0 Y10.0 Z15.0 F120.0\nX40.0 Y30.0\nM30\n%\n";

    [Fact]
    public void ExplainsEachLineAndKeepsModalMotion()
    {
        Walkthrough w = ProgramWalkthrough.Build(Prog);
        string Explain(string text) => w.Lines.First(l => l.Text == text).Explanation;
        Assert.Contains("comentário", Explain("(BRUTO: BLOCO 80 X 60 X 20 MM)"), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("milímetros", Explain("G21 G90 G17 G94"));
        Assert.Contains("absolutas", Explain("G21 G90 G17 G94"));
        Assert.Contains("rápido", Explain("G00 X10.0 Y10.0"));
        Assert.Contains("120", Explain("G01 X40.0 Y10.0 Z15.0 F120.0"));
        string modal = Explain("X40.0 Y30.0");
        Assert.Contains("linha reta", modal);
        Assert.Contains("linha anterior", modal);
        Assert.Contains("fim do programa", Explain("M30"));
    }

    [Fact]
    public void MovingLinesCarryThePathAndStockComesFromTheHeader()
    {
        Walkthrough w = ProgramWalkthrough.Build(Prog);
        WalkLine rapid = w.Lines.First(l => l.Text == "G00 X10.0 Y10.0");
        Assert.True(rapid.Rapid);
        Assert.Equal(2, rapid.Path.Count);
        Assert.Equal(10, rapid.Path[1].X);
        WalkLine cut = w.Lines.First(l => l.Text.StartsWith("G01"));
        Assert.False(cut.Rapid);
        Assert.Equal(15, cut.Path[^1].Z);
        Assert.Empty(w.Lines.First(l => l.Text == "G54").Path);
        Assert.Equal(new CncStudio.Core.Geometry.Point3(0, 0, -20), w.StockMin);
        Assert.Equal(new CncStudio.Core.Geometry.Point3(80, 60, 0), w.StockMax);
    }

    [Fact]
    public void EveryEmbeddedLessonProgramHasAWalkthrough()
    {
        AcademyJson.UseLibrary(null);
        foreach (string id in new[] { "acad-m1-a01", "acad-m1-a02", "acad-m1-a03", "acad-m1-a04", "acad-m1-a05" })
        {
            JsonNode lesson = JsonNode.Parse(AcademyJson.Dispatch("lesson", $"{{\"id\":\"{id}\"}}"))!;
            JsonArray lines = lesson["walk"]!["lines"]!.AsArray();
            Assert.True(lines.Count > 5);
            Assert.Contains(lines, l => l!["path"]!.AsArray().Count >= 2);
        }
    }
}

public class ExerciseTests
{
    private static ExerciseEditor Editor(params ExerciseCheck[] checks) => new ExerciseEditor("G21 G90 G17\nG00 X10.0 Y10.0", "", checks);

    [Fact]
    public void PositionCheckPassesAndFailsWithTheRealPosition()
    {
        ExerciseEditor e = Editor(new ExerciseCheck("posicao_final", "", 40, 10, null, "Dica."), new ExerciseCheck("sem_erros", "", null, null, null, ""));
        Assert.True(ExerciseChecker.Check(e, "G00 X40.0 Y10.0").Passed);
        CheckResult bad = ExerciseChecker.Check(e, "G00 X30.0 Y10.0");
        Assert.False(bad.Passed);
        Assert.Contains("X30", bad.Messages[0]);
        Assert.Contains("Dica.", bad.Messages[0]);
    }

    [Fact]
    public void ModalLineAndContainsCheckWork()
    {
        ExerciseEditor e = Editor(new ExerciseCheck("contem", "X30.0", null, null, null, ""), new ExerciseCheck("posicao_final", "", 30, 10, null, ""));
        Assert.True(ExerciseChecker.Check(e, "X30.0").Passed);
        Assert.False(ExerciseChecker.Check(e, "X30").Passed);                  // "X30" não é o texto X30.0
        Assert.False(ExerciseChecker.Check(e, "(X30.0)\nY10.0").Passed);      // comentário não conta
    }

    [Fact]
    public void ErrorCheckCatchesBrokenCode()
    {
        ExerciseEditor e = Editor(new ExerciseCheck("sem_erros", "", null, null, null, ""));
        Assert.False(ExerciseChecker.Check(e, "____").Passed);
        Assert.True(ExerciseChecker.Check(e, "G00 X20.0").Passed);
    }

    [Fact]
    public void Module1ExercisesAcceptTheirSolutionAndRejectTheStart()
    {
        AcademyLibrary lib = AcademyLibrary.FromEmbedded();
        int tested = 0;
        foreach (Lesson l in lib.Module("acad-m1")!.Lessons)
        {
            ExerciseEditor? e = l.Exercise?.Editor;
            if (e is null) continue;
            tested++;
            Assert.True(ExerciseChecker.Check(e, e.Solution).Passed, l.Id);
            Assert.False(ExerciseChecker.Check(e, e.Initial).Passed, l.Id + " (o início não pode já estar certo)");
        }

        Assert.True(tested >= 4);
    }

    [Fact]
    public void ValidatorRejectsUnknownDiagramBadVideoAndImpossibleExercise()
    {
        var lesson = AcademyReader.ReadLesson("""
        {"id":"a","modulo":"m","titulo":"t","nivel":"Introdução","caminho":"Comum","status":"rascunho","objetivo":"o","texto":"x","erro_comum":"e",
         "programa":"p.nc","aviso":"Simule antes de rodar na máquina","diagramas":["foguete"],"video":{"titulo":"v","url":"http://exemplo.com/v"},
         "exercicio":{"tipo":"t","enunciado":"e","gabarito":"g","editor":{"prefixo":"G21","inicial":"","solucao":"G00 X1.0","checagens":[{"tipo":"posicao_final","x":99}]}},"quiz":[]}
        """, out _)!;
        var issues = AcademyValidator.Validate([lesson], null, new Dictionary<string, string>());
        Assert.Contains(issues, i => i.Message.Contains("foguete"));
        Assert.Contains(issues, i => i.Message.Contains("YouTube"));
        Assert.Contains(issues, i => i.Message.Contains("não passa nas conferências"));
    }

    [Fact]
    public void DispatchCheckUsesTheLessonExercise()
    {
        AcademyJson.UseLibrary(null);
        Assert.True((bool)JsonNode.Parse(AcademyJson.Dispatch("check", "{\"id\":\"acad-m1-a02\",\"text\":\"G00 X40.0 Y10.0\"}"))!["passed"]!);
        Assert.False((bool)JsonNode.Parse(AcademyJson.Dispatch("check", "{\"id\":\"acad-m1-a02\",\"text\":\"G00 X30.0 Y10.0\"}"))!["passed"]!);
        Assert.False((bool)JsonNode.Parse(AcademyJson.Dispatch("check", "{\"id\":\"acad-m1-a04\",\"text\":\"x\"}"))!["ok"]!);
        JsonNode lesson = JsonNode.Parse(AcademyJson.Dispatch("lesson", "{\"id\":\"acad-m1-a03\"}"))!;
        Assert.Equal("zeros", (string)lesson["diagrams"]![0]!);
        Assert.Contains("G21", (string)lesson["exercise"]!["editor"]!["prefix"]!);
    }
}


public class AcademyExtrasTests
{
    [Fact]
    public void StreakCountsConsecutiveDaysAndForgivesToday()
    {
        AcademyProgress p = new();
        foreach (string d in new[] { "2026-10-20", "2026-10-22", "2026-10-23", "2026-10-24", "lixo" })
        {
            p.RecordStudyDay(d);
        }

        Assert.Equal(3, p.Streak("2026-10-24"));
        Assert.Equal(3, p.Streak("2026-10-25"));     // hoje ainda não estudou: conta até ontem
        Assert.Equal(0, p.Streak("2026-10-27"));
        Assert.Equal(0, p.Streak("hoje"));
        Assert.Equal(4, AcademyProgress.FromJson(p.ToJson()).StudyDays.Count);
    }

    [Fact]
    public void ModuleBadgeNeedsAllLessonsAndTheFinalTest()
    {
        AcademyLibrary lib = AcademyLibrary.FromEmbedded();
        AcademyModule m = lib.Module("acad-m1")!;
        AcademyProgress p = new();
        JsonNode Module(AcademyProgress pr) => JsonNode.Parse(AcademyJson.Catalog(pr.ToJson(), "2026-10-24"))!["tracks"]![0]!["levels"]![0]!["module"]!;
        Assert.False((bool)Module(p)["badge"]!);
        foreach (Lesson l in m.Lessons) p.MarkDone(l.Id);
        Assert.False((bool)Module(p)["badge"]!);
        p.RecordScore(m.Final!.Id, 100);
        Assert.True((bool)Module(p)["badge"]!);
    }

    [Fact]
    public void GradeRecordsTheStudyDay()
    {
        AcademyJson.UseLibrary(null);
        JsonNode lesson = JsonNode.Parse(AcademyJson.Dispatch("lesson", "{\"id\":\"acad-m1-a01\"}"))!;
        Assert.False(string.IsNullOrEmpty((string)lesson["opening"]!));
        Assert.Equal(3, lesson["summary"]!.AsArray().Count);
        JsonArray quiz = lesson["quiz"]!.AsArray();
        string ids = new JsonArray(quiz.Select(q => (JsonNode)(string)q!["id"]!).ToArray()).ToJsonString();
        string right = new JsonArray(quiz.Select(q => (JsonNode)(int)q!["correct"]!).ToArray()).ToJsonString();
        string args = $"{{\"quizId\":\"acad-m1-a01-quiz\",\"ids\":{ids},\"answers\":{right},\"progress\":\"\",\"today\":\"2026-10-24\"}}";
        JsonNode r = JsonNode.Parse(AcademyJson.Dispatch("grade", args))!;
        Assert.Equal(1, AcademyProgress.FromJson((string)r["progress"]!).Streak("2026-10-24"));
    }

    [Fact]
    public void ValidatorRequiresSummaryAndKnownParagraphTags()
    {
        var lesson = AcademyReader.ReadLesson("""
        {"id":"a","modulo":"m","titulo":"t","nivel":"Introdução","caminho":"Comum","status":"rascunho","objetivo":"o",
         "texto":"[atenção] ok\n\n[perigo] marca inventada","erro_comum":"e","programa":"p.nc","aviso":"Simule antes de rodar na máquina","resumo":["só um"],"quiz":[]}
        """, out _)!;
        var issues = AcademyValidator.Validate([lesson], null, new Dictionary<string, string>());
        Assert.Contains(issues, i => i.Message.Contains("resumo"));
        Assert.Contains(issues, i => i.Message.Contains("[perigo]"));
        Assert.DoesNotContain(issues, i => i.Message.Contains("\"[atenção]\" desconhecida"));
    }
}
