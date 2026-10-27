using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Scene;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Simulation;

/// <summary>
/// Remoção de material em voxels para programas com eixo rotativo: a ferramenta anda em coordenadas
/// da máquina e cada ponto é levado para a peça parada (A/B desfeitos por <see cref="RotaryPartFrame"/>),
/// onde o <see cref="VoxelStock"/> é cortado com o eixo da ferramenta na direção certa.
/// Só movimentos de corte (G01/G02/G03) removem material; rápidos não (sem aviso de colisão).
/// Mesma interface e mesmo andar por distância/linha/movimento do <see cref="MaterialSimulator"/>.
/// </summary>
public sealed class VoxelSimulator : IMaterialSimulation
{
    /// <summary>Comprimento da parte da ferramenta que corta o material (mm).</summary>
    public const double CutLength = 25;

    private readonly VoxelStock _stock;
    private readonly ToolPath _path;
    private readonly IReadOnlyList<Move> _moves;
    private readonly Func<int, ToolProfile> _profileOf;
    private readonly RotaryPartFrame? _frame;
    private ToolProfile _profile = ToolProfile.Flat(10);
    private readonly List<Point3> _polyline = [];
    private readonly List<double> _cumulative = [];
    private int _moveIndex;
    private int _pieceIndex;
    private double _distanceInPiece;

    public VoxelSimulator(VoxelStock stock, ToolPath path, Func<int, ToolProfile> profileOf)
    {
        _stock = stock;
        _path = path;
        _moves = path.Moves;
        _profileOf = profileOf;
        _frame = path.PartFrame;
        Reset();
    }

    public VoxelStock Stock => _stock;

    private sealed record Checkpoint(ulong[] Material, double Travelled, Point3 ToolPosition);

    private const int MaxCheckpoints = 12;
    private readonly SortedList<int, Checkpoint> _checkpoints = [];

    private int CheckpointStride => Math.Max(1, (_moves.Count + MaxCheckpoints - 1) / MaxCheckpoints);

    private void TakeCheckpoint()
    {
        if (_moveIndex > 0 && _moveIndex % CheckpointStride == 0 && !_checkpoints.ContainsKey(_moveIndex))
        {
            _checkpoints[_moveIndex] = new Checkpoint(_stock.TakeSnapshot(), Travelled, ToolPosition);
        }
    }

    private void RewindFor(int moveIndex)
    {
        Checkpoint? best = null;
        int bestIndex = 0;
        foreach ((int index, Checkpoint checkpoint) in _checkpoints)
        {
            if (index <= moveIndex)
            {
                best = checkpoint;
                bestIndex = index;
            }
        }

        if (best is null)
        {
            Reset();
            return;
        }

        _stock.Restore(best.Material);
        _moveIndex = bestIndex;
        Travelled = best.Travelled;
        ToolPosition = best.ToolPosition;
        LoadCurrentMove();
    }

    public int CurrentMoveIndex => _moveIndex;
    public bool IsFinished => _moveIndex >= _moves.Count;
    public Point3 ToolPosition { get; private set; }
    public Move? CurrentMove => _moves.Count == 0 ? null : _moves[Math.Min(_moveIndex, _moves.Count - 1)];
    public int CurrentTool => CurrentMove?.Tool ?? 0;
    public double CurrentToolDiameter => _profile.Diameter;
    public int CurrentLineNumber => CurrentMove?.LineNumber ?? 0;
    public double Travelled { get; private set; }
    public IReadOnlyList<int> RapidCollisions { get; } = [];
    public int Version => _stock.Version;

    public void Reset()
    {
        _stock.Reset();
        _moveIndex = 0;
        Travelled = 0;
        ToolPosition = _moves.Count > 0 ? _moves[0].Start : Point3.Origin;
        LoadCurrentMove();
    }

    public void Advance(double distance)
    {
        while (distance > 0 && !IsFinished)
        {
            distance -= AdvanceWithinPiece(distance);
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
            AdvanceWithinPiece(double.PositiveInfinity);
        }
    }

    public void RunToMove(int moveIndex)
    {
        moveIndex = Math.Clamp(moveIndex, 0, _moves.Count);
        if (moveIndex < _moveIndex || (moveIndex == _moveIndex && (_pieceIndex > 0 || _distanceInPiece > 0)))
        {
            RewindFor(moveIndex);
        }

        while (_moveIndex < moveIndex)
        {
            AdvanceWithinPiece(double.PositiveInfinity);
        }
    }

    public void RunToEnd()
    {
        while (!IsFinished)
        {
            AdvanceWithinPiece(double.PositiveInfinity);
        }
    }

