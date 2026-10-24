using CncStudio.Core.Dialects;
using CncStudio.Core.Parsing;

namespace CncStudio.Core.Academy;

/// <summary>Confere se o conteúdo entregue segue a ficha ACAD-01 (formato e qualidade mínima dos quizzes).</summary>
public static class AcademyValidator
{
    public const int QuestionsPerLesson = 4;
    public const int MinFinalQuestions = 12;
    private static readonly string[] AllowedStatus = ["rascunho", "revisado"];
    private static readonly string[] Levels = ["Introdução", "Iniciante", "Básico", "Intermediário", "Avançado", "Especialista"];
    private static readonly string[] Diagrams = ["eixos", "ponto-xy", "zeros"];
    private static readonly string[] CheckKinds = ["posicao_final", "contem", "sem_erros"];
    private static readonly string[] Tracks = ["Comum", "Fresa", "Torno"];

    /// <param name="programs">Texto dos programas de exemplo, pelo caminho citado na aula.</param>
    public static List<ContentIssue> Validate(IReadOnlyList<Lesson> lessons, FinalTest? final, IReadOnlyDictionary<string, string> programs)
    {
        List<ContentIssue> issues = [];
        HashSet<string> ids = [];

        foreach (Lesson lesson in lessons)
        {
            string where = lesson.Id.Length > 0 ? lesson.Id : "(aula sem id)";
            Require(issues, where, lesson.Id, "id");
            if (lesson.Id.Length > 0 && !ids.Add(lesson.Id))
            {
                issues.Add(new ContentIssue(where, "Id repetido."));
            }

            Require(issues, where, lesson.Title, "titulo");
            Require(issues, where, lesson.Goal, "objetivo");
            Require(issues, where, lesson.Text, "texto");
            Require(issues, where, lesson.CommonMistake, "erro_comum");
            if (!AllowedStatus.Contains(lesson.Status))
            {
                issues.Add(new ContentIssue(where, "status precisa ser \"rascunho\" ou \"revisado\"."));
            }

            if (!Levels.Contains(lesson.Level))
            {
                issues.Add(new ContentIssue(where, $"nivel \"{lesson.Level}\" desconhecido."));
            }

            if (!Tracks.Contains(lesson.Track))
            {
                issues.Add(new ContentIssue(where, $"caminho \"{lesson.Track}\" desconhecido."));
            }

            if (!lesson.Warning.Contains("Simule antes de rodar na máquina", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ContentIssue(where, "Falta o aviso \"Simule antes de rodar na máquina\"."));
            }

            int words = lesson.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            if (words is < 250 or > 600)
            {
                issues.Add(new ContentIssue(where, $"Texto com {words} palavras (esperado 300–500).", IsError: false));
            }

            if (lesson.Exercise is null || lesson.Exercise.Prompt.Length == 0 || lesson.Exercise.Answer.Length == 0)
            {
                issues.Add(new ContentIssue(where, "Exercício com enunciado e gabarito é obrigatório."));
            }

            CheckProgram(issues, where, lesson, programs);
            CheckExtras(issues, where, lesson);

            if (lesson.Quiz.Count != QuestionsPerLesson)
            {
                issues.Add(new ContentIssue(where, $"O quiz precisa de {QuestionsPerLesson} perguntas (tem {lesson.Quiz.Count})."));
            }

            CheckQuestions(issues, where, lesson.Quiz, ids);
        }

        if (final is null)
        {
            issues.Add(new ContentIssue("teste final", "O módulo precisa de um teste final."));
        }
        else
        {
            const string where = "teste final";
            if (!AllowedStatus.Contains(final.Status))
            {
                issues.Add(new ContentIssue(where, "status precisa ser \"rascunho\" ou \"revisado\"."));
            }

            if (final.Questions.Count < MinFinalQuestions)
            {
                issues.Add(new ContentIssue(where, $"O teste final precisa de pelo menos {MinFinalQuestions} perguntas (tem {final.Questions.Count})."));
            }

            CheckQuestions(issues, where, final.Questions, ids);
            CheckBalance(issues, where, final.Questions);
            if (final.Questions.Count(q => q.Kind.Contains("leitura", StringComparison.OrdinalIgnoreCase)) < 2)
            {
                issues.Add(new ContentIssue(where, "Faltam 2 perguntas de leitura de programa (tipo \"leitura_programa\")."));
            }
        }

        return issues;
    }

