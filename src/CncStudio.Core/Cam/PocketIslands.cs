namespace CncStudio.Core.Cam;

public static partial class CamOperations
{
    /// <summary>Contornos de <paramref name="all"/> que ficam por inteiro dentro de <paramref name="boundary"/> (candidatos a ilha).</summary>
    public static IReadOnlyList<Contour> FindIslands(Contour boundary, IEnumerable<Contour> all)
    {
        List<Point2> outer = boundary.Sample();
        var islands = new List<Contour>();
        foreach (Contour c in all)
        {
            if (ReferenceEquals(c, boundary) || !c.IsClosed)
            {
                continue;
            }

            if (c.Sample().All(p => InsidePolygon(outer, p)))
            {
                islands.Add(c);
            }
        }

        return islands;
    }

    private static bool InsidePolygon(IReadOnlyList<Point2> poly, Point2 p)
    {
        bool inside = false;
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            Point2 a = poly[i], b = poly[j];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < ((b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y)) + a.X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    /// <summary>Faixas de X (ordenadas) em que a linha horizontal y está dentro do polígono.</summary>
    private static List<(double A, double B)> Spans(IReadOnlyList<Point2> poly, double y)
    {
        var xs = new List<double>();
        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            Point2 a = poly[i], b = poly[j];
            if ((a.Y > y) != (b.Y > y))
            {
                xs.Add(((b.X - a.X) * (y - a.Y) / (b.Y - a.Y)) + a.X);
            }
        }

        xs.Sort();
        var spans = new List<(double, double)>();
        for (int k = 0; k + 1 < xs.Count; k += 2)
        {
            spans.Add((xs[k], xs[k + 1]));
        }

        return spans;
    }

    private static List<(double A, double B)> Subtract(List<(double A, double B)> from, List<(double A, double B)> cut)
    {
        var result = new List<(double A, double B)>();
        foreach ((double a, double b) in from)
        {
            double start = a;
            foreach ((double c, double d) in cut.OrderBy(x => x.A))
            {
                if (d <= start || c >= b)
                {
                    continue;
                }

                if (c > start)
                {
                    result.Add((start, c));
                }

                start = Math.Max(start, d);
            }

            if (start < b)
            {
                result.Add((start, b));
            }
        }

        return result;
    }

