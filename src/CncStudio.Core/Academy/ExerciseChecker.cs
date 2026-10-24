using System.Text.RegularExpressions;
using CncStudio.Core.Dialects;
using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;

namespace CncStudio.Core.Academy;

/// <summary>Resultado da conferência de um exercício: passou ou não, com uma mensagem por conferência que falhou.</summary>
public sealed record CheckResult(bool Passed, IReadOnlyList<string> Messages);

/// <summary>Confere o programa que o aluno digitou: junta o prefixo da aula, analisa com o Core e aplica as conferências.</summary>
public static partial class ExerciseChecker
{
    private const double Tolerance = 0.001;

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex CommentRegex();

    public static CheckResult Check(ExerciseEditor editor, string studentText)
    {
        string full = (editor.Prefix.Length > 0 ? editor.Prefix.TrimEnd() + "\n" : "") + studentText.Replace("\r\n", "\n");
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(full, Dialect.Fanuc);
        string code = CommentRegex().Replace(full, " ").ToUpperInvariant();
        List<string> failed = [];

        foreach (ExerciseCheck c in editor.Checks)
        {
            string? message = c.Kind switch
            {
                "sem_erros" => analysis.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error)
                    ? "O programa tem erro: confira a digitação das linhas." : null,
                "contem" => ContainsWord(code, c.Text.ToUpperInvariant()) ? null : $"Falta {c.Text} no programa.",
                "posicao_final" => PositionMessage(analysis, c),
                _ => null,
            };
            if (message is not null)
            {
                failed.Add(c.Hint.Length > 0 ? message + " " + c.Hint : message);
            }
        }

        return new CheckResult(failed.Count == 0, failed);
    }

    private static bool ContainsWord(string code, string word) =>
        Regex.IsMatch(code, @"(?<![A-Z0-9.])" + Regex.Escape(word) + @"(?![0-9.])");

    private static string? PositionMessage(ProgramAnalysis analysis, ExerciseCheck c)
    {
        var moves = analysis.ToolPath.Moves;
        if (moves.Count == 0)
        {
            return "O programa ainda não move a ferramenta.";
        }

        Point3 end = moves[^1].End;
        bool ok = (c.X is null || Math.Abs(end.X - c.X.Value) < Tolerance)
            && (c.Y is null || Math.Abs(end.Y - c.Y.Value) < Tolerance)
            && (c.Z is null || Math.Abs(end.Z - c.Z.Value) < Tolerance);
        if (ok)
        {
            return null;
        }

        string Show(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        return $"A ferramenta termina em X{Show(end.X)} Y{Show(end.Y)} Z{Show(end.Z)}, e não onde o exercício pede.";
    }
}
