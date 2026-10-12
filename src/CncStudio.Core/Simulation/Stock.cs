using System.Globalization;
using System.Text.RegularExpressions;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;

namespace CncStudio.Core.Simulation;

/// <summary>
/// Bruto informado pelo usuário, em coordenadas do programa (zero de peça aplicado).
/// Fresa: bloco de MinX..MaxX, MinY..MaxY, MinZ (fundo)..MaxZ (topo).
/// Torno: barra de raio MaxX (MinX = 0) entre MinZ e MaxZ (face).
/// </summary>
public sealed partial record Stock(MachineKind Machine, double MinX, double MaxX, double MinY, double MaxY, double MinZ, double MaxZ)
{
    /// <summary>Bloco da fresa.</summary>
    public static Stock Block(double minX, double maxX, double minY, double maxY, double minZ, double maxZ) =>
        new(MachineKind.Mill, Math.Min(minX, maxX), Math.Max(minX, maxX), Math.Min(minY, maxY), Math.Max(minY, maxY),
            Math.Min(minZ, maxZ), Math.Max(minZ, maxZ));

    /// <summary>Barra do torno: diâmetro × comprimento, com a face em "faceZ" (normalmente Z0 ou um pouco acima).</summary>
    public static Stock Bar(double diameter, double length, double faceZ = 0) =>
        new(MachineKind.Lathe, 0, Math.Abs(diameter) / 2, 0, 0, faceZ - Math.Abs(length), faceZ);

    public double Diameter => MaxX * 2;
    public double Length => MaxZ - MinZ;

    public bool IsValid => Machine == MachineKind.Lathe
        ? MaxX > 0 && MaxZ > MinZ
        : MaxX > MinX && MaxY > MinY && MaxZ > MinZ;

    public Bounds3 Bounds => Machine == MachineKind.Lathe
        ? Bounds3.Empty.Include(new Point3(0, 0, MinZ)).Include(new Point3(MaxX, 0, MaxZ))
        : Bounds3.Empty.Include(new Point3(MinX, MinY, MinZ)).Include(new Point3(MaxX, MaxY, MaxZ));

    /// <summary>"100 × 80 × 20 mm (X 0 a 100 · Y 0 a 80 · Z −20 a 0)" / "Ø50 × 80 mm (face em Z 0)".</summary>
    public string Describe(CultureInfo? culture = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        string N(double v) => v.ToString("0.###", culture);
        return Machine == MachineKind.Lathe
            ? $"Ø{N(Diameter)} × {N(Length)} mm (face em Z {N(MaxZ)})"
            : $"{N(MaxX - MinX)} × {N(MaxY - MinY)} × {N(MaxZ - MinZ)} mm (X {N(MinX)} a {N(MaxX)} · Y {N(MinY)} a {N(MaxY)} · Z {N(MinZ)} a {N(MaxZ)})";
    }

    /// <summary>
    /// Bruto sugerido a partir de um comentário do programa, ex.: "(BRUTO 100 X 80 X 20)",
    /// "(BRUTO D50 X 70)", "; STOCK 120x60x25". O tamanho vem do texto; a posição é palpite:
    /// na fresa, canto no zero se o corte está todo em X/Y positivos, senão centrado no corte;
    /// topo em Z0. No torno, face em Z0. Null se o comentário não descreve um bruto.
    /// </summary>
    public static Stock? FromComment(string comment, MachineKind machine, Bounds3 cutting)
    {
        Match bar = BarRegex().Match(comment);
        if (machine == MachineKind.Lathe)
        {
            return bar.Success ? Bar(Number(bar.Groups[1].Value), Number(bar.Groups[2].Value)) : null;
        }

        Match block = BlockRegex().Match(comment);
        if (!block.Success)
        {
            return null;
        }

        double length = Number(block.Groups[1].Value);
        double width = Number(block.Groups[2].Value);
        double height = Number(block.Groups[3].Value);

        double minX, minY;
        if (cutting.IsEmpty || (cutting.Min.X >= -1 && cutting.Min.Y >= -1))
        {
            minX = 0;
            minY = 0;
        }
        else
        {
            minX = (cutting.Min.X + cutting.Max.X) / 2 - length / 2;
            minY = (cutting.Min.Y + cutting.Max.Y) / 2 - width / 2;
        }

        return Block(minX, minX + length, minY, minY + width, -height, 0);
    }

    private static double Number(string text) => double.Parse(text.Replace(',', '.'), CultureInfo.InvariantCulture);

    // BRUTO|STOCK|BLANK ... D|Ø|DIAM 50 X 70
    [GeneratedRegex(@"(?:BRUTO|STOCK|BLANK)\b[^0-9DØ]*(?:D|Ø|DIAM\.?)\s*(\d+(?:[.,]\d+)?)\s*(?:X|×|\*)\s*(\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex BarRegex();

    // BRUTO|STOCK|BLANK ... 100 X 80 X 20
    [GeneratedRegex(@"(?:BRUTO|STOCK|BLANK)\b\D*?(\d+(?:[.,]\d+)?)\s*(?:X|×|\*)\s*(\d+(?:[.,]\d+)?)\s*(?:X|×|\*)\s*(\d+(?:[.,]\d+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex BlockRegex();
}
