using System.Diagnostics;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core;

/// <summary>Resultado da análise completa de um programa (o que a interface precisa).</summary>
/// <param name="Dialect">Dialeto usado.</param>
/// <param name="ToolPath">Trajetória da ferramenta.</param>
/// <param name="Diagnostics">Avisos e erros do parser e da trajetória, em ordem de linha.</param>
/// <param name="BlockCount">Quantidade de blocos executáveis.</param>
/// <param name="Elapsed">Tempo gasto na análise.</param>
public sealed record ProgramAnalysis(
    Dialect Dialect,
    ToolPath ToolPath,
    IReadOnlyList<ParseDiagnostic> Diagnostics,
    int BlockCount,
    TimeSpan Elapsed)
{
    public int ErrorCount => Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Error);
    public int WarningCount => Diagnostics.Count(d => d.Severity == DiagnosticSeverity.Warning);
}

/// <summary>
/// Faz parse + trajetória de uma vez. Pensado para rodar fora da thread de UI.
/// Os blocos do parser são descartados no fim: só a trajetória fica na memória.
/// </summary>
public static class ProgramAnalyzer
{
    public static ProgramAnalysis Analyze(string text, Dialect dialect, CancellationToken cancellationToken = default) =>
        Analyze(text, dialect, MachineSettings.Default, cancellationToken);

    public static ProgramAnalysis Analyze(string text, Dialect dialect, MachineSettings settings, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        ParseResult parse = GCodeParser.Parse(text, dialect);
        cancellationToken.ThrowIfCancellationRequested();

        ToolPath toolPath = MachineExecutor.Execute(parse, settings);
        cancellationToken.ThrowIfCancellationRequested();

        List<ParseDiagnostic> diagnostics = parse.Diagnostics
            .Concat(toolPath.Diagnostics)
            .OrderBy(d => d.LineNumber)
            .ThenBy(d => d.Column)
            .ToList();

        return new ProgramAnalysis(dialect, toolPath, diagnostics, parse.Blocks.Count, stopwatch.Elapsed);
    }
}
