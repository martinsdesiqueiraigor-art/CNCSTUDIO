using CncStudio.Core.Dialects;

namespace CncStudio.Core.Parsing;

/// <summary>Resultado do parse (só sintaxe) de um programa inteiro.</summary>
public sealed record ParseResult(
    Dialect Dialect,
    IReadOnlyList<NcBlock> Blocks,
    IReadOnlyList<ParseDiagnostic> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error);
}
