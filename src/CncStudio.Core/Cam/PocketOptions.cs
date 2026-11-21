namespace CncStudio.Core.Cam;

/// <summary>
/// Como a ferramenta desce no bolsão (OPS-A-01). <see cref="Default"/> é o que o bolsão sempre fez: rampa de uma volta pelo primeiro
/// anel, com a inclinação que a volta der. Os outros são escolha do usuário, com os números digitados.
/// </summary>
public enum PocketEntry
{
    /// <summary>Rampa de uma volta pelo primeiro anel (como antes).</summary>
    Default,

    /// <summary>Rampa com ângulo máximo digitado: dá quantas voltas inteiras forem precisas para não passar do ângulo.</summary>
    Ramp,

    /// <summary>Hélice com diâmetro e passo digitados, num ponto livre do bolsão.</summary>
    Helix,

    /// <summary>Mergulho reto no início do anel (a ferramenta precisa cortar no centro).</summary>
    Plunge,

    /// <summary>Mergulho num furo já feito por uma Furação anterior que cai dentro do bolsão.</summary>
    PreDrill,
}

/// <summary>Ordem em que o bolsão limpa a área (OPS-A-01).</summary>
public enum PocketStrategy
{
    /// <summary>Anéis paralelos ao contorno, de fora para dentro (como antes).</summary>
    Rings,

    /// <summary>Passes retos em X, indo e voltando, mais a volta na parede.</summary>
    ZigZag,

    /// <summary>Os mesmos anéis, de dentro para fora: a parede fica por último.</summary>
    Parallel,

    /// <summary>Espiral contínua de dentro para fora, terminando numa volta na parede.</summary>
    Spiral,

    /// <summary>
    /// CAM-ADAPT-01: desbaste adaptativo (Fusion 2D Adaptive): largura de corte nunca acima da carga ótima, caminho suave,
    /// ligações por baixo e volta na parede no fim de cada nível. Na tela é a operação "Desbaste adaptativo".
    /// </summary>
    Adaptive,
}

public static partial class CamOperations
{
    private const int SpiralSamples = 60;

    /// <summary>Confere os números das escolhas novas do bolsão. Null = bom.</summary>
    internal static string? PocketOptionsError(PocketParams p, CamTool tool)
    {
        if (p.Strategy == PocketStrategy.Adaptive && AdaptiveError(p, tool) is { } adaptive)
        {
            return adaptive;
        }

        switch (p.Entry)
        {
            case PocketEntry.Ramp when !(p.RampAngle > 0 && p.RampAngle < 90):
                return "O ângulo da rampa precisa ficar entre 0 e 90°.";
            case PocketEntry.Helix when p.HelixDiameter <= tool.Diameter:
                return "O diâmetro da hélice precisa ser maior que o diâmetro da ferramenta.";
            case PocketEntry.Helix when p.HelixPitch <= 0:
                return "O passo da hélice precisa ser maior que zero.";
        }

        return p.Strategy is PocketStrategy.Parallel or PocketStrategy.Spiral && p.Entry == PocketEntry.Default
            ? "Esta estratégia começa de dentro: escolha a entrada (rampa, hélice, mergulho reto ou pré-furo)."
            : null;
    }

    /// <summary>Bolsão por anéis com entrada e/ou estratégia escolhidas (anéis, de dentro para fora, ou espiral).</summary>
    private static OperationResult PocketWithOptions(Contour ccw, List<Contour> rings, CamTool tool, CamLevels levels, PocketParams p)
    {
        bool insideOut = p.Strategy is PocketStrategy.Parallel or PocketStrategy.Spiral;
        List<Contour> seq = insideOut ? [.. Enumerable.Reverse(rings)] : rings;
        var warnings = new List<string>();
        var moves = new List<ToolMove> { Rapid(seq[0].Segments[0].Start, levels.SafeZ) };
        double previousZ = levels.TopZ;
        foreach (double z in levels.Depths())
        {
            EntryDescent(moves, warnings, rings[0], seq[0], previousZ, z, tool, levels, p, islandsPresent: false);
            Point2 here = seq[0].Segments[0].Start;
            if (p.Strategy == PocketStrategy.Spiral)
            {
                SpiralPass(moves, seq, z, tool, ref here);
            }
            else
            {
                foreach (Segment2 s in seq[0].Segments)
                {
                    moves.Add(Along(s, z, tool.FeedXY));
                }

                for (int i = 1; i < seq.Count; i++)
                {
                    Contour ring = seq[i].StartingAt(here);
                    Point2 s0 = ring.Segments[0].Start;
                    moves.Add(Feed(s0, z, tool.FeedXY));
                    foreach (Segment2 s in ring.Segments)
                    {
                        moves.Add(Along(s, z, tool.FeedXY));
                    }

                    here = s0;
                }
            }

            moves.Add(Rapid(here, levels.ClearanceZ));
            previousZ = z;
        }

        if (p.FinishAllowance > 0)
        {
            var finish = new ContourParams(ContourSide.Inside, Climb: true, ControllerComp: false, Allowance: 0, LeadRadius: Math.Min(p.LeadRadius, tool.Radius));
            string? error = AppendContour(moves, warnings, ccw, tool, levels, finish, [levels.BottomZ], moveToSafe: false);
            if (error is not null)
            {
                return Fail("Acabamento do bolsão: " + error);
            }
        }

        moves.Add(Rapid(moves[^1].XY, levels.SafeZ));
        warnings.Add("Bolsão sem ilhas: se houver ilhas, use a simulação para conferir.");
        return new OperationResult(new CamOperation("Bolsão", tool, moves, null, warnings), null);
    }

