using CncStudio.Core.Generators;

namespace CncStudio.Core.Cam;

/// <summary>
/// Gravação enrolada num cilindro (4º eixo contínuo, modo calculado): o desenho "desenrolado" (u = ao longo do eixo, v = ao redor,
/// em mm na superfície) vira X (ou Y) e o ângulo A (ou B) juntos. A ferramenta fica na vertical, sobre a linha de centro,
/// e só Z fixo: sem TCP e sem G68.2.
/// </summary>
public sealed record WrapSetup(double Diameter, double StartAxial = 0, double StartAngle = 0, double Depth = 0.3, double PassDepth = 0.3,
    double Clearance = 3, double SurfaceFeed = 200, double PlungeFeed = 60);

public static class RotaryWrap
{
    /// <summary>
    /// Operação com os traços desenrolados. <paramref name="strokes"/> em (u, v) mm; v = 0 é a posição angular <see cref="WrapSetup.StartAngle"/>.
    /// O avanço de cada trecho é corrigido: o controle trata graus como milímetros no vetor do movimento, então F sai maior ou menor
    /// para a superfície andar em <see cref="WrapSetup.SurfaceFeed"/>. O eixo vem do perfil (A gira em torno de X, B em torno de Y), na mesa.
    /// </summary>
    public static OperationResult Engrave(string name, CamTool tool, MachineProfile machine, string letter,
        IReadOnlyList<IReadOnlyList<(double U, double V)>> strokes, WrapSetup w)
    {
        if (machine.Mode != RotaryOutputMode.Calculated)
        {
            return new OperationResult(null, "Este recurso usa o modo calculado; troque o modo no perfil da máquina.");
        }

        RotaryAxisConfig? axis = machine.AxisOf(letter);
        if (axis is null || axis.Carrier != RotaryCarrier.Table || letter is not ("A" or "B"))
        {
            return new OperationResult(null, "Gravação no cilindro: o perfil precisa ter o eixo A ou B na mesa.");
        }

        if (w.Diameter <= 0 || w.Depth <= 0 || w.PassDepth <= 0 || w.SurfaceFeed <= 0 || w.PlungeFeed <= 0 || w.Clearance < 0)
        {
            return new OperationResult(null, "Gravação no cilindro: diâmetro, profundidade, passe e avanços precisam ser maiores que zero.");
        }

        if (strokes.Count == 0 || strokes.All(s => s.Count == 0))
        {
            return new OperationResult(null, "Nada para gravar.");
        }

        bool a = letter == "A";
        double radius = w.Diameter / 2;
        double sign = axis.Reversed ? -1 : 1;
        double Angle(double v) => sign * (w.StartAngle + (v / radius * 180 / Math.PI));
        double surfaceZ = axis.PivotZ + radius;
        double safeZ = surfaceZ + w.Clearance;
        double hold = a ? axis.PivotY : axis.PivotX;
        int passes = Math.Max(1, (int)Math.Ceiling((w.Depth / w.PassDepth) - 1e-9));

        ToolMove Place(MoveKind kind, double u, double v, double z, double feed = 0) => new(kind, a ? w.StartAxial + u : hold, a ? hold : w.StartAxial + u, z, Feed: feed)
        {
            RotaryLetter = letter,
            RotaryAngle = Angle(v),
        };

        var moves = new List<ToolMove>();
        double min = double.MaxValue, max = double.MinValue;
        foreach (IReadOnlyList<(double U, double V)> stroke in strokes)
        {
            if (stroke.Count == 0)
            {
                continue;
            }

            foreach ((double _, double v) in stroke)
            {
                double ang = Angle(v);
                min = Math.Min(min, ang);
                max = Math.Max(max, ang);
            }

            for (int pass = 1; pass <= (stroke.Count == 1 ? 1 : passes); pass++)
            {
                double z = surfaceZ - (w.Depth * pass / (stroke.Count == 1 ? 1 : passes));
                (double u0, double v0) = stroke[0];
                moves.Add(Place(MoveKind.Rapid, u0, v0, safeZ));
                moves.Add(Place(MoveKind.Feed, u0, v0, z, w.PlungeFeed));
                for (int i = 1; i < stroke.Count; i++)
                {
                    (double u1, double v1) = stroke[i];
                    (double up, double vp) = stroke[i - 1];
                    double du = u1 - up, dv = v1 - vp;
                    double surface = Math.Sqrt((du * du) + (dv * dv));
                    double degrees = dv / radius * 180 / Math.PI;
                    double commanded = Math.Sqrt((du * du) + (degrees * degrees));
                    if (surface < 1e-9)
                    {
                        continue;
                    }

                    moves.Add(Place(MoveKind.Feed, u1, v1, z, Math.Max(1, w.SurfaceFeed * commanded / surface)));
                }

                moves.Add(Place(MoveKind.Rapid, stroke[^1].U, stroke[^1].V, safeZ));
            }
        }

        if (!axis.Contains(min) || !axis.Contains(max))
        {
            return new OperationResult(null, $"A gravação passa de {min:0.#}° a {max:0.#}°, fora do curso do eixo {letter} ({axis.MinAngle:0.#}° a {axis.MaxAngle:0.#}°). Reduza o texto ou o ângulo inicial.");
        }

        var warnings = new List<string>
        {
            $"Gravação enrolada: A e {(a ? "X" : "Y")} andam juntos. O avanço de cada trecho foi corrigido para o controle tratar graus como milímetros; confira no ar, com avanço baixo.",
            "Ferramenta de ponta fina na vertical, sobre a linha de centro do cilindro: o ponto zero de Z é o centro do eixo menos o raio.",
        };
        return new OperationResult(new CamOperation(name, tool, moves, null, warnings), null);
    }

    /// <summary>Traços de um texto em fonte de traço único, desenrolados: letras ao longo do eixo, altura ao redor, centralizadas em v = 0.</summary>
    public static IReadOnlyList<IReadOnlyList<(double U, double V)>> TextStrokes(string text, double height, double widthPercent = 100, double gapPercent = 20, bool centerAxial = false)
    {
        string t = StrokeFont.Normalize(text).Trim();
        double scale = height / StrokeFont.CapHeight;
        double sx = scale * widthPercent / 100, sy = scale, gap = gapPercent / 100 * height;
        var placed = new List<(StrokeFont.Glyph Glyph, double U)>();
        double u = 0;
        foreach (char c in t)
        {
            if (!StrokeFont.TryGet(c, out StrokeFont.Glyph? glyph))
            {
                continue;
            }

            placed.Add((glyph, u));
            u += (glyph.Width * sx) + gap;
        }

        double total = Math.Max(0, u - gap);
        double shift = centerAxial ? -total / 2 : 0;
        var result = new List<IReadOnlyList<(double U, double V)>>();
        foreach ((StrokeFont.Glyph glyph, double gu) in placed)
        {
            foreach (IReadOnlyList<StrokeFont.Seg> stroke in glyph.Strokes)
            {
                var pts = StrokeFont.Flatten(stroke, sx, sy, 0.01);
                if (pts.Count > 0)
                {
                    result.Add(pts.Select(p => (shift + gu + p.X, p.Y - (height / 2))).ToList());
                }
            }
        }

        return result;
    }
}
