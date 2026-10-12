using CncStudio.Core.Dialects;
using CncStudio.Core.Parsing;

namespace CncStudio.Core.Execution;

/// <summary>Tipo de máquina: muda o significado de vários códigos (G90, G98/G99, X em diâmetro...).</summary>
public enum MachineKind
{
    /// <summary>Fresadora / centro de usinagem.</summary>
    Mill,

    /// <summary>Torno (eixos X e Z, X programado em diâmetro).</summary>
    Lathe,
}

/// <summary>
/// Descobre se o programa é de torno olhando os códigos usados.
///
/// Regra: é torno se aparece algum código típico de torno (G71/G70/G76, U/W, G96,
/// CYCLE95/97, DIAMON, LIMS...) e nenhum sinal claro de fresa (eixo Y, M06, G43,
/// ciclos de furação CYCLE81–83). Na dúvida, fresa.
/// </summary>
public static class MachineDetector
{
    private static readonly HashSet<string> SiemensLatheKeywords =
        ["CYCLE93", "CYCLE94", "CYCLE95", "CYCLE96", "CYCLE97", "CYCLE98", "DIAMON", "DIAMOF", "DIAM90", "LIMS"];

    private static readonly HashSet<string> SiemensMillKeywords = ["CYCLE81", "CYCLE82", "CYCLE83", "CYCLE800", "POCKET3", "POCKET4"];

    public static MachineKind Detect(ParseResult parse)
    {
        bool siemens = parse.Dialect == Dialect.Siemens;
        int lathe = 0;
        bool mill = false;

        foreach (NcBlock block in parse.Blocks)
        {
            foreach (AddressWord word in block.Words)
            {
                switch (word.Letter)
                {
                    case 'Y':
                        mill = true;
                        break;
                    case 'U' or 'W' when !siemens:
                        lathe++;
                        break;
                    case 'M' when word.Value == 6:
                        mill = true;
                        break;
                    case 'G':
                        int code = (int)word.Value;
                        if (code is 43 or 44)
                        {
                            mill = true;
                        }
                        else if (code == 96 || (!siemens && code is 70 or 71 or 72 or 76))
                        {
                            lathe++;
                        }

                        break;
                }
            }

            foreach (Statement statement in block.Statements)
            {
                if (statement.Kind == StatementKind.Expression && statement.Text.TrimStart().StartsWith("Y=", StringComparison.OrdinalIgnoreCase))
                {
                    mill = true; // Siemens "Y=R1"
                }

                if (statement.Kind != StatementKind.Keyword)
                {
                    continue;
                }

                if (SiemensLatheKeywords.Contains(statement.Name))
                {
                    lathe++;
                }
                else if (SiemensMillKeywords.Contains(statement.Name))
                {
                    mill = true;
                }
            }
        }

        return lathe > 0 && !mill ? MachineKind.Lathe : MachineKind.Mill;
    }
}