    /// <summary>
    /// Espiral de dentro para fora: entre um anel e o seguinte a trajetória abre aos poucos (interpolação por comprimento), sem levantar,
    /// e termina numa volta exata no anel de fora. Termina com a ferramenta no começo dessa volta (<paramref name="here"/> = onde parou).
    /// </summary>
    private static void SpiralPass(List<ToolMove> moves, List<Contour> inToOut, double z, CamTool tool, ref Point2 here)
    {
        Point2 start = inToOut[0].Segments[0].Start;
        List<Point2> current = Resample(inToOut[0], SpiralSamples);
        for (int k = 1; k < inToOut.Count; k++)
        {
            Contour nextRing = inToOut[k].StartingAt(current[0]);
            List<Point2> next = Resample(nextRing, SpiralSamples);
            for (int i = 1; i <= SpiralSamples; i++)
            {
                double u = (double)i / SpiralSamples;
                Point2 a = current[i % SpiralSamples], b = next[i % SpiralSamples];
                moves.Add(Feed(new Point2(a.X + ((b.X - a.X) * u), a.Y + ((b.Y - a.Y) * u)), z, tool.FeedXY));
            }

            current = next;
            start = current[0];
        }

        // volta exata no anel de fora, a partir de onde a espiral chegou
        Contour last = inToOut[^1].StartingAt(start);
        moves.Add(Feed(last.Segments[0].Start, z, tool.FeedXY));
        foreach (Segment2 s in last.Segments)
        {
            moves.Add(Along(s, z, tool.FeedXY));
        }

        here = last.Segments[0].Start;
    }

    /// <summary>n pontos igualmente espaçados por comprimento ao longo do contorno fechado, a partir do começo dele.</summary>
    private static List<Point2> Resample(Contour c, int n)
    {
        List<Point2> poly = c.Sample();
        if (poly.Count > 1 && poly[0].DistanceTo(poly[^1]) < 1e-9)
        {
            poly.RemoveAt(poly.Count - 1);
        }

        poly.Add(poly[0]);
        var cumulative = new double[poly.Count];
        for (int i = 1; i < poly.Count; i++)
        {
            cumulative[i] = cumulative[i - 1] + poly[i - 1].DistanceTo(poly[i]);
        }

        double total = cumulative[^1];
        var result = new List<Point2>(n);
        int seg = 1;
        for (int i = 0; i < n; i++)
        {
            double target = total * i / n;
            while (seg < poly.Count - 1 && cumulative[seg] < target)
            {
                seg++;
            }

            double span = cumulative[seg] - cumulative[seg - 1];
            double t = span < 1e-12 ? 0 : (target - cumulative[seg - 1]) / span;
            Point2 a = poly[seg - 1], b = poly[seg];
            result.Add(new Point2(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t)));
        }

