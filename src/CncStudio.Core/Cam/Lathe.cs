namespace CncStudio.Core.Cam;

/// <summary>
/// Ferramenta de torno: avanço em mm/rot e rotação. Com <see cref="Rpm"/> = 0 usa velocidade de corte constante
/// (G96, m/min) limitada por <see cref="MaxRpm"/>; com <see cref="Rpm"/> &gt; 0 usa rotação fixa (G97).
/// </summary>
public sealed record LatheTool(int Number, string Name = "", double SurfaceSpeed = 200, double MaxRpm = 3000, double Rpm = 0)
{
    public string? Validate()
    {
        if (Number < 1 || Number > 99)
        {
            return "O número da ferramenta de torno vai de 1 a 99.";
        }

        if (Rpm <= 0 && SurfaceSpeed <= 0)
        {
            return "Informe a velocidade de corte ou a rotação.";
        }

        return MaxRpm <= 0 ? "A rotação máxima precisa ser maior que zero." : null;
    }
}

/// <summary>Movimento simples do torno no plano (Z, raio). O X do programa é o diâmetro (2 × raio).</summary>
public readonly record struct LatheMove(MoveKind Kind, double Z, double R, double Feed = 0);

/// <summary>
/// Contorno de torno: trechos (reta/arco) em (Z, raio), do lado do bruto para o lado da placa (Z diminuindo).
/// <see cref="Point2.X"/> = Z e <see cref="Point2.Y"/> = raio. Z sempre rumo à placa; o raio pode ter rebaixos (tipo II).
/// </summary>
public sealed record LatheProfile(IReadOnlyList<Segment2> Segments)
{
    public Point2 Start => Segments[0].Start;

    public Point2 End => Segments[^1].End;

    /// <summary>Monta o perfil a partir de uma cadeia: orienta para Z diminuir e confere continuidade.</summary>
    public static LatheProfile? From(IReadOnlyList<Segment2> chain, out string? error)
    {
        error = null;
        if (chain.Count == 0)
        {
            error = "O perfil está vazio.";
            return null;
        }

        for (int i = 0; i + 1 < chain.Count; i++)
        {
            if (chain[i].End.DistanceTo(chain[i + 1].Start) > 1e-3)
            {
                error = "O perfil tem uma interrupção: as pontas dos trechos precisam se encontrar.";
                return null;
            }
        }

        var segments = chain.ToList();
        if (segments[0].Start.X < segments[^1].End.X)
        {
            segments.Reverse();
            for (int i = 0; i < segments.Count; i++)
            {
                Segment2 s = segments[i];
                segments[i] = s.IsArc ? Segment2.Arc(s.End, s.Start, s.Center, !s.Ccw) : Segment2.Line(s.End, s.Start);
            }
        }

        if (segments.Any(s => s.Start.Y < -1e-9 || s.End.Y < -1e-9))
        {
            error = "O perfil tem raio negativo (abaixo da linha de centro).";
            return null;
        }

        return new LatheProfile(segments);
    }

    /// <summary>Raio mínimo e máximo ao longo do perfil.</summary>
    public (double Min, double Max) RadiusRange()
    {
        List<Point2> points = Segments.SelectMany(s => s.Sample(0.01)).ToList();
        return (points.Min(p => p.Y), points.Max(p => p.Y));
    }
}

/// <summary>Ciclo de desbaste e acabamento de perfil (G71/G70 no Fanuc, CYCLE95 no Siemens). Tudo em mm; sobremetal por lado.</summary>
public sealed record LatheCycle(
    LatheProfile Profile, bool Internal, bool Rough, bool Finish,
    double StartZ, double StartR, double DepthOfCut, double Retract, double AllowanceR, double AllowanceZ, double RoughFeed, double FinishFeed)
{
    /// <summary>Perfil com rebaixo (o raio vai e volta): o Fanuc exige o 1º bloco do perfil com X e Z (G71 tipo II).</summary>
    public bool HasUndercut { get; init; }
}

