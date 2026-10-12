namespace CncStudio.Core.Parsing;

public enum DiagnosticSeverity
{
    Warning,
    Error,
}

/// <summary>Um aviso ou erro associado a uma posição do arquivo.</summary>
/// <param name="LineNumber">Linha (base 1).</param>
/// <param name="Column">Coluna inicial (base 0).</param>
/// <param name="Length">Tamanho do trecho destacado (0 = linha inteira).</param>
/// <param name="Severity">Gravidade.</param>
/// <param name="Message">Mensagem em português para o usuário.</param>
public sealed record ParseDiagnostic(int LineNumber, int Column, int Length, DiagnosticSeverity Severity, string Message);
