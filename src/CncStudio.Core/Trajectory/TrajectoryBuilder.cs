using System.Globalization;
using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;

namespace CncStudio.Core.Trajectory;

/// <summary>Parâmetros de construção da trajetória.</summary>
public sealed class TrajectoryOptions
{
    /// <summary>Posição da ferramenta antes do primeiro movimento.</summary>
    public Point3 StartPosition { get; init; } = Point3.Origin;

    /// <summary>Erro máximo de corda ao interpolar arcos (mm).</summary>
    public double ArcTolerance { get; init; } = 0.005;

    /// <summary>Ângulo máximo de cada passo do arco, mesmo em raios pequenos (graus).</summary>
    public double MaxArcStepDegrees { get; init; } = 10;

    /// <summary>Diferença máxima aceita entre raio inicial e final de um arco (mm).</summary>
    public double ArcRadiusTolerance { get; init; } = 0.01;
}

/// <summary>Transforma os blocos do parser em segmentos retos (arcos são interpolados).</summary>
public static class TrajectoryBuilder
{
    public static ToolPath Build(ParseResult parse, TrajectoryOptions? options = null)
    {
        options ??= new TrajectoryOptions();
        var segments = new List<Segment>();
        var diagnostics = new List<ParseDiagnostic>();
        Point3 position = options.StartPosition;

        foreach (Block block in parse.Blocks)
        {
            if (!block.HasMotion)
            {
                continue;
            }

            // Eixo não programado mantém a posição atual (coordenadas absolutas)
            var target = new Point3(block.X ?? position.X, block.Y ?? position.Y, block.Z ?? position.Z);

            if (block.IsArc)
            {
                // Arco sem I/J já foi reportado como erro pelo parser: só atualizamos a posição
                if (block.I.HasValue || block.J.HasValue)
                {
                    AddArc(segments, diagnostics, position, target, block, options);
                }
            }
            else
            {
                AddLine(segments, position, target, block);
            }

            position = target;
        }

        return new ToolPath(segments, diagnostics);
    }

    private static void AddLine(List<Segment> segments, Point3 start, Point3 end, Block block)
    {
        if (start == end)
        {
            return; // movimento nulo não gera segmento
        }

        segments.Add(new Segment(start, end, block.Motion, block.LineNumber, block.Feed));
    }

    /// <summary>
    /// Interpola um arco no plano XY (com Z linear = hélice) em segmentos retos.
    /// Centro = ponto inicial + (I, J). Se início e fim coincidem, é um círculo completo.
    /// </summary>
    private static void AddArc(
        List<Segment> segments,
        List<ParseDiagnostic> diagnostics,
        Point3 start,
        Point3 end,
        Block block,
        TrajectoryOptions options)
    {
        double centerX = start.X + (block.I ?? 0);
        double centerY = start.Y + (block.J ?? 0);
        double startRadius = Math.Sqrt(Sqr(start.X - centerX) + Sqr(start.Y - centerY));
        double endRadius = Math.Sqrt(Sqr(end.X - centerX) + Sqr(end.Y - centerY));

        if (startRadius < 1e-9)
        {
            diagnostics.Add(new ParseDiagnostic(block.LineNumber, 0, 0, DiagnosticSeverity.Error,
                "Arco com raio zero (I e J apontam para o próprio ponto inicial)."));
            return;
        }

        double radiusDifference = Math.Abs(endRadius - startRadius);
        if (radiusDifference > options.ArcRadiusTolerance)
        {
            diagnostics.Add(new ParseDiagnostic(block.LineNumber, 0, 0, DiagnosticSeverity.Warning,
                string.Create(CultureInfo.InvariantCulture,
                    $"Raio inicial ({startRadius:0.###}) e final ({endRadius:0.###}) do arco diferem em {radiusDifference:0.###} mm.")));
        }

        double startAngle = Math.Atan2(start.Y - centerY, start.X - centerX);
        double endAngle = Math.Atan2(end.Y - centerY, end.X - centerX);
        double sweep = endAngle - startAngle;

        // Ajusta o sentido: G02 (horário) tem varredura negativa, G03 positiva.
        // Varredura ~0 vira 360° (círculo completo).
        const double epsilon = 1e-9;
        if (block.Motion == MotionMode.ClockwiseArc)
        {
            if (sweep > -epsilon) sweep -= 2 * Math.PI;
        }
        else
        {
            if (sweep < epsilon) sweep += 2 * Math.PI;
        }

        // Passo angular: respeita o erro de corda e o ângulo máximo
        double radius = Math.Max(startRadius, endRadius);
        double stepByTolerance = radius > options.ArcTolerance
            ? 2 * Math.Acos(1 - options.ArcTolerance / radius)
            : Math.PI / 2;
        double maxStep = options.MaxArcStepDegrees * Math.PI / 180;
        double step = Math.Min(stepByTolerance, maxStep);
        int count = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / step));

        Point3 previous = start;
        for (int k = 1; k <= count; k++)
        {
            double t = (double)k / count;
            Point3 next;
            if (k == count)
            {
                next = end; // garante que o arco termina exatamente no ponto programado
            }
            else
            {
                double angle = startAngle + sweep * t;
                double r = startRadius + (endRadius - startRadius) * t; // absorve pequenas diferenças de raio
                next = new Point3(
                    centerX + r * Math.Cos(angle),
                    centerY + r * Math.Sin(angle),
                    start.Z + (end.Z - start.Z) * t);
            }

            segments.Add(new Segment(previous, next, block.Motion, block.LineNumber, block.Feed));
            previous = next;
        }
    }

    private static double Sqr(double v) => v * v;
}
