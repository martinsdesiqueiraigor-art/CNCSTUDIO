using System.Text;

namespace CncStudio.Core.Generators;

/// <summary>
/// Furo por interpolação helicoidal: a fresa desce em hélice (G03, concordante) com raio (furo − fresa) / 2 e dá uma volta
/// final no fundo. Serve para furos até 2× o diâmetro da fresa (acima disso sobra um núcleo: use o bolsão circular). Macro B: <c>O9016</c>.
/// </summary>
public sealed class HelicalHoleGenerator : ProgramGenerator
{
    private static readonly IReadOnlyList<GeneratorParameter> Fields =
    [
        new("X", "Centro X", "mm", 0, -9999, 9999),
        new("Y", "Centro Y", "mm", 0, -9999, 9999),
        new("B", "Diâmetro do furo", "mm", 14, 0.1, 9999),
        new("D", "Diâmetro da fresa", "mm", 10, 0.1, 500, Hint: "O furo fica entre a fresa e 2× a fresa."),
        new("W", "Z inicial (topo do bruto)", "mm", 0, -500, 40, Hint: "Onde o bruto começa; a ferramenta desce daqui. Se o material vier maior, informe aqui."),
        new("Z", "Profundidade", "mm", -10, -500, 50, Hint: "Negativa (abaixo do topo)."),
        new("Q", "Descida por volta", "mm", 1, 0.01, 50, Hint: "Passo da hélice."),
        new("F", "Avanço", "mm/min", 150, 1, 10000),
        new("S", "Rotação", "rpm", 2500, 1, 30000),
        new("T", "Ferramenta (T)", "", 1, 1, 99, IsInteger: true, Hint: "Fresa de topo, corretor H igual ao T."),
    ];

    public override string Id => "helixhole";

    public override string Title => "Furo helicoidal";

    public override string Description => "Abre um furo com fresa de topo descendo em hélice (interpolação), sem broca.";

    public override IReadOnlyList<GeneratorParameter> Parameters => Fields;

    protected override IEnumerable<string> ValidateRules(IReadOnlyDictionary<string, double> v)
    {
        if (v["Z"] >= v["W"])
        {
            yield return "Profundidade: precisa ficar abaixo do Z inicial.";
        }

        if (v["B"] <= v["D"] + 0.02)
        {
            yield return "Diâmetro do furo: precisa ser maior que a fresa.";
        }

        if (v["B"] > 2 * v["D"])
        {
            yield return "Diâmetro do furo: acima de 2× a fresa sobra um núcleo; use o bolsão circular.";
        }
    }

    protected override string Build(IReadOnlyDictionary<string, double> v, GeneratorOutput output)
    {
        var sb = new StringBuilder();
        Header(sb, "FURO HELICOIDAL", 1016, (int)Math.Round(v["T"]), v["S"], $"FRESA DE TOPO D{Num(v["D"]).TrimEnd('.')}");

        if (output == GeneratorOutput.MacroCall)
        {
            return MacroProgram(sb, 9016, "07_FURO_HELICOIDAL_macro.nc",
                ('X', v["X"]), ('Y', v["Y"]), ('W', v["W"]), ('B', v["B"]), ('D', v["D"]), ('Z', v["Z"]), ('Q', v["Q"]), ('F', v["F"]), ('S', v["S"]));
        }

        double radius = (v["B"] - v["D"]) / 2;
        double top = v["W"];
        int turns = (int)Math.Ceiling((top - v["Z"]) / v["Q"] - 1e-9);
        double xs = v["X"] + radius;

        Line(sb, "G00", ('Z', Math.Max(5, v["W"] + 5)));
        Line(sb, "G00", ('X', v["X"]), ('Y', v["Y"]));
        Line(sb, "G00", ('Z', top + 1));
        Line(sb, "G01", ('Z', top), ('F', v["F"]));
        Line(sb, "G01", ('X', xs), ('F', v["F"]));
        for (int turn = 1; turn <= turns; turn++)
        {
            double z = Math.Max(top - turn * v["Q"], v["Z"]);
            Line(sb, "G03", ('X', xs), ('Y', v["Y"]), ('I', -radius), ('J', 0), ('Z', z), ('F', v["F"]));
        }

        Line(sb, "G03", ('X', xs), ('Y', v["Y"]), ('I', -radius), ('J', 0), ('F', v["F"]));
        Line(sb, "G01", ('X', v["X"]), ('Y', v["Y"]), ('F', v["F"]));
        Line(sb, "G00", ('Z', Math.Max(5, v["W"] + 5)));
        Footer(sb);
        sb.Append("%\n");
        return sb.ToString();
    }
}
