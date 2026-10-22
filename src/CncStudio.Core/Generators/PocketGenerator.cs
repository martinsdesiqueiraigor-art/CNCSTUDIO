using System.Text;

namespace CncStudio.Core.Generators;

/// <summary>
/// Bolsão retangular: passes em Z e varredura em zigue-zague com fresa de topo. O sobremetal <c>U</c> fica nas paredes e
/// não há passe de acabamento. Entrada vertical (fresa com corte ao centro). Macro B correspondente: <c>O9011</c>.
/// </summary>
public sealed class PocketGenerator : ProgramGenerator
{
    private static readonly IReadOnlyList<GeneratorParameter> Fields =
    [
        new("X", "Centro X", "mm", 0, -9999, 9999),
        new("Y", "Centro Y", "mm", 0, -9999, 9999),
        new("I", "Comprimento (X)", "mm", 30, 0.1, 9999),
        new("J", "Largura (Y)", "mm", 20, 0.1, 9999),
        new("Z", "Profundidade", "mm", -4, -500, -0.001, Hint: "Negativa (abaixo do topo da peça)."),
        new("Q", "Passe em Z", "mm", 1, 0.01, 100),
        new("D", "Diâmetro da fresa", "mm", 10, 0.1, 500, Hint: "Precisa ter corte ao centro (entrada vertical)."),
        new("U", "Sobremetal nas paredes", "mm", 0.2, 0.01, 50, Hint: "Fica nas paredes; não há passe de acabamento."),
        new("F", "Avanço", "mm/min", 100, 1, 10000),
        new("S", "Rotação", "rpm", 1800, 1, 30000),
        new("T", "Ferramenta (T)", "", 1, 1, 99, IsInteger: true, Hint: "Fresa de topo, com corretor H igual ao T."),
    ];

    public override string Id => "pocket";

    public override string Title => "Bolsão retangular";

    public override string Description => "Bolsão retangular por camadas, varredura em zigue-zague.";

    public override IReadOnlyList<GeneratorParameter> Parameters => Fields;

    protected override IEnumerable<string> ValidateRules(IReadOnlyDictionary<string, double> v)
    {
        double min = v["D"] + 2 * v["U"];
        if (v["I"] <= min)
        {
            yield return $"Comprimento: precisa ser maior que a fresa mais o sobremetal ({Fmt(min)} mm).";
        }

        if (v["J"] <= min)
        {
            yield return $"Largura: precisa ser maior que a fresa mais o sobremetal ({Fmt(min)} mm).";
        }
    }

    protected override string Build(IReadOnlyDictionary<string, double> v, GeneratorOutput output)
    {
        var sb = new StringBuilder();
        Header(sb, "BOLSAO RETANGULAR", 1011, (int)Math.Round(v["T"]), v["S"], $"FRESA DE TOPO D{Num(v["D"]).TrimEnd('.')}");

        if (output == GeneratorOutput.MacroCall)
        {
            sb.Append("G65 P9011 ")
              .Append(Word('X', v["X"])).Append(' ').Append(Word('Y', v["Y"])).Append(' ')
              .Append(Word('I', v["I"])).Append(' ').Append(Word('J', v["J"])).Append(' ')
              .Append(Word('Z', v["Z"])).Append(' ').Append(Word('Q', v["Q"])).Append(' ')
              .Append(Word('D', v["D"])).Append(' ').Append(Word('U', v["U"])).Append(' ')
              .Append(Word('F', v["F"])).Append(' ').Append(Word('S', v["S"])).Append('\n');
            Footer(sb);
            sb.Append('\n').Append(MacroText("02_BOLSAO_RETANGULAR_macro.nc").Trim('%').Trim()).Append('\n').Append('%').Append('\n');
            return sb.ToString();
        }

        // Mesma varredura da macro O9011: linhas espaçadas de meio diâmetro, a última encostada no limite
        double xLeft = v["X"] - v["I"] / 2 + v["D"] / 2 + v["U"], xRight = v["X"] + v["I"] / 2 - v["D"] / 2 - v["U"];
        double yLow = v["Y"] - v["J"] / 2 + v["D"] / 2 + v["U"], yHigh = v["Y"] + v["J"] / 2 - v["D"] / 2 - v["U"];
        double step = v["D"] * 0.5;
        int rows = (int)Math.Ceiling((yHigh - yLow) / step - 1e-9);
        string feed = Word('F', v["F"]);

        sb.Append("G00 Z5.\n");
        sb.Append("G00 ").Append(Word('X', xLeft)).Append(' ').Append(Word('Y', yLow)).Append('\n');
        double z = 0;
        while (z > v["Z"] + 1e-9)
        {
            z = Math.Max(z - v["Q"], v["Z"]);
            sb.Append("G01 ").Append(Word('Z', z)).Append(' ').Append(feed).Append('\n');
            bool toRight = true;
            for (int row = 0; row <= rows; row++)
            {
                double y = Math.Min(yLow + row * step, yHigh);
                sb.Append("G01 ").Append(Word('Y', y)).Append(' ').Append(feed).Append('\n');
                sb.Append("G01 ").Append(Word('X', toRight ? xRight : xLeft)).Append(' ').Append(feed).Append('\n');
                toRight = !toRight;
            }

            sb.Append("G01 ").Append(Word('X', xLeft)).Append(' ').Append(Word('Y', yLow)).Append(' ').Append(feed).Append('\n');
        }

        sb.Append("G00 Z5.\n");
        Footer(sb);
        sb.Append('%').Append('\n');
        return sb.ToString();
    }
}
