using CncStudio.Core.Dialects;

namespace CncStudio.Core.Parsing;

/// <summary>Resultado do parse de um programa inteiro.</summary>
public sealed record ParseResult(
    Dialect Dialect,
    IReadOnlyList<Block> Blocks,
    IReadOnlyList<ParseDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}
