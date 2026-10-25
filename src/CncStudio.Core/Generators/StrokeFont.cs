using System.Text;

namespace CncStudio.Core.Generators;

/// <summary>
/// Fonte de traço único (uma linha por letra) para gravação: cada letra é desenhada com retas e arcos de elipse,
/// numa grade de altura 10 (maiúsculas e números) e largura própria por letra. Fonte própria do Casillas Code
/// (desenhada à mão para este projeto, sem copiar fontes de terceiros).
/// </summary>
public static class StrokeFont
{
    /// <summary>Altura de uma maiúscula na grade da fonte.</summary>
    public const double CapHeight = 10;

    /// <summary>Peça de um traço: ponto (<c>L</c>) ou arco de elipse (<c>A</c>) a partir do ponto atual.</summary>
    public readonly record struct Seg(char Kind, double A, double B, double C, double D, double E, double F);

    /// <summary>Uma letra: largura (sem o espaço entre letras) e seus traços (o lápis levanta entre um e outro).</summary>
    public sealed record Glyph(double Width, IReadOnlyList<IReadOnlyList<Seg>> Strokes);

    private static Seg L(double x, double y) => new('L', x, y, 0, 0, 0, 0);

    /// <summary>Arco de elipse: centro, raios x e y, ângulo inicial e final em graus (positivo = anti-horário).</summary>
    private static Seg A(double cx, double cy, double rx, double ry, double a0, double a1) => new('A', cx, cy, rx, ry, a0, a1);

    private static IReadOnlyList<Seg> S(params Seg[] segs) => segs;

    private static Glyph G(double width, params IReadOnlyList<Seg>[] strokes) => new(width, strokes);

    private static readonly Dictionary<char, Glyph> Glyphs = new()
    {
        [' '] = G(4),
        ['A'] = G(6, S(L(0, 0), L(3, 10), L(6, 0)), S(L(1.05, 3.5), L(4.95, 3.5))),
        ['B'] = G(6, S(L(0, 0), L(0, 10), L(3.5, 10), A(3.5, 7.5, 2.5, 2.5, 90, -90)), S(L(0, 5), L(3.5, 5), A(3.5, 2.5, 2.5, 2.5, 90, -90), L(0, 0))),
        ['C'] = G(6, S(A(3, 5, 3, 5, 40, 320))),
        ['D'] = G(6, S(L(0, 0), L(0, 10), L(2, 10), A(2, 5, 4, 5, 90, -90), L(0, 0))),
        ['E'] = G(5, S(L(5, 10), L(0, 10), L(0, 0), L(5, 0)), S(L(0, 5), L(4, 5))),
        ['F'] = G(5, S(L(5, 10), L(0, 10), L(0, 0)), S(L(0, 5), L(4, 5))),
        ['G'] = G(6, S(A(3, 5, 3, 5, 45, 360), L(3.5, 5))),
        ['H'] = G(6, S(L(0, 0), L(0, 10)), S(L(6, 0), L(6, 10)), S(L(0, 5), L(6, 5))),
        ['I'] = G(2, S(L(0, 10), L(2, 10)), S(L(1, 10), L(1, 0)), S(L(0, 0), L(2, 0))),
        ['J'] = G(5, S(L(5, 10), L(5, 3), A(2.5, 3, 2.5, 3, 0, -180))),
        ['K'] = G(6, S(L(0, 10), L(0, 0)), S(L(6, 10), L(0, 4.5)), S(L(2, 6.5), L(6, 0))),
        ['L'] = G(5, S(L(0, 10), L(0, 0), L(5, 0))),
        ['M'] = G(8, S(L(0, 0), L(0, 10), L(4, 3), L(8, 10), L(8, 0))),
        ['N'] = G(6, S(L(0, 0), L(0, 10), L(6, 0), L(6, 10))),
        ['O'] = G(6, S(A(3, 5, 3, 5, 90, 450))),
        ['P'] = G(6, S(L(0, 0), L(0, 10), L(3.5, 10), A(3.5, 7.25, 2.5, 2.75, 90, -90), L(0, 4.5))),
        ['Q'] = G(6, S(A(3, 5, 3, 5, 90, 450)), S(L(4, 3), L(6, 0))),
        ['R'] = G(6, S(L(0, 0), L(0, 10), L(3.5, 10), A(3.5, 7.25, 2.5, 2.75, 90, -90), L(0, 4.5)), S(L(3.2, 4.5), L(6, 0))),
        ['S'] = G(6, S(A(3, 7.5, 3, 2.5, 35, 270), A(3, 2.5, 3, 2.5, 90, -145))),
        ['T'] = G(6, S(L(0, 10), L(6, 10)), S(L(3, 10), L(3, 0))),
        ['U'] = G(6, S(L(0, 10), L(0, 3), A(3, 3, 3, 3, 180, 360), L(6, 10))),
        ['V'] = G(6, S(L(0, 10), L(3, 0), L(6, 10))),
        ['W'] = G(8, S(L(0, 10), L(2, 0), L(4, 7), L(6, 0), L(8, 10))),
        ['X'] = G(6, S(L(0, 10), L(6, 0)), S(L(0, 0), L(6, 10))),
        ['Y'] = G(6, S(L(0, 10), L(3, 5), L(6, 10)), S(L(3, 5), L(3, 0))),
        ['Z'] = G(6, S(L(0, 10), L(6, 10), L(0, 0), L(6, 0))),
        ['0'] = G(6, S(A(3, 5, 3, 5, 90, 450))),
        ['1'] = G(4, S(L(0, 8), L(2, 10), L(2, 0))),
        ['2'] = G(6, S(A(3, 7, 3, 3, 160, -30), L(0, 0), L(6, 0))),
        ['3'] = G(6, S(A(3, 7.5, 2.8, 2.5, 150, -90), A(3, 2.5, 3, 2.5, 90, -150))),
        ['4'] = G(6, S(L(4.5, 0), L(4.5, 10), L(0, 3), L(6, 3))),
        ['5'] = G(6, S(L(5.5, 10), L(0.8, 10), A(3, 3.3, 3, 3.3, 125, -155))),
        ['6'] = G(6, S(A(3, 3.2, 3, 3.2, 90, 450)), S(L(0, 3.2), L(0.4, 6.5), L(2, 9.3), L(4.5, 10))),
        ['7'] = G(6, S(L(0, 10), L(6, 10), L(2, 0))),
        ['8'] = G(6, S(A(3, 7.5, 2.6, 2.5, 90, 450)), S(A(3, 2.7, 3, 2.7, 90, 450))),
        ['9'] = G(6, S(A(3, 6.8, 3, 3.2, -90, 270)), S(L(6, 6.8), L(5.6, 3.5), L(4, 0.7), L(1.5, 0))),
        ['-'] = G(4, S(L(0.5, 5), L(3.5, 5))),
        ['_'] = G(6, S(L(0, 0), L(6, 0))),
        ['='] = G(6, S(L(0, 3.5), L(6, 3.5)), S(L(0, 6.5), L(6, 6.5))),
        ['+'] = G(6, S(L(1, 5), L(5, 5)), S(L(3, 2), L(3, 8))),
        ['/'] = G(5, S(L(0, 0), L(5, 10))),
        ['.'] = G(2, S(L(1, 0))),
        [','] = G(2, S(L(1.2, 0.5), L(0.4, -1.5))),
        [':'] = G(2, S(L(1, 1.5)), S(L(1, 6.5))),
        ['!'] = G(2, S(L(1, 10), L(1, 3)), S(L(1, 0))),
        ['('] = G(3, S(A(3, 5, 3, 6, 100, 260))),
        [')'] = G(3, S(A(0, 5, 3, 6, -80, 80))),
    };

