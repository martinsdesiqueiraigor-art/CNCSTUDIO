using CncStudio.Core.Geometry;

namespace CncStudio.Core.Trajectory;

/// <summary>
/// Trecho contínuo da trajetória para desenho: uma sequência de pontos do mesmo tipo de
/// movimento (G00, G01, G02 ou G03). Movimentos encadeados do mesmo tipo viram uma única figura.
/// </summary>
/// <param name="Motion">Tipo de movimento (define a cor no backplot).</param>
/// <param name="Points">Pontos 3D em ordem (pelo menos 2).</param>
/// <param name="FirstMove">Índice (na trajetória) do primeiro movimento da figura.</param>
/// <param name="MoveStarts">Para cada movimento da figura, o índice em Points do seu ponto inicial.</param>
public sealed record PathPolyline(MotionMode Motion, Point3[] Points, int FirstMove, int[] MoveStarts)
{
    public bool IsRapid => Motion == MotionMode.Rapid;

    /// <summary>Índice (na trajetória) do último movimento da figura.</summary>
    public int LastMove => FirstMove + MoveStarts.Length - 1;

    /// <summary>
    /// Quantidade de pontos até o fim do movimento "moveIndex" (para desenhar a figura só até ali).
    /// </summary>
    public int PointCountThrough(int moveIndex)
    {
        int k = moveIndex - FirstMove;
        if (k < 0)
        {
            return 0;
        }

        return k + 1 >= MoveStarts.Length ? Points.Length : MoveStarts[k + 1] + 1;
    }
}

/// <summary>Converte a trajetória em figuras prontas para o backplot.</summary>
public static class PathPolylineBuilder
{
    /// <summary>
    /// Junta movimentos consecutivos do mesmo tipo em figuras contínuas e interpola os arcos
    /// com a tolerância informada (para tela, algo como 1/4000 do tamanho da peça basta).
    /// </summary>
    public static List<PathPolyline> Build(IReadOnlyList<Move> moves, double arcTolerance)
    {
        var figures = new List<PathPolyline>();
        var points = new List<Point3>();
        var starts = new List<int>();
        MotionMode current = MotionMode.Rapid;
        int firstMove = 0;

        for (int i = 0; i < moves.Count; i++)
        {
            Move move = moves[i];
            bool continues = points.Count > 0
                && move.Motion == current
                && move.Start == points[^1];

            if (!continues)
            {
                Flush(figures, points, starts, current, firstMove);
                points.Add(move.Start);
                current = move.Motion;
                firstMove = i;
            }

            starts.Add(points.Count - 1);
            move.AppendPolyline(points, arcTolerance);
        }

        Flush(figures, points, starts, current, firstMove);
        return figures;
    }

    /// <summary>Figura de um único movimento (ex.: destaque da linha do cursor).</summary>
    public static PathPolyline Single(Move move, int index, double arcTolerance)
    {
        var points = new List<Point3> { move.Start };
        move.AppendPolyline(points, arcTolerance);
        return new PathPolyline(move.Motion, points.ToArray(), index, [0]);
    }

    /// <summary>Tolerância de arco adequada para desenhar a trajetória inteira na tela.</summary>
    public static double ScreenTolerance(ToolPath path)
    {
        double size = Math.Max(path.Bounds.SizeX, Math.Max(path.Bounds.SizeY, path.Bounds.SizeZ));
        return Math.Max(0.001, size / 4000);
    }

    private static void Flush(List<PathPolyline> figures, List<Point3> points, List<int> starts, MotionMode motion, int firstMove)
    {
        if (points.Count >= 2 && starts.Count > 0)
        {
            figures.Add(new PathPolyline(motion, points.ToArray(), firstMove, starts.ToArray()));
        }

        points.Clear();
        starts.Clear();
    }
}
