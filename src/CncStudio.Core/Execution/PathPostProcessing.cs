using CncStudio.Core.Geometry;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Execution;

/// <summary>Informações extras de cada movimento usadas no pós-processamento.</summary>
internal struct MoveTag
{
    /// <summary>Compensação de raio: 0 = G40, +1 = G41 (esquerda), −1 = G42 (direita).</summary>
    public sbyte CompensationSide;

    /// <summary>Raio usado na compensação (mm).</summary>
    public double CompensationRadius;

    /// <summary>Canto no fim deste movimento: arredondamento (,R) ou chanfro (,C).</summary>
    public double CornerRadius;

    public double CornerChamfer;
}

/// <summary>
/// Ajustes feitos na trajetória depois da execução, porque dependem do movimento seguinte:
/// cantos arredondados/chanfrados (Fanuc ,R / ,C; Siemens RND / CHR) e compensação de raio G41/G42.
/// A compensação trabalha no plano XY; os cantos, no plano das duas retas.
/// </summary>
internal static class PathPostProcessing
{
    private const double Epsilon = 1e-7;

    // ───────────────────────── Cantos ,R / ,C ─────────────────────────

    /// <summary>
    /// Insere o arredondamento ou chanfro entre duas retas consecutivas. O plano é o das duas
    /// retas: XY na fresa (mesmo Z), ZX no torno (mesmo Y) ou YZ.
    /// </summary>
    public static void ApplyCorners(List<Move> moves, List<MoveTag> tags, Action<int, string> warn)
    {
        for (int i = 0; i < moves.Count - 1; i++)
        {
            MoveTag tag = tags[i];
            if (tag.CornerRadius <= 0 && tag.CornerChamfer <= 0)
            {
                continue;
            }

            Move a = moves[i];
            Move b = moves[i + 1];
            WorkPlane? found = a.IsArc || b.IsArc || a.IsRapid || b.IsRapid ? null : CommonPlane(a, b);
            if (found is not { } plane)
            {
                warn(a.LineNumber, "Canto (,R/,C, RND/CHR) só é aplicado entre duas retas de corte no mesmo plano.");
                continue;
            }

            // Trabalha nas coordenadas do plano (u, v), com w constante
            Point3 P(Point3 p)
            {
                (double u, double v, double w) = plane.ToPlane(p);
                return new Point3(u, v, w);
            }

            Point3 R(Point3 p) => plane.FromPlane(p.X, p.Y, p.Z);

            (double ux, double uy, double lengthA) = Direction(P(a.Start), P(a.End));
            (double vx, double vy, double lengthB) = Direction(P(b.Start), P(b.End));
            if (lengthA < Epsilon || lengthB < Epsilon)
            {
                continue;
            }

            double cosine = Math.Clamp(-(ux * vx + uy * vy), -1, 1);
            double angle = Math.Acos(cosine); // ângulo interno do canto
            if (angle < 1e-6 || Math.PI - angle < 1e-6)
            {
                continue; // retas alinhadas: não há canto
            }

            double distance = tag.CornerRadius > 0
                ? tag.CornerRadius / Math.Tan(angle / 2)
                : tag.CornerChamfer;

            if (distance > lengthA + Epsilon || distance > lengthB + Epsilon)
            {
                warn(a.LineNumber, "Canto (,R/,C, RND/CHR) maior que as retas do canto: não aplicado.");
                continue;
            }

            Point3 corner = P(a.End);
            var t1 = new Point3(corner.X - ux * distance, corner.Y - uy * distance, corner.Z);
            var t2 = new Point3(corner.X + vx * distance, corner.Y + vy * distance, corner.Z);

            Move inserted;
            if (tag.CornerRadius > 0)
            {
                // Centro na bissetriz, para dentro do canto
                double bx = vx - ux;
                double by = vy - uy;
                double bl = Math.Sqrt(bx * bx + by * by);
                double centerDistance = tag.CornerRadius / Math.Sin(angle / 2);
                double cx = corner.X + bx / bl * centerDistance;
                double cy = corner.Y + by / bl * centerDistance;
                bool counterClockwise = ux * vy - uy * vx > 0; // virando à esquerda
                inserted = ArcMove(t1, t2, cx, cy, counterClockwise, a) with { Start = R(t1), End = R(t2), Plane = plane };
            }
            else
            {
                inserted = new Move(R(t1), R(t2), MotionMode.Linear, a.LineNumber, a.Feed, a.Tool);
            }

            moves[i] = a with { End = R(t1) };
            moves[i + 1] = b with { Start = R(t2) };
            moves.Insert(i + 1, inserted);
            tags.Insert(i + 1, tags[i] with { CornerRadius = 0, CornerChamfer = 0 });
            i++; // pula o movimento inserido
        }
    }

