namespace CncStudio.Core.Trajectory;

/// <summary>
/// Divide a trajetória em passes para navegar no backplot.
///
/// Um passe é: os rápidos de aproximação + os movimentos de corte seguintes. Ou seja, um passe
/// novo começa em cada G00 que vem logo depois de um corte. No desbaste de torno, cada nível
/// vira um passe; na furação, cada furo; num contorno de fresa sem rápidos, um passe só.
/// </summary>
public static class PathPasses
{
    /// <summary>Índice do primeiro movimento de cada passe (vazio se não há movimentos).</summary>
    public static int[] Starts(IReadOnlyList<Move> moves)
    {
        var starts = new List<int>();
        for (int i = 0; i < moves.Count; i++)
        {
            if (i == 0 || (moves[i].IsRapid && !moves[i - 1].IsRapid))
            {
                starts.Add(i);
            }
        }

        return starts.ToArray();
    }

    /// <summary>Passe (base 0) que contém o movimento; -1 se não há passes.</summary>
    public static int PassOf(int[] starts, int moveIndex)
    {
        if (starts.Length == 0)
        {
            return -1;
        }

        int found = Array.BinarySearch(starts, moveIndex);
        return found >= 0 ? found : Math.Max(0, ~found - 1);
    }

    /// <summary>Fim (exclusivo) do passe.</summary>
    public static int EndOf(int[] starts, int pass, int moveCount) =>
        pass + 1 < starts.Length ? starts[pass + 1] : moveCount;
}
