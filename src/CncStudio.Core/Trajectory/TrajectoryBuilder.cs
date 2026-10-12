using System.Globalization;
using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;

namespace CncStudio.Core.Trajectory;

/// <summary>Parâmetros de construção da trajetória.</summary>
public sealed class TrajectoryOptions
{
    /// <summary>Posição da ferramenta antes do primeiro movimento.</summary>
    public Point3 StartPosition { get; init; } = Point3.Origin;

    /// <summary>Diferença máxima aceita entre raio inicial e final de um arco (mm).</summary>
    public double ArcRadiusTolerance { get; init; } = 0.01;
}

/// <summary>Transforma os blocos do parser em movimentos (um por bloco com deslocamento).</summary>
public static class TrajectoryBuilder
{
    public static ToolPath Build(ParseResult parse, TrajectoryOptions? options = null)
    {
        options ??= new TrajectoryOptions();
        var moves = new List<Move>(parse.Blocks.Count);
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
                    AddArc(moves, diagnostics, position, target, block, options);
                }
            }
            else if (position != target)
            {
                // Movimento nulo não gera nada
                moves.Add(new Move(position, target, block.Motion, block.LineNumber, block.Feed));
            }

            position = target;
        }

        moves.TrimExcess();
        return new ToolPath(moves, diagnostics);
    }

    /// <summary>
    /// Calcula a geometria do arco no plano XY (com Z linear = hélice).
    /// Centro = ponto inicial + (I, J). Se início e fim coincidem, é um círculo completo.
    /// </summary>
    private static void AddArc(
        List<Move> moves,
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

        moves.Add(new Move(start, end, block.Motion, block.LineNumber, block.Feed)
        {
            CenterX = centerX,
            CenterY = centerY,
            StartRadius = startRadius,
            EndRadius = endRadius, // a diferença de raio é absorvida ao longo do arco
            StartAngle = startAngle,
            Sweep = sweep,
        });
    }

    private static double Sqr(double v) => v * v;
}
