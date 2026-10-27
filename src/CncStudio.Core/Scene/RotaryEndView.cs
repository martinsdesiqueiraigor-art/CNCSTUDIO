using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Setup;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Scene;

/// <summary>Trecho do percurso na vista de frente para o eixo (coordenadas U, V em mm).</summary>
public readonly record struct EndViewSegment(double U1, double V1, double U2, double V2, bool Rapid);

/// <summary>
/// Vista 2D olhando ao longo do eixo rotativo (A: ao longo de X, plano YZ; B: ao longo de Y, plano XZ): o contorno do bruto gira
/// com o ângulo atual, a ferramenta fica parada e o percurso já executado aparece na superfície, girado junto com a peça.
/// Só dados, sem desenho: desktop e web desenham igual. U vai para a direita e V para cima.
/// </summary>
public sealed class RotaryEndView
{
    private RotaryEndView()
    {
    }

    /// <summary>Letra do eixo (A ou B).</summary>
    public char AxisLetter { get; private init; }

    /// <summary>Ângulo atual do eixo, em graus.</summary>
    public double Angle { get; private init; }

    /// <summary>Centro de giro (pivô), com o zero de peça somado.</summary>
    public (double U, double V) Center { get; private init; }

    /// <summary>Contorno do bruto (polígono fechado, já girado); vazio sem bruto informado.</summary>
    public IReadOnlyList<(double U, double V)> Outline { get; private init; } = [];

    /// <summary>Furo do tubo (polígono fechado); vazio nos outros brutos.</summary>
    public IReadOnlyList<(double U, double V)> Hole { get; private init; } = [];

    /// <summary>Marca do "topo" da peça parada (do centro à borda), para o giro ser visível num círculo.</summary>
    public (double U1, double V1, double U2, double V2) Mark { get; private init; }

    /// <summary>Percurso executado, na peça (girado com ela).</summary>
    public IReadOnlyList<EndViewSegment> Path { get; private init; } = [];

    /// <summary>Ponta da ferramenta (parada na máquina).</summary>
    public (double U, double V) Tool { get; private init; }

    public double ToolDiameter { get; private init; }

    /// <summary>Raio que enquadra bruto, percurso e ferramenta ao redor do centro.</summary>
    public double FitRadius { get; private init; }

    private const int CircleSegments = 72;