    /// <summary>Plano em que as duas retas estão (o eixo perpendicular não muda em nenhuma).</summary>
    private static WorkPlane? CommonPlane(Move a, Move b)
    {
        static bool Same(double p, double q, double r, double s) =>
            Math.Abs(p - q) <= Epsilon && Math.Abs(q - r) <= Epsilon && Math.Abs(r - s) <= Epsilon;

        if (Same(a.Start.Z, a.End.Z, b.Start.Z, b.End.Z)) return WorkPlane.XY;
        if (Same(a.Start.Y, a.End.Y, b.Start.Y, b.End.Y)) return WorkPlane.ZX;
        if (Same(a.Start.X, a.End.X, b.Start.X, b.End.X)) return WorkPlane.YZ;
        return null;
    }

    // ───────────────────────── Compensação G41/G42 ─────────────────────────

    /// <summary>Um trecho do contorno (reta ou arco) no plano XY.</summary>
    private sealed class Element
    {
        public required Move Source { get; init; }
        public bool IsArc => Source.IsArc;
        public bool CounterClockwise => Source.Motion == MotionMode.CounterClockwiseArc;
        public double CenterX => Source.CenterU;
        public double CenterY => Source.CenterV;

        // Geometria compensada
        public Point3 Start { get; set; }
        public Point3 End { get; set; }
        public double Radius { get; set; }
        public bool ZOnly { get; init; }
    }

    /// <summary>
    /// Desloca a trajetória do raio da ferramenta para o lado indicado (G41 esquerda, G42 direita).
    /// Entrada e saída do tipo "A" (aproxima direto ao ponto compensado); cantos externos ganham
    /// um arco em volta do canto, cantos internos são aparados na interseção.
    /// </summary>
    public static List<Move> ApplyCompensation(List<Move> moves, List<MoveTag> tags, Action<int, string> warn, Action<int, string> error)
    {
        var output = new List<Move>(moves.Count);
        int i = 0;
        while (i < moves.Count)
        {
            if (tags[i].CompensationSide == 0)
            {
                output.Add(moves[i]);
                i++;
                continue;
            }

            // Trecho compensado: do movimento de ativação até o último antes do G40
            int start = i;
            while (i < moves.Count && tags[i].CompensationSide != 0)
            {
                i++;
            }

            int exitIndex = i < moves.Count ? i : -1; // primeiro movimento com G40 (saída)
            CompensateRun(moves, tags, start, i, exitIndex, output, warn, error);
            if (exitIndex >= 0)
            {
                i++; // a saída já foi gerada
            }
        }

        return output;
    }

