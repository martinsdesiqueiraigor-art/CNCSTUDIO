using System.Text;

namespace CncStudio.Core.Generators;

/// <summary>
/// Faceamento: zigue-zague sobre um retângulo, com a fresa entrando e saindo por fora da peça, passes em Z e sobreposição
/// pelo passo lateral (% do diâmetro). Macro B: <c>O9013</c>.
/// </summary>
public sealed class FacingGenerator : ProgramGenerator
{
    private static readonly IReadOnlyList<GeneratorParameter> Fields =
    [
        new("X", "Centro X", "mm", 0, -9999, 9999),
        new("Y", "Centro Y", "mm", 0, -9999, 9999),
        new("I", "Comprimento (X)", "mm", 100, 0.1, 9999),
        new("J", "Largura (Y)", "mm", 60, 0.1, 9999),
        new("D", "Diâmetro da fresa", "mm", 50, 0.1, 500, Hint: "Fresa de facear ou de topo."),
        new("E", "Passo lateral", "% do diâmetro", 70, 10, 100),
        new("W", "Z inicial (topo do bruto)", "mm", 0, -500, 40, Hint: "Onde o bruto começa; a ferramenta desce daqui. Se o material vier maior, informe aqui."),
        new("Z", "Z final (face pronta)", "mm", -1, -50, 50, Hint: "Altura final da face. Do Z inicial até aqui, em passes."),
        new("Q", "Passe em Z", "mm", 0.5, 0.01, 50),
        new("F", "Avanço", "mm/min", 400, 1, 10000),
        new("S", "Rotação", "rpm", 2000, 1, 30000),
        new("T", "Ferramenta (T)", "", 1, 1, 99, IsInteger: true, Hint: "Corretor H igual ao T."),
    ];

    public override string Id => "facing";

    public override string Title => "Faceamento";

    public override string Description => "Aplaina a face de um retângulo em zigue-zague, entrando e saindo por fora da peça.";

    public override IReadOnlyList<GeneratorParameter> Parameters => Fields;

    protected override IEnumerable<string> ValidateRules(IReadOnlyDictionary<string, double> v)
    {
        if (v["Z"] >= v["W"])
        {
            yield return "Profundidade: precisa ficar abaixo do Z inicial.";
        }

        yield break;
    }

    protected override string Build(IReadOnlyDictionary<string, double> v, GeneratorOutput output)
    {
        var sb = new StringBuilder();
        Header(sb, "FACEAMENTO", 1013, (int)Math.Round(v["T"]), v["S"], $"FRESA D{Num(v["D"]).TrimEnd('.')}");

        if (output == GeneratorOutput.MacroCall)
        {
            return MacroProgram(sb, 9013, "04_FACEAMENTO_macro.nc",
                ('X', v["X"]), ('Y', v["Y"]), ('W', v["W"]), ('I', v["I"]), ('J', v["J"]), ('D', v["D"]), ('E', v["E"]),
                ('Z', v["Z"]), ('Q', v["Q"]), ('F', v["F"]), ('S', v["S"]));
        }

        double step = v["D"] * v["E"] / 100;
        double xStart = v["X"] - v["I"] / 2 - v["D"] / 2 - 2, xEnd = v["X"] + v["I"] / 2 + v["D"] / 2 + 2;
        double yLow = v["Y"] - v["J"] / 2 + v["D"] / 2, yHigh = v["Y"] + v["J"] / 2 - v["D"] / 2;
        int rows = 0;
        if (v["J"] > v["D"])
        {
            rows = (int)Math.Ceiling((yHigh - yLow) / step - 1e-9);
        }
        else
        {
            yLow = yHigh = v["Y"];
        }

        Line(sb, "G00", ('Z', Math.Max(5, v["W"] + 5)));
        Line(sb, "G00", ('X', xStart), ('Y', yLow));
        double z = v["W"];
        while (z > v["Z"] + 1e-9)
        {
            z = Math.Max(z - v["Q"], v["Z"]);
            Line(sb, "G01", ('Z', z), ('F', v["F"]));
            bool toRight = true;
            for (int row = 0; row <= rows; row++)
            {
                double y = Math.Min(yLow + row * step, yHigh);
                Line(sb, "G01", ('Y', y), ('F', v["F"]));
                Line(sb, "G01", ('X', toRight ? xEnd : xStart), ('F', v["F"]));
                toRight = !toRight;
            }

            Line(sb, "G01", ('X', xStart), ('Y', yLow), ('F', v["F"]));
        }

        Line(sb, "G00", ('Z', Math.Max(5, v["W"] + 5)));
        Footer(sb);
        sb.Append("%\n");
        return sb.ToString();
    }
}