/// <summary>Operação de torno: movimentos simples (faceamento, furação) ou um ciclo de perfil.</summary>
public sealed record LatheOperation(string Name, LatheTool Tool, double SafeZ, double SafeR, IReadOnlyList<LatheMove> Moves, LatheCycle? Cycle, IReadOnlyList<string> Warnings)
{
    /// <summary>Rosca (G76 no Fanuc, CYCLE97 no Siemens). Preenchido só nas operações de rosca.</summary>
    public LatheThread? Thread { get; init; }
}

/// <summary>
/// Rosca longitudinal. Z de <see cref="StartZ"/> (lado da entrada, maior Z) a <see cref="EndZ"/>; o ciclo ainda corre
/// <see cref="Approach"/> antes e <see cref="Overrun"/> depois. Diâmetro nominal = crista (externa) ou raiz (interna).
/// </summary>
public sealed record LatheThread(
    bool Internal, double MajorDiameter, double Pitch, double StartZ, double EndZ, double Approach, double Overrun,
    double Height, double FirstDepth, double MinDepth, int FinishPasses, double Allowance, double Clearance, double Rpm)
{
    /// <summary>Raio da crista (externa: nominal; interna: furo).</summary>
    public double CrestRadius => Internal ? MajorDiameter / 2 - Height : MajorDiameter / 2;

    /// <summary>Raio da raiz (externa: nominal − altura; interna: nominal).</summary>
    public double RootRadius => Internal ? MajorDiameter / 2 : MajorDiameter / 2 - Height;

    /// <summary>Raio do ponto de partida do ciclo, fora do material.</summary>
    public double StartRadius => Internal ? CrestRadius - Clearance : CrestRadius + Clearance;

    /// <summary>Profundidades de cada passe a partir da crista (as mesmas do G76 do simulador).</summary>
    public IReadOnlyList<double> Depths() => Execution.MachineExecutor.ThreadDepths(Height, FirstDepth, MinDepth, Allowance, FinishPasses);
}

/// <summary>
/// Parâmetros da rosca. Altura em zero = padrão ISO (0,6134·passo externa; 0,5413·passo interna).
/// Rpm fixo (rosca não se faz em velocidade de corte constante).
/// </summary>
public sealed record ThreadingParams(
    bool Internal, double MajorDiameter, double Pitch, double StartZ, double EndZ, double Rpm = 600,
    double Height = 0, double FirstDepth = 0, double MinDepth = 0.05, int FinishPasses = 2, double Allowance = 0.05,
    double Approach = 0, double Overrun = 0, double Clearance = 2);

public sealed record LatheResult(LatheOperation? Operation, string? Error)
{
    public bool Ok => Operation is not null;
}

public sealed record TurningParams(
    double StockDiameter, bool Internal = false, bool Rough = true, bool Finish = true, double Clearance = 2,
    double DepthOfCut = 1.5, double Retract = 0.5, double AllowanceR = 0.2, double AllowanceZ = 0.1, double RoughFeed = 0.25, double FinishFeed = 0.1);

/// <summary>Operações de torno 2D. Saídas neutras; o pós-processador escreve o G-code.</summary>
public static class LatheOperations
{
    private static LatheResult Fail(string message) => new(null, message);

