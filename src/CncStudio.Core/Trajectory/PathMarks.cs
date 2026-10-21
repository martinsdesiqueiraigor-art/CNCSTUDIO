namespace CncStudio.Core.Trajectory;

/// <summary>Troca de ferramenta: o movimento em que a ferramenta passa a valer e o número dela.</summary>
/// <param name="MoveIndex">Índice do primeiro movimento feito com a ferramenta nova.</param>
/// <param name="Tool">Número T da ferramenta nova.</param>
/// <param name="PreviousTool">Ferramenta anterior (0 na primeira).</param>
/// <param name="LineNumber">Linha do arquivo desse movimento.</param>
public readonly record struct ToolChange(int MoveIndex, int Tool, int PreviousTool, int LineNumber);

/// <summary>
/// Marcas para a barra de progresso: onde a ferramenta troca e quanto a trajetória mede.
/// Tudo calculado só a partir dos movimentos (sem depender da simulação).
/// </summary>
public static class PathMarks
{
    /// <summary>
    /// Trocas de ferramenta em ordem de execução. A primeira ferramenta também é uma marca
    /// (PreviousTool = 0). Movimentos sem ferramenta (T0) não geram marca nem apagam a anterior.
    /// </summary>
    public static IReadOnlyList<ToolChange> ToolChanges(IReadOnlyList<Move> moves)
    {
        var changes = new List<ToolChange>();
        int current = 0;
        for (int i = 0; i < moves.Count; i++)
        {
            int tool = moves[i].Tool;
            if (tool == 0 || tool == current)
            {
                continue;
            }

            changes.Add(new ToolChange(i, tool, current, moves[i].LineNumber));
            current = tool;
        }

        return changes;
    }

    /// <summary>Comprimento total da trajetória (mm), somando todos os movimentos, inclusive os rápidos.</summary>
    public static double TotalLength(IReadOnlyList<Move> moves)
    {
        double sum = 0;
        for (int i = 0; i < moves.Count; i++)
        {
            sum += moves[i].Length;
        }

        return sum;
    }

    /// <summary>
    /// Movimento da troca mais próxima antes ou em "moveIndex" (-1 se nenhuma): serve para o "tocar na marca".
    /// </summary>
    public static int ChangeAtOrBefore(IReadOnlyList<ToolChange> changes, int moveIndex)
    {
        int found = -1;
        for (int i = 0; i < changes.Count && changes[i].MoveIndex <= moveIndex; i++)
        {
            found = i;
        }

        return found;
    }

    /// <summary>
    /// Distância a mostrar ao lado do total: a simulação mede os arcos por cordas (um pouco abaixo do
    /// comprimento exato), então nunca passa do total e, ao terminar, mostra o total inteiro.
    /// </summary>
    public static double DisplayTravelled(double travelled, double total, bool finished) =>
        finished ? total : Math.Clamp(travelled, 0, total);

    /// <summary>Texto curto da distância: "523,4 mm" ou "12,35 m" a partir de 10 m.</summary>
    public static string FormatDistance(double millimetres, System.Globalization.CultureInfo culture) =>
        millimetres >= 10_000
            ? (millimetres / 1000).ToString("0.00", culture) + " m"
            : millimetres.ToString("0.0", culture) + " mm";
}
