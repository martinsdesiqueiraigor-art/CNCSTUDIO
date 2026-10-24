namespace CncStudio.Core.Academy;

/// <summary>Uma pergunta de múltipla escolha (4 alternativas, A–D).</summary>
public sealed record QuizQuestion(
    string Id, string Prompt, IReadOnlyList<string> Options, int CorrectIndex, string Explanation, string Hint, string Kind = "");

/// <summary>Uma conferência do exercício no editor: "posicao_final" (x, y, z opcionais), "contem" (texto no código) ou "sem_erros".</summary>
public sealed record ExerciseCheck(string Kind, string Text, double? X, double? Y, double? Z, string Hint);

/// <summary>Exercício feito no editor da aula: linhas fixas antes (prefixo), texto inicial do aluno e as conferências.</summary>
public sealed record ExerciseEditor(string Prefix, string Initial, IReadOnlyList<ExerciseCheck> Checks, string Solution = "");

/// <summary>Exercício de uma aula (corrigir ou completar um programa) com o gabarito e, se houver, a versão conferida no editor.</summary>
public sealed record LessonExercise(string Kind, string Prompt, string Answer, ExerciseEditor? Editor = null);

/// <summary>Vídeo opcional da aula (link para o YouTube).</summary>
public sealed record LessonVideo(string Title, string Url);

/// <summary>Uma aula: texto curto, programa de exemplo, exercício e quiz.</summary>
public sealed record Lesson(
    string Id, string ModuleId, string Title, string Level, string Track, string Status, string Goal, string Prerequisite,
    string Text, string CommonMistake, string ProgramPath, string Warning, LessonExercise? Exercise,
    IReadOnlyList<QuizQuestion> Quiz, string Notes, IReadOnlyList<string>? Diagrams = null, LessonVideo? Video = null);

/// <summary>Teste final do módulo.</summary>
public sealed record FinalTest(string Id, string ModuleId, string Status, int PassPercent, bool CanSkip, IReadOnlyList<QuizQuestion> Questions);

/// <summary>Um módulo (lista de aulas e teste final) de um caminho e nível.</summary>
public sealed record AcademyModule(string Id, string Title, string Track, string Level, IReadOnlyList<Lesson> Lessons, FinalTest? Final);

/// <summary>Problema achado ao conferir o conteúdo; <see cref="IsError"/> falso = aviso.</summary>
public sealed record ContentIssue(string Where, string Message, bool IsError = true);
