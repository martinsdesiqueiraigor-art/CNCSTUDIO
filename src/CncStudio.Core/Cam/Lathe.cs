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
/// <see cref="Point2.X"/> = Z e <see cref="Point2.Y"/> = raio. Perfil de desbaste "tipo I": o raio só cresce (externo) ou só diminui (interno).
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
    double StartZ, double StartR, double DepthOfCut, double Retract, double AllowanceR, double AllowanceZ, double RoughFeed, double FinishFeed);

/// <summary>Operação de torno: movimentos simples (faceamento, furação) ou um ciclo de perfil.</summary>
public sealed record LatheOperation(string Name, LatheTool Tool, double SafeZ, double SafeR, IReadOnlyList<LatheMove> Moves, LatheCycle? Cycle, IReadOnlyList<string> Warnings);

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

        // Tipo I: o raio só cresce (externo) ou só diminui (interno) ao se aproximar da placa
        var points = profile.Segments.SelectMany(s => s.Sample(0.01)).ToList();
        double sign = p.Internal ? -1 : 1;
        for (int i = 1; i < points.Count; i++)
        {
            if (points[i].X > points[i - 1].X + 1e-6)
            {
                return Fail("O perfil volta para trás em Z. Para desbaste ele precisa avançar sempre em direção à placa.");
            }

            if (sign * (points[i].Y - points[i - 1].Y) < -1e-6)
            {
                return Fail(p.Internal
                    ? "Perfil interno com rebaixo: o raio só pode diminuir em direção ao fundo (sem canal ou cantos para trás)."
                    : "Perfil externo com rebaixo: o raio só pode aumentar em direção à placa (sem canal ou cantos para trás).");
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
        if (profile.Start.Y > 1e-9 && !p.Internal && profile.Start.Y > stockR)
        {
            warnings.Add("O início do perfil está fora do bruto.");
        }

        double startZ = profile.Start.X + p.Clearance;
        var cycle = new LatheCycle(profile, p.Internal, p.Rough, p.Finish, startZ, startR, p.DepthOfCut, p.Retract, p.AllowanceR, p.AllowanceZ, p.RoughFeed, p.FinishFeed);
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
}
