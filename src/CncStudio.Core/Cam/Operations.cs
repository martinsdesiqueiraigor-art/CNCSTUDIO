namespace CncStudio.Core.Cam;

public enum ContourSide { Outside, Inside, OnLine }

/// <summary>
/// Parâmetros do contorno. <see cref="Climb"/>: concordante (padrão). <see cref="ControllerComp"/>: o programa segue o desenho e o
/// controle compensa o raio (G41/G42); desligado, o CAM já desloca o percurso. <see cref="LeadRadius"/>: raio do arco de entrada e saída.
/// </summary>
public sealed record ContourParams(ContourSide Side, bool Climb = true, bool ControllerComp = false, double Allowance = 0, double LeadRadius = 3);

public sealed record PocketParams(double StepOverPercent = 60, double FinishAllowance = 0, double LeadRadius = 2);

/// <summary>Operações de fresa 2,5D. Todas devolvem movimentos neutros; o pós-processador escreve o G-code.</summary>
public static class CamOperations
{
    private static OperationResult Fail(string message) => new(null, message);

    private static Point2 LeftOf(Point2 t) => new(-t.Y, t.X);

    private static Point2 Rot90(Point2 v, bool ccw) => ccw ? new Point2(-v.Y, v.X) : new Point2(v.Y, -v.X);

    private static ToolMove Rapid(Point2 p, double z) => new(MoveKind.Rapid, p.X, p.Y, z);

    private static ToolMove Feed(Point2 p, double z, double feed, CutterComp? comp = null) => new(MoveKind.Feed, p.X, p.Y, z, Feed: feed, Comp: comp);

    private static ToolMove Along(Segment2 s, double z, double feed) =>
        s.IsArc
            ? new ToolMove(s.Ccw ? MoveKind.ArcCcw : MoveKind.ArcCw, s.End.X, s.End.Y, z, s.Center, feed)
            : Feed(s.End, z, feed);

    // ---------------------------------------------------------------- Faceamento

    /// <summary>Faceamento em zigue-zague de um retângulo (canto inferior esquerdo x,y), entrando e saindo por fora da peça.</summary>
    public static OperationResult Facing(CamTool tool, CamLevels levels, double x, double y, double width, double height, double stepOverPercent)
    {
        string? error = tool.Validate() ?? levels.Validate();
        if (error is not null)
        {
            return Fail(error);
        }

        if (width <= 0 || height <= 0)
        {
            return Fail("O retângulo precisa ter largura e altura maiores que zero.");
        }

        if (stepOverPercent is <= 0 or > 100)
        {
            return Fail("O passo lateral precisa ficar entre 0 e 100% do diâmetro.");
        }

        var warnings = new List<string>();
        double r = tool.Radius;
        double yLow = y + r, yHigh = y + height - r;
        if (yHigh < yLow)
        {
            yLow = yHigh = y + (height / 2);
            warnings.Add("A ferramenta é mais larga que a peça: ela passa uma vez pelo meio e sai dos dois lados.");
        }

        double step = tool.Diameter * stepOverPercent / 100;
        int rows = yHigh - yLow < 1e-9 ? 1 : (int)Math.Ceiling(((yHigh - yLow) / step) - 1e-9) + 1;
        double xIn = x - r - 2, xOut = x + width + r + 2;

        var moves = new List<ToolMove> { Rapid(new Point2(xIn, yLow), levels.SafeZ) };
        foreach (double z in levels.Depths())
        {
            bool leftToRight = true;
            moves.Add(Rapid(new Point2(xIn, yLow), levels.ClearanceZ));
            moves.Add(Feed(new Point2(xIn, yLow), z, tool.FeedZ));
            for (int i = 0; i < rows; i++)
            {
                double yy = rows == 1 ? yLow : yLow + ((yHigh - yLow) * i / (rows - 1));
                if (i > 0)
                {
                    moves.Add(Feed(new Point2(leftToRight ? xIn : xOut, yy), z, tool.FeedXY));
                }

                moves.Add(Feed(new Point2(leftToRight ? xOut : xIn, yy), z, tool.FeedXY));
                leftToRight = !leftToRight;
            }

            ToolMove last = moves[^1];
            moves.Add(Rapid(last.XY, levels.ClearanceZ));
        }

        ToolMove end = moves[^1];
        moves.Add(Rapid(end.XY, levels.SafeZ));
        return new OperationResult(new CamOperation("Faceamento", tool, moves, null, warnings), null);
    }

