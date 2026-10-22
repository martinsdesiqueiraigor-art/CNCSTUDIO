using System.Text;

namespace CncStudio.Core.Generators;

/// <summary>Bolsão circular: anéis concêntricos do centro para a parede (G03, concordante), por camadas. Macro B: <c>O9015</c>.</summary>
public sealed class CircularPocketGenerator : ProgramGenerator
{
    private static readonly IReadOnlyList<GeneratorParameter> Fields =
    [
        new("X", "Centro X", "mm", 0, -9999, 9999),
        new("Y", "Centro Y", "mm", 0, -9999, 9999),
        new("B", "Diâmetro do bolsão", "mm", 40, 0.1, 9999),
        new("D", "Diâmetro da fresa", "mm", 10, 0.1, 500, Hint: "Precisa ter corte ao centro (entrada vertical)."),
        new("U", "Sobremetal na parede", "mm", 0.2, 0.01, 50, Hint: "Fica na parede; não há passe de acabamento."),
        new("E", "Passo entre anéis", "% do diâmetro", 70, 10, 100),
        new("Z", "Profundidade", "mm", -5, -500, -0.001, Hint: "Negativa (abaixo do topo)."),
        new("Q", "Passe em Z", "mm", 1, 0.01, 100),
        new("F", "Avanço", "mm/min", 100, 1, 10000),
        new("S", "Rotação", "rpm", 1800, 1, 30000),
        new("T", "Ferramenta (T)", "", 1, 1, 99, IsInteger: true, Hint: "Fresa de topo, corretor H igual ao T."),
    ];

    public override string Id => "circpocket";

    public override string Title => "Bolsão circular";

    public override string Description => "Bolsão redondo por camadas, em anéis do centro para a parede.";

    public override IReadOnlyList<GeneratorParameter> Parameters => Fields;

    protected override IEnumerable<string> ValidateRules(IReadOnlyDictionary<string, double> v)
    {
        double min = v["D"] + 2 * v["U"];
        if (v["B"] <= min)
        {
            yield return $"Diâmetro do bolsão: precisa ser maior que a fresa mais o sobremetal ({Fmt(min)} mm).";
        }
    }

    protected override string Build(IReadOnlyDictionary<string, double> v, GeneratorOutput output)
    {
        var sb = new StringBuilder();
        Header(sb, "BOLSAO CIRCULAR", 1015, (int)Math.Round(v["T"]), v["S"], $"FRESA DE TOPO D{Num(v["D"]).TrimEnd('.')}");

        if (output == GeneratorOutput.MacroCall)
        {
            return MacroProgram(sb, 9015, "06_BOLSAO_CIRCULAR_macro.nc",
                ('X', v["X"]), ('Y', v["Y"]), ('B', v["B"]), ('D', v["D"]), ('U', v["U"]), ('E', v["E"]),
                ('Z', v["Z"]), ('Q', v["Q"]), ('F', v["F"]), ('S', v["S"]));
        }

        double maxRadius = (v["B"] - v["D"]) / 2 - v["U"];
        double step = v["D"] * v["E"] / 100;
        int rings = (int)Math.Ceiling(maxRadius / step - 1e-9);

        sb.Append("G00 Z5.\n");
        Line(sb, "G00", ('X', v["X"]), ('Y', v["Y"]));
        double z = 0;
        while (z > v["Z"] + 1e-9)
        {
            z = Math.Max(z - v["Q"], v["Z"]);
            Line(sb, "G01", ('Z', z), ('F', v["F"]));
            for (int ring = 1; ring <= rings; ring++)
            {
                double r = Math.Min(ring * step, maxRadius);
                Line(sb, "G01", ('X', v["X"] + r), ('Y', v["Y"]), ('F', v["F"]));
                Line(sb, "G03", ('X', v["X"] + r), ('Y', v["Y"]), ('I', -r), ('J', 0), ('F', v["F"]));
            }

            Line(sb, "G01", ('X', v["X"]), ('Y', v["Y"]), ('F', v["F"]));
        }

        sb.Append("G00 Z5.\n");
        Footer(sb);
        sb.Append("%\n");
        return sb.ToString();
    }
}
