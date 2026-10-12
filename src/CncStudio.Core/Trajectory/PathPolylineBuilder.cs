using CncStudio.Core.Geometry;

namespace CncStudio.Core.Trajectory;

/// <summary>
/// Trecho contínuo da trajetória para desenho: uma sequência de pontos do mesmo
/// tipo (rápido ou corte). Movimentos encadeados viram uma única figura.
/// </summary>
/// <param name="IsRapid">True para G00.</param>
/// <param name="Points">Pontos 3D em ordem (pelo menos 2).</param>
public sealed record PathPolyline(bool IsRapid, Point3[] Points);

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
        bool currentIsRapid = false;

        foreach (Move move in moves)
        {
            bool continues = points.Count > 0
                && move.IsRapid == currentIsRapid
                && move.Start == points[^1];

            if (!continues)
            {
                Flush(figures, points, currentIsRapid);
                points.Add(move.Start);
                currentIsRapid = move.IsRapid;
            }

            move.AppendPolyline(points, arcTolerance);
        }

        Flush(figures, points, currentIsRapid);
        return figures;
    }

    /// <summary>Tolerância de arco adequada para desenhar a trajetória inteira na tela.</summary>
    public static double ScreenTolerance(ToolPath path)
    {
        double size = Math.Max(path.Bounds.SizeX, Math.Max(path.Bounds.SizeY, path.Bounds.SizeZ));
        return Math.Max(0.001, size / 4000);
    }

    private static void Flush(List<PathPolyline> figures, List<Point3> points, bool isRapid)
    {
        if (points.Count >= 2)
        {
            figures.Add(new PathPolyline(isRapid, points.ToArray()));
        }

        points.Clear();
    }
}
