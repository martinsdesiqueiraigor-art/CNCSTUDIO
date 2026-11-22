namespace CncStudio.Core.Cam;

public enum ContourSide { Outside, Inside, OnLine }

/// <summary>
/// Parâmetros do contorno. <see cref="Climb"/>: concordante (padrão). <see cref="ControllerComp"/>: o programa segue o desenho e o
/// controle compensa o raio (G41/G42); desligado, o CAM já desloca o percurso. <see cref="LeadRadius"/>: raio do arco de entrada e saída.
/// </summary>
public sealed record ContourParams(ContourSide Side, bool Climb = true, bool ControllerComp = false, double Allowance = 0, double LeadRadius = 3);

/// <summary>
/// OPS-A-02, Chanfro com fresa em V de ponta afiada. <see cref="Side"/>: por fora ou por dentro do contorno (sobre a linha não vale).
/// <see cref="TipAngle"/>: ângulo incluído da ponta, em graus. <see cref="Width"/>: largura horizontal do chanfro no topo.
/// <see cref="Depth"/>: quanto a ponta desce abaixo do topo. <see cref="LeadRadius"/>: raio do arco de entrada e saída. Percurso concordante.
/// </summary>
public sealed record ChamferParams(ContourSide Side, double TipAngle, double Width, double Depth, double LeadRadius);

/// <summary>
/// Bolsão: passo lateral, sobremetal e raio de entrada do acabamento. OPS-A-01: a entrada em Z e a estratégia são escolha do usuário
/// (<see cref="PocketEntry"/>, <see cref="PocketStrategy"/>); sem escolha o bolsão sai como antes (arquivo versão ≤ 6, G-code igual).
/// </summary>
public sealed record PocketParams(double StepOverPercent = 60, double FinishAllowance = 0, double LeadRadius = 2)
{
    /// <summary>Como a ferramenta desce: <see cref="PocketEntry.Default"/> = rampa de uma volta no primeiro anel (como sempre foi).</summary>
    public PocketEntry Entry { get; init; } = PocketEntry.Default;

    /// <summary>Rampa: ângulo máximo de descida, em graus (digitado). Só vale com <see cref="PocketEntry.Ramp"/>.</summary>
    public double RampAngle { get; init; }

    /// <summary>Hélice: diâmetro do furo que ela abre, em mm (digitado). Só vale com <see cref="PocketEntry.Helix"/>.</summary>
    public double HelixDiameter { get; init; }

    /// <summary>Hélice: descida por volta, em mm (digitado). Só vale com <see cref="PocketEntry.Helix"/>.</summary>
    public double HelixPitch { get; init; }

    /// <summary>Ordem em que a ferramenta limpa a área.</summary>
    public PocketStrategy Strategy { get; init; } = PocketStrategy.Rings;

    /// <summary>
    /// Pré-furo: ponto (XY) do furo já feito por uma Furação anterior que cai dentro do bolsão. Preenchido ao gerar o trabalho
    /// (<see cref="CamJob"/>), nunca vai para o arquivo. Null = nenhum furo achado.
    /// </summary>
    public Point2? PreDrillPoint { get; init; }

    /// <summary>
    /// CAM-ADAPT-01b: no desbaste adaptativo com borda aberta, o corte do bruto na altura (onde há material de verdade). Calculado
    /// pelo trabalho, como <see cref="PreDrillPoint"/>; não vai para o arquivo. Null = o contorno é parede de verdade.
    /// </summary>
    public Contour? AdaptiveStock { get; init; }

    /// <summary>
    /// CAM-ADAPT-01 (estratégia <see cref="PocketStrategy.Adaptive"/>): carga ótima, a largura de corte máxima em mm (Fusion: Optimal
    /// load). Digitada; 0 = vazio (a operação não gera).
    /// </summary>
    public double OptimalLoad { get; init; }

    /// <summary>CAM-ADAPT-01: raio mínimo da trajetória em mm (0 = sem mínimo). Vale a partir da fatia b.</summary>
    public double MinRadius { get; init; }

    /// <summary>CAM-ADAPT-01: corta nos dois sentidos (concordante e discordante). Vale a partir da fatia b.</summary>
    public bool BothWays { get; init; }

    /// <summary>CAM-ADAPT-01: sobremetal no fundo, em mm (o da parede é <see cref="FinishAllowance"/>).</summary>
    public double FloorAllowance { get; init; }

    /// <summary>Alguma escolha nova (entrada ou estratégia): o arquivo precisa da versão 7.</summary>
    public bool UsesOptions => Entry != PocketEntry.Default || Strategy != PocketStrategy.Rings;
}