    /// <summary>
    /// Desbaste e acabamento de perfil externo ou interno. <paramref name="stockDiameter"/>: diâmetro do bruto (externo) ou do pré-furo (interno).
    /// A face da peça já deve estar feita no Z do início do perfil (use o faceamento antes).
    /// </summary>
    public static LatheResult Turning(LatheProfile profile, LatheTool tool, double safeZ, double safeR, TurningParams p)
    {
        string? error = tool.Validate();
        if (error is not null)
        {
            return Fail(error);
        }

        if (p.StockDiameter <= 0)
        {
            return Fail("O diâmetro do bruto (ou do pré-furo) precisa ser maior que zero.");
        }

        if (!p.Rough && !p.Finish)
        {
            return Fail("Marque desbaste, acabamento ou os dois.");
        }

        if (p.Rough && p.DepthOfCut <= 0)
        {
            return Fail("A profundidade de corte precisa ser maior que zero.");
        }

        if (p.RoughFeed <= 0 || p.FinishFeed <= 0)
        {
            return Fail("Os avanços (mm/rot) precisam ser maiores que zero.");
        }

        if (p.AllowanceR < 0 || p.AllowanceZ < 0)
        {
            return Fail("O sobremetal não pode ser negativo.");
        }

        // Em Z o perfil precisa avançar sempre rumo à placa. O raio pode ir e voltar (rebaixo): G71 tipo II / CYCLE95 com rebaixo.
        var points = profile.Segments.SelectMany(s => s.Sample(0.01)).ToList();
        double sign = p.Internal ? -1 : 1;
        bool undercut = false;
        for (int i = 1; i < points.Count; i++)
        {
            if (points[i].X > points[i - 1].X + 1e-6)
            {
                return Fail("O perfil volta para trás em Z. Para desbaste ele precisa avançar sempre em direção à placa.");
            }

            if (sign * (points[i].Y - points[i - 1].Y) < -1e-6)
            {
                undercut = true;
            }
        }

        double stockR = p.StockDiameter / 2;
        (double minR, double maxR) = profile.RadiusRange();
        if (!p.Internal && stockR < maxR - 1e-6)
        {
            return Fail($"O bruto (Ø{p.StockDiameter:0.###}) é menor que o maior diâmetro do perfil (Ø{maxR * 2:0.###}).");
        }

        if (p.Internal && stockR > minR + 1e-6)
        {
            return Fail($"O pré-furo (Ø{p.StockDiameter:0.###}) é maior que o menor diâmetro do perfil (Ø{minR * 2:0.###}).");
        }

        double startR = p.Internal ? stockR - p.Clearance : stockR + p.Clearance;
        if (startR <= 0)
        {
            return Fail("A folga de aproximação não cabe dentro do pré-furo.");
        }

        var warnings = new List<string>();
        if (undercut)
        {
            warnings.Add("Perfil com rebaixo: o desbaste mergulha dentro do rebaixo (G71 tipo II no Fanuc, CYCLE95 no Siemens). Confira se a pastilha alcança o fundo e as paredes.");
            if (p.AllowanceZ > 0)
            {
                // O sobremetal em Z desloca o perfil inteiro: as paredes que olham para a placa seriam cortadas a mais
                warnings.Add("Sobremetal em Z zerado nesta operação: com rebaixo ele cortaria a parede do rebaixo a mais.");
            }
        }

        if (profile.Start.Y > 1e-9 && !p.Internal && profile.Start.Y > stockR)
        {
            warnings.Add("O início do perfil está fora do bruto.");
        }

        double startZ = profile.Start.X + p.Clearance;
        var cycle = new LatheCycle(profile, p.Internal, p.Rough, p.Finish, startZ, startR, p.DepthOfCut, p.Retract, p.AllowanceR, undercut ? 0 : p.AllowanceZ, p.RoughFeed, p.FinishFeed) { HasUndercut = undercut };
        string name = (p.Internal ? "Torneamento interno" : "Torneamento externo") + (p.Rough && p.Finish ? " (desbaste e acabamento)" : p.Rough ? " (desbaste)" : " (acabamento)");
        return new LatheResult(new LatheOperation(name, tool, safeZ, safeR, [], cycle, warnings), null);
    }

