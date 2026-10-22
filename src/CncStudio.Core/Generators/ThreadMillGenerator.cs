using System.Text;

namespace CncStudio.Core.Generators;

/// <summary>
/// Fresamento de rosca helicoidal (interna ou externa, direita ou esquerda) com fresa de rosca, em passe único na profundidade.
/// Concordante: interna G03, externa G02. Sem arco de entrada (aproximação radial). Macro B: <c>O9017</c>.
/// Direita sobe em G03 e desce em G02; esquerda é o contrário.
/// </summary>
public sealed class ThreadMillGenerator : ProgramGenerator
{
    private static readonly IReadOnlyList<GeneratorParameter> Fields =
    [
        new("X", "Centro X", "mm", 0, -9999, 9999),
        new("Y", "Centro Y", "mm", 0, -9999, 9999),
        new("B", "Diâmetro nominal da rosca", "mm", 16, 0.1, 9999, Hint: "Ex.: 16 para M16."),
        new("D", "Diâmetro da fresa de rosca", "mm", 10, 0.1, 500),
        new("K", "Passo", "mm", 2, 0.05, 20, Hint: "Ex.: 2 para M16 grossa."),
        new("Z", "Comprimento da rosca", "mm", -15, -500, -0.001, Hint: "Negativo, do topo para baixo."),
        new("H", "Tipo (0 = interna, 1 = externa)", "", 0, 0, 1, IsInteger: true),
        new("E", "Mão (0 = direita, 1 = esquerda)", "", 0, 0, 1, IsInteger: true),
        new("F", "Avanço", "mm/min", 120, 1, 10000, Hint: "Na trajetória do centro da fresa."),
        new("S", "Rotação", "rpm", 3000, 1, 30000),
        new("T", "Ferramenta (T)", "", 1, 1, 99, IsInteger: true, Hint: "Fresa de rosca, corretor H igual ao T."),
    ];

    public override string Id => "threadmill";

    public override string Title => "Rosca helicoidal";

    public override string Description => "Fresamento de rosca interna ou externa, direita ou esquerda, com fresa de rosca.";

    public override IReadOnlyList<GeneratorParameter> Parameters => Fields;

    protected override IEnumerable<string> ValidateRules(IReadOnlyDictionary<string, double> v)
    {
        if (-v["Z"] < v["K"])
        {
            yield return "Comprimento da rosca: precisa ter pelo menos um passo.";
        }

        if (v["H"] == 0 && v["D"] > v["B"] - 1.25 * v["K"])
        {
            yield return $"Fresa: para rosca interna precisa ser menor que o nominal menos 1,25 × passo ({Fmt(v["B"] - 1.25 * v["K"])} mm).";
        }
    }

    protected override string Build(IReadOnlyDictionary<string, double> v, GeneratorOutput output)
    {
        bool external = v["H"] == 1, left = v["E"] == 1;
        var sb = new StringBuilder();
        string kind = external ? "EXTERNA" : "INTERNA";
        Header(sb, "ROSCA HELICOIDAL " + kind, 1017, (int)Math.Round(v["T"]), v["S"], $"FRESA DE ROSCA D{Num(v["D"]).TrimEnd('.')}");

        if (output == GeneratorOutput.MacroCall)
        {
            return MacroProgram(sb, 9017, "08_ROSCA_HELICOIDAL_macro.nc",
                ('X', v["X"]), ('Y', v["Y"]), ('B', v["B"]), ('D', v["D"]), ('K', v["K"]), ('Z', v["Z"]),
                ('H', v["H"]), ('E', v["E"]), ('F', v["F"]), ('S', v["S"]));
        }

        double radius = external ? (v["B"] + v["D"]) / 2 : (v["B"] - v["D"]) / 2;
        bool up = external ? left : !left;           // interna: direita sobe; externa: direita desce
        int turns = (int)Math.Ceiling(-v["Z"] / v["K"] - 1e-9);
        double zStart = up ? v["Z"] : v["Z"] + turns * v["K"];
        double xStart = v["X"] + radius, xApproach = xStart + 5;
        string arc = external ? "G02" : "G03";

        sb.Append("G00 Z5.\n");
        if (external)
        {
            Line(sb, "G00", ('X', xApproach), ('Y', v["Y"]));
        }
        else
        {
            Line(sb, "G00", ('X', v["X"]), ('Y', v["Y"]));
        }

        Line(sb, "G00", ('Z', zStart));
        Line(sb, "G01", ('X', xStart), ('F', v["F"]));
        for (int turn = 1; turn <= turns; turn++)
        {
            double z = up ? zStart + turn * v["K"] : zStart - turn * v["K"];
            Line(sb, arc, ('X', xStart), ('Y', v["Y"]), ('I', -radius), ('J', 0), ('Z', z), ('F', v["F"]));
        }

        if (external)
        {
            Line(sb, "G01", ('X', xApproach), ('Y', v["Y"]), ('F', v["F"]));
        }
        else
        {
            Line(sb, "G01", ('X', v["X"]), ('Y', v["Y"]), ('F', v["F"]));
        }

        sb.Append("G00 Z5.\n");
        Footer(sb);
        sb.Append("%\n");
        return sb.ToString();
    }
}