    public bool AdvanceToward(int moveIndex, double maxDistance)
    {
        moveIndex = Math.Clamp(moveIndex, 0, _moves.Count);
        if (moveIndex < _moveIndex || (moveIndex == _moveIndex && (_pieceIndex > 0 || _distanceInPiece > 0)))
        {
            RewindFor(moveIndex);
        }

        while (_moveIndex < moveIndex && maxDistance > 0)
        {
            maxDistance -= AdvanceWithinPiece(maxDistance);
        }

        return _moveIndex >= moveIndex;
    }

    private double AdvanceWithinPiece(double maxDistance)
    {
        Point3 a = _polyline[_pieceIndex];
        Point3 b = _polyline[_pieceIndex + 1];
        double length = a.DistanceTo(b);
        double step = Math.Min(maxDistance, length - _distanceInPiece);
        double from = _distanceInPiece;

        _distanceInPiece += step;
        Travelled += step;
        bool pieceDone = _distanceInPiece >= length - 1e-9;
        Point3 to = pieceDone ? b : Point3.Lerp(a, b, _distanceInPiece / length);

        Move move = _moves[_moveIndex];
        if (!move.IsRapid)
        {
            CutPiece(move, a, b, length, from, pieceDone ? length : _distanceInPiece);
        }

        ToolPosition = to;
        if (pieceDone)
        {
            _pieceIndex++;
            _distanceInPiece = 0;
            if (_pieceIndex >= _polyline.Count - 1)
            {
                _moveIndex++;
                LoadCurrentMove();
                TakeCheckpoint();
            }
        }

        return step;
    }

    private void LoadCurrentMove()
    {
        _polyline.Clear();
        _cumulative.Clear();
        _pieceIndex = 0;
        _distanceInPiece = 0;
        if (IsFinished)
        {
            return;
        }

        Move move = _moves[_moveIndex];
        _profile = _profileOf(move.Tool) ?? ToolProfile.Flat(10);
        _polyline.Add(move.Start);
        move.AppendPolyline(_polyline, Math.Max(0.001, _stock.VoxelSize * 0.25));
        double total = 0;
        _cumulative.Add(0);
        for (int i = 1; i < _polyline.Count; i++)
        {
            total += _polyline[i - 1].DistanceTo(_polyline[i]);
            _cumulative.Add(total);
        }
    }

    // Carimba a ferramenta em pontos do trecho espaçados de meio voxel (e de no máximo 1° do eixo rotativo);
    // só os pontos entre "from" e "to", para andar por fatias dar o mesmo material que andar de uma vez
    private void CutPiece(Move move, Point3 a, Point3 b, double length, double from, double to)
    {
        RotaryAngles start = _path.RotaryAtStart(_moveIndex), end = _path.RotaryAt(_moveIndex);
        double turn = Math.Max(Math.Abs(end.A - start.A), Math.Max(Math.Abs(end.B - start.B), Math.Abs(end.C - start.C)));
        double total = _cumulative[^1];
        double pieceStart = _cumulative[_pieceIndex];
        if (total <= 1e-12 && turn <= 1e-9)
        {
            return; // movimento parado e sem giro: nada a cortar
        }

        int count = Math.Max(1, (int)Math.Ceiling(length / (_stock.VoxelSize * 0.5)));
        if (total <= 1e-12)
        {
            count = Math.Max(1, (int)Math.Ceiling(turn)); // só giro: um carimbo por grau
        }
        else
        {
            count = Math.Max(count, (int)Math.Ceiling(turn * (length / total)));
        }

        (int first, int last) = MaterialSimulator.StampRange(count, length, from, to);
        Point3 offset = _path.Modals[move.ModalId].Offset;
        for (int k = first; k <= last; k++)
        {
            Point3 p = Point3.Lerp(a, b, (double)k / count);
            double fraction = total <= 1e-12 ? (double)k / count : Math.Clamp((pieceStart + (length * k / count)) / total, 0, 1);
            RotaryAngles angles = RotaryAngles.Lerp(start, end, fraction);
            Point3 tip = p, up = new(p.X, p.Y, p.Z + 1);
            if (_frame is not null && angles.IsRotated)
            {
                tip = _frame.ToPart(p, offset, angles);
                up = _frame.ToPart(up, offset, angles);
            }

            Point3 dir = new(up.X - tip.X, up.Y - tip.Y, up.Z - tip.Z);
            double len = Math.Sqrt((dir.X * dir.X) + (dir.Y * dir.Y) + (dir.Z * dir.Z));
            dir = new Point3(dir.X / len, dir.Y / len, dir.Z / len);
            _stock.Carve(tip, dir, _profile, CutLength);
        }
    }
}
