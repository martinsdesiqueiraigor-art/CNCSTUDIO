using CncStudio.Core.Cam;
using CncStudio.Core.Geometry;

namespace CncStudio.Core.Cam3D;

/// <summary>
/// Contorno de uma parede da peça (CAM-FACE-02): o laço da seção horizontal que passa pela parede clicada, o lado em que fica o
/// material e as alturas lidas da malha. Parede reta = seção no meio da altura; parede inclinada = seção junto da base (o desbaste
/// em degraus deixa escada de material, nunca entra na peça). Pino e furo completos viram círculo exato.
/// </summary>
/// <param name="Side">Fora = o material fica dentro do laço (pino, ressalto); dentro = furo ou rebaixo.</param>
/// <param name="ChamferWidth">Só parede inclinada: largura horizontal da inclinação (do topo da parede até a base).</param>
/// <param name="ChamferTipAngle">Só parede inclinada: ângulo incluído da fresa em V que copia a inclinação.</param>
public sealed record WallContour(Contour Contour, ContourSide Side, double TopZ, double BottomZ, bool Inclined, double ChamferWidth, double ChamferTipAngle);

public static class WallSection
{
    /// <summary>Abaixo disso (em |nz| médio) a parede é tratada como vertical (≈ 1°).</summary>
    public const double VerticalNz = 0.02;

    /// <summary>A parede clicada como contorno; <c>Why</c> diz por que não serve.</summary>
    public static (WallContour? Wall, string? Why) Of(MeshFeatures features, MeshFace face)
    {
        if (face.Kind == FaceKind.Plane && Math.Abs(face.Normal.Z) >= 0.99)
        {
            return (null, "Face horizontal não é parede.");
        }

        double top = face.Box.Max.Z, bottom = face.Box.Min.Z, height = top - bottom;
        if (height < 0.01)
        {
            return (null, "A parede não tem altura em Z.");
        }

        // inclinação média dos triângulos da parede (área como peso)
        double sumA = 0, sumNz = 0, sumNh = 0;
        bool down = false;
        foreach (int t in face.Triangles)
        {
            (Point3 a, Point3 b, Point3 c) = features.Mesh.Triangle(t);
            Point3 n = Cross(b - a, c - a);
            double len = Math.Sqrt((n.X * n.X) + (n.Y * n.Y) + (n.Z * n.Z));
            if (len < 1e-12)
            {
                continue;
            }

            double area = len / 2, nz = n.Z / len, nh = Math.Sqrt((n.X * n.X) + (n.Y * n.Y)) / len;
            down |= nz < -0.05;
            sumA += area;
            sumNz += area * nz;
            sumNh += area * nh;
        }

        if (sumA <= 0)
        {
            return (null, "Não achei os triângulos da parede (malha aberta?).");
        }

        if (down)
        {
            return (null, "Parede voltada para baixo (contrassaída): a fresa só chega por cima (3 eixos).");
        }

        double avgNz = sumNz / sumA, avgNh = sumNh / sumA;
        bool inclined = avgNz >= VerticalNz;
        if (avgNh < 0.05)
        {
            return (null, "Face quase horizontal: use Faceamento ou Bolsão.");
        }

        // parede reta: seção no meio; inclinada: logo acima da base (o contorno fica no pé, nunca dentro da peça)
        double z = inclined ? bottom + Math.Min(0.001, height / 10) : (top + bottom) / 2;
        List<(Point3 A, Point3 B, bool Own)> segs = Slice(features, face, z);
        List<List<(Point3 A, Point3 B, bool Own)>> loops = Stitch(segs);
        List<(Point3 A, Point3 B, bool Own)>? loop = loops.Where(l => l.Count >= 3).OrderByDescending(l => l.Count(s => s.Own)).FirstOrDefault();
        if (loop is null || !loop.Any(s => s.Own))
        {
            return (null, "A seção desta parede não fecha um laço (malha aberta?).");
        }

        List<Point3> ring = [.. loop.Select(s => s.A)];
        ContourSide side = MaterialInside(features, face, loop, ring) ? ContourSide.Outside : ContourSide.Inside;
        Contour contour = !inclined && face.Kind is FaceKind.Boss or FaceKind.Hole && face.Diameter > 0
            ? Contour.Circle(new Point2(face.Center.X, face.Center.Y), face.Diameter / 2)
            : CamJobPart.Polygon(ring);
        double width = inclined ? height * avgNz / avgNh : 0;
        double tip = inclined ? 2 * Math.Atan2(width, height) * 180 / Math.PI : 0;
        return (new WallContour(contour, side, Math.Round(top, 3), Math.Round(bottom, 3), inclined, Math.Round(width, 3), Math.Round(tip, 1)), null);
    }