    /// <summary>Caracteres que a fonte sabe desenhar (letras sem acento, números e alguns sinais).</summary>
    public static string Supported { get; } = string.Concat(Glyphs.Keys.Where(c => c != ' ').OrderBy(c => c));

    /// <summary>Maiúscula sem acento (Ã vira A, Ç vira C); caracteres sem equivalente ficam como estão.</summary>
    public static string Normalize(string text)
    {
        var sb = new StringBuilder();
        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            {
                sb.Append(char.ToUpperInvariant(c));
            }
        }

        return sb.ToString();
    }

    public static bool TryGet(char c, out Glyph glyph)
    {
        if (Glyphs.TryGetValue(c, out Glyph? g))
        {
            glyph = g;
            return true;
        }

        glyph = null!;
        return false;
    }

    /// <summary>
    /// Pontos de um traço, já em milímetros: <paramref name="sx"/> e <paramref name="sy"/> são mm por unidade da grade;
    /// os arcos viram segmentos curtos que se afastam da curva no máximo <paramref name="tolerance"/> mm.
    /// Um traço de um ponto só (ponto final, dois-pontos) vira lista de um item.
    /// </summary>
    public static List<(double X, double Y)> Flatten(IReadOnlyList<Seg> stroke, double sx, double sy, double tolerance)
    {
        List<(double X, double Y)> pts = [];
        foreach (Seg seg in stroke)
        {
            if (seg.Kind == 'L')
            {
                pts.Add((seg.A * sx, seg.B * sy));
                continue;
            }

            double rx = seg.C * sx, ry = seg.D * sy;
            double sweep = (seg.F - seg.E) * Math.PI / 180;
            double radius = Math.Max(rx, ry);
            double maxStep = radius <= tolerance ? Math.PI / 2 : 2 * Math.Acos(1 - tolerance / radius);
            maxStep = Math.Min(maxStep, Math.PI / 6);
            int n = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweep) / maxStep - 1e-9));
            for (int i = 0; i <= n; i++)
            {
                double a = (seg.E * Math.PI / 180) + sweep * i / n;
                pts.Add((seg.A * sx + rx * Math.Cos(a), seg.B * sy + ry * Math.Sin(a)));
            }
        }

        return pts;
    }
}