    private static void CompensateRun(
        List<Move> moves, List<MoveTag> tags, int start, int end, int exitIndex,
        List<Move> output, Action<int, string> warn, Action<int, string> error)
    {
        int side = tags[start].CompensationSide;
        double r = tags[start].CompensationRadius;
        Move activation = moves[start];

        if (r <= 0 || moves.Skip(start).Take(end - start).Any(m => m.IsArc && m.Plane != WorkPlane.XY))
        {
            // Sem raio (ferramenta sem diâmetro) ou fora do plano XY: mantém o programado
            for (int k = start; k < end; k++) output.Add(moves[k]);
            if (exitIndex >= 0) output.Add(moves[exitIndex]);
            return;
        }

        // Elementos do contorno: tudo depois do movimento de ativação
        var elements = new List<Element>();
        for (int k = start + 1; k < end; k++)
        {
            Move m = moves[k];
            bool zOnly = !m.IsArc && Math.Abs(m.End.X - m.Start.X) < Epsilon && Math.Abs(m.End.Y - m.Start.Y) < Epsilon;
            var element = new Element { Source = m, ZOnly = zOnly };
            if (!zOnly && !Offset(element, side, r))
            {
                error(m.LineNumber, "Raio da ferramenta maior que o raio do arco: compensação impossível neste trecho.");
                for (int j = start; j < end; j++) output.Add(moves[j]);
                if (exitIndex >= 0) output.Add(moves[exitIndex]);
                return;
            }

            elements.Add(element);
        }

        var contour = elements.Where(e => !e.ZOnly).ToList();
        if (contour.Count == 0)
        {
            // Só a ativação: desloca o ponto final pela própria direção dela
            var single = new Element { Source = activation };
            Offset(single, side, r);
            output.Add(activation with { End = single.End });
            if (exitIndex >= 0) output.Add(moves[exitIndex] with { Start = single.End });
            return;
        }

        // Cantos entre elementos consecutivos do contorno
        var joins = new List<Move>?[contour.Count]; // joins[k] = conexões ANTES do elemento k
        for (int k = 1; k < contour.Count; k++)
        {
            joins[k] = Join(contour[k - 1], contour[k], side, r, warn);
        }

        // Ativação: do ponto atual até o início compensado do primeiro elemento
        Point3 firstStart = contour[0].Start;
        output.Add(activation with { End = new Point3(firstStart.X, firstStart.Y, activation.End.Z) });
        Point3 current = output[^1].End;

        int contourIndex = 0;
        foreach (Element element in elements)
        {
            if (element.ZOnly)
            {
                // Subida/descida em Z: fica no ponto compensado atual
                var target = new Point3(current.X, current.Y, element.Source.End.Z);
                output.Add(element.Source with { Start = current, End = target });
                current = target;
                continue;
            }

            if (contourIndex > 0 && joins[contourIndex] is { } connection)
            {
                foreach (Move join in connection)
                {
                    output.Add(join with { Start = new Point3(join.Start.X, join.Start.Y, current.Z), End = new Point3(join.End.X, join.End.Y, current.Z) });
                    current = output[^1].End;
                }
            }

            var elementStart = new Point3(element.Start.X, element.Start.Y, current.Z);
            if (DistanceXY(elementStart, current) > 1e-5)
            {
                output.Add(new Move(current, elementStart, MotionMode.Linear, element.Source.LineNumber, element.Source.Feed, element.Source.Tool));
            }

            output.Add(BuildOffsetMove(element, elementStart));
            current = output[^1].End;
            contourIndex++;
        }

        // Saída (G40): do último ponto compensado até o ponto programado
        if (exitIndex >= 0)
        {
            Move exit = moves[exitIndex];
            output.Add(exit.IsArc ? exit : exit with { Start = current });
        }
    }

    /// <summary>Calcula o início, fim e raio compensados do elemento. False se o arco ficaria com raio ≤ 0.</summary>
    private static bool Offset(Element element, int side, double r)
    {
        Move m = element.Source;
        if (!element.IsArc)
        {
            (double dx, double dy, double length) = Direction(m.Start, m.End);
            if (length < Epsilon)
            {
                element.Start = m.Start;
                element.End = m.End;
                return true;
            }

            // Normal à esquerda (−dy, dx); G42 usa a direita
            double nx = -dy * side * r;
            double ny = dx * side * r;
            element.Start = new Point3(m.Start.X + nx, m.Start.Y + ny, m.Start.Z);
            element.End = new Point3(m.End.X + nx, m.End.Y + ny, m.End.Z);
            return true;
        }

        // Arco: G41 em arco anti-horário fica do lado do centro (raio diminui), e vice-versa
        bool towardsCenter = (side > 0) == element.CounterClockwise;
        double startRadius = m.StartRadius + (towardsCenter ? -r : r);
        double endRadius = m.EndRadius + (towardsCenter ? -r : r);
        if (startRadius <= Epsilon || endRadius <= Epsilon)
        {
            return false;
        }

        element.Radius = startRadius;
        element.Start = PointOnCircle(element.CenterX, element.CenterY, m.Start, m.StartRadius, startRadius);
        element.End = PointOnCircle(element.CenterX, element.CenterY, m.End, m.EndRadius, endRadius);
        return true;
    }

