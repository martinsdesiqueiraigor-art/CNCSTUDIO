using System.Text;

namespace CncStudio.Core.Generators;

/// <summary>
/// Rasgo reto com ângulo: linhas ao longo do rasgo, lado a lado se a fresa for mais estreita que ele; as extremidades ficam
/// com o raio da fresa (use fresa de mesmo diâmetro da largura para extremidade totalmente redonda). Macro B: <c>O9014</c>.
/// </summary>
public sealed class SlotGenerator : ProgramGenerator
{
    private static readonly IReadOnlyList<GeneratorParameter> Fields =
    [
        new("X", "Centro X", "mm", 0, -9999, 9999),
        new("Y", "Centro Y", "mm", 0, -9999, 9999),
        new("I", "Comprimento total", "mm", 40, 0.1, 9999, Hint: "Ponta a ponta, já com as extremidades."),
        new("J", "Largura", "mm", 10, 0.1, 9999),
        new("A", "Ângulo", "graus", 0, -360, 360, Hint: "0 = ao longo de X; anti-horário."),
        new("Z", "Profundidade", "mm", -5, -500, -0.001, Hint: "Negativa (abaixo do topo)."),
        new("Q", "Passe em Z", "mm", 1, 0.01, 100),
        new("D", "Diâmetro da fresa", "mm", 10, 0.1, 500, Hint: "Até a largura do rasgo; com corte ao centro."),
        new("F", "Avanço", "mm/min", 100, 1, 10000),
        new("S", "Rotação", "rpm", 1800, 1, 30000),
        new("T", "Ferramenta (T)", "", 1, 1, 99, IsInteger: true, Hint: "Fresa de topo, corretor H igual ao T."),
    ];

    public override string Id => "slot";

    public override string Title => "Rasgo";

    public override string Description => "Rasgo reto (chaveta) com extremidades redondas, por camadas.";

    public override IReadOnlyList<GeneratorParameter> Parameters => Fields;

    protected override IEnumerable<string> ValidateRules(IReadOnlyDictionary<string, double> v)
    {
        if (v["D"] > v["J"])
        {
            yield return "Fresa: o diâmetro não pode ser maior que a largura do rasgo.";
        }

        if (v["I"] <= v["J"])
        {
            yield return "Comprimento: precisa ser maior que a largura.";
        }
    }

    protected override string Build(IReadOnlyDictionary<string, double> v, GeneratorOutput output)
    {
        var sb = new StringBuilder();
        Header(sb, "RASGO", 1014, (int)Math.Round(v["T"]), v["S"], $"FRESA DE TOPO D{Num(v["D"]).TrimEnd('.')}");

        if (output == GeneratorOutput.MacroCall)
        {
            return MacroProgram(sb, 9014, "05_RASGO_macro.nc",
                ('X', v["X"]), ('Y', v["Y"]), ('I', v["I"]), ('J', v["J"]), ('A', v["A"]), ('Z', v["Z"]),
                ('Q', v["Q"]), ('D', v["D"]), ('F', v["F"]), ('S', v["S"]));
        }

        double uStart = -(v["I"] - v["D"]) / 2, uEnd = (v["I"] - v["D"]) / 2;
        double vStart = -(v["J"] - v["D"]) / 2, vEnd = (v["J"] - v["D"]) / 2;
        double step = v["D"] * 0.5;
        int rows = vEnd > 0 ? (int)Math.Ceiling((vEnd - vStart) / step - 1e-9) : 0;
        (double X, double Y) At(double u, double w) => Rotate(v["X"], v["Y"], u, w, v["A"]);

        sb.Append("G00 Z5.\n");
        var start = At(uStart, vStart);
        Line(sb, "G00", ('X', start.X), ('Y', start.Y));
        double z = 0;
        while (z > v["Z"] + 1e-9)
        {
            z = Math.Max(z - v["Q"], v["Z"]);
            Line(sb, "G01", ('Z', z), ('F', v["F"]));
            double u = uStart;
            bool forward = true;
            for (int row = 0; row <= rows; row++)
            {
                double w = Math.Min(vStart + row * step, vEnd);
                if (row > 0)
                {
                    var across = At(u, w);
                    Line(sb, "G01", ('X', across.X), ('Y', across.Y), ('F', v["F"]));
                }

                u = forward ? uEnd : uStart;
                var along = At(u, w);
                Line(sb, "G01", ('X', along.X), ('Y', along.Y), ('F', v["F"]));
                forward = !forward;
            }

            var back = At(uStart, vStart);
            Line(sb, "G01", ('X', back.X), ('Y', back.Y), ('F', v["F"]));
        }

        sb.Append("G00 Z5.\n");
        Footer(sb);
        sb.Append("%\n");
        return sb.ToString();
    }
}
