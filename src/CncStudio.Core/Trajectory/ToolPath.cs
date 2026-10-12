using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;

namespace CncStudio.Core.Trajectory;

/// <summary>Trajetória completa da ferramenta, pronta para backplot e simulação.</summary>
public sealed class ToolPath
{
    public ToolPath(IReadOnlyList<Move> moves, IReadOnlyList<ParseDiagnostic> diagnostics)
    {
        Moves = moves;
        Diagnostics = diagnostics;

        var all = Bounds3.Empty;
        var cutting = Bounds3.Empty;
        foreach (Move move in moves)
        {
            all = move.IncludeIn(all);
            if (!move.IsRapid)
            {
                cutting = move.IncludeIn(cutting);
            }
        }

        Bounds = all;
        CuttingBounds = cutting;
    }

    /// <summary>Movimentos em ordem de execução: no máximo um por linha do arquivo.</summary>
    public IReadOnlyList<Move> Moves { get; }

    /// <summary>Avisos gerados ao montar a trajetória (ex.: raio de arco inconsistente).</summary>
    public IReadOnlyList<ParseDiagnostic> Diagnostics { get; }

    /// <summary>Envelope de todos os movimentos, inclusive rápidos.</summary>
    public Bounds3 Bounds { get; }

    /// <summary>Envelope só dos movimentos de corte (G01/G02/G03).</summary>
    public Bounds3 CuttingBounds { get; }

    /// <summary>
    /// Índice do movimento gerado por uma linha do arquivo, ou -1 se a linha não gerou movimento.
    /// Usa busca binária, pois os movimentos estão em ordem crescente de linha.
    /// </summary>
    public int IndexOfLine(int lineNumber)
    {
        int low = 0;
        int high = Moves.Count - 1;
        while (low <= high)
        {
            int mid = (low + high) / 2;
            int midLine = Moves[mid].LineNumber;
            if (midLine == lineNumber)
            {
                return mid;
            }

            if (midLine < lineNumber)
            {
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return -1;
    }
}
