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

/// <summary>
/// Uma aula: texto curto, programa de exemplo, exercício e quiz. <see cref="Dialect"/> ("fanuc"|"siemens") e <see cref="Machine"/>
/// ("fresa"|"torno") valem para ler e conferir os programas; no formato v1 são sempre fanuc/fresa. <see cref="Sections"/> só existe no v2.
/// </summary>
public sealed record Lesson(
    string Id, string ModuleId, string Title, string Level, string Track, string Status, string Goal, string Prerequisite,
    string Text, string CommonMistake, string ProgramPath, string Warning, LessonExercise? Exercise,
    IReadOnlyList<QuizQuestion> Quiz, string Notes, IReadOnlyList<string>? Diagrams = null, LessonVideo? Video = null,
    IReadOnlyList<string>? Summary = null, string Opening = "", string Dialect = "fanuc", string Machine = "fresa",
    int Version = 1, LessonSections? Sections = null, LessonStock? Stock = null);

/// <summary>Bruto opcional da aula no v2 (bloco: comprimento X, largura Y, altura Z; ou barra: diâmetro e comprimento).</summary>
public sealed record LessonStock(string Kind, double X, double Y, double Z, double Diameter, double Length);

/// <summary>Endereço explicado na aba Sintaxe ("X" → "posição no eixo X").</summary>
public sealed record SyntaxParam(string Address, string Concept);

/// <summary>
/// Seções da aula no JSON v2 (uma por aba). <see cref="Status"/>: estado de cada seção (visao, trajetoria, sintaxe, exemplo,
/// cuidados, praticar → "rascunho"|"revisado"; vazio = ausente).
/// </summary>
public sealed record LessonSections(
    IReadOnlyDictionary<string, string> Status, IReadOnlyList<string> Concepts, string Image, string TrajectoryPath,
    IReadOnlyList<string> SyntaxBlocks, IReadOnlyList<SyntaxParam> SyntaxParams, IReadOnlyList<string> ExampleSteps,
    IReadOnlyList<string> Errors, IReadOnlyList<string> Alerts)
{
    /// <summary>Nomes das seções do v2, na ordem das abas.</summary>
    public static readonly IReadOnlyList<string> Names = ["visao", "trajetoria", "sintaxe", "exemplo", "cuidados", "praticar"];
}

/// <summary>Teste final do módulo.</summary>
public sealed record FinalTest(string Id, string ModuleId, string Status, int PassPercent, bool CanSkip, IReadOnlyList<QuizQuestion> Questions);

/// <summary>Um módulo (lista de aulas e teste final) de um caminho e nível.</summary>
public sealed record AcademyModule(string Id, string Title, string Track, string Level, IReadOnlyList<Lesson> Lessons, FinalTest? Final);

/// <summary>Problema achado ao conferir o conteúdo; <see cref="IsError"/> falso = aviso.</summary>
public sealed record ContentIssue(string Where, string Message, bool IsError = true);
