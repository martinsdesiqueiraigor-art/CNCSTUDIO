namespace CncStudio.Core.Cam;

/// <summary>
/// Escolha de contorno por ponto (clique na vista 2D): vale a borda mais perto, se estiver dentro da tolerância;
/// senão, o menor contorno que contém o ponto (o de dentro, quando há contornos aninhados).
/// </summary>
public static class ContourPick
{
    /// <summary>Índice do contorno escolhido, ou -1 se o ponto não está perto de nenhum nem dentro de algum.</summary>
    public static int At(IReadOnlyList<Contour> contours, Point2 p, double tolerance)
    {
        int best = -1;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < contours.Count; i++)
        {
            double d = contours[i].ClosestPoint(p).Point.DistanceTo(p);
            if (d <= tolerance && d < bestDistance)
            {
                best = i;
                bestDistance = d;
            }
        }

        if (best >= 0)
        {
            return best;
        }

        double smallest = double.MaxValue;
        for (int i = 0; i < contours.Count; i++)
        {
            double area = Math.Abs(contours[i].SignedArea);
            if (area < smallest && Contains(contours[i], p))
            {
                best = i;
                smallest = area;
            }
        }

        return best;
    }

    /// <summary>Ponto dentro do contorno fechado (regra par-ímpar sobre a polilinha amostrada).</summary>
    public static bool Contains(Contour contour, Point2 p)
    {
        List<Point2> pts = contour.Sample(0.02);
        bool inside = false;
        for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
        {
            if ((pts[i].Y > p.Y) != (pts[j].Y > p.Y)
                && p.X < ((pts[j].X - pts[i].X) * (p.Y - pts[i].Y) / (pts[j].Y - pts[i].Y)) + pts[i].X)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