    private static void CheckExtras(List<ContentIssue> issues, string where, Lesson lesson)
    {
        foreach (string d in lesson.Diagrams ?? [])
        {
            if (!Diagrams.Contains(d))
            {
                issues.Add(new ContentIssue(where, $"Diagrama \"{d}\" desconhecido (use: {string.Join(", ", Diagrams)})."));
            }
        }

        if (lesson.Video is { } v && !(v.Url.StartsWith("https://www.youtube.com/", StringComparison.Ordinal) || v.Url.StartsWith("https://youtu.be/", StringComparison.Ordinal)))
        {
            issues.Add(new ContentIssue(where, "O link do vídeo precisa ser do YouTube (https://www.youtube.com/… ou https://youtu.be/…)."));
        }

        ExerciseEditor? editor = lesson.Exercise?.Editor;
        if (editor is null)
        {
            return;
        }

        if (editor.Checks.Count == 0)
        {
            issues.Add(new ContentIssue(where, "Exercício no editor sem conferências."));
        }

        foreach (ExerciseCheck c in editor.Checks)
        {
            if (!CheckKinds.Contains(c.Kind))
            {
                issues.Add(new ContentIssue(where, $"Conferência \"{c.Kind}\" desconhecida."));
            }
            else if (c.Kind == "posicao_final" && c.X is null && c.Y is null && c.Z is null)
            {
                issues.Add(new ContentIssue(where, "\"posicao_final\" precisa de x, y ou z."));
            }
            else if (c.Kind == "contem" && c.Text.Length == 0)
            {
                issues.Add(new ContentIssue(where, "\"contem\" precisa do campo \"texto\"."));
            }
        }

        // o gabarito do exercício precisa passar nas próprias conferências (evita exercício impossível)
        if (lesson.Exercise is { Answer.Length: > 0 } && editor.Checks.Count > 0 && editor.Solution.Length > 0)
        {
            CheckResult r = ExerciseChecker.Check(editor, editor.Solution);
            if (!r.Passed)
            {
                issues.Add(new ContentIssue(where, "A solução do exercício não passa nas conferências: " + string.Join(" ", r.Messages)));
            }
        }
    }

    private static void Require(List<ContentIssue> issues, string where, string value, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new ContentIssue(where, $"Campo \"{field}\" vazio."));
        }
    }

    private static void CheckProgram(List<ContentIssue> issues, string where, Lesson lesson, IReadOnlyDictionary<string, string> programs)
    {
        if (lesson.ProgramPath.Length == 0 || !programs.TryGetValue(lesson.ProgramPath, out string? text))
        {
            issues.Add(new ContentIssue(where, $"Programa de exemplo não encontrado: {lesson.ProgramPath}."));
            return;
        }

        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(text, Dialect.Fanuc);
        foreach (ParseDiagnostic d in analysis.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error))
        {
            issues.Add(new ContentIssue(where, $"Programa {lesson.ProgramPath}: {d.Message}"));
        }

        if (!text.Contains("SIMULE ANTES DE RODAR NA MAQUINA", StringComparison.OrdinalIgnoreCase))
        {
            issues.Add(new ContentIssue(where, $"Programa {lesson.ProgramPath} sem o comentário \"SIMULE ANTES DE RODAR NA MAQUINA\".", IsError: false));
        }
    }

    private static void CheckQuestions(List<ContentIssue> issues, string where, IReadOnlyList<QuizQuestion> questions, HashSet<string> ids)
    {
        foreach (QuizQuestion q in questions)
        {
            string at = $"{where} / {(q.Id.Length > 0 ? q.Id : "(pergunta sem id)")}";
            if (q.Id.Length == 0 || !ids.Add(q.Id))
            {
                issues.Add(new ContentIssue(at, "Id da pergunta ausente ou repetido."));
            }

            Require(issues, at, q.Prompt, "enunciado");
            if (q.Options.Count != 4 || q.Options.Any(string.IsNullOrWhiteSpace))
            {
                issues.Add(new ContentIssue(at, "A pergunta precisa de exatamente 4 alternativas preenchidas."));
            }
            else if (q.Options.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 4)
            {
                issues.Add(new ContentIssue(at, "Alternativas repetidas."));
            }

            if (q.CorrectIndex is < 0 or > 3)
            {
                issues.Add(new ContentIssue(at, "\"correta\" precisa ser A, B, C ou D."));
            }

            if (q.Explanation.Length < 20)
            {
                issues.Add(new ContentIssue(at, "Explicação ausente ou curta demais."));
            }
            else if (q.Explanation.Contains("não atende ao enunciado", StringComparison.OrdinalIgnoreCase))
            {
                issues.Add(new ContentIssue(at, "Explicação genérica: diga por que cada alternativa errada está errada."));
            }
        }

        // dica igual em todas as perguntas não ajuda ninguém
        var hints = questions.Where(q => q.Hint.Length > 0).Select(q => q.Hint).ToList();
        if (questions.Count > 1 && hints.Count > 1 && hints.Distinct().Count() == 1)
        {
            issues.Add(new ContentIssue(where, "Todas as dicas são iguais; escreva uma dica por pergunta."));
        }
    }

    private static void CheckBalance(List<ContentIssue> issues, string where, IReadOnlyList<QuizQuestion> questions)
    {
        if (questions.Count < 8)
        {
            return;
        }

        int max = Enumerable.Range(0, 4).Max(i => questions.Count(q => q.CorrectIndex == i));
        if (max > Math.Ceiling(questions.Count * 0.4))
        {
            issues.Add(new ContentIssue(where, $"Respostas corretas desbalanceadas ({max} na mesma letra de {questions.Count}); distribua entre A–D."));
        }
    }
}
