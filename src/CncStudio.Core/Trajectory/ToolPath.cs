using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;

namespace CncStudio.Core.Trajectory;

/// <summary>Trajetória completa da ferramenta, pronta para backplot e simulação.</summary>
public sealed class ToolPath
{
    public ToolPath(IReadOnlyList<Segment> segments, IReadOnlyList<ParseDiagnostic> diagnostics)
    {
        Segments = segments;
        Diagnostics = diagnostics;

        var all = Bounds3.Empty;
        var cutting = Bounds3.Empty;
        foreach (Segment s in segments)
        {
            all = all.Include(s.Start).Include(s.End);
            if (!s.IsRapid)
            {
                cutting = cutting.Include(s.Start).Include(s.End);
            }
        }

        Bounds = all;
        CuttingBounds = cutting;
    }

    public IReadOnlyList<Segment> Segments { get; }

    /// <summary>Avisos gerados ao montar a trajetória (ex.: raio de arco inconsistente).</summary>
    public IReadOnlyList<ParseDiagnostic> Diagnostics { get; }

    /// <summary>Envelope de todos os movimentos, inclusive rápidos.</summary>
    public Bounds3 Bounds { get; }

    /// <summary>Envelope só dos movimentos de corte (G01/G02/G03).</summary>
    public Bounds3 CuttingBounds { get; }

    /// <summary>
    /// Retorna o intervalo de segmentos gerados por uma linha do arquivo.
    /// Usa busca binária, pois os segmentos estão em ordem crescente de linha.
    /// Count = 0 se a linha não gerou movimento.
    /// </summary>
    public (int Start, int Count) GetSegmentRange(int lineNumber)
    {
        int first = LowerBound(lineNumber);
        int last = LowerBound(lineNumber + 1);
        return (first, last - first);
    }

    /// <summary>Primeiro índice cujo LineNumber é maior ou igual ao informado.</summary>
    private int LowerBound(int lineNumber)
    {
        int low = 0;
        int high = Segments.Count;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (Segments[mid].LineNumber < lineNumber)
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
