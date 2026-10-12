namespace CncStudio.Core.Execution;

/// <summary>Mensagem do programa (Siemens MSG) e a partir de qual movimento ela vale.</summary>
/// <param name="MoveIndex">Índice do primeiro movimento depois da mensagem.</param>
/// <param name="LineNumber">Linha do MSG.</param>
/// <param name="Text">Texto já montado (vazio = apaga a mensagem).</param>
public sealed record ProgramMessage(int MoveIndex, int LineNumber, string Text);