    // ---------------------------------------------------------------- Contorno

    public static OperationResult Contour(Contour part, CamTool tool, CamLevels levels, ContourParams p)
    {
        string? error = tool.Validate() ?? levels.Validate();
        if (error is not null)
        {
            return Fail(error);
        }

        var warnings = new List<string>();
        var moves = new List<ToolMove>();
        error = AppendContour(moves, warnings, part, tool, levels, p, levels.Depths(), moveToSafe: true);
        if (error is not null)
        {
            return Fail(error);
        }

        if (p.Side == ContourSide.Inside)
        {
            warnings.Add("Contorno por dentro: a ferramenta desce na vertical dentro do desenho. Faça um pré-furo ou use o bolsão antes.");
        }

        ToolMove end = moves[^1];
        moves.Add(Rapid(end.XY, levels.SafeZ));
        return new OperationResult(new CamOperation("Contorno", tool, moves, null, warnings), null);
    }

    /// <summary>Gera os passes de contorno nas profundidades dadas. Devolve o erro, se houver.</summary>
    private static string? AppendContour(List<ToolMove> moves, List<string> warnings, Contour part, CamTool tool, CamLevels levels, ContourParams p, IReadOnlyList<double> depths, bool moveToSafe)
    {
        if (!part.IsClosed)
        {
            return "O contorno não está fechado.";
        }

        double r = tool.Radius;
        bool comp = p.ControllerComp && p.Side != ContourSide.OnLine;
        if (p.LeadRadius <= 0)
        {
            return "O raio de entrada e saída precisa ser maior que zero.";
        }

        if (comp && p.LeadRadius <= r)
        {
            return "Com compensação no controle, o raio de entrada precisa ser maior que o raio da ferramenta.";
        }

        if (comp && p.Allowance > 0)
        {
            return "O sobremetal só vale com o percurso já deslocado (sem compensação no controle).";
        }

        // Sentido: concordante = ferramenta à esquerda; por fora gira no sentido horário, por dentro no anti-horário
        bool wantCcw = p.Side switch
        {
            ContourSide.Outside => !p.Climb,
            ContourSide.Inside => p.Climb,
            _ => part.IsCounterClockwise,
        };
        Contour oriented = part.IsCounterClockwise == wantCcw ? part : part.Reversed();

        Contour path = oriented;
        if (!comp && p.Side != ContourSide.OnLine)
        {
            OffsetResult off = oriented.OffsetLeft(p.Climb ? r + p.Allowance : -(r + p.Allowance));
            if (!off.Ok)
            {
                return off.Error;
            }

            path = off.Contour!;
        }

        path = path.StartingAt(path.MidpointOfLongest());

        bool toolLeft = p.Side == ContourSide.OnLine || p.Climb;
        Point2 p0 = path.Segments[0].Start;
        Point2 t = path.Segments[0].StartTangent;
        Point2 center = p0 + (LeftOf(t) * (toolLeft ? p.LeadRadius : -p.LeadRadius));
        bool arcCcw = toolLeft;
        Point2 a = center + Rot90(p0 - center, !arcCcw);
        Point2 aOut = center + Rot90(p0 - center, arcCcw);
        MoveKind arcKind = arcCcw ? MoveKind.ArcCcw : MoveKind.ArcCw;
        double lineLen = r + 1;
        Point2 tangentIn = Rot90(a - center, arcCcw).Normalized();
        Point2 tangentOut = Rot90(aOut - center, arcCcw).Normalized();
        Point2 b = a - (tangentIn * lineLen);
        Point2 bOut = aOut + (tangentOut * lineLen);
        CutterComp onComp = toolLeft ? CutterComp.Left : CutterComp.Right;

        if (moveToSafe)
        {
            moves.Add(Rapid(comp ? b : a, levels.SafeZ));
        }

        foreach (double z in depths)
        {
            moves.Add(Rapid(comp ? b : a, levels.ClearanceZ));
            moves.Add(Feed(comp ? b : a, z, tool.FeedZ));
            if (comp)
            {
                moves.Add(Feed(a, z, tool.FeedXY, onComp));
            }

            moves.Add(new ToolMove(arcKind, p0.X, p0.Y, z, center, tool.FeedXY));
            foreach (Segment2 s in path.Segments)
            {
                moves.Add(Along(s, z, tool.FeedXY));
            }

            moves.Add(new ToolMove(arcKind, aOut.X, aOut.Y, z, center, tool.FeedXY));
            if (comp)
            {
                moves.Add(Feed(bOut, z, tool.FeedXY, CutterComp.Off));
            }

            moves.Add(Rapid(comp ? bOut : aOut, levels.ClearanceZ));
        }

        return null;
    }

