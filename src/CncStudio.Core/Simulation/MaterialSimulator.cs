using CncStudio.Core.Geometry;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Simulation;

/// <summary>
/// Percorre a trajetória removendo material do <see cref="HeightMap"/> com uma
/// fresa de topo reto. Permite avançar por distância (animação suave) ou por linha (passo a passo).
///
/// Não é thread-safe: a UI deve chamar sempre a partir da mesma thread de simulação.
/// </summary>
public sealed class MaterialSimulator
{
    private readonly HeightMap _map;
    private readonly IReadOnlyList<Segment> _segments;
    private readonly double _toolRadius;

    private int _segmentIndex;
    private double _distanceInSegment;

    public MaterialSimulator(HeightMap map, IReadOnlyList<Segment> segments, double toolDiameter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(toolDiameter, 0);
        _map = map;
        _segments = segments;
        _toolRadius = toolDiameter / 2;
        Reset();
    }

    public HeightMap Map => _map;

    /// <summary>Posição atual da ponta da ferramenta.</summary>
    public Point3 ToolPosition { get; private set; }

    /// <summary>Índice do segmento em execução (igual a Segments.Count quando terminou).</summary>
    public int CurrentSegmentIndex => _segmentIndex;

    public bool IsFinished => _segmentIndex >= _segments.Count;

    /// <summary>Linha do arquivo em execução (0 se não há segmentos).</summary>
    public int CurrentLineNumber =>
        _segments.Count == 0 ? 0 : _segments[Math.Min(_segmentIndex, _segments.Count - 1)].LineNumber;

    /// <summary>Volta ao início: bruto inteiro e ferramenta no primeiro ponto.</summary>
    public void Reset()
    {
        _map.Reset();
        _segmentIndex = 0;
        _distanceInSegment = 0;
        ToolPosition = _segments.Count > 0 ? _segments[0].Start : Point3.Origin;
    }

    /// <summary>Avança a ferramenta "distance" mm ao longo da trajetória, removendo material.</summary>
    public void Advance(double distance)
    {
        while (distance > 0 && !IsFinished)
        {
            Segment segment = _segments[_segmentIndex];
            double length = segment.Length;
            double remaining = length - _distanceInSegment;
            double step = Math.Min(distance, remaining);

            Point3 from = ToolPosition;
            _distanceInSegment += step;
            distance -= step;

            bool segmentDone = _distanceInSegment >= length - 1e-9;
            Point3 to = segmentDone ? segment.End : segment.PointAt(_distanceInSegment / length);

            CutAlong(from, to);
            ToolPosition = to;

            if (segmentDone)
            {
                _segmentIndex++;
                _distanceInSegment = 0;
            }
        }
    }

    /// <summary>Executa até o fim da linha atual (todos os segmentos dela). Usado no botão "passo".</summary>
    public void AdvanceToNextLine()
    {
        if (IsFinished)
        {
            return;
        }

        int line = _segments[_segmentIndex].LineNumber;
        while (!IsFinished && _segments[_segmentIndex].LineNumber == line)
        {
            double remaining = _segments[_segmentIndex].Length - _distanceInSegment;
            if (remaining > 0)
            {
                // Avança exatamente o que falta: o segmento termina sem invadir o próximo
                Advance(remaining);
            }
            else
            {
                _segmentIndex++;
                _distanceInSegment = 0;
            }
        }
    }

    /// <summary>Executa a trajetória inteira de uma vez.</summary>
    public void RunToEnd()
    {
        while (!IsFinished)
        {
            Advance(double.MaxValue);
        }
    }

    /// <summary>
    /// Remove material ao longo de um trecho reto: "carimba" o disco da ferramenta em
    /// pontos espaçados de meia célula, interpolando Z (cobre rampas e mergulhos).
    /// </summary>
    private void CutAlong(Point3 a, Point3 b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double lengthXY = Math.Sqrt(dx * dx + dy * dy);
        double spacing = _map.CellSize * 0.5;
        int count = Math.Max(1, (int)Math.Ceiling(lengthXY / spacing));

        for (int k = 0; k <= count; k++)
        {
            Point3 p = Point3.Lerp(a, b, (double)k / count);
            _map.CutDisc(p.X, p.Y, _toolRadius, p.Z);
        }
    }
}
