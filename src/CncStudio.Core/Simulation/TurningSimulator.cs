using CncStudio.Core.Geometry;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Simulation;

/// <summary>
/// Percorre a trajetória do torno removendo material da <see cref="TurningStock"/>.
/// Mesmo funcionamento da simulação da fresa (por distância, por linha ou até um movimento).
/// </summary>
public sealed class TurningSimulator : IMaterialSimulation
{
    private const double NoseRadius = 0.4;

    private readonly TurningStock _stock;
    private readonly IReadOnlyList<Move> _moves;
    private readonly Func<int, double> _diameterOf;
    private readonly List<Point3> _polyline = [];
    private readonly List<int> _rapidCollisions = [];
    private int _moveIndex;
    private int _pieceIndex;
    private double _distanceInPiece;

    public TurningSimulator(TurningStock stock, IReadOnlyList<Move> moves, Func<int, double> diameterOfTool)
    {
        _stock = stock;
        _moves = moves;
        _diameterOf = diameterOfTool;
        Reset();
    }

    public TurningStock Stock => _stock;
    public int CurrentMoveIndex => _moveIndex;
    public bool IsFinished => _moveIndex >= _moves.Count;
    public Point3 ToolPosition { get; private set; }
    public Move? CurrentMove => _moves.Count == 0 ? null : _moves[Math.Min(_moveIndex, _moves.Count - 1)];
    public int CurrentTool => CurrentMove?.Tool ?? 0;
    public double CurrentToolDiameter => _diameterOf(CurrentTool);
    public int CurrentLineNumber => CurrentMove?.LineNumber ?? 0;
    public double Travelled { get; private set; }
    public IReadOnlyList<int> RapidCollisions => _rapidCollisions;
    public int Version => _stock.Version;

    public void Reset()
    {
        _stock.Reset();
        _moveIndex = 0;
        Travelled = 0;
        _rapidCollisions.Clear();
        ToolPosition = _moves.Count > 0 ? _moves[0].Start : Point3.Origin;
        Load();
    }

    public void Advance(double distance)
    {
        while (distance > 0 && !IsFinished)
        {
            distance -= Step(distance);
        }
    }

    public void AdvanceLine()
    {
        if (IsFinished)
        {
            return;
        }

        int line = _moves[_moveIndex].LineNumber;
        while (!IsFinished && _moves[_moveIndex].LineNumber == line)
        {
            Step(double.PositiveInfinity);
        }
    }

    public void RunToMove(int moveIndex)
    {
        moveIndex = Math.Clamp(moveIndex, 0, _moves.Count);
        if (moveIndex < _moveIndex || (moveIndex == _moveIndex && (_pieceIndex > 0 || _distanceInPiece > 0)))
        {
            Reset();
        }

        while (_moveIndex < moveIndex)
        {
            Step(double.PositiveInfinity);
        }
    }

    public void RunToEnd()
    {
        while (!IsFinished)
        {
            Step(double.PositiveInfinity);
        }
    }

    private double Step(double maxDistance)
    {
        Point3 a = _polyline[_pieceIndex];
        Point3 b = _polyline[_pieceIndex + 1];
        double length = a.DistanceTo(b);
        double step = Math.Min(maxDistance, length - _distanceInPiece);
        _distanceInPiece += step;
        Travelled += step;
        bool done = _distanceInPiece >= length - 1e-9;
        Point3 to = done ? b : Point3.Lerp(a, b, _distanceInPiece / length);

        bool removed = CutAlong(ToolPosition, to);
        ToolPosition = to;
        if (removed && _moveIndex > 0 && _moves[_moveIndex].IsRapid && (_rapidCollisions.Count == 0 || _rapidCollisions[^1] != _moveIndex))
        {
            _rapidCollisions.Add(_moveIndex);
        }

        if (done)
        {
            _pieceIndex++;
            _distanceInPiece = 0;
            if (_pieceIndex >= _polyline.Count - 1)
            {
                _moveIndex++;
                Load();
            }
        }

        return step;
    }

    private void Load()
    {
        _polyline.Clear();
        _pieceIndex = 0;
        _distanceInPiece = 0;
        if (IsFinished)
        {
            return;
        }

        Move move = _moves[_moveIndex];
        _polyline.Add(move.Start);
        move.AppendPolyline(_polyline, Math.Max(0.002, _stock.SliceLength * 0.25));
    }

    /// <summary>Corta em pontos espaçados de meia fatia ao longo do trecho (em Z e em X).</summary>
    private bool CutAlong(Point3 a, Point3 b)
    {
        double spacing = _stock.SliceLength * 0.5;
        double span = Math.Max(Math.Abs(b.Z - a.Z), Math.Abs(b.X - a.X));
        int count = Math.Max(1, (int)Math.Ceiling(span / spacing));
        double drillRadius = Math.Max(0.5, _diameterOf(_moves[_moveIndex].Tool) / 2);
        bool removed = false;
        for (int k = 0; k <= count; k++)
        {
            Point3 p = Point3.Lerp(a, b, (double)k / count);
            removed |= _stock.Cut(p.Z, p.X, NoseRadius, drillRadius);
        }

        return removed;
    }
}