    /// <summary>Monta a vista; null quando o programa não gira a peça (nada a mostrar).</summary>
    /// <param name="path">Trajetória com o giro da mesa (<see cref="ToolPath.PartFrame"/>).</param>
    /// <param name="stock">Bruto do setup (null = sem contorno).</param>
    /// <param name="executed">Quantos movimentos já foram executados por inteiro.</param>
    /// <param name="partialTip">Ponta no movimento em andamento (o de índice "executed"), se houver.</param>
    /// <param name="toolDiameter">Diâmetro da ferramenta em uso.</param>
    public static RotaryEndView? Build(ToolPath path, SetupStock? stock, int executed, Point3? partialTip, double toolDiameter)
    {
        RotaryPartFrame? frame = path.PartFrame;
        if (frame is null || path.Moves.Count == 0 || !path.Modals.Any(m => m.Rotary.IsRotated))
        {
            return null;
        }

        bool useA = frame.HasA && (path.Modals.Any(m => Math.Abs(m.Rotary.A) > 1e-9) || !frame.HasB);
        char letter = useA ? 'A' : 'B';
        executed = Math.Clamp(executed, 0, path.Moves.Count);

        // Movimento que dá o estado atual: o em andamento ou o último executado
        int current = partialTip is not null && executed < path.Moves.Count ? executed : Math.Max(0, executed - 1);
        Move currentMove = path.Moves[current];
        RotaryAngles now = path.RotaryAt(current);
        if (partialTip is { } tipNow && current == executed)
        {
            double length = currentMove.Start.DistanceTo(currentMove.End);
            if (length > 1e-9)
            {
                now = RotaryAngles.Lerp(path.RotaryAtStart(current), now, Math.Clamp(currentMove.Start.DistanceTo(tipNow) / length, 0, 1));
            }
        }
        else if (executed == 0)
        {
            now = default;
        }

        Point3 offset = path.Modals[currentMove.ModalId].Offset;
        double[] matrix = frame.ToMachineMatrix(offset, now);
        (double, double) Uv(Point3 p) => useA ? (p.Y, p.Z) : (p.X, p.Z);
        (double, double) Rotated(Point3 partPoint) => Uv(Apply(matrix, partPoint));

        Point3 pivot = useA ? new Point3(0, frame.AY + offset.Y, frame.AZ + offset.Z) : new Point3(frame.BX + offset.X, 0, frame.BZ + offset.Z);
        (double, double) center = Uv(pivot);

        // Bruto: círculo (cilindro ao longo do eixo da vista) ou caixa; em coordenadas da máquina (zero de peça somado)
        SetupStock? resolved = stock?.Resolved(frame);
        var outline = new List<(double, double)>();
        var hole = new List<(double, double)>();
        double radius = 0;
        if (resolved is { IsRound: true } round && round.Validate() is null && round.Axis == (useA ? SetupAxis.X : SetupAxis.Y))
        {
            radius = round.Diameter / 2;
            for (int i = 0; i < CircleSegments; i++)
            {
                double t = 2 * Math.PI * i / CircleSegments;
                outline.Add(Rotated(PartPoint(useA, pivot, radius * Math.Sin(t), radius * Math.Cos(t))));
                if (round.Kind == SetupStockKind.Tube)
                {
                    double ri = round.InnerDiameter / 2;
                    hole.Add(Rotated(PartPoint(useA, pivot, ri * Math.Sin(t), ri * Math.Cos(t))));
                }
            }
        }
        else if (resolved is { IsRound: false } prism && prism.Validate() is null)
        {
            Bounds3 box = prism.Extent;
            Point3 o = offset;
            double u0 = useA ? box.Min.Y : box.Min.X, u1 = useA ? box.Max.Y : box.Max.X;
            foreach ((double du, double dv) in new[] { (u0, box.Min.Z), (u1, box.Min.Z), (u1, box.Max.Z), (u0, box.Max.Z) })
            {
                Point3 corner = useA ? new Point3(0, du + o.Y, dv + o.Z) : new Point3(du + o.X, 0, dv + o.Z);
                outline.Add(Rotated(corner));
                radius = Math.Max(radius, Math.Sqrt(Math.Pow(Uv(corner).Item1 - center.Item1, 2) + Math.Pow(Uv(corner).Item2 - center.Item2, 2)));
            }
        }

        // Marca do topo da peça parada
        double markLength = radius > 0 ? radius : 10;
        (double mu1, double mv1) = Rotated(PartPoint(useA, pivot, 0, 0));
        (double mu2, double mv2) = Rotated(PartPoint(useA, pivot, 0, markLength));

        // Percurso executado: cada ponto vai para a peça parada (desfaz o ângulo daquele instante) e volta com o ângulo atual
        var segments = new List<EndViewSegment>();
        int last = Math.Min(executed + (partialTip is not null ? 1 : 0), path.Moves.Count);
        var points = new List<Point3>();
        for (int i = 0; i < last; i++)
        {
            Move move = path.Moves[i];
            Point3 end = move.End;
            if (partialTip is { } tip && i == executed)
            {
                end = tip;
            }

            RotaryAngles a0 = path.RotaryAtStart(i), a1 = path.RotaryAt(i);
            Point3 off = path.Modals[move.ModalId].Offset;
            points.Clear();
            points.Add(move.Start);
            if (move.IsArc && !(i == executed && partialTip is not null))
            {
                move.AppendPolyline(points, 0.2);
            }
            else
            {
                points.Add(end);
            }

            double total = 0;
            for (int k = 1; k < points.Count; k++)
            {
                total += points[k - 1].DistanceTo(points[k]);
            }

            double travelled = 0;
            double fullLength = move.Start.DistanceTo(move.End);
            (double, double)? previous = null;
            for (int k = 0; k < points.Count; k++)
            {
                if (k > 0)
                {
                    travelled += points[k - 1].DistanceTo(points[k]);
                }

                double fraction = fullLength > 1e-9 ? Math.Clamp(travelled / (total <= 1e-9 ? 1 : total) * (move.Start.DistanceTo(end) / fullLength), 0, 1) : (k == 0 ? 0 : 1);
                RotaryAngles at = RotaryAngles.Lerp(a0, a1, fullLength > 1e-9 ? fraction : (k == 0 ? 0 : 1));
                Point3 partPoint = frame.ToPart(points[k], off, at);
                (double, double) uv = Rotated(partPoint);
                if (previous is { } p)
                {
                    segments.Add(new EndViewSegment(p.Item1, p.Item2, uv.Item1, uv.Item2, move.IsRapid));
                }

                previous = uv;
            }
        }

        Point3 toolPoint = partialTip ?? (executed <= 0 ? path.Moves[0].Start : path.Moves[Math.Min(executed, path.Moves.Count) - 1].End);
        (double, double) toolUv = Uv(toolPoint);

        double fit = radius;
        foreach (EndViewSegment s in segments)
        {
            fit = Math.Max(fit, Distance(center, (s.U1, s.V1)));
            fit = Math.Max(fit, Distance(center, (s.U2, s.V2)));
        }

        fit = Math.Max(fit, Distance(center, toolUv));
        return new RotaryEndView
        {
            AxisLetter = letter,
            Angle = useA ? now.A : now.B,
            Center = center,
            Outline = outline,
            Hole = hole,
            Mark = (mu1, mv1, mu2, mv2),
            Path = segments,
            Tool = toolUv,
            ToolDiameter = toolDiameter,
            FitRadius = Math.Max(fit, 5),
        };
    }

    private static double Distance((double, double) a, (double, double) b) =>
        Math.Sqrt(Math.Pow(a.Item1 - b.Item1, 2) + Math.Pow(a.Item2 - b.Item2, 2));

    // Ponto da peça parada no plano da vista: "side" para a direita do pivô e "up" para cima (em Z)
    private static Point3 PartPoint(bool useA, Point3 pivot, double side, double up) =>
        useA ? new Point3(pivot.X, pivot.Y + side, pivot.Z + up) : new Point3(pivot.X + side, pivot.Y, pivot.Z + up);

    private static Point3 Apply(double[] m, Point3 p) => new(
        (m[0] * p.X) + (m[4] * p.Y) + (m[8] * p.Z) + m[12],
        (m[1] * p.X) + (m[5] * p.Y) + (m[9] * p.Z) + m[13],
        (m[2] * p.X) + (m[6] * p.Y) + (m[10] * p.Z) + m[14]);
}
