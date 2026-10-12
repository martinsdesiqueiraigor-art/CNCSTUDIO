using CncStudio.Core.Geometry;

namespace CncStudio.Core.Trajectory;

/// <summary>Tipo de movimento (grupo modal 01).</summary>
public enum MotionMode
{
    /// <summary>G00 — avanço rápido.</summary>
    Rapid = 0,

    /// <summary>G01 — interpolação linear.</summary>
    Linear = 1,

    /// <summary>G02 — arco horário.</summary>
    ClockwiseArc = 2,

    /// <summary>G03 — arco anti-horário.</summary>
    CounterClockwiseArc = 3,
}

/// <summary>
/// Um movimento da ferramenta: reta (G00/G01) ou arco/hélice (G02/G03) em qualquer plano.
///
/// O arco é guardado como UM movimento (centro, raios, ângulos no plano) e só vira
/// pontos quando alguém precisa desenhar ou simular — cada consumidor escolhe
/// a sua tolerância (<see cref="AppendPolyline"/>). Assim a memória não explode
/// em arquivos cheios de arcos.
/// </summary>
public readonly record struct Move
{
    public Move(Point3 start, Point3 end, MotionMode motion, int lineNumber, double feed, int tool = 0)
    {
        Start = start;
        End = end;
        Motion = motion;
        LineNumber = lineNumber;
        Feed = feed;
        Tool = tool;
    }

    /// <summary>Ponto inicial (mm, coordenadas com o zero de peça aplicado).</summary>
    public Point3 Start { get; init; }

    /// <summary>Ponto final (mm).</summary>
    public Point3 End { get; init; }

    /// <summary>G00, G01, G02 ou G03.</summary>
    public MotionMode Motion { get; init; }

    /// <summary>Linha do arquivo que gerou o movimento (base 1).</summary>
    public int LineNumber { get; init; }

    /// <summary>Avanço em mm/min (sem uso em G00).</summary>
    public double Feed { get; init; }

    /// <summary>Número da ferramenta ativa (0 = nenhuma definida).</summary>
    public int Tool { get; init; }

    // Geometria do arco (só vale quando IsArc), em coordenadas do plano (u, v)
    public WorkPlane Plane { get; init; }
    public double CenterU { get; init; }
    public double CenterV { get; init; }
    public double StartRadius { get; init; }
    public double EndRadius { get; init; }
    public double StartAngle { get; init; }

    /// <summary>Varredura em radianos: negativa no G02 (horário), positiva no G03.</summary>
    public double Sweep { get; init; }

    public bool IsRapid => Motion == MotionMode.Rapid;
    public bool IsArc => Motion is MotionMode.ClockwiseArc or MotionMode.CounterClockwiseArc;

    /// <summary>Comprimento percorrido (mm). Para hélices, combina o arco com o deslocamento perpendicular.</summary>
    public double Length
    {
        get
        {
            if (!IsArc)
            {
                return Start.DistanceTo(End);
            }

            double arcLength = (StartRadius + EndRadius) / 2 * Math.Abs(Sweep);
            double dw = Plane.ToPlane(End).W - Plane.ToPlane(Start).W;
            return Math.Sqrt(arcLength * arcLength + dw * dw);
        }
    }

    /// <summary>Ponto ao longo do movimento: t = 0 no início, t = 1 no fim.</summary>
    public Point3 PointAt(double t)
    {
        if (t >= 1)
        {
            return End;
        }

        if (!IsArc)
        {
            return Point3.Lerp(Start, End, t);
        }

        double startW = Plane.ToPlane(Start).W;
        double endW = Plane.ToPlane(End).W;
        double angle = StartAngle + Sweep * t;
        double radius = StartRadius + (EndRadius - StartRadius) * t;
        return Plane.FromPlane(
            CenterU + radius * Math.Cos(angle),
            CenterV + radius * Math.Sin(angle),
            startW + (endW - startW) * t);
    }

    /// <summary>
    /// Acrescenta em "output" os pontos do movimento DEPOIS do ponto inicial, até o final.
    /// Retas acrescentam só o ponto final. Arcos são divididos de forma que o erro de corda
    /// não passe de "tolerance" (mm) e nenhum passo passe de "maxStepDegrees".
    /// </summary>
    public void AppendPolyline(List<Point3> output, double tolerance, double maxStepDegrees = 10)
    {
        if (!IsArc)
        {
            output.Add(End);
            return;
        }

        int count = ArcStepCount(tolerance, maxStepDegrees);
        for (int k = 1; k < count; k++)
        {
            output.Add(PointAt((double)k / count));
        }

        output.Add(End); // termina exatamente no ponto programado
    }

    /// <summary>Quantidade de passos para interpolar o arco com a tolerância informada.</summary>
    public int ArcStepCount(double tolerance, double maxStepDegrees = 10)
    {
        double radius = Math.Max(StartRadius, EndRadius);
        double stepByTolerance = radius > tolerance
            ? 2 * Math.Acos(1 - tolerance / radius)
            : Math.PI / 2;
        double step = Math.Min(stepByTolerance, maxStepDegrees * Math.PI / 180);
        return Math.Max(1, (int)Math.Ceiling(Math.Abs(Sweep) / step));
    }

    /// <summary>
    /// Inclui o movimento no envelope. Para arcos, considera também os pontos extremos
    /// (0°, 90°, 180°, 270° no plano) que caem dentro da varredura — não só início e fim.
    /// </summary>
    public Bounds3 IncludeIn(Bounds3 bounds)
    {
        bounds = bounds.Include(Start).Include(End);
        if (!IsArc)
        {
            return bounds;
        }

        double quarter = Math.PI / 2;
        double from = Math.Min(StartAngle, StartAngle + Sweep);
        double to = Math.Max(StartAngle, StartAngle + Sweep);
        for (double angle = Math.Ceiling(from / quarter) * quarter; angle <= to; angle += quarter)
        {
            bounds = bounds.Include(PointAt((angle - StartAngle) / Sweep));
        }

        return bounds;
    }
}
