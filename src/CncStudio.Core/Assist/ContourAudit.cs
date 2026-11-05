using System.Globalization;
using CncStudio.Core.Cam;

namespace CncStudio.Core.Assist;

/// <summary>Achados da conferência do desenho: <see cref="Errors"/> impedem o plano; <see cref="Warnings"/> só avisam.</summary>
public sealed record ContourFindings(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings);

/// <summary>
/// Conferência do desenho antes de o Assist escolher "o contorno da peça" (auditoria A-03). O plano escolhe o contorno de maior área
/// como peça; se o desenho é ambíguo ou tem defeito, adivinhar corta no lugar errado. Aqui o desenho é <b>recusado</b> (com o motivo)
/// em vez de seguir com uma escolha silenciosa. Cobre: contorno aberto, contornos que se cruzam, contorno que cruza a si mesmo,
/// dois contornos grandes separados e contornos duplicados.
/// </summary>
public static class ContourAudit
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Se o segundo maior contorno, fora do principal, tem pelo menos esta fração da área do principal, o desenho é ambíguo.</summary>
    public const double AmbiguousAreaRatio = 0.5;

    public static ContourFindings Check(IReadOnlyList<Contour> contours)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        List<Contour> closed = contours.Where(c => c.IsClosed).ToList();
        List<Contour> open = contours.Where(c => !c.IsClosed).ToList();
        if (closed.Count == 0)
        {
            return new ContourFindings(errors, warnings);
        }

        Contour outer = closed.OrderByDescending(c => Math.Abs(c.SignedArea)).First();
        (double minX, double minY, double maxX, double maxY) = outer.BoundsXY();

        if (open.Count > 0)
        {
            bool surrounds = open.Any(o =>
            {
                (double a, double b, double c, double d) = o.BoundsXY();
                return a <= minX + 1e-6 && b <= minY + 1e-6 && c >= maxX - 1e-6 && d >= maxY - 1e-6;
            });
            if (surrounds)
            {
                errors.Add("Há um contorno aberto que envolve o maior contorno fechado: o contorno da peça pode estar aberto (linhas que não se encontram). Feche-o no desenho; o plano não adivinha qual é a peça.");
            }
            else
            {
                warnings.Add($"{open.Count} contorno(s) aberto(s) no desenho foram ignorados (o plano usa só contornos fechados).");
            }
        }

        for (int i = 0; i < closed.Count; i++)
        {
            if (closed[i].SelfIntersects())
            {
                errors.Add($"O contorno {i + 1} cruza a si mesmo (formato de 8 ou laço). Corrija o desenho antes de planejar.");
            }
        }

        for (int i = 0; i < closed.Count; i++)
        {
            for (int j = i + 1; j < closed.Count; j++)
            {
                if (BoundsOverlap(closed[i], closed[j]) && closed[i].CrossesContour(closed[j]))
                {
                    errors.Add($"Os contornos {i + 1} e {j + 1} se cruzam. Não dá para saber o que é peça, furo ou bolsão; corrija o desenho.");
                }
            }
        }

        double outerArea = Math.Abs(outer.SignedArea);
        List<Contour> outside = closed.Where(c => !ReferenceEquals(c, outer) && !CamOperations.FindIslands(outer, closed).Contains(c)).ToList();
        foreach (Contour c in outside)
        {
            double area = Math.Abs(c.SignedArea);
            if (outerArea > 0 && area >= outerArea * AmbiguousAreaRatio)
            {
                errors.Add($"Há dois contornos grandes separados (áreas {outerArea:0.##} e {area:0.##} mm²): não dá para saber qual é a peça. Deixe só a peça no desenho, ou planeje uma de cada vez.");
                break;
            }
        }

        for (int i = 0; i < closed.Count; i++)
        {
            for (int j = i + 1; j < closed.Count; j++)
            {
                if (SameShape(closed[i], closed[j]))
                {
                    warnings.Add($"Os contornos {i + 1} e {j + 1} são idênticos (duplicados): apague um, ou a peça seria cortada duas vezes.");
                }
            }
        }

        return new ContourFindings(errors.Distinct().ToList(), warnings);
    }

    private static bool BoundsOverlap(Contour a, Contour b)
    {
        (double a1, double a2, double a3, double a4) = a.BoundsXY();
        (double b1, double b2, double b3, double b4) = b.BoundsXY();
        return a1 <= b3 && b1 <= a3 && a2 <= b4 && b2 <= a4;
    }

    private static bool SameShape(Contour a, Contour b)
    {
        double areaA = Math.Abs(a.SignedArea), areaB = Math.Abs(b.SignedArea);
        if (Math.Abs(areaA - areaB) > 1e-6 * Math.Max(1, areaA) || Math.Abs(a.Perimeter - b.Perimeter) > 1e-6 * Math.Max(1, a.Perimeter))
        {
            return false;
        }

        (double a1, double a2, double a3, double a4) = a.BoundsXY();
        (double b1, double b2, double b3, double b4) = b.BoundsXY();
        return Math.Abs(a1 - b1) < 1e-6 && Math.Abs(a2 - b2) < 1e-6 && Math.Abs(a3 - b3) < 1e-6 && Math.Abs(a4 - b4) < 1e-6;
    }
}