    /// <summary>
    /// Ligação entre dois elementos compensados: nada (tangentes), arco em volta do canto
    /// (canto externo) ou aparo na interseção (canto interno).
    /// </summary>
    private static List<Move>? Join(Element previous, Element next, int side, double r, Action<int, string> warn)
    {
        if (DistanceXY(previous.End, next.Start) < 1e-6)
        {
            return null;
        }

        (double ax, double ay) = TangentAtEnd(previous);
        (double bx, double by) = TangentAtStart(next);
        double turn = ax * by - ay * bx; // > 0: vira à esquerda
        bool outside = side > 0 ? turn < 0 : turn > 0;

        Point3 corner = previous.Source.End;
        if (outside || Math.Abs(turn) < 1e-9)
        {
            // Arco de raio r em volta do canto programado, ligando as duas extremidades
            bool counterClockwise = side < 0; // G41 contorna cantos externos no sentido horário
            Move arc = ArcMove(previous.End, next.Start, corner.X, corner.Y, counterClockwise, next.Source);
            return [arc];
        }

        // Canto interno: interseção dos dois elementos compensados
        if (Intersect(previous, next, corner) is { } point)
        {
            previous.End = new Point3(point.X, point.Y, previous.End.Z);
            next.Start = new Point3(point.X, point.Y, next.Start.Z);
            return null;
        }

        warn(next.Source.LineNumber, "Canto interno sem interseção com o raio da ferramenta (possível interferência).");
        return [new Move(previous.End, next.Start, MotionMode.Linear, next.Source.LineNumber, next.Source.Feed, next.Source.Tool)];
    }

    private static Move BuildOffsetMove(Element element, Point3 start)
    {
        Move m = element.Source;
        var end = new Point3(element.End.X, element.End.Y, m.End.Z);
        if (!element.IsArc)
        {
            return m with { Start = start, End = end };
        }

        return ArcMove(start, end, element.CenterX, element.CenterY, element.CounterClockwise, m,
            fullCircle: Math.Abs(m.Sweep) > 2 * Math.PI - 1e-6);
    }

    /// <summary>Monta um arco no plano XY de "from" até "to" em volta de (cx, cy).</summary>
    private static Move ArcMove(Point3 from, Point3 to, double cx, double cy, bool counterClockwise, Move template, bool fullCircle = false)
    {
        double startRadius = Math.Sqrt(Sqr(from.X - cx) + Sqr(from.Y - cy));
        double endRadius = Math.Sqrt(Sqr(to.X - cx) + Sqr(to.Y - cy));
        double startAngle = Math.Atan2(from.Y - cy, from.X - cx);
        double endAngle = Math.Atan2(to.Y - cy, to.X - cx);
        double sweep = endAngle - startAngle;

        // Varredura ~0 vira volta completa (só acontece em círculo completo)
        if (counterClockwise)
        {
            if (sweep < 1e-9) sweep += 2 * Math.PI;
        }
        else
        {
            if (sweep > -1e-9) sweep -= 2 * Math.PI;
        }

        if (!fullCircle && Math.Abs(Math.Abs(sweep) - 2 * Math.PI) < 1e-9)
        {
            sweep = 0;
        }

        return new Move(from, to, counterClockwise ? MotionMode.CounterClockwiseArc : MotionMode.ClockwiseArc,
            template.LineNumber, template.Feed, template.Tool)
        {
            Plane = WorkPlane.XY,
            CenterU = cx,
            CenterV = cy,
            StartRadius = startRadius,
            EndRadius = endRadius,
            StartAngle = startAngle,
            Sweep = sweep,
        };
    }

    // ───────────────────────── Geometria ─────────────────────────

    private static (double X, double Y, double Length) Direction(Point3 from, Point3 to)
    {
        double dx = to.X - from.X;
        double dy = to.Y - from.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        return length < Epsilon ? (0, 0, 0) : (dx / length, dy / length, length);
    }

    private static (double X, double Y) TangentAtStart(Element e) => Tangent(e, e.Source.Start, atStart: true);

    private static (double X, double Y) TangentAtEnd(Element e) => Tangent(e, e.Source.End, atStart: false);