    /// <summary>
    /// Faceamento: passes em Z do bruto até a face pronta, do diâmetro do bruto até o centro (ou até <paramref name="innerDiameter"/>).
    /// </summary>
    public static LatheResult Facing(LatheTool tool, double safeZ, double safeR, double stockDiameter, double stockFaceZ, double finalFaceZ, double depthOfCut, double feed, double innerDiameter = 0, double clearance = 2)
    {
        string? error = tool.Validate();
        if (error is not null)
        {
            return Fail(error);
        }

        if (stockDiameter <= 0 || innerDiameter < 0 || innerDiameter >= stockDiameter)
        {
            return Fail("Diâmetros inválidos: o bruto precisa ser maior que o diâmetro interno.");
        }

        if (stockFaceZ <= finalFaceZ)
        {
            return Fail("A face do bruto precisa ficar acima (maior Z) da face pronta.");
        }

        if (depthOfCut <= 0 || feed <= 0)
        {
            return Fail("Profundidade e avanço precisam ser maiores que zero.");
        }

        int passes = (int)Math.Ceiling(((stockFaceZ - finalFaceZ) / depthOfCut) - 1e-9);
        double startR = (stockDiameter / 2) + clearance;
        double endR = innerDiameter / 2;
        var moves = new List<LatheMove> { new(MoveKind.Rapid, stockFaceZ + clearance, startR) };
        for (int i = 1; i <= passes; i++)
        {
            double z = i == passes ? finalFaceZ : stockFaceZ - ((stockFaceZ - finalFaceZ) * i / passes);
            moves.Add(new LatheMove(MoveKind.Rapid, stockFaceZ + clearance, startR));
            moves.Add(new LatheMove(MoveKind.Rapid, z, startR));
            moves.Add(new LatheMove(MoveKind.Feed, z, endR, feed));
            moves.Add(new LatheMove(MoveKind.Feed, z + 0.5, endR + 0.5, feed)); // alivia a ponta antes de voltar
            moves.Add(new LatheMove(MoveKind.Rapid, z + 0.5, startR));
        }

        moves.Add(new LatheMove(MoveKind.Rapid, safeZ, safeR));
        return new LatheResult(new LatheOperation("Faceamento", tool, safeZ, safeR, moves, null, []), null);
    }

    /// <summary>Furação axial na linha de centro, em bicadas (movimentos simples, roda em qualquer controle). <paramref name="topZ"/> = face, <paramref name="bottomZ"/> = fundo.</summary>
    public static LatheResult Drilling(LatheTool tool, double safeZ, double safeR, double topZ, double bottomZ, double peck, double feed, double clearance = 2)
    {
        string? error = tool.Validate();
        if (error is not null)
        {
            return Fail(error);
        }

        if (bottomZ >= topZ)
        {
            return Fail("O fundo do furo precisa ficar abaixo da face.");
        }

        if (peck <= 0 || feed <= 0)
        {
            return Fail("Bicada e avanço precisam ser maiores que zero.");
        }

        var moves = new List<LatheMove> { new(MoveKind.Rapid, topZ + clearance, 0) };
        double depth = 0;
        double total = topZ - bottomZ;
        while (depth < total - 1e-9)
        {
            double previous = depth;
            depth = Math.Min(total, depth + peck);
            if (previous > 0)
            {
                moves.Add(new LatheMove(MoveKind.Rapid, topZ - previous + 1, 0));
            }

            moves.Add(new LatheMove(MoveKind.Feed, topZ - depth, 0, feed));
            moves.Add(new LatheMove(MoveKind.Rapid, topZ + clearance, 0));
        }

        moves.Add(new LatheMove(MoveKind.Rapid, safeZ, safeR));
        return new LatheResult(new LatheOperation("Furação axial", tool, safeZ, safeR, moves, null, []), null);
    }