    // ---------------------------------------------------------------- Bolsão

    /// <summary>Bolsão por anéis concêntricos (de fora para dentro), com entrada em rampa pelo primeiro anel. Sem ilhas.</summary>
    public static OperationResult Pocket(Contour boundary, CamTool tool, CamLevels levels, PocketParams p)
    {
        string? error = tool.Validate() ?? levels.Validate();
        if (error is not null)
        {
            return Fail(error);
        }

        if (!boundary.IsClosed)
        {
            return Fail("O contorno não está fechado.");
        }

        if (p.StepOverPercent is <= 0 or > 100)
        {
            return Fail("O passo lateral precisa ficar entre 0 e 100% do diâmetro.");
        }

        double r = tool.Radius;
        double step = tool.Diameter * p.StepOverPercent / 100;
        Contour ccw = boundary.IsCounterClockwise ? boundary : boundary.Reversed();

        OffsetResult first = ccw.OffsetOutside(-(r + p.FinishAllowance));
        if (!first.Ok)
        {
            return Fail("O bolsão não comporta a ferramenta: " + first.Error);
        }

        var rings = new List<Contour> { first.Contour!.StartingAt(first.Contour.MidpointOfLongest()) };
        Contour current = first.Contour;
        while (rings.Count < 500)
        {
            OffsetResult next = current.OffsetOutside(-step);
            if (!next.Ok)
            {
                next = current.OffsetOutside(-step / 2);
            }

            if (!next.Ok)
            {
                next = current.OffsetOutside(-step / 4);
            }

            if (!next.Ok || Math.Abs(next.Contour!.SignedArea) < 1e-3)
            {
                break;
            }

            current = next.Contour;
            rings.Add(current);
        }

        var warnings = new List<string>();
        var moves = new List<ToolMove> { Rapid(rings[0].Segments[0].Start, levels.SafeZ) };
        double previousZ = levels.TopZ;
        foreach (double z in levels.Depths())
        {
            Point2 start = rings[0].Segments[0].Start;
            moves.Add(Rapid(start, levels.ClearanceZ));
            moves.Add(Feed(start, previousZ, tool.FeedZ));

            // Rampa: desce ao longo do primeiro anel
            double perimeter = rings[0].Perimeter;
            if (previousZ - z > perimeter * 0.176 && !warnings.Any(w => w.StartsWith("Rampa", StringComparison.Ordinal)))
            {
                warnings.Add("Rampa muito inclinada (bolsão pequeno para o passe em Z): reduza o passe em Z.");
            }

            double travelled = 0;
            foreach (Segment2 s in rings[0].Segments)
            {
                travelled += s.Length;
                moves.Add(Along(s, previousZ + ((z - previousZ) * travelled / perimeter), tool.FeedXY));
            }

            foreach (Segment2 s in rings[0].Segments)
            {
                moves.Add(Along(s, z, tool.FeedXY));
            }

            Point2 here = rings[0].Segments[0].Start;
            for (int i = 1; i < rings.Count; i++)
            {
                Contour ring = rings[i].StartingAt(here);
                Point2 s0 = ring.Segments[0].Start;
                moves.Add(Feed(s0, z, tool.FeedXY));
                foreach (Segment2 s in ring.Segments)
                {
                    moves.Add(Along(s, z, tool.FeedXY));
                }

                here = s0;
            }

            moves.Add(Rapid(here, levels.ClearanceZ));
            previousZ = z;
        }

        if (p.FinishAllowance > 0)
        {
            var finish = new ContourParams(ContourSide.Inside, Climb: true, ControllerComp: false, Allowance: 0, LeadRadius: Math.Min(p.LeadRadius, r));
            error = AppendContour(moves, warnings, ccw, tool, levels, finish, [levels.BottomZ], moveToSafe: false);
            if (error is not null)
            {
                return Fail("Acabamento do bolsão: " + error);
            }
        }

        ToolMove end = moves[^1];
        moves.Add(Rapid(end.XY, levels.SafeZ));
        warnings.Add("Bolsão sem ilhas: se houver ilhas, use a simulação para conferir.");
        return new OperationResult(new CamOperation("Bolsão", tool, moves, null, warnings), null);
    }

