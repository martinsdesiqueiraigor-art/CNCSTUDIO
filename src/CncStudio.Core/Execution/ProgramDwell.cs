namespace CncStudio.Core.Execution;

/// <summary>
/// Tempo de espera do programa (G04, fundo do furo no G82/CYCLE82/CYCLE83).
/// Não gera movimento, mas entra no tempo estimado.
/// </summary>
/// <param name="MoveIndex">Índice do primeiro movimento depois da espera.</param>
/// <param name="LineNumber">Linha que pediu a espera.</param>
/// <param name="Seconds">Duração em segundos.</param>
/// <param name="Tool">Ferramenta ativa durante a espera.</param>
public sealed record ProgramDwell(int MoveIndex, int LineNumber, double Seconds, int Tool);