    /// <summary>Rosca ISO longitudinal (passo em mm), externa ou interna, em vários passes. Roda em G76 (Fanuc) ou CYCLE97 (Siemens).</summary>
    public static LatheResult Threading(LatheTool tool, double safeZ, double safeR, ThreadingParams p)
    {
        string? error = tool.Validate();
        if (error is not null)
        {
            return Fail(error);
        }

        if (p.Pitch <= 0 || p.MajorDiameter <= 0)
        {
            return Fail("Passo e diâmetro da rosca precisam ser maiores que zero.");
        }

        if (p.StartZ <= p.EndZ)
        {
            return Fail("A rosca começa no Z maior e termina no menor (fim abaixo do início).");
        }

        if (p.Rpm <= 0 || p.FinishPasses < 1 || p.Allowance < 0 || p.MinDepth <= 0 || p.Clearance <= 0)
        {
            return Fail("Rotação, passes de acabamento, sobremetal, incremento mínimo e folga precisam ser válidos.");
        }

        double height = p.Height > 0 ? p.Height : (p.Internal ? 0.5413 : 0.6134) * p.Pitch;
        double first = p.FirstDepth > 0 ? p.FirstDepth : Math.Max(0.05, height * 0.35);
        double approach = p.Approach > 0 ? p.Approach : 2 * p.Pitch;
        double overrun = p.Overrun > 0 ? p.Overrun : p.Pitch;
        if (height >= p.MajorDiameter / 2 || first > height)
        {
            return Fail("Altura do filete ou 1º passe maior que a rosca permite.");
        }

        var thread = new LatheThread(p.Internal, p.MajorDiameter, p.Pitch, p.StartZ, p.EndZ, approach, overrun,
            height, first, p.MinDepth, p.FinishPasses, p.Allowance, p.Clearance, p.Rpm);
        if (thread.StartRadius < 0.5)
        {
            return Fail("Furo pequeno demais para a folga de entrada da rosca.");
        }

        var warnings = new List<string>();
        if (p.Pitch > 0 && p.Rpm * p.Pitch > 3000)
        {
            warnings.Add("Avanço de rosca alto (rpm × passo): confira o limite da máquina.");
        }

        var op = new LatheOperation(p.Internal ? "Rosca interna" : "Rosca externa", tool with { Rpm = p.Rpm }, safeZ, safeR, [], null, warnings) { Thread = thread };
        return new LatheResult(op, null);
    }

