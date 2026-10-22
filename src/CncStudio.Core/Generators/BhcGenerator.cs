using System.Text;

namespace CncStudio.Core.Generators;

/// <summary>
/// Furação em círculo (BHC): 2 a 36 furos equidistantes num círculo, a partir de um ângulo inicial (de +X, anti-horário).
/// Sem picada usa G81, com picada (Q &gt; 0) usa G83. Macro B correspondente: <c>O9010</c>.
/// </summary>
public sealed class BhcGenerator : ProgramGenerator
{
    private static readonly IReadOnlyList<GeneratorParameter> Fields =
    [
        new("X", "Centro X", "mm", 0, -9999, 9999),
        new("Y", "Centro Y", "mm", 0, -9999, 9999),
        new("D", "Diâmetro do círculo", "mm", 40, 0.1, 9999, Hint: "Diâmetro, não raio."),
        new("N", "Número de furos", "", 6, 2, 36, IsInteger: true),
        new("A", "Ângulo do 1º furo", "graus", 0, -360, 360, Hint: "Medido de +X, anti-horário."),
        new("Z", "Fundo", "mm", -12, -500, -0.001, Hint: "Negativo (abaixo do zero da peça)."),
        new("R", "Plano de retorno", "mm", 2, 0.1, 100, Hint: "Altura acima da peça onde o ciclo recua."),
        new("Q", "Picada (0 = sem)", "mm", 3, 0, 100, Hint: "0 usa G81; maior que 0 usa G83."),
        new("F", "Avanço", "mm/min", 90, 1, 10000),
        new("S", "Rotação", "rpm", 1100, 1, 30000),
        new("T", "Ferramenta (T)", "", 1, 1, 99, IsInteger: true, Hint: "Broca, com corretor H igual ao T."),
    ];

    public override string Id => "bhc";

    public override string Title => "Furação em círculo";

    public override string Description => "Furos equidistantes num círculo (BHC), com G81 ou G83.";

    public override IReadOnlyList<GeneratorParameter> Parameters => Fields;

    protected override IEnumerable<string> ValidateRules(IReadOnlyDictionary<string, double> v)
    {
        if (v["Q"] > 0 && v["Q"] > -v["Z"])
        {
            yield return "Picada: não pode ser maior que a profundidade.";
        }
    }

    protected override string Build(IReadOnlyDictionary<string, double> v, GeneratorOutput output)
    {
        int count = (int)Math.Round(v["N"]);
        bool peck = v["Q"] > 0;
        var sb = new StringBuilder();
        Header(sb, "BHC - FURACAO EM CIRCULO", 1010, (int)Math.Round(v["T"]), v["S"], $"BROCA - {count} FUROS");

        if (output == GeneratorOutput.MacroCall)
        {
            sb.Append("G65 P9010 ")
              .Append(Word('X', v["X"])).Append(' ').Append(Word('Y', v["Y"])).Append(' ')
              .Append(Word('D', v["D"])).Append(' ').Append(Word('H', count)).Append(' ')
              .Append(Word('A', v["A"])).Append(' ').Append(Word('Z', v["Z"])).Append(' ')
              .Append(Word('R', v["R"]));
            if (peck)
            {
                sb.Append(' ').Append(Word('Q', v["Q"]));
            }

            sb.Append(' ').Append(Word('F', v["F"])).Append('\n');
            Footer(sb);
            sb.Append('\n').Append(MacroText("01_BHC_macro.nc").Trim('%').Trim()).Append('\n').Append('%').Append('\n');
            return sb.ToString();
        }

        double radius = v["D"] / 2;
        for (int i = 0; i < count; i++)
        {
            double angle = (v["A"] + i * 360.0 / count) * Math.PI / 180;
            string x = Word('X', v["X"] + radius * Math.Cos(angle)), y = Word('Y', v["Y"] + radius * Math.Sin(angle));
            if (i == 0)
            {
                sb.Append("G00 ").Append(x).Append(' ').Append(y).Append('\n');
                sb.Append(peck ? "G83 " : "G81 ").Append(x).Append(' ').Append(y).Append(' ')
                  .Append(Word('Z', v["Z"])).Append(' ').Append(Word('R', v["R"]));
                if (peck)
                {
                    sb.Append(' ').Append(Word('Q', v["Q"]));
                }

                sb.Append(' ').Append(Word('F', v["F"])).Append('\n');
            }
            else
            {
                sb.Append(x).Append(' ').Append(y).Append('\n');
            }
        }

        sb.Append("G80\n");
        Footer(sb);
        sb.Append('%').Append('\n');
        return sb.ToString();
    }
}