    private static (double X, double Y) Tangent(Element e, Point3 point, bool atStart)
    {
        if (!e.IsArc)
        {
            (double x, double y, _) = Direction(e.Source.Start, e.Source.End);
            return (x, y);
        }

        double rx = point.X - e.CenterX;
        double ry = point.Y - e.CenterY;
        double length = Math.Sqrt(rx * rx + ry * ry);
        _ = atStart;
        return e.CounterClockwise ? (-ry / length, rx / length) : (ry / length, -rx / length);
    }

    private static Point3 PointOnCircle(double cx, double cy, Point3 original, double originalRadius, double newRadius)
    {
        double scale = newRadius / originalRadius;
        return new Point3(cx + (original.X - cx) * scale, cy + (original.Y - cy) * scale, original.Z);
    }

    /// <summary>Interseção entre dois elementos compensados (reta/arco), a mais próxima do canto programado.</summary>
    private static (double X, double Y)? Intersect(Element a, Element b, Point3 corner)
    {
        var candidates = new List<(double X, double Y)>();

        if (!a.IsArc && !b.IsArc)
        {
            if (LineLine(a.Start, a.End, b.Start, b.End) is { } p) candidates.Add(p);
        }
        else if (a.IsArc && b.IsArc)
        {
            candidates.AddRange(CircleCircle(a.CenterX, a.CenterY, a.Radius, b.CenterX, b.CenterY, b.Radius));
        }
        else
        {
            Element line = a.IsArc ? b : a;
            Element circle = a.IsArc ? a : b;
            candidates.AddRange(LineCircle(line.Start, line.End, circle.CenterX, circle.CenterY, circle.Radius));
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates.MinBy(p => Sqr(p.X - corner.X) + Sqr(p.Y - corner.Y));
    }

    private static (double X, double Y)? LineLine(Point3 p1, Point3 p2, Point3 p3, Point3 p4)
    {
        double d = (p1.X - p2.X) * (p3.Y - p4.Y) - (p1.Y - p2.Y) * (p3.X - p4.X);
        if (Math.Abs(d) < 1e-12)
        {
            return null;
        }

        double t = ((p1.X - p3.X) * (p3.Y - p4.Y) - (p1.Y - p3.Y) * (p3.X - p4.X)) / d;
        return (p1.X + t * (p2.X - p1.X), p1.Y + t * (p2.Y - p1.Y));
    }

    private static IEnumerable<(double X, double Y)> LineCircle(Point3 p1, Point3 p2, double cx, double cy, double r)
    {
        double dx = p2.X - p1.X;
        double dy = p2.Y - p1.Y;
        double fx = p1.X - cx;
        double fy = p1.Y - cy;
        double a = dx * dx + dy * dy;
        double b = 2 * (fx * dx + fy * dy);
        double c = fx * fx + fy * fy - r * r;
        double discriminant = b * b - 4 * a * c;
        if (a < 1e-12 || discriminant < 0)
        {
            yield break;
        }

        double root = Math.Sqrt(discriminant);
        foreach (double t in new[] { (-b - root) / (2 * a), (-b + root) / (2 * a) })
        {
            yield return (p1.X + t * dx, p1.Y + t * dy);
        }
    }

    private static IEnumerable<(double X, double Y)> CircleCircle(double x0, double y0, double r0, double x1, double y1, double r1)
    {
        double dx = x1 - x0;
        double dy = y1 - y0;
        double d = Math.Sqrt(dx * dx + dy * dy);
        if (d < 1e-12 || d > r0 + r1 || d < Math.Abs(r0 - r1))
        {
            yield break;
        }

        double a = (r0 * r0 - r1 * r1 + d * d) / (2 * d);
        double h = Math.Sqrt(Math.Max(0, r0 * r0 - a * a));
        double mx = x0 + a * dx / d;
        double my = y0 + a * dy / d;
        yield return (mx + h * dy / d, my - h * dx / d);
        yield return (mx - h * dy / d, my + h * dx / d);
    }

    private static double DistanceXY(Point3 a, Point3 b) => Math.Sqrt(Sqr(a.X - b.X) + Sqr(a.Y - b.Y));

    private static double Sqr(double v) => v * v;
}
