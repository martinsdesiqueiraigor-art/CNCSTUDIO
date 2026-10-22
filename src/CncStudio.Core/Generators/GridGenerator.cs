using System.Text;

namespace CncStudio.Core.Generators;

/// <summary>Furação em grade: colunas × linhas de furos, com ângulo, percorrendo em serpentina. Macro B: <c>O9012</c>.</summary>
public sealed class GridGenerator : ProgramGenerator
{
    private static readonly IReadOnlyList<GeneratorParameter> Fields =
    [
        new("X", "1º furo, X", "mm", 0, -9999, 9999),
        new("Y", "1º furo, Y", "mm", 0, -9999, 9999),
        new("I", "Colunas (em X)", "", 4, 1, 100, IsInteger: true),
        new("J", "Linhas (em Y)", "", 3, 1, 100, IsInteger: true),
        new("U", "Espaçamento em X", "mm", 20, 0.001, 9999),
        new("V", "Espaçamento em Y", "mm", 15, 0.001, 9999),
        new("A", "Ângulo da grade", "graus", 0, -360, 360, Hint: "Gira a grade em volta do 1º furo."),
        new("W", "Z inicial (topo do bruto)", "mm", 0, -500, 40, Hint: "Onde o bruto começa; a ferramenta desce daqui. Se o material vier maior, informe aqui."),
        new("Z", "Fundo", "mm", -10, -500, 50, Hint: "Negativo (abaixo do zero da peça)."),
        new("R", "Folga acima do bruto", "mm", 2, 0.1, 100, Hint: "O ciclo recua a Z inicial + esta folga."),
        new("Q", "Picada (0 = sem)", "mm", 0, 0, 100, Hint: "0 usa G81; maior que 0 usa G83."),
        new("F", "Avanço", "mm/min", 90, 1, 10000),
        new("S", "Rotação", "rpm", 1100, 1, 30000),
        new("T", "Ferramenta (T)", "", 1, 1, 99, IsInteger: true, Hint: "Broca, com corretor H igual ao T."),
    ];

    public override string Id => "grid";

    public override string Title => "Furação em grade";

    public override string Description => "Matriz de furos (colunas × linhas) com ângulo, em serpentina, com G81 ou G83.";

    public override IReadOnlyList<GeneratorParameter> Parameters => Fields;

    protected override IEnumerable<string> ValidateRules(IReadOnlyDictionary<string, double> v)
    {
        double holes = v["I"] * v["J"];
        if (holes < 2)
        {
            yield return "Grade: informe pelo menos 2 furos (colunas × linhas).";
        }

        if (holes > 500)
        {
            yield return $"Grade: {Fmt(holes)} furos é demais; o limite é 500.";
        }

        if (v["Z"] >= v["W"])
        {
            yield return "Fundo: precisa ficar abaixo do Z inicial.";
        }

        if (v["Q"] > 0 && v["Q"] > v["W"] - v["Z"])
        {
            yield return "Picada: não pode ser maior que a profundidade.";
        }
    }

    protected override string Build(IReadOnlyDictionary<string, double> v, GeneratorOutput output)
    {
        int columns = (int)Math.Round(v["I"]), rows = (int)Math.Round(v["J"]);
        bool peck = v["Q"] > 0;
        var sb = new StringBuilder();
        Header(sb, "FURACAO EM GRADE", 1012, (int)Math.Round(v["T"]), v["S"], $"BROCA - GRADE {columns} X {rows}");

        if (output == GeneratorOutput.MacroCall)
        {
            List<(char, double)> words =
            [
                ('X', v["X"]), ('Y', v["Y"]), ('I', columns), ('J', rows), ('U', v["U"]), ('V', v["V"]),
                ('A', v["A"]), ('Z', v["Z"]), ('R', v["W"] + v["R"]),
            ];
            if (peck)
            {
                words.Add(('Q', v["Q"]));
            }

            words.Add(('F', v["F"]));
            return MacroProgram(sb, 9012, "03_GRADE_macro.nc", [.. words]);
        }

        bool first = true;
        for (int row = 0; row < rows; row++)
        {
            for (int k = 0; k < columns; k++)
            {
                int column = row % 2 == 0 ? k : columns - 1 - k;
                (double x, double y) = Rotate(v["X"], v["Y"], column * v["U"], row * v["V"], v["A"]);
                if (first)
                {
                    Line(sb, "G00", ('X', x), ('Y', y));
                    List<(char, double)> cycle = [('X', x), ('Y', y), ('Z', v["Z"]), ('R', v["W"] + v["R"])];
                    if (peck)
                    {
                        cycle.Add(('Q', v["Q"]));
                    }

                    cycle.Add(('F', v["F"]));
                    Line(sb, peck ? "G83" : "G81", [.. cycle]);
                    first = false;
                }
                else
                {
                    Line(sb, "", ('X', x), ('Y', y));
                }
            }
        }

        sb.Append("G80\n");
        Footer(sb);
        sb.Append("%\n");
        return sb.ToString();
    }
}
