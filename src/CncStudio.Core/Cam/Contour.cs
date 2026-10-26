namespace CncStudio.Core.Cam;

/// <summary>Resultado de um deslocamento: o contorno novo ou o motivo de não ter dado.</summary>
public sealed record OffsetResult(Contour? Contour, string? Error)
{
    public bool Ok => Contour is not null;
}

/// <summary>
/// Contorno fechado em 2D feito de retas e arcos. O deslocamento (offset) é a base da compensação de ferramenta no CAM:
/// bolsão, contorno por dentro ou por fora.
/// </summary>
public sealed class Contour
{
    private const double Eps = 1e-7;

    public Contour(IReadOnlyList<Segment2> segments)
    {
        Segments = segments;
    }

    public IReadOnlyList<Segment2> Segments { get; }

    /// <summary>Início de cada trecho no fim do anterior (fecha, dentro de uma tolerância).</summary>
    public bool IsClosed
    {
        get
        {
            if (Segments.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < Segments.Count; i++)
            {
                Segment2 next = Segments[(i + 1) % Segments.Count];
                if (Segments[i].End.DistanceTo(next.Start) > 1e-4)
                {
                    return false;
                }
            }

            return true;
        }
    }

    /// <summary>Área com sinal: positiva se o contorno gira no sentido anti-horário.</summary>
    public double SignedArea => Segments.Sum(s => s.AreaTerm());

    public bool IsCounterClockwise => SignedArea > 0;

    public double Perimeter => Segments.Sum(s => s.Length);

    public static Contour Rectangle(double x, double y, double width, double height) =>
        FromPoints([new(x, y), new(x + width, y), new(x + width, y + height), new(x, y + height)]);

    /// <summary>Círculo anti-horário (um arco só, início igual ao fim).</summary>
    public static Contour Circle(Point2 center, double radius)
    {
        var start = new Point2(center.X + radius, center.Y);
        return new Contour([Segment2.Arc(start, start, center, ccw: true)]);
    }

    /// <summary>Polígono fechado a partir dos vértices (o último liga ao primeiro).</summary>
    public static Contour FromPoints(IReadOnlyList<Point2> points)
    {
        var segments = new List<Segment2>();
        for (int i = 0; i < points.Count; i++)
        {
            segments.Add(Segment2.Line(points[i], points[(i + 1) % points.Count]));
        }

        return new Contour(segments);
    }

    /// <summary>Mesmo contorno percorrido ao contrário.</summary>
    public Contour Reversed()
    {
        var list = new List<Segment2>();
        for (int i = Segments.Count - 1; i >= 0; i--)
        {
            Segment2 s = Segments[i];
            list.Add(s.IsArc ? Segment2.Arc(s.End, s.Start, s.Center, !s.Ccw) : Segment2.Line(s.End, s.Start));
        }

        return new Contour(list);
    }

    /// <summary>Pontos do contorno inteiro (para desenho e conferências).</summary>
    public List<Point2> Sample(double tolerance = 0.01)
    {
        var points = new List<Point2>();
        foreach (Segment2 s in Segments)
        {
            bool first = true;
            foreach (Point2 p in s.Sample(tolerance))
            {
                if (first && points.Count > 0)
                {
                    first = false;
                    continue;
                }

                first = false;
                points.Add(p);
            }
        }

        return points;
    }

    /// <summary>
    /// Desloca o contorno para fora (distância positiva, aumenta) ou para dentro (negativa, diminui), qualquer que seja o
    /// sentido de percurso. Devolve erro se o deslocamento não cabe (cantos internos menores que a distância, contorno que se cruza).
    /// </summary>
    public OffsetResult OffsetOutside(double distance) =>
        OffsetLeft(IsCounterClockwise ? -distance : distance);

