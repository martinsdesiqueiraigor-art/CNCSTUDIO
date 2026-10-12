using CncStudio.Core.Geometry;

namespace CncStudio.Core.Trajectory;

/// <summary>
/// Acha movimentos e pontos da trajetória perto de um ponto da tela (já convertido para
/// coordenadas da vista). A projeção 3D → 2D vem de quem chama (cada vista tem a sua).
/// </summary>
public static class PathHitTest
{
    /// <summary>
    /// Índice do movimento mais próximo de (u, v) na vista, até "maxDistance" (unidades da vista).
    /// Retorna -1 se nenhum estiver perto. Com empate, prefere corte a rápido.
    /// </summary>
    public static int NearestMove(IReadOnlyList<Move> moves, Func<Point3, (double U, double V)> project,
        double u, double v, double maxDistance, double arcTolerance)
    {
        int best = -1;
        double bestDistance = maxDistance;
        bool bestIsRapid = true;
        var points = new List<Point3>();

        for (int i = 0; i < moves.Count; i++)
        {
            Move move = moves[i];
            points.Clear();
            points.Add(move.Start);
            move.AppendPolyline(points, arcTolerance);

            (double pu, double pv) = project(points[0]);
            for (int k = 1; k < points.Count; k++)
            {
                (double qu, double qv) = project(points[k]);
                double d = SegmentDistance(u, v, pu, pv, qu, qv);
                bool better = d < bestDistance - 1e-9 || (Math.Abs(d - bestDistance) <= 1e-9 && bestIsRapid && !move.IsRapid);
                if (better && d <= maxDistance)
                {
                    best = i;
                    bestDistance = d;
                    bestIsRapid = move.IsRapid;
                }

                (pu, pv) = (qu, qv);
            }
        }

        return best;
    }

    /// <summary>
    /// Ponto da trajetória (início ou fim de movimento) mais próximo de (u, v), até "maxDistance".
    /// Usado para "encaixar" a medição nos pontos programados.
    /// </summary>
    public static Point3? NearestPoint(IReadOnlyList<Move> moves, Func<Point3, (double U, double V)> project,
        double u, double v, double maxDistance)
    {
        Point3? best = null;
        double bestDistance = maxDistance;

        void Consider(Point3 p)
        {
            (double pu, double pv) = project(p);
            double d = Math.Sqrt((pu - u) * (pu - u) + (pv - v) * (pv - v));
            if (d <= bestDistance)
            {
                best = p;
                bestDistance = d;
            }
        }

        foreach (Move move in moves)
        {
            Consider(move.Start);
            Consider(move.End);
        }

        return best;
    }

    /// <summary>Distância do ponto (u, v) ao segmento (a, b).</summary>
    private static double SegmentDistance(double u, double v, double au, double av, double bu, double bv)
    {
        double du = bu - au;
        double dv = bv - av;
        double lengthSquared = du * du + dv * dv;
        double t = lengthSquared < 1e-18 ? 0 : Math.Clamp(((u - au) * du + (v - av) * dv) / lengthSquared, 0, 1);
        double cu = au + t * du - u;
        double cv = av + t * dv - v;
        return Math.Sqrt(cu * cu + cv * cv);
    }
}