/// <summary>Operações de fresa 2,5D. Todas devolvem movimentos neutros; o pós-processador escreve o G-code.</summary>
public static partial class CamOperations
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

    /// <summary>
    /// Distância do eixo da ferramenta V até a aresta (o <c>o</c> do chanfro): <c>Depth·tan(α/2) − Width</c>. Null e o motivo em
    /// <paramref name="error"/> se os números não fecham (ponta afiada; ponta plana deixaria o chanfro menor).
    /// </summary>
    public static double? ChamferOffset(CamTool tool, ChamferParams p, out string? error)
    {
        error = null;
        if (p.Side == ContourSide.OnLine)
        {
            error = "O chanfro é por fora ou por dentro do contorno.";
        }
        else if (!(p.TipAngle > 0 && p.TipAngle < 180))
        {
            error = "O ângulo da ponta precisa ficar entre 0 e 180 graus.";
        }
        else if (!(p.Width > 0) || !(p.Depth > 0))
        {
            error = "A largura e a profundidade do chanfro precisam ser maiores que zero.";
        }
        else if (!(p.LeadRadius > 0))
        {
            error = "O raio de entrada e saída precisa ser maior que zero.";
        }

        if (error is not null)
        {
            return null;
        }

        double tan = Math.Tan(p.TipAngle * Math.PI / 360);
        double radiusAtTop = p.Depth * tan;
        if (radiusAtTop > tool.Radius + 1e-9)
        {
            error = $"A ferramenta V não alcança: nessa profundidade o cone pede Ø {2 * radiusAtTop:0.###} e a ferramenta tem Ø {tool.Diameter:0.###}.";
            return null;
        }

        double offset = radiusAtTop - p.Width;
        if (offset < -1e-9)
        {
            error = $"A profundidade é pequena para essa largura: com ponta de {p.TipAngle:0.###}° o chanfro de {p.Width:0.###} pede profundidade de pelo menos {p.Width / tan:0.###}.";
            return null;
        }

        return Math.Max(0, offset);
    }

    /// <summary>
    /// Chanfro da aresta do contorno com fresa em V (OPS-A-02): um passe só, com a ponta em Z topo − profundidade e o eixo a
    /// <see cref="ChamferOffset"/> da aresta. Reaproveita o gerador do contorno (arco de entrada e saída, sentido concordante).
    /// </summary>
    public static OperationResult Chamfer(Contour part, CamTool tool, CamLevels levels, ChamferParams p)
    {
        string? error = tool.Validate();
        if (error is not null)
        {
            return Fail(error);
        }

        if (!(levels.SafeZ > levels.ClearanceZ && levels.ClearanceZ >= levels.TopZ))
        {
            return Fail("Os Z precisam seguir: seguro > aproximação ≥ topo da peça.");
        }

        if (ChamferOffset(tool, p, out error) is not { } offset)
        {
            return Fail(error!);
        }

        // o gerador do contorno desloca pelo raio da ferramenta: aqui o "raio" é a distância do eixo à aresta
        CamTool shifted = tool with { Diameter = offset * 2 };
        ContourSide side = offset <= 1e-9 ? ContourSide.OnLine : p.Side;
        var warnings = new List<string>();
        var moves = new List<ToolMove>();
        double bottom = levels.TopZ - p.Depth;
        error = AppendContour(moves, warnings, part, shifted, levels, new ContourParams(side, true, false, 0, p.LeadRadius), [bottom], moveToSafe: true);
        if (error is not null)
        {
            return Fail(error);
        }

        warnings.Add("Chanfro: considera ponta afiada. Ponta plana deixa o chanfro menor que o digitado. Meça a primeira peça.");
        ToolMove end = moves[^1];
        moves.Add(Rapid(end.XY, levels.SafeZ));
        return new OperationResult(new CamOperation("Chanfro", tool, moves, null, warnings), null);
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

        error = PocketOptionsError(p, tool);
        if (error is not null)
        {
            return Fail(error);
        }

        if (p.Strategy == PocketStrategy.Adaptive)
        {
            return AdaptivePocket(boundary, [], tool, levels, p);
        }

        if (p.Strategy == PocketStrategy.ZigZag)
        {
            return PocketLinesCore(boundary, [], tool, levels, p, "Bolsão");
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

        if (p.UsesOptions)
        {
            return PocketWithOptions(ccw, rings, tool, levels, p);
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

        if (kind == DrillCycleKind.Tap && (tool.SpindleRpm <= 0 || tool.FeedZ <= 0))
        {
            return Fail("Macho: informe a rotação e o avanço (passo × rotação) da ferramenta.");
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