    /// <summary>
    /// Canal radial retangular em movimentos simples (roda em qualquer controle). Z do canal vai de
    /// <paramref name="zStart"/> (parede de maior Z) a <paramref name="zStart"/> − <paramref name="width"/>.
    /// A ferramenta tem a largura <paramref name="toolWidth"/> e seu Z de programa é a aresta do lado de maior Z.
    /// Desbaste em mergulhos com quebra-cavaco; acabamento (opcional) com sobremetal <paramref name="allowance"/> nas paredes e no fundo.
    /// </summary>
    public static LatheResult Grooving(LatheTool tool, double safeZ, double safeR, GroovingParams p)
    {
        string? error = tool.Validate();
        if (error is not null)
        {
            return Fail(error);
        }

        if (p.SurfaceDiameter <= 0 || p.BottomDiameter <= 0 || p.ToolWidth <= 0 || p.Width <= 0 || p.Peck <= 0 || p.Feed <= 0 || p.Clearance <= 0 || p.Allowance < 0 || p.Retract <= 0)
        {
            return Fail("Diâmetros, larguras, mergulho, avanço e folga precisam ser maiores que zero.");
        }

        if (p.Internal ? p.BottomDiameter <= p.SurfaceDiameter : p.BottomDiameter >= p.SurfaceDiameter)
        {
            return Fail(p.Internal ? "O fundo do canal interno precisa ter diâmetro maior que o furo." : "O fundo do canal precisa ter diâmetro menor que o da superfície.");
        }

        if (p.Width < p.ToolWidth - 1e-6)
        {
            return Fail($"A ferramenta ({p.ToolWidth:0.##} mm) é mais larga que o canal ({p.Width:0.##} mm).");
        }

        double s = p.Internal ? -1 : 1;                    // sentido "para fora" do material no raio
        double rTop = p.SurfaceDiameter / 2, rBottom = p.BottomDiameter / 2;
        double clear = rTop + s * p.Clearance;
        if (clear < 0.2)
        {
            return Fail("Furo pequeno demais para a folga de aproximação.");
        }

        double zUpper = p.ZStart;
        double zLowerTool = p.ZStart - p.Width + p.ToolWidth;   // Z da ferramenta com a aresta na parede de menor Z
        double span = zUpper - zLowerTool;                      // curso útil da ferramenta (0 = largura exata)
        bool finish = p.Finish && p.Allowance > 0 && span > 1e-6;
        double wall = finish ? Math.Min(p.Allowance, span / 2) : 0;
        double bottomAllow = finish ? p.Allowance : 0;
        double rRough = rBottom + s * bottomAllow;              // fundo do desbaste: para antes do fundo, deixando sobremetal
        if (p.Internal ? rRough < rTop : rRough > rTop)
        {
            rRough = rTop;
        }

        var moves = new List<LatheMove>();
        void Rapid(double z, double r) => moves.Add(new LatheMove(MoveKind.Rapid, z, r));
        void Feed(double z, double r) => moves.Add(new LatheMove(MoveKind.Feed, z, r, p.Feed));

        double firstZ = zUpper - wall, lastZ = zLowerTool + wall;
        var positions = new List<double> { firstZ };
        double step = Math.Max(0.1, p.ToolWidth * 0.8);
        for (double z = firstZ - step; z > lastZ + 1e-6; z -= step)
        {
            positions.Add(z);
        }

        if (lastZ < firstZ - 1e-6)
        {
            positions.Add(lastZ);
        }

        // Entrada: externo sobe para o raio seguro e desce; interno entra pelo eixo
        if (p.Internal)
        {
            Rapid(safeZ, clear);
            Rapid(firstZ, clear);
        }
        else
        {
            Rapid(firstZ, safeR);
            Rapid(firstZ, clear);
        }

        double depthTotal = Math.Abs(rRough - rTop);
        foreach (double z in positions)
        {
            Rapid(z, clear);
            double done = 0;
            while (done < depthTotal - 1e-9)
            {
                double previous = done;
                done = Math.Min(depthTotal, done + p.Peck);
                if (previous > 0)
                {
                    Rapid(z, rTop - s * (previous - 0.2 > 0 ? previous - 0.2 : 0)); // volta perto do fundo anterior
                }

                Feed(z, rTop - s * done);
                if (done < depthTotal - 1e-9)
                {
                    Rapid(z, rTop - s * Math.Max(0, done - p.Retract));              // quebra-cavaco
                }
            }

            Rapid(z, clear);
        }

        if (finish)
        {
            // 1) parede de baixo: mergulha e se afasta para o lado já limpo
            Rapid(zLowerTool, clear);
            Feed(zLowerTool, rBottom);
            Feed(zLowerTool + wall, rBottom);
            Rapid(zLowerTool + wall, clear);
            // 2) parede de cima: mergulha e arrasta pelo fundo até a parede de baixo
            Rapid(zUpper, clear);
            Feed(zUpper, rBottom);
            Feed(zLowerTool, rBottom);
            Rapid(zLowerTool, clear);
        }

        if (p.Internal)
        {
            Rapid(safeZ, clear);
            Rapid(safeZ, safeR);
        }
        else
        {
            Rapid(moves[^1].Z, safeR);
            Rapid(safeZ, safeR);
        }

        var warnings = new List<string>();
        if (p.Internal)
        {
            warnings.Add("Canal interno: confira a entrada da barra no furo antes de rodar.");
        }

        return new LatheResult(new LatheOperation(p.Internal ? "Canal interno" : "Canal externo", tool, safeZ, safeR, moves, null, warnings), null);
    }
}

/// <summary>Canal radial. Largura do canal e da ferramenta em mm; Z de início = parede de maior Z.</summary>
public sealed record GroovingParams(
    bool Internal, double SurfaceDiameter, double BottomDiameter, double ZStart, double Width, double ToolWidth,
    double Peck = 2, double Feed = 0.08, double Retract = 0.5, double Allowance = 0.1, bool Finish = true, double Clearance = 2);
