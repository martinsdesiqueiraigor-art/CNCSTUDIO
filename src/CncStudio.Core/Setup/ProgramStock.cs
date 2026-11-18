using System.Globalization;
using System.Text.RegularExpressions;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Simulation;

namespace CncStudio.Core.Setup;

/// <summary>
/// WEB-02/WEB-04: bruto do comentário do programa já no formato do setup, na posição que o programa indica. A mesma escrita no
/// torno e na fresa; o último número é sempre o comprimento (torno) ou a altura (fresa):
/// redondo "(BRUTO D100 X 60)", tubo "(BRUTO D100 X D70 X 100)", prisma ou barra retangular "(BRUTO 100 X 80 X 20)" e sextavado
/// "(BRUTO S60 X 100)" (chave 60; também "SEXT60"). Na fresa, cilindro, tubo e sextavado ficam em pé (eixo Z).
/// Posição: a linha "ZERO G54: CENTRO DA PECA, Z0 NO TOPO" (escrita pelo CAM) manda; sem ela, cada eixo é deduzido do corte
/// (corte dos dois lados do zero → bruto centrado no zero; só no positivo → canto no zero; só no negativo → canto oposto no zero;
/// corte todo acima de Z0 → Z0 na base). Torno: a barra no eixo Z com a face em Z0, como antes.
/// O tamanho vem do texto; a posição é a do programa, nunca um valor escondido: sem comentário de bruto, null.
/// </summary>
public static partial class ProgramStock
{
    /// <summary>Bruto do programa no formato do setup; null se nenhum comentário descreve um bruto.</summary>
    public static SetupStock? Read(string programText, MachineKind machine, Bounds3 cutting)
    {
        string[] lines = (programText ?? "").Split('\n');
        if (machine == MachineKind.Lathe)
        {
            foreach (string line in lines)
            {
                if (Shape(line) is { } bar)
                {
                    bar.X = 0;
                    bar.Y = 0;
                    bar.Z = -bar.Height; // face em Z0
                    return bar;
                }
            }

            return null;
        }

        (double fx, double fy, bool? base0) = ZeroOf(lines);
        foreach (string line in lines)
        {
            if (Shape(line) is not { } shape)
            {
                continue;
            }

            (double sx, double sy) = shape.Kind switch
            {
                SetupStockKind.Prism => (shape.Width, shape.Depth),
                SetupStockKind.Hexagon => (shape.Width * 2 / Math.Sqrt(3), shape.Width), // cantos em X, faces em Y
                _ => (shape.Diameter, shape.Diameter),
            };
            double ax = double.IsNaN(fx) ? Fraction(cutting, c => c.X) : fx;
            double ay = double.IsNaN(fy) ? Fraction(cutting, c => c.Y) : fy;
            bool atBase = base0 ?? (!cutting.IsEmpty && cutting.Min.Z >= -1 && cutting.Max.Z >= 1);

            // ponto do zero = menor canto + fração × medida → menor canto = −fração × medida
            double minX = -ax * sx, minY = -ay * sy;
            shape.Z = atBase ? 0 : -shape.Height;
            if (shape.Kind == SetupStockKind.Prism)
            {
                shape.X = minX;
                shape.Y = minY;
            }
            else
            {
                shape.X = minX + (sx / 2); // redondo, tubo e sextavado: (X, Y) é o centro
                shape.Y = minY + (sy / 2);
            }

            shape.X = Clean(shape.X);
            shape.Y = Clean(shape.Y);
            return shape;
        }

        return null;
    }

    private static double Clean(double v) => Math.Abs(v) < 1e-9 ? 0 : v;

    // fração do zero ao longo do eixo: 0 = canto menor, 0,5 = centro, 1 = canto maior
    private static double Fraction(Bounds3 cutting, Func<Point3, double> axis)
    {
        if (cutting.IsEmpty)
        {
            return 0;
        }

        double lo = axis(cutting.Min), hi = axis(cutting.Max);
        return lo >= -1 ? 0 : hi <= 1 ? 1 : 0.5;
    }

