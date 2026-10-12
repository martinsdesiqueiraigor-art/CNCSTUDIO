using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;

namespace CncStudio.Core.Trajectory;

/// <summary>
/// Trecho reto da trajetória da ferramenta. Arcos chegam aqui já interpolados
/// em vários segmentos, todos com o mesmo <see cref="Motion"/> e a mesma linha de origem.
/// </summary>
/// <param name="Start">Ponto inicial (mm).</param>
/// <param name="End">Ponto final (mm).</param>
/// <param name="Motion">Movimento que gerou o segmento (G00, G01, G02 ou G03).</param>
/// <param name="LineNumber">Linha do arquivo que gerou o segmento (base 1).</param>
/// <param name="Feed">Avanço em mm/min (sem uso em G00).</param>
public readonly record struct Segment(Point3 Start, Point3 End, MotionMode Motion, int LineNumber, double Feed)
{
    public bool IsRapid => Motion == MotionMode.Rapid;

    public double Length => Start.DistanceTo(End);

    /// <summary>Ponto ao longo do segmento: t = 0 no início, t = 1 no fim.</summary>
    public Point3 PointAt(double t) => Point3.Lerp(Start, End, t);
}