    // ---------------------------------------------------------------- Furação

    /// <summary>Furos inteiros (círculos do desenho): centro e diâmetro.</summary>
    public static IReadOnlyList<DrillHole> HolesFromCircles(IEnumerable<Contour> contours)
    {
        var holes = new List<DrillHole>();
        foreach (Contour c in contours)
        {
            if (c.Segments.Count == 1 && c.Segments[0].IsArc && c.Segments[0].Start.DistanceTo(c.Segments[0].End) < 1e-6)
            {
                holes.Add(new DrillHole(c.Segments[0].Center, c.Segments[0].Radius * 2));
            }
        }

        return holes;
    }

    /// <summary>Ordena os pontos sempre indo ao mais próximo, a partir de <paramref name="from"/>.</summary>
    public static IReadOnlyList<Point2> OrderNearest(IEnumerable<Point2> points, Point2 from)
    {
        var pool = points.ToList();
        var result = new List<Point2>();
        Point2 current = from;
        while (pool.Count > 0)
        {
            int best = 0;
            for (int i = 1; i < pool.Count; i++)
            {
                if (pool[i].DistanceTo(current) < pool[best].DistanceTo(current))
                {
                    best = i;
                }
            }

            current = pool[best];
            result.Add(current);
            pool.RemoveAt(best);
        }

        return result;
    }

    /// <summary>
    /// Furação em ciclo fixo. <paramref name="topZ"/> = face da peça, <paramref name="bottomZ"/> = fundo (ponta), <paramref name="retractZ"/> = plano R.
    /// </summary>
    public static OperationResult Drilling(CamTool tool, double safeZ, IEnumerable<Point2> points, DrillCycleKind kind, double topZ, double bottomZ, double retractZ, double peck = 0, double dwellSeconds = 0)
    {
        string? error = tool.Validate();
        if (error is not null)
        {
            return Fail(error);
        }

        if (bottomZ >= topZ)
        {
            return Fail("O fundo precisa ficar abaixo do topo.");
        }

        if (retractZ < topZ || safeZ < retractZ)
        {
            return Fail("O plano R precisa ficar acima da face e abaixo da altura segura.");
        }

        if (kind is DrillCycleKind.Peck or DrillCycleKind.ChipBreak && peck <= 0)
        {
            return Fail("A bicada (Q) precisa ser maior que zero.");
        }

        if (kind == DrillCycleKind.Dwell && dwellSeconds <= 0)
        {
            return Fail("O tempo de permanência precisa ser maior que zero.");
        }

        IReadOnlyList<Point2> ordered = OrderNearest(points, new Point2(0, 0));
        if (ordered.Count == 0)
        {
            return Fail("Nenhum furo para fazer.");
        }

        var cycle = new DrillCycle(kind, ordered, topZ, bottomZ, retractZ, safeZ, peck, dwellSeconds, tool.FeedZ);
        return new OperationResult(new CamOperation("Furação", tool, [], cycle, []), null);
    }
}
