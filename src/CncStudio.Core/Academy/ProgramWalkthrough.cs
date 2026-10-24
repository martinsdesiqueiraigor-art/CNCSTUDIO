using System.Globalization;
using System.Text.RegularExpressions;
using CncStudio.Core.Dialects;
using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Academy;

/// <summary>Uma linha do programa de exemplo, com a explicação e o percurso que ela faz (vazio se não move).</summary>
public sealed record WalkLine(int Number, string Text, string Explanation, bool Rapid, IReadOnlyList<Point3> Path);

/// <summary>O programa lido linha a linha: explicação por linha, percurso e o bruto (bloco) para desenhar.</summary>
public sealed record Walkthrough(IReadOnlyList<WalkLine> Lines, Point3 StockMin, Point3 StockMax);

/// <summary>Monta a leitura "linha a linha" de um programa de exemplo: o que cada linha faz e por onde a ferramenta anda.</summary>
public static partial class ProgramWalkthrough
{
    [GeneratedRegex(@"BLOCO\s+(\d+(?:[.,]\d+)?)\s*X\s*(\d+(?:[.,]\d+)?)\s*X\s*(\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex StockRegex();

    public static Walkthrough Build(string program)
    {
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(program, Dialect.Fanuc);
        string[] source = program.Replace("\r\n", "\n").Split('\n');
        Dictionary<int, Move> byLine = [];
        foreach (Move m in analysis.ToolPath.Moves)
        {
            byLine.TryAdd(m.LineNumber, m);
        }

        LineExplainer explainer = new();
        List<WalkLine> lines = [];
        for (int i = 0; i < source.Length; i++)
        {
            string text = source[i].TrimEnd();
            if (text.Length == 0)
            {
                continue;
            }

            string explanation = explainer.Explain(text);
            List<Point3> path = [];
            bool rapid = false;
            if (byLine.TryGetValue(i + 1, out Move move))
            {
                path.Add(move.Start);
                move.AppendPolyline(path, 0.2);
                rapid = move.IsRapid;
            }

            lines.Add(new WalkLine(i + 1, text, explanation, rapid, path));
        }

        return new Walkthrough(lines, Stock(program, analysis).Min, Stock(program, analysis).Max);
    }

    /// <summary>Bloco do cabeçalho "(BRUTO: BLOCO 80 X 60 X 20 MM)" com zero no canto inferior esquerdo e Z0 no topo; sem isso, usa o envelope do percurso.</summary>
    private static (Point3 Min, Point3 Max) Stock(string program, ProgramAnalysis analysis)
    {
        Match m = StockRegex().Match(program);
        if (m.Success)
        {
            double Num(int g) => double.Parse(m.Groups[g].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
            return (new Point3(0, 0, -Num(3)), new Point3(Num(1), Num(2), 0));
        }

        Bounds3 b = analysis.ToolPath.Bounds;
        return (new Point3(b.Min.X, b.Min.Y, Math.Min(b.Min.Z, -10)), new Point3(b.Max.X, b.Max.Y, Math.Max(b.Max.Z, 0)));
    }
}

/// <summary>Explica cada linha de G-code em português, acompanhando os modos ativos (G00/G01, G90, avanço).</summary>
public sealed partial class LineExplainer
{
    [GeneratedRegex(@"([A-Z])\s*(-?\d*\.?\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex WordRegex();

    private string _motion = "";
    private bool _absolute = true;

    public string Explain(string rawLine)
    {
        string line = rawLine.Trim();
        if (line == "%")
        {
            return "Marca de início e fim do arquivo.";
        }

        if (line.StartsWith('('))
        {
            return "Comentário: o controle ignora; serve para quem lê o programa.";
        }

        string code = Regex.Replace(line, @"\([^)]*\)", " ").Trim();
        List<string> parts = [];
        double? x = null, y = null, z = null, feed = null;
        bool motionOnLine = false;

        foreach (Match w in WordRegex().Matches(code))
        {
            char letter = char.ToUpperInvariant(w.Groups[1].Value[0]);
            double value = double.Parse(w.Groups[2].Value, CultureInfo.InvariantCulture);
            switch (letter)
            {
                case 'O': parts.Add($"Número do programa: O{(int)value}."); break;
                case 'N': break;
                case 'X': x = value; break;
                case 'Y': y = value; break;
                case 'Z': z = value; break;
                case 'F': feed = value; break;
                case 'S': parts.Add($"Rotação do spindle: {Fmt(value)} rpm."); break;
                case 'T': parts.Add($"Seleciona a ferramenta T{(int)value}."); break;
                case 'H': parts.Add($"Usa o comprimento da ferramenta H{(int)value}."); break;
                case 'G':
                    string g = Gcode((int)Math.Round(value * 10), ref motionOnLine);
                    if (g.Length > 0) parts.Add(g);
                    break;
                case 'M': parts.Add(Mcode((int)value)); break;
            }
        }

        if (x is not null || y is not null || z is not null)
        {
            string where = string.Join(" ", new[] { x is null ? null : $"X{Fmt(x.Value)}", y is null ? null : $"Y{Fmt(y.Value)}", z is null ? null : $"Z{Fmt(z.Value)}" }.Where(s => s is not null));
            string how = _motion switch
            {
                "G00" => "em movimento rápido, sem cortar",
                "G01" => "em linha reta com avanço de trabalho",
                "G02" => "em arco horário",
                "G03" => "em arco anti-horário",
                _ => "(modo de movimento ainda não definido)",
            };
            string basis = _absolute ? "no ponto" : "deslocando";
            parts.Add($"Move a ferramenta {how} {basis} {where}." + (_motion == "G01" && feed is not null ? $" Avanço {Fmt(feed.Value)} mm/min." : ""));
            if (!motionOnLine && _motion.Length > 0)
            {
                parts.Add("(O modo de movimento vem da linha anterior: ele continua valendo.)");
            }
        }
        else if (feed is not null)
        {
            parts.Add($"Avanço de {Fmt(feed.Value)} mm/min.");
        }

        return parts.Count == 0 ? "Linha sem comando de movimento." : string.Join(" ", parts);
    }

    private string Gcode(int tenths, ref bool motion)
    {
        switch (tenths)
        {
            case 0: _motion = "G00"; motion = true; return "G00: movimento rápido (não corta).";
            case 10: _motion = "G01"; motion = true; return "G01: movimento linear com avanço.";
            case 20: _motion = "G02"; motion = true; return "G02: arco no sentido horário.";
            case 30: _motion = "G03"; motion = true; return "G03: arco no sentido anti-horário.";
            case 170: return "G17: trabalha no plano XY.";
            case 180: return "G18: trabalha no plano ZX.";
            case 190: return "G19: trabalha no plano YZ.";
            case 200: return "G20: medidas em polegadas.";
            case 210: return "G21: medidas em milímetros.";
            case 400: return "G40: cancela a compensação do raio.";
            case 430: return "G43: ativa a compensação do comprimento da ferramenta.";
            case 490: return "G49: cancela a compensação de comprimento.";
            case 800: return "G80: cancela ciclo fixo.";
            case 900: _absolute = true; return "G90: coordenadas absolutas (medidas a partir do zero).";
            case 910: _absolute = false; return "G91: coordenadas incrementais (medidas a partir da posição atual).";
            case 940: return "G94: avanço em mm/min.";
            case 950: return "G95: avanço em mm por rotação.";
            case 540: return "G54: escolhe o sistema de coordenadas de trabalho (zero da peça). Os valores precisam estar medidos na máquina.";
            case 550: return "G55: escolhe o sistema de coordenadas de trabalho G55.";
            case 560: return "G56: escolhe o sistema de coordenadas de trabalho G56.";
            case 570: return "G57: escolhe o sistema de coordenadas de trabalho G57.";
            case 580: return "G58: escolhe o sistema de coordenadas de trabalho G58.";
            case 590: return "G59: escolhe o sistema de coordenadas de trabalho G59.";
            default: return $"G{tenths / 10.0:0.#}: veja a ajuda do comando.";
        }
    }

    private static string Mcode(int m) => m switch
    {
        3 => "M03: liga o spindle no sentido horário.",
        4 => "M04: liga o spindle no sentido anti-horário.",
        5 => "M05: para o spindle.",
        6 => "M06: troca de ferramenta.",
        8 => "M08: liga o refrigerante.",
        9 => "M09: desliga o refrigerante.",
        2 => "M02: fim do programa.",
        30 => "M30: fim do programa (volta ao início).",
        _ => $"M{m:00}: função auxiliar da máquina.",
    };

    private static string Fmt(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}