        return result;
    }

    // ---------------------------------------------------------------- entradas

    /// <summary>
    /// Desce da altura <paramref name="fromZ"/> até <paramref name="toZ"/> começando em <paramref name="startRing"/> (a ferramenta termina
    /// no começo dele, já em <paramref name="toZ"/>). <paramref name="region"/> é o anel de fora (onde o centro da ferramenta pode andar).
    /// </summary>
    private static void EntryDescent(List<ToolMove> moves, List<string> warnings, Contour region, Contour startRing, double fromZ, double toZ,
        CamTool tool, CamLevels levels, PocketParams p, bool islandsPresent, bool outerRing = true)
    {
        Point2 start = startRing.Segments[0].Start;
        double dz = fromZ - toZ;
        switch (p.Entry)
        {
            case PocketEntry.Ramp:
                {
                    int laps = Math.Max(1, (int)Math.Ceiling((dz / (Math.Tan(p.RampAngle * Math.PI / 180) * startRing.Perimeter)) - 1e-9));
                    if (laps > 3)
                    {
                        AddOnce(warnings, "Rampa", $"Rampa de {p.RampAngle:0.##}°: o anel é curto e a descida dá {laps} voltas. Considere hélice ou mergulho reto.");
                    }

                    RingRamp(moves, startRing, fromZ, toZ, laps, tool, levels);
                    return;
                }

            case PocketEntry.Helix when !islandsPresent && TryHelix(moves, region, start, fromZ, toZ, tool, levels, p):
                return;
            case PocketEntry.Helix:
                AddOnce(warnings, "Hélice", islandsPresent
                    ? "Hélice não vale no bolsão com ilhas: a descida é em rampa de uma volta pelo contorno."
                    : "A hélice não cabe no bolsão (ou o caminho até o primeiro anel sai da área): a descida é em rampa de uma volta pelo contorno.");
                RingRamp(moves, startRing, fromZ, toZ, 1, tool, levels);
                return;
            case PocketEntry.PreDrill when outerRing && p.PreDrillPoint is { } hole && PreDrillFits(region, hole, start):
                moves.Add(Rapid(hole, levels.ClearanceZ));
                moves.Add(Feed(hole, fromZ, tool.FeedZ));
                moves.Add(Feed(hole, toZ, tool.FeedZ));
                if (hole.DistanceTo(start) > 1e-6)
                {
                    moves.Add(Feed(start, toZ, tool.FeedXY));
                }

                return;
            case PocketEntry.PreDrill when outerRing:
                AddOnce(warnings, "Pré-furo", "Pré-furo: nenhuma Furação anterior cai dentro do bolsão (ou o caminho até o anel sai da área): a descida é em mergulho reto no início.");
                break;
            case PocketEntry.Default:
                RingRamp(moves, startRing, fromZ, toZ, 1, tool, levels);
                return;
        }

        moves.Add(Rapid(start, levels.ClearanceZ));
        moves.Add(Feed(start, fromZ, tool.FeedZ));
        moves.Add(Feed(start, toZ, tool.FeedZ));
    }

    private static void AddOnce(List<string> warnings, string prefix, string text)
    {
        if (!warnings.Any(w => w.StartsWith(prefix, StringComparison.Ordinal)))
        {
            warnings.Add(text);
        }
    }

    /// <summary>Desce em rampa dando voltas inteiras no anel; chega em <paramref name="toZ"/> no começo dele.</summary>
    private static void RingRamp(List<ToolMove> moves, Contour ring, double fromZ, double toZ, int laps, CamTool tool, CamLevels levels)
    {
        Point2 start = ring.Segments[0].Start;
        moves.Add(Rapid(start, levels.ClearanceZ));
        moves.Add(Feed(start, fromZ, tool.FeedZ));
        double total = ring.Perimeter * laps;
        double travelled = 0;
        for (int lap = 0; lap < laps; lap++)
        {
            foreach (Segment2 s in ring.Segments)
            {
                travelled += s.Length;
                moves.Add(Along(s, fromZ - ((fromZ - toZ) * travelled / total), tool.FeedXY));
            }
        }
    }

    /// <summary>Ponto dentro do polígono (para a geração do trabalho achar o pré-furo).</summary>
    internal static bool PointInside(IReadOnlyList<Point2> polygon, Point2 p) => InsidePolygon(polygon, p);

    private static bool PreDrillFits(Contour region, Point2 hole, Point2 start)
    {
        List<Point2> poly = region.Sample();
        return InsidePolygon(poly, hole) && SegmentInside(poly, hole, start);
    }

    private static bool SegmentInside(IReadOnlyList<Point2> poly, Point2 a, Point2 b)
    {
        for (int i = 0; i <= 16; i++)
        {
            double t = i / 16.0;
            if (!InsidePolygon(poly, new Point2(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t))))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Hélice num ponto livre do bolsão; termina no começo do anel, em toZ. Falso se nenhum ponto serve.</summary>
    private static bool TryHelix(List<ToolMove> moves, Contour region, Point2 ringStart, double fromZ, double toZ, CamTool tool, CamLevels levels, PocketParams p)
    {
        double rp = (p.HelixDiameter - tool.Diameter) / 2;
        List<Point2> poly = region.Sample();
        (double minX, double minY, double maxX, double maxY) = region.BoundsXY();
        Point2[] candidates =
        [
            new(poly.Average(q => q.X), poly.Average(q => q.Y)),
            new((minX + maxX) / 2, (minY + maxY) / 2),
            ringStart,
        ];
        foreach (Point2 c in candidates)
        {
            if (!InsidePolygon(poly, c) || !Enumerable.Range(0, 24).All(i =>
                {
                    double a = i * Math.PI / 12;
                    return InsidePolygon(poly, new Point2(c.X + (rp * Math.Cos(a)), c.Y + (rp * Math.Sin(a))));
                }))
            {
                continue;
            }

            var p0 = new Point2(c.X + rp, c.Y);
            if (!SegmentInside(poly, p0, ringStart))
            {
                continue;
            }

            double dz = fromZ - toZ;
            int laps = Math.Max(1, (int)Math.Ceiling((dz / p.HelixPitch) - 1e-9));
            moves.Add(Rapid(p0, levels.ClearanceZ));
            moves.Add(Feed(p0, fromZ, tool.FeedZ));
            for (int k = 1; k <= 2 * laps; k++)
            {
                Point2 end = k % 2 == 1 ? new Point2(c.X - rp, c.Y) : p0;
                moves.Add(new ToolMove(MoveKind.ArcCcw, end.X, end.Y, fromZ - (dz * k / (2 * laps)), c, tool.FeedXY));
            }

            if (p0.DistanceTo(ringStart) > 1e-6)
            {
                moves.Add(Feed(ringStart, toZ, tool.FeedXY));
            }

            return true;
        }

        return false;
    }
}
