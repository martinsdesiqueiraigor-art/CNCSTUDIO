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
        var tools = new SortedSet<int>();
        foreach (Move move in moves)
        {
            all = move.IncludeIn(all);
            if (!move.IsRapid)
            {
                cutting = move.IncludeIn(cutting);
            }

            if (move.Tool != 0)
            {
                tools.Add(move.Tool);
            }
        }

        Bounds = all;
        CuttingBounds = cutting;
        Tools = tools.ToArray();
    }

    /// <summary>Movimentos em ordem de execução.</summary>
    public IReadOnlyList<Move> Moves { get; }

    /// <summary>Avisos e erros de execução (comandos não suportados, raio de arco inconsistente...).</summary>
    public IReadOnlyList<ParseDiagnostic> Diagnostics { get; }

    /// <summary>Envelope de todos os movimentos, inclusive rápidos.</summary>
    public Bounds3 Bounds { get; }

    /// <summary>Envelope só dos movimentos de corte (G01/G02/G03).</summary>
    public Bounds3 CuttingBounds { get; }

    /// <summary>Ferramentas usadas, em ordem crescente.</summary>
    public IReadOnlyList<int> Tools { get; }

    /// <summary>
    /// Índices dos movimentos gerados por uma linha do arquivo (vazio se a linha não gerou movimento).
    /// Uma linha pode gerar vários movimentos (ciclo de furação, repetições).
    /// Hoje os movimentos de uma linha são contíguos e em ordem; a busca é binária.
    /// </summary>
    public IReadOnlyList<int> MovesForLine(int lineNumber)
    {
        int first = LowerBound(lineNumber);
        int last = LowerBound(lineNumber + 1);
        if (first >= last)
        {
            return [];
        }

        var indices = new int[last - first];
        for (int k = 0; k < indices.Length; k++)
        {
            indices[k] = first + k;
        }

        return indices;
    }

    /// <summary>Primeiro índice cujo LineNumber é maior ou igual ao informado.</summary>
    private int LowerBound(int lineNumber)
    {
        int low = 0;
        int high = Moves.Count;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (Moves[mid].LineNumber < lineNumber)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }
}
