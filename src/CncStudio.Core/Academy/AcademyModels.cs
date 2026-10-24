namespace CncStudio.Core.Academy;

/// <summary>Uma pergunta de múltipla escolha (4 alternativas, A–D).</summary>
public sealed record QuizQuestion(
    string Id, string Prompt, IReadOnlyList<string> Options, int CorrectIndex, string Explanation, string Hint, string Kind = "");

/// <summary>Exercício de uma aula (corrigir ou completar um programa) com o gabarito.</summary>
public sealed record LessonExercise(string Kind, string Prompt, string Answer);

/// <summary>Uma aula: texto curto, programa de exemplo, exercício e quiz.</summary>
public sealed record Lesson(
    string Id, string ModuleId, string Title, string Level, string Track, string Status, string Goal, string Prerequisite,
    string Text, string CommonMistake, string ProgramPath, string Warning, LessonExercise? Exercise,
    IReadOnlyList<QuizQuestion> Quiz, string Notes);

/// <summary>Teste final do módulo.</summary>
public sealed record FinalTest(string Id, string ModuleId, string Status, int PassPercent, bool CanSkip, IReadOnlyList<QuizQuestion> Questions);

/// <summary>Um módulo (lista de aulas e teste final) de um caminho e nível.</summary>
public sealed record AcademyModule(string Id, string Title, string Track, string Level, IReadOnlyList<Lesson> Lessons, FinalTest? Final);

/// <summary>Problema achado ao conferir o conteúdo; <see cref="IsError"/> falso = aviso.</summary>
public sealed record ContentIssue(string Where, string Message, bool IsError = true);