    // forma e medidas do bruto de uma linha (sem posição)
    private static SetupStock? Shape(string line)
    {
        Match tube = TubeRegex().Match(line);
        if (tube.Success)
        {
            double outer = Number(tube.Groups[1].Value), inner = Number(tube.Groups[2].Value), height = Number(tube.Groups[3].Value);
            return new SetupStock { Kind = SetupStockKind.Tube, Diameter = Math.Max(outer, inner), InnerDiameter = Math.Min(outer, inner), Height = height, Axis = SetupAxis.Z };
        }

        Match hex = HexRegex().Match(line);
        if (hex.Success)
        {
            return new SetupStock { Kind = SetupStockKind.Hexagon, Width = Number(hex.Groups[1].Value), Height = Number(hex.Groups[2].Value), Axis = SetupAxis.Z };
        }

        if (Stock.FromComment(line, MachineKind.Mill, Bounds3.Empty) is { } block)
        {
            return new SetupStock
            {
                Kind = SetupStockKind.Prism, Width = block.MaxX - block.MinX, Depth = block.MaxY - block.MinY, Height = block.MaxZ - block.MinZ,
            };
        }

        Match round = RoundRegex().Match(line);
        return round.Success
            ? new SetupStock { Kind = SetupStockKind.Round, Diameter = Number(round.Groups[1].Value), Height = Number(round.Groups[2].Value), Axis = SetupAxis.Z }
            : null;
    }

    // a linha "ZERO ..." do programa: frações em X e Y (NaN = não diz) e Z0 na base (null = não diz)
    private static (double X, double Y, bool? Base) ZeroOf(string[] lines)
    {
        foreach (string line in lines)
        {
            Match m = ZeroRegex().Match(line);
            if (!m.Success)
            {
                continue;
            }

            string where = m.Groups[1].Value.ToUpperInvariant();
            (double x, double y) = where switch
            {
                var w when w.StartsWith("CENTRO", StringComparison.Ordinal) => (0.5, 0.5),
                "CANTO INFERIOR ESQUERDO" => (0, 0),
                "CANTO INFERIOR DIREITO" => (1, 0),
                "CANTO SUPERIOR ESQUERDO" => (0, 1),
                "CANTO SUPERIOR DIREITO" => (1, 1),
                "MEIO DA ARESTA INFERIOR" => (0.5, 0),
                "MEIO DA ARESTA SUPERIOR" => (0.5, 1),
                "MEIO DA ARESTA ESQUERDA" => (0, 0.5),
                "MEIO DA ARESTA DIREITA" => (1, 0.5),
                _ => (double.NaN, double.NaN), // origem do desenho: a posição sai do corte
            };
            bool? atBase = m.Groups[2].Success ? m.Groups[2].Value.ToUpperInvariant().Contains("BASE", StringComparison.Ordinal) : null;
            return (x, y, atBase);
        }

        return (double.NaN, double.NaN, null);
    }

    private static double Number(string text) => double.Parse(text.Replace(',', '.'), CultureInfo.InvariantCulture);

    // BRUTO ... D50 D30 X 30 | Ø50/Ø30 X 30 | D50 X D30 X 30 (tubo: externo, interno, altura)
    [GeneratedRegex(@"(?:BRUTO|STOCK|BLANK)\b[^0-9\n)]*?(?:\bDIAM\.?|\bD|Ø)\s*(\d+(?:[.,]\d+)?)\s*(?:[X×*/]\s*)?(?:\bDIAM\.?|\bDI|\bD|Ø)\s*(\d+(?:[.,]\d+)?)\s*(?:X|×|\*)\s*(\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex TubeRegex();

    // BRUTO ... S60 X 100 | SEXT60 X 100 | SEXTAVADO 60 X 100 (chave, comprimento)
    [GeneratedRegex(@"(?:BRUTO|STOCK|BLANK)\b[^0-9\n)]*?(?:\bSEXT(?:AVADO)?\.?|\bS)\s*(\d+(?:[.,]\d+)?)\s*(?:X|×|\*)\s*(\d+(?:[.,]\d+)?)(?!\s*(?:X|×|\*)\s*\d)", RegexOptions.IgnoreCase)]
    private static partial Regex HexRegex();

    // BRUTO ... D50 X 30 | REDONDO Ø50 X 30
    [GeneratedRegex(@"(?:BRUTO|STOCK|BLANK)\b[^0-9\n)]*?(?:\bDIAM\.?|\bD|Ø)\s*(\d+(?:[.,]\d+)?)\s*(?:X|×|\*)\s*(\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex RoundRegex();

    // ZERO G54: CENTRO DA PECA, Z0 NO TOPO (linha do CAM; também aceita "(ZERO NO CENTRO, Z0 NA BASE)" escrito à mão)
    [GeneratedRegex(@"\bZERO\b[^\n)]*?(CENTRO|CANTO (?:INFERIOR|SUPERIOR) (?:ESQUERDO|DIREITO)|MEIO DA ARESTA (?:INFERIOR|SUPERIOR|ESQUERDA|DIREITA)|ORIGEM DO DESENHO)(?:[^\n)]*?Z0\s+(NO TOPO|NA BASE))?", RegexOptions.IgnoreCase)]
    private static partial Regex ZeroRegex();
}