    /// <summary>
    /// Bolsão com ilhas: contorna a parede e cada ilha, e limpa o resto em passes paralelos (X) que desviam das ilhas.
    /// As ilhas precisam ficar por inteiro dentro do bolsão e longe o bastante da parede e umas das outras para a ferramenta passar.
    /// </summary>
    public static OperationResult PocketWithIslands(Contour boundary, IReadOnlyList<Contour> islands, CamTool tool, CamLevels levels, PocketParams p)
    {
        if (islands.Count == 0)
        {
            return Pocket(boundary, tool, levels, p);
        }

        string? error = tool.Validate() ?? levels.Validate();
        if (error is not null)
        {
            return Fail(error);
        }

        if (!boundary.IsClosed || islands.Any(i => !i.IsClosed))
        {
            return Fail("O contorno ou uma ilha não está fechado.");
        }

        if (p.StepOverPercent is <= 0 or > 100)
        {
            return Fail("O passo lateral precisa ficar entre 0 e 100% do diâmetro.");
        }

        double r = tool.Radius;
        double clear = r + p.FinishAllowance;
        double step = tool.Diameter * p.StepOverPercent / 100;
        OffsetResult wall = boundary.OffsetOutside(-clear);
        if (!wall.Ok)
        {
            return Fail("O bolsão não comporta a ferramenta: " + wall.Error);
        }

        List<Point2> wallPoly = wall.Contour!.Sample();
        var grown = new List<Contour>();
        var grownPolys = new List<List<Point2>>();
        for (int i = 0; i < islands.Count; i++)
        {
            OffsetResult g = islands[i].OffsetOutside(clear);
            if (!g.Ok)
            {
                return Fail($"Ilha {i + 1}: não foi possível contornar com esta ferramenta: " + g.Error);
            }

            List<Point2> poly = g.Contour!.Sample();
            if (!poly.All(pt => InsidePolygon(wallPoly, pt)))
            {
                return Fail($"Ilha {i + 1} perto demais da parede (ou fora do bolsão): a ferramenta não passa entre as duas.");
            }

            grown.Add(g.Contour);
            grownPolys.Add(poly);
        }

        for (int i = 0; i < grownPolys.Count; i++)
        {
            for (int j = 0; j < grownPolys.Count; j++)
            {
                if (i != j && grownPolys[i].Any(pt => InsidePolygon(grownPolys[j], pt)))
                {
                    return Fail($"Ilhas {i + 1} e {j + 1} muito próximas: a ferramenta não passa entre elas.");
                }
            }
        }

        // Anéis de contorno (parede e ilhas), começando num ponto bom de cada um
        var rings = new List<Contour> { wall.Contour.StartingAt(wall.Contour.MidpointOfLongest()) };
        rings.AddRange(grown.Select(g => g.StartingAt(g.MidpointOfLongest())));

        // Passes paralelos (linhas horizontais) fora das ilhas
        var lines = new List<(Point2 From, Point2 To)>();
        double minY = wallPoly.Min(pt => pt.Y), maxY = wallPoly.Max(pt => pt.Y);
        bool forward = true;
        for (double y = minY + step; y < maxY - 1e-6; y += step)
        {
            List<(double A, double B)> spans = Subtract(Spans(wallPoly, y), grownPolys.SelectMany(poly => Spans(poly, y)).ToList());
            if (!forward)
            {
                spans.Reverse();
            }

            foreach ((double a, double b) in spans)
            {
                if (b - a < 0.01)
                {
                    continue;
                }

                lines.Add(forward ? (new Point2(a, y), new Point2(b, y)) : (new Point2(b, y), new Point2(a, y)));
            }

            forward = !forward;
        }

        var warnings = new List<string>();
        var moves = new List<ToolMove> { Rapid(rings[0].Segments[0].Start, levels.SafeZ) };
        double previousZ = levels.TopZ;
        foreach (double z in levels.Depths())
        {
            double dz = previousZ - z;
            foreach (Contour ring in rings)
            {
                Point2 start = ring.Segments[0].Start;
                moves.Add(Rapid(start, levels.ClearanceZ));
                moves.Add(Feed(start, previousZ, tool.FeedZ));
                double perimeter = ring.Perimeter;
                if (dz > perimeter * 0.176 && !warnings.Any(w => w.StartsWith("Rampa", StringComparison.Ordinal)))
                {
                    warnings.Add("Rampa muito inclinada (contorno pequeno para o passe em Z): reduza o passe em Z.");
                }

                double travelled = 0;
                foreach (Segment2 s in ring.Segments)
                {
                    travelled += s.Length;
                    moves.Add(Along(s, previousZ - (dz * travelled / perimeter), tool.FeedXY));
                }

                foreach (Segment2 s in ring.Segments)
                {
                    moves.Add(Along(s, z, tool.FeedXY));
                }
            }

            foreach ((Point2 from, Point2 to) in lines)
            {
                moves.Add(Rapid(from, levels.ClearanceZ));
                moves.Add(Feed(from, previousZ, tool.FeedZ));
                double length = from.DistanceTo(to);
                int legs = Math.Clamp((int)Math.Ceiling(dz / (length * 0.176)), 1, 40);
                if (legs > 1 && !warnings.Any(w => w.StartsWith("Rampa em zigue-zague", StringComparison.Ordinal)))
                {
                    warnings.Add("Rampa em zigue-zague nos passes curtos: reduza o passe em Z se preferir descer mais suave.");
                }

                // Desce em zigue-zague sobre a própria linha e termina em z no fim dela
                bool atEnd = false;
                for (int leg = 1; leg <= legs; leg++)
                {
                    double legZ = previousZ - (dz * leg / legs);
                    moves.Add(Feed(atEnd ? from : to, legZ, tool.FeedXY));
                    atEnd = !atEnd;
                }

                // Termina em z numa ponta; corta a linha inteira em z para limpar a cunha da rampa
                moves.Add(Feed(atEnd ? from : to, z, tool.FeedXY));
            }

            moves.Add(Rapid(moves[^1].XY, levels.ClearanceZ));
            previousZ = z;
        }

        if (p.FinishAllowance > 0)
        {
            double lead = Math.Min(p.LeadRadius, r);
            var inside = new ContourParams(ContourSide.Inside, Climb: true, ControllerComp: false, Allowance: 0, LeadRadius: lead);
            error = AppendContour(moves, warnings, boundary.IsCounterClockwise ? boundary : boundary.Reversed(), tool, levels, inside, [levels.BottomZ], moveToSafe: false);
            if (error is not null)
            {
                return Fail("Acabamento do bolsão: " + error);
            }

            var outside = new ContourParams(ContourSide.Outside, Climb: true, ControllerComp: false, Allowance: 0, LeadRadius: lead);
            for (int i = 0; i < islands.Count; i++)
            {
                error = AppendContour(moves, warnings, islands[i], tool, levels, outside, [levels.BottomZ], moveToSafe: false);
                if (error is not null)
                {
                    return Fail($"Acabamento da ilha {i + 1}: {error}");
                }
            }
        }

        moves.Add(Rapid(moves[^1].XY, levels.SafeZ));
        warnings.Add("Bolsão com ilhas: confira na simulação antes de cortar.");
        return new OperationResult(new CamOperation("Bolsão com ilhas", tool, moves, null, warnings), null);
    }
}