    /// <summary>Desloca para a esquerda do sentido de percurso (positivo) ou para a direita (negativo).</summary>
    public OffsetResult OffsetLeft(double d)
    {
        if (!IsClosed)
        {
            return new OffsetResult(null, "O contorno não está fechado.");
        }

        if (Math.Abs(d) < Eps)
        {
            return new OffsetResult(this, null);
        }

        int n = Segments.Count;
        var off = new List<Segment2>(n);
        foreach (Segment2 s in Segments)
        {
            Segment2? o = OffsetSegment(s, d);
            if (o is null)
            {
                return new OffsetResult(null, "Um arco do contorno tem raio menor que o deslocamento.");
            }

            off.Add(o.Value);
        }

        // Junção de cada par de trechos vizinhos: encontro (canto interno) ou arco de concordância (canto externo)
        var joins = new Segment2?[n];
        for (int i = 0; i < n; i++)
        {
            int j = (i + 1) % n;
            Point2 pivot = Segments[i].End;
            Point2 t0 = Segments[i].EndTangent;
            Point2 t1 = Segments[j].StartTangent;
            double cross = t0.Cross(t1);
            double dot = t0.Dot(t1);
            if (Math.Abs(cross) < 1e-9 && dot > 0)
            {
                continue; // continua tangente: nada a fazer
            }

            bool gap = (Math.Abs(cross) < 1e-9) || (cross * d < 0);
            if (gap)
            {
                if (off[i].End.DistanceTo(off[j].Start) > Eps)
                {
                    joins[i] = Segment2.Arc(off[i].End, off[j].Start, pivot, ccw: d < 0);
                }

                continue;
            }

            Point2? x = IntersectNear(off[i], off[j], pivot);
            if (x is null)
            {
                return new OffsetResult(null, "O deslocamento não cabe neste contorno.");
            }

            off[i] = off[i] with { End = x.Value };
            off[j] = off[j] with { Start = x.Value };
        }

        // Cada trecho precisa continuar andando no mesmo sentido (senão o canto foi comido)
        for (int i = 0; i < n; i++)
        {
            Segment2 o = off[i];
            Segment2 orig = Segments[i];
            if (!o.IsArc)
            {
                Point2 dir = (o.End - o.Start);
                if (dir.Length < Eps || dir.Dot(orig.End - orig.Start) <= 0)
                {
                    return new OffsetResult(null, "O deslocamento é grande demais para este contorno.");
                }
            }
            else if (o.Length < Eps || Math.Abs(o.Sweep) > Math.Abs(orig.Sweep) + 1e-6)
            {
                return new OffsetResult(null, "O deslocamento é grande demais para este contorno.");
            }
        }

        var result = new List<Segment2>();
        for (int i = 0; i < n; i++)
        {
            result.Add(off[i]);
            if (joins[i] is { } join)
            {
                result.Add(join);
            }
        }

        var contour = new Contour(result);
        if (!contour.IsClosed)
        {
            return new OffsetResult(null, "O deslocamento não fechou o contorno.");
        }

        if ((contour.SignedArea > 0) != (SignedArea > 0) || Math.Abs(contour.SignedArea) < Eps)
        {
            return new OffsetResult(null, "O deslocamento é grande demais: o contorno some ou se inverte.");
        }

        if (contour.SelfIntersects())
        {
            return new OffsetResult(null, "O deslocamento é grande demais: o contorno se cruza.");
        }

        return new OffsetResult(contour, null);
    }

    private static Segment2? OffsetSegment(Segment2 s, double d)
    {
        if (!s.IsArc)
        {
            Point2 dir = (s.End - s.Start).Normalized();
            var normal = new Point2(-dir.Y, dir.X);
            return Segment2.Line(s.Start + (normal * d), s.End + (normal * d));
        }

        double r = s.Radius;
        double newRadius = s.Ccw ? r - d : r + d;
        if (newRadius <= Eps)
        {
            return null;
        }

        double k = newRadius / r;
        Point2 start = s.Center + ((s.Start - s.Center) * k);
        Point2 end = s.Center + ((s.End - s.Center) * k);
        return Segment2.Arc(start, end, s.Center, s.Ccw);
    }

