using System.Text;

namespace CncStudio.Core.Generators;

/// <summary>
/// Gravação de texto: escreve letras, números e alguns sinais em traço único (fonte própria <see cref="StrokeFont"/>),
/// com altura, espaçamento, ângulo e alinhamento. Cada traço desce até a profundidade (em passes, indo e voltando)
/// e sobe ao terminar. Só gera programa pronto: a fonte fica escrita no próprio programa, sem macro.
/// </summary>
public sealed class TextEngravingGenerator : ProgramGenerator
{
    private const int MaxChars = 60;

    private static readonly IReadOnlyList<GeneratorParameter> Fields =
    [
        new("X", "Ponto X", "mm", 0, -9999, 9999, Hint: "Onde o texto começa (ou o centro, ou o fim, conforme o alinhamento)."),
        new("Y", "Ponto Y (linha de base)", "mm", 0, -9999, 9999, Hint: "A base das letras maiúsculas fica neste Y."),
        new("H", "Altura das letras", "mm", 10, 0.5, 500, Hint: "Altura de uma maiúscula."),
        new("L", "Largura das letras", "%", 100, 30, 300, Hint: "100 = proporção normal; menos estreita, mais alarga."),
        new("E", "Espaço entre letras", "% da altura", 20, 0, 300),
        new("A", "Ângulo", "graus", 0, -360, 360, Hint: "0 = texto ao longo de X; anti-horário."),
        new("N", "Alinhamento", "0, 1 ou 2", 0, 0, 2, IsInteger: true, Hint: "0 = começa no ponto; 1 = centralizado no ponto; 2 = termina no ponto."),
        new("W", "Z inicial (topo do bruto)", "mm", 0, -500, 40, Hint: "Onde o bruto começa; a ferramenta desce daqui."),
        new("Z", "Profundidade", "mm", -0.3, -50, 50, Hint: "Negativa (abaixo do topo). Gravação costuma ser rasa."),
        new("Q", "Passe em Z", "mm", 0.3, 0.01, 50, Hint: "Se a profundidade for maior, faz mais passes, indo e voltando no traço."),
        new("R", "Avanço de descida", "mm/min", 60, 1, 10000),
        new("F", "Avanço de gravação", "mm/min", 200, 1, 10000),
        new("S", "Rotação", "rpm", 8000, 1, 30000),
        new("T", "Ferramenta (T)", "", 1, 1, 99, IsInteger: true, Hint: "Ferramenta de gravação (ponta em V ou fresa fina), corretor H igual ao T."),
    ];

    private static readonly IReadOnlyList<GeneratorTextParameter> TextFields =
    [
        new("TX", "Texto", "CASILLAS", MaxChars, "Letras (sem acento: Ã vira A), números e . , : ! - _ = + / ( ). Uma linha só."),
    ];

    public override string Id => "text";

    public override string Title => "Texto (gravação)";

    public override string Description => "Grava letras e números em traço único, com altura, espaçamento, ângulo e alinhamento.";

    public override IReadOnlyList<GeneratorParameter> Parameters => Fields;

    public override IReadOnlyList<GeneratorTextParameter> TextParameters => TextFields;

    public override bool SupportsMacro => false;

    protected override IEnumerable<string> ValidateRules(IReadOnlyDictionary<string, double> v)
    {
        if (v["Z"] >= v["W"])
        {
            yield return "Profundidade: precisa ficar abaixo do Z inicial.";
        }
    }

    protected override IEnumerable<string> ValidateTexts(IReadOnlyDictionary<string, double> values, IReadOnlyDictionary<string, string> texts)
    {
        texts.TryGetValue("TX", out string? raw);
        string text = StrokeFont.Normalize(raw ?? "").Trim();
        if (text.Length == 0)
        {
            yield return "Texto: digite o que será gravado.";
            yield break;
        }

        if (text.Length > MaxChars)
        {
            yield return $"Texto: no máximo {MaxChars} caracteres.";
        }

        var bad = text.Where(c => !StrokeFont.TryGet(c, out _)).Distinct().ToList();
        if (bad.Count > 0)
        {
            yield return "Texto: não consigo desenhar " + string.Join(" ", bad.Select(c => $"\"{c}\"")) + ". Aceito: " + StrokeFont.Supported + ".";
        }
    }

