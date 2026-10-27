using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Scene;

/// <summary>
/// Segmentos de reta de um mesmo papel (trajetória em avanço, rápidos, rápidos no material).
/// Posições em pares (início, fim) de 3 floats; cada segmento lembra o movimento de origem,
/// para a tela levar à linha do programa.
/// </summary>
public sealed class SceneLines
{
    private readonly List<float> _positions = [];
    private readonly List<int> _moves = [];

    public SceneLines(SceneRole role)
    {
        Role = role;
    }

    public SceneRole Role { get; }

    /// <summary>x, y, z do início e do fim de cada segmento (6 floats por segmento).</summary>
    public IReadOnlyList<float> Positions => _positions;

    /// <summary>Índice do movimento (em ToolPath.Moves) de cada segmento.</summary>
    public IReadOnlyList<int> MoveOfSegment => _moves;

    public int SegmentCount => _moves.Count;

    private float[]? _array;

    /// <summary>As posições num vetor (feito uma vez; as linhas não mudam depois de montadas).</summary>
    public float[] PositionArray => _array ??= [.. _positions];

    internal void Add(Point3 a, Point3 b, int move)
    {
        _positions.Add((float)a.X);
        _positions.Add((float)a.Y);
        _positions.Add((float)a.Z);
        _positions.Add((float)b.X);
        _positions.Add((float)b.Y);
        _positions.Add((float)b.Z);
        _moves.Add(move);
    }

    /// <summary>As mesmas linhas com cada ponto passado por "map" (ex.: torno para a cena).</summary>
    public SceneLines Mapped(Func<Point3, Point3> map)
    {
        var copy = new SceneLines(Role);
        for (int i = 0; i < _moves.Count; i++)
        {
            int k = i * 6;
            copy.Add(map(new Point3(_positions[k], _positions[k + 1], _positions[k + 2])),
                map(new Point3(_positions[k + 3], _positions[k + 4], _positions[k + 5])), _moves[i]);
        }

        return copy;
    }

    /// <summary>
    /// Trajetória separada por papel: avanço, rápido e rápido que encostou no material
    /// ("rapidCollisions", na ordem da simulação). Arcos viram segmentos com a tolerância dada.
    /// </summary>
    public static (SceneLines Feed, SceneLines Rapid, SceneLines RapidInMaterial) FromToolPath(
        ToolPath path, IReadOnlyCollection<int>? rapidCollisions = null, double tolerance = 0.01, RotaryPartFrame? partFrame = null)
    {
        var feed = new SceneLines(SceneRole.FeedPath);
        var rapid = new SceneLines(SceneRole.RapidPath);
        var collided = new SceneLines(SceneRole.RapidInMaterial);
        HashSet<int> hits = rapidCollisions is null ? [] : [.. rapidCollisions];
        var points = new List<Point3>();

        for (int i = 0; i < path.Moves.Count; i++)
        {
            Move move = path.Moves[i];
            SceneLines target = !move.IsRapid ? feed : hits.Contains(i) ? collided : rapid;
            points.Clear();
            points.Add(move.Start);
            move.AppendPolyline(points, tolerance);
            RotaryAngles endAngles = path.RotaryAt(i), startAngles = path.RotaryAtStart(i);
            if (partFrame is not null && (endAngles.IsRotated || startAngles.IsRotated))
            {
                Point3 offset = path.Modals[move.ModalId].Offset;
                for (int k = 0; k < points.Count; k++)
                {
                    // o primeiro ponto tem o ângulo do começo; os outros, o do fim (giro e deslocamento juntos = reta no desenvolvimento)
                    points[k] = partFrame.ToPart(points[k], offset, k == 0 ? startAngles : endAngles);
                }
            }
            for (int k = 0; k + 1 < points.Count; k++)
            {
                target.Add(points[k], points[k + 1], i);
            }
        }

        return (feed, rapid, collided);
    }
}