    /// <summary>Ponto de encontro das duas curvas (prolongadas) mais perto do pivô; null se não se encontram.</summary>
    private static Point2? IntersectNear(Segment2 a, Segment2 b, Point2 pivot)
    {
        List<Point2> candidates;
        if (!a.IsArc && !b.IsArc)
        {
            candidates = LineLine(a, b);
        }
        else if (a.IsArc && b.IsArc)
        {
            candidates = CircleCircle(a.Center, a.Radius, b.Center, b.Radius);
        }
        else if (a.IsArc)
        {
            candidates = LineCircle(b, a.Center, a.Radius);
        }
        else
        {
            candidates = LineCircle(a, b.Center, b.Radius);
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates.OrderBy(p => p.DistanceTo(pivot)).First();
    }

    private static List<Point2> LineLine(Segment2 a, Segment2 b)
    {
        Point2 r = a.End - a.Start;
        Point2 s = b.End - b.Start;
        double denominator = r.Cross(s);
        if (Math.Abs(denominator) < 1e-12)
        {
            return [];
        }

        double t = (b.Start - a.Start).Cross(s) / denominator;
        return [a.Start + (r * t)];
    }

    private static List<Point2> LineCircle(Segment2 line, Point2 center, double radius)
    {
        Point2 d = line.End - line.Start;
        Point2 f = line.Start - center;
        double qa = d.Dot(d);
        double qb = 2 * f.Dot(d);
        double qc = f.Dot(f) - (radius * radius);
        double disc = (qb * qb) - (4 * qa * qc);
        if (disc < 0)
        {
            if (disc > -1e-9)
            {
                disc = 0;
            }
            else
            {
                return [];
            }
        }

        double root = Math.Sqrt(disc);
        return [line.Start + (d * ((-qb - root) / (2 * qa))), line.Start + (d * ((-qb + root) / (2 * qa)))];
    }

    private static List<Point2> CircleCircle(Point2 c0, double r0, Point2 c1, double r1)
    {
        double dist = c0.DistanceTo(c1);
        if (dist < 1e-12 || dist > r0 + r1 + 1e-9 || dist < Math.Abs(r0 - r1) - 1e-9)
        {
            return [];
        }

        double along = ((r0 * r0) - (r1 * r1) + (dist * dist)) / (2 * dist);
        double h2 = (r0 * r0) - (along * along);
        double h = h2 > 0 ? Math.Sqrt(h2) : 0;
        Point2 dir = (c1 - c0) * (1 / dist);
        Point2 mid = c0 + (dir * along);
        var perp = new Point2(-dir.Y, dir.X);
        return [mid + (perp * h), mid - (perp * h)];
    }

    /// <summary>Ponto do contorno mais perto de <paramref name="p"/> e o índice do trecho onde está.</summary>
    public (int Index, Point2 Point) ClosestPoint(Point2 p)
    {
        int bestIndex = 0;
        Point2 best = Segments[0].Start;
        double bestDist = double.MaxValue;
        for (int i = 0; i < Segments.Count; i++)
        {
            Point2 q = ClosestOnSegment(Segments[i], p);
            double d = q.DistanceTo(p);
            if (d < bestDist)
            {
                bestDist = d;
                best = q;
                bestIndex = i;
            }
        }

        return (bestIndex, best);
    }

    private static Point2 ClosestOnSegment(Segment2 s, Point2 p)
    {
        if (!s.IsArc)
        {
            Point2 d = s.End - s.Start;
            double len2 = d.Dot(d);
            double t = len2 < 1e-18 ? 0 : Math.Clamp((p - s.Start).Dot(d) / len2, 0, 1);
            return s.Start + (d * t);
        }

        Point2 radial = p - s.Center;
        if (radial.Length > 1e-12)
        {
            double a0 = Math.Atan2(s.Start.Y - s.Center.Y, s.Start.X - s.Center.X);
            double a = Math.Atan2(radial.Y, radial.X);
            double delta = s.Ccw ? a - a0 : a0 - a;
            while (delta < 0)
            {
                delta += 2 * Math.PI;
            }

            while (delta >= 2 * Math.PI)
            {
                delta -= 2 * Math.PI;
            }

            if (delta <= Math.Abs(s.Sweep))
            {
                return s.Center + (radial.Normalized() * s.Radius);
            }
        }

        return p.DistanceTo(s.Start) <= p.DistanceTo(s.End) ? s.Start : s.End;
    }

    /// <summary>Ponto do meio do trecho mais longo (onde entra e sai a ferramenta, longe de cantos).</summary>
    public Point2 MidpointOfLongest()
    {
        Segment2 best = Segments[0];
        foreach (Segment2 s in Segments)
        {
            if (s.Length > best.Length)
            {
                best = s;
            }
        }

        if (!best.IsArc)
        {
            return (best.Start + best.End) * 0.5;
        }

        double a0 = Math.Atan2(best.Start.Y - best.Center.Y, best.Start.X - best.Center.X);
        double a = a0 + (best.Sweep / 2);
        return new Point2(best.Center.X + (best.Radius * Math.Cos(a)), best.Center.Y + (best.Radius * Math.Sin(a)));
    }

    /// <summary>Mesmo contorno, começando (e terminando) no ponto do contorno mais perto de <paramref name="p"/>.</summary>
    public Contour StartingAt(Point2 p)
    {
        (int index, Point2 q) = ClosestPoint(p);
        var list = new List<Segment2>();
        Segment2 s = Segments[index];
        bool atStart = q.DistanceTo(s.Start) < 1e-7;
        bool atEnd = q.DistanceTo(s.End) < 1e-7 && !atStart;
        int n = Segments.Count;
        if (atStart || atEnd)
        {
            int first = atEnd ? (index + 1) % n : index;
            for (int i = 0; i < n; i++)
            {
                list.Add(Segments[(first + i) % n]);
            }

            return new Contour(list);
        }

        Segment2 head = s.IsArc ? Segment2.Arc(q, s.End, s.Center, s.Ccw) : Segment2.Line(q, s.End);
        Segment2 tail = s.IsArc ? Segment2.Arc(s.Start, q, s.Center, s.Ccw) : Segment2.Line(s.Start, q);
        list.Add(head);
        for (int i = 1; i < n; i++)
        {
            list.Add(Segments[(index + i) % n]);
        }

        list.Add(tail);
        return new Contour(list);
    }

    /// <summary>Verdadeiro se algum trecho cruza outro que não é vizinho dele.</summary>
    public bool SelfIntersects()
    {
        var pieces = Segments.Select(s => s.Sample(0.005).ToList()).ToList();
        int n = pieces.Count;
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 2; j < n; j++)
            {
                if (i == 0 && j == n - 1)
                {
                    continue; // vizinhos pelo fechamento
                }

                if (PolylinesCross(pieces[i], pieces[j]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool PolylinesCross(List<Point2> a, List<Point2> b)
    {
        for (int i = 0; i + 1 < a.Count; i++)
        {
            for (int j = 0; j + 1 < b.Count; j++)
            {
                if (ProperCross(a[i], a[i + 1], b[j], b[j + 1]))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool ProperCross(Point2 p1, Point2 p2, Point2 p3, Point2 p4)
    {
        Point2 r = p2 - p1;
        Point2 s = p4 - p3;
        double denominator = r.Cross(s);
        if (Math.Abs(denominator) < 1e-14)
        {
            return false;
        }

        double t = (p3 - p1).Cross(s) / denominator;
        double u = (p3 - p1).Cross(r) / denominator;
        const double margin = 1e-6;
        return t > margin && t < 1 - margin && u > margin && u < 1 - margin;
    }
}