    /// <summary>
    /// O material fica dentro do laço? Um ponto a 0,05 mm da parede, no sentido da normal (para fora do material): dentro do laço =
    /// o vazio está dentro (furo); fora = o material está dentro (pino).
    /// </summary>
    private static bool MaterialInside(MeshFeatures features, MeshFace face, List<(Point3 A, Point3 B, bool Own)> loop, List<Point3> ring)
    {
        (Point3 A, Point3 B, bool Own) seg = loop.First(s => s.Own);
        Point3 mid = new((seg.A.X + seg.B.X) / 2, (seg.A.Y + seg.B.Y) / 2, seg.A.Z);
        Point3 n = face.Normal;
        int best = -1;
        double bestD = double.MaxValue;
        foreach (int t in face.Triangles)
        {
            (Point3 a, Point3 b, Point3 c) = features.Mesh.Triangle(t);
            double d = Dist2((a.X + b.X + c.X) / 3 - mid.X, (a.Y + b.Y + c.Y) / 3 - mid.Y) + Math.Pow(((a.Z + b.Z + c.Z) / 3) - mid.Z, 2);
            if (d < bestD)
            {
                bestD = d;
                best = t;
            }
        }

        if (best >= 0)
        {
            (Point3 a, Point3 b, Point3 c) = features.Mesh.Triangle(best);
            n = Cross(b - a, c - a);
        }

        double h = Math.Sqrt((n.X * n.X) + (n.Y * n.Y));
        if (h < 1e-12)
        {
            return true;
        }

        var probe = new Point2(mid.X + (n.X / h * 0.05), mid.Y + (n.Y / h * 0.05));
        return !Inside(ring, probe);
    }

    /// <summary>Segmentos da malha inteira no plano Z; marca os que saem da parede clicada.</summary>
    private static List<(Point3 A, Point3 B, bool Own)> Slice(MeshFeatures features, MeshFace face, double z)
    {
        var own = new HashSet<int>(face.Triangles);
        var segs = new List<(Point3, Point3, bool)>();
        TriangleMesh mesh = features.Mesh;
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            (Point3 a, Point3 b, Point3 c) = mesh.Triangle(t);
            var cut = new List<Point3>(2);
            Cross(a, b, z, cut);
            Cross(b, c, z, cut);
            Cross(c, a, z, cut);
            if (cut.Count == 2 && Dist2(cut[0].X - cut[1].X, cut[0].Y - cut[1].Y) > 1e-14)
            {
                segs.Add((cut[0], cut[1], own.Contains(t)));
            }
        }

        return segs;
    }

    private static void Cross(Point3 p, Point3 q, double z, List<Point3> cut)
    {
        // meio-aberto: vértice exatamente no plano conta só de um lado (sem ponto repetido)
        bool pa = p.Z > z, qa = q.Z > z;
        if (pa == qa)
        {
            return;
        }

        double t = (z - p.Z) / (q.Z - p.Z);
        cut.Add(new Point3(p.X + ((q.X - p.X) * t), p.Y + ((q.Y - p.Y) * t), z));
    }

    /// <summary>Junta os segmentos pelas pontas em laços fechados (pontas iguais a 1 µm).</summary>
    private static List<List<(Point3 A, Point3 B, bool Own)>> Stitch(List<(Point3 A, Point3 B, bool Own)> segs)
    {
        static (long, long) Key(Point3 p) => ((long)Math.Round(p.X * 1e6 / 1), (long)Math.Round(p.Y * 1e6 / 1));
        static (long, long) Coarse((long X, long Y) k) => (k.X / 1000, k.Y / 1000);
        var byPoint = new Dictionary<(long, long), List<int>>();
        for (int i = 0; i < segs.Count; i++)
        {
            foreach (Point3 p in new[] { segs[i].A, segs[i].B })
            {
                (long, long) k = Coarse(Key(p));
                if (!byPoint.TryGetValue(k, out List<int>? l))
                {
                    byPoint[k] = l = [];
                }

                l.Add(i);
            }
        }

        var used = new bool[segs.Count];
        var loops = new List<List<(Point3, Point3, bool)>>();
        for (int s = 0; s < segs.Count; s++)
        {
            if (used[s])
            {
                continue;
            }

            used[s] = true;
            var loop = new List<(Point3, Point3, bool)> { segs[s] };
            Point3 start = segs[s].A, end = segs[s].B;
            while (Dist2(end.X - start.X, end.Y - start.Y) > 1e-10)
            {
                int next = -1;
                bool flip = false;
                (long cx, long cy) = Coarse(Key(end));
                for (long dx = -1; dx <= 1 && next < 0; dx++)
                {
                    for (long dy = -1; dy <= 1 && next < 0; dy++)
                    {
                        if (!byPoint.TryGetValue((cx + dx, cy + dy), out List<int>? cands))
                        {
                            continue;
                        }

                        foreach (int c in cands)
                        {
                            if (used[c])
                            {
                                continue;
                            }

                            if (Dist2(segs[c].A.X - end.X, segs[c].A.Y - end.Y) < 1e-10)
                            {
                                next = c;
                                break;
                            }

                            if (Dist2(segs[c].B.X - end.X, segs[c].B.Y - end.Y) < 1e-10)
                            {
                                next = c;
                                flip = true;
                                break;
                            }
                        }
                    }
                }

                if (next < 0)
                {
                    break; // laço aberto (malha com furo): fica de fora
                }

                used[next] = true;
                (Point3 A, Point3 B, bool Own) n = flip ? (segs[next].B, segs[next].A, segs[next].Own) : segs[next];
                loop.Add(n);
                end = n.B;
            }

            if (Dist2(end.X - start.X, end.Y - start.Y) <= 1e-10)
            {
                loops.Add(loop);
            }
        }

        return loops;
    }

    private static bool Inside(List<Point3> ring, Point2 p)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            Point3 a = ring[i], b = ring[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < ((b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y)) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static double Dist2(double dx, double dy) => (dx * dx) + (dy * dy);

    private static Point3 Cross(Point3 u, Point3 v) => new((u.Y * v.Z) - (u.Z * v.Y), (u.Z * v.X) - (u.X * v.Z), (u.X * v.Y) - (u.Y * v.X));
}
