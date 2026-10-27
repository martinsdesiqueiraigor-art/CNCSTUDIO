namespace CncStudio.Core.Cam;

public enum MoveKind { Rapid, Feed, ArcCw, ArcCcw }

/// <summary>Compensação de raio pelo controle: esquerda (G41), direita (G42) ou desligada (G40).</summary>
public enum CutterComp { Off, Left, Right }

/// <summary>
/// Um movimento do percurso, independente de dialeto. Arco: <see cref="Center"/> absoluto no plano XY; se o Z muda no arco, é hélice.
/// <see cref="Comp"/> só aparece no movimento em que a compensação liga ou desliga (null = sem mudança).
/// </summary>
public readonly record struct ToolMove(MoveKind Kind, double X, double Y, double Z, Point2 Center = default, double Feed = 0, CutterComp? Comp = null)
{
    public Point2 XY => new(X, Y);

    /// <summary>Letra do eixo rotativo posicionado neste movimento ("A", "B" ou "C"); null = sem palavra rotativa.</summary>
    public string? RotaryLetter { get; init; }

    /// <summary>Ângulo (graus) da palavra rotativa, já com o sentido da máquina.</summary>
    public double RotaryAngle { get; init; }

    public bool IsArc => Kind is MoveKind.ArcCw or MoveKind.ArcCcw;
}

/// <summary>Ferramenta de fresa usada numa operação.</summary>
public sealed record CamTool(int Number, double Diameter, double SpindleRpm, double FeedXY, double FeedZ, string Name = "")
{
    public double Radius => Diameter / 2;

    public string? Validate()
    {
        if (Diameter <= 0)
        {
            return "Diâmetro da ferramenta precisa ser maior que zero.";
        }

        if (FeedXY <= 0 || FeedZ <= 0)
        {
            return "Os avanços precisam ser maiores que zero.";
        }

        return SpindleRpm <= 0 ? "A rotação precisa ser maior que zero." : null;
    }
}

/// <summary>Alturas Z da operação: segura (rápido), de aproximação, topo da peça, fundo e profundidade por passe.</summary>
public sealed record CamLevels(double SafeZ, double ClearanceZ, double TopZ, double BottomZ, double StepDown)
{
    public string? Validate()
    {
        if (StepDown <= 0)
        {
            return "O passe em Z precisa ser maior que zero.";
        }

        if (BottomZ >= TopZ)
        {
            return "O fundo precisa ficar abaixo do topo.";
        }

        if (ClearanceZ < TopZ)
        {
            return "A altura de aproximação não pode ficar abaixo do topo.";
        }

        return SafeZ < ClearanceZ ? "A altura segura não pode ficar abaixo da altura de aproximação." : null;
    }

    /// <summary>Profundidade de cada passe, do primeiro ao fundo, em partes iguais (nenhuma maior que o passe em Z).</summary>
    public IReadOnlyList<double> Depths()
    {
        int n = (int)Math.Ceiling(((TopZ - BottomZ) / StepDown) - 1e-9);
        var list = new List<double>();
        for (int i = 1; i <= n; i++)
        {
            list.Add(i == n ? BottomZ : TopZ - ((TopZ - BottomZ) * i / n));
        }

        return list;
    }
}

public enum DrillCycleKind { Drill, Dwell, Peck, ChipBreak }

/// <summary>Furação em ciclo fixo (G81 simples, G82 com tempo, G83 com bicada, G73 quebra-cavaco); o pós-processador escolhe o código.</summary>
public sealed record DrillCycle(DrillCycleKind Kind, IReadOnlyList<Point2> Points, double TopZ, double BottomZ, double RetractZ, double SafeZ, double Peck, double DwellSeconds, double Feed)
{
    /// <summary>Eixo rotativo (A ou B) a girar para o ângulo <see cref="RotaryAngle"/> antes dos furos (face girada). Null = sem giro.</summary>
    public string? RotaryLetter { get; init; }

    /// <summary>Ângulo do eixo rotativo na máquina, em graus.</summary>
    public double RotaryAngle { get; init; }
}

/// <summary>Furo achado no desenho: centro e diâmetro.</summary>
public readonly record struct DrillHole(Point2 Center, double Diameter);

/// <summary>Saída de uma operação do CAM: movimentos (ou ciclo de furação) e avisos.</summary>
public sealed record CamOperation(string Name, CamTool Tool, IReadOnlyList<ToolMove> Moves, DrillCycle? Drill, IReadOnlyList<string> Warnings);

public sealed record OperationResult(CamOperation? Operation, string? Error)
{
    public bool Ok => Operation is not null;
}