    protected override string Build(IReadOnlyDictionary<string, double> values, GeneratorOutput output) =>
        BuildWithTexts(values, new Dictionary<string, string>(), output);

    protected override string BuildWithTexts(IReadOnlyDictionary<string, double> v, IReadOnlyDictionary<string, string> texts, GeneratorOutput output)
    {
        texts.TryGetValue("TX", out string? raw);
        string text = StrokeFont.Normalize(raw ?? "").Trim();
        double scale = v["H"] / StrokeFont.CapHeight;
        double sx = scale * v["L"] / 100, sy = scale;
        double gap = v["E"] / 100 * v["H"];

        // posição (em mm, ao longo do texto) de cada letra e largura total
        List<(StrokeFont.Glyph Glyph, double U)> placed = [];
        double u = 0;
        foreach (char c in text)
        {
            StrokeFont.TryGet(c, out StrokeFont.Glyph glyph);
            placed.Add((glyph, u));
            u += glyph.Width * sx + gap;
        }

        double total = u - gap;
        double start = (int)Math.Round(v["N"]) switch { 1 => -total / 2, 2 => -total, _ => 0 };

        double depth = v["W"] - v["Z"];
        int passes = Math.Max(1, (int)Math.Ceiling(depth / v["Q"] - 1e-9));
        double clear = v["W"] + 2;
        double tolerance = 0.01;

        var sb = new StringBuilder();
        string note = "GRAVACAO T" + ((int)Math.Round(v["T"])).ToString(System.Globalization.CultureInfo.InvariantCulture);
        Header(sb, "GRAVACAO DE TEXTO", 1020, (int)Math.Round(v["T"]), v["S"], note);
        sb.Append("(TEXTO: ").Append(text.Replace('(', ' ').Replace(')', ' ')).Append(")\n");
        Line(sb, "G00", ('Z', clear));

        double lastFeed = double.NaN;
        void Feed(StringBuilder b, double f)
        {
            if (f != lastFeed)
            {
                b.Append(' ').Append(Word('F', f));
                lastFeed = f;
            }
        }

        (double X, double Y) At(double uu, double vv) => Rotate(v["X"], v["Y"], start + uu, vv, v["A"]);

        foreach ((StrokeFont.Glyph glyph, double gu) in placed)
        {
            foreach (IReadOnlyList<StrokeFont.Seg> stroke in glyph.Strokes)
            {
                var pts = StrokeFont.Flatten(stroke, sx, sy, tolerance);
                if (pts.Count == 0)
                {
                    continue;
                }

                var path = pts.Select(p => At(gu + p.X, p.Y)).ToList();
                var first = path[0];
                Line(sb, "G00", ('X', first.X), ('Y', first.Y));
                bool forward = true;
                int strokePasses = path.Count == 1 ? 1 : passes; // ponto: uma só descida, até o fundo
                for (int pass = 1; pass <= strokePasses; pass++)
                {
                    double z = v["W"] - depth * pass / strokePasses;
                    sb.Append("G01 ").Append(Word('Z', z));
                    Feed(sb, v["R"]);
                    sb.Append('\n');
                    if (path.Count == 1)
                    {
                        break;
                    }

                    for (int i = 1; i < path.Count; i++)
                    {
                        var p = path[forward ? i : path.Count - 1 - i];
                        sb.Append("G01 ").Append(Word('X', p.X)).Append(' ').Append(Word('Y', p.Y));
                        Feed(sb, v["F"]);
                        sb.Append('\n');
                    }

                    forward = !forward;
                }

                Line(sb, "G00", ('Z', clear));
            }
        }

        Footer(sb);
        sb.Append("%\n");
        return sb.ToString();
    }
}
