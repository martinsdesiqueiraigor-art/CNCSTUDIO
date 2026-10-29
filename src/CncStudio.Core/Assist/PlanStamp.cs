using System.Globalization;
using System.Text;
using CncStudio.Core.Cam;

namespace CncStudio.Core.Assist;

/// <summary>
/// Registro do que o plano sugerido usou: desenho (geometria e ordem dos contornos), espessura, material e ferramentas.
/// Guardado junto do plano, serve para recusar a aplicação quando qualquer um desses dados mudou depois da sugestão:
/// as operações apontam para contornos pela posição na lista, e uma posição pode passar a apontar para outra geometria.
/// Os números são comparados com 4 casas decimais (0,0001 mm): ruído menor que isso não invalida o plano.
/// </summary>
public sealed record PlanStamp(string Drawing, string Thickness, string Material, string Tools)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static string N(double v) => double.IsFinite(v) ? Math.Round(v, 4).ToString("0.####", Inv) : "nan";

    public static PlanStamp Of(PlanInput input)
    {
        var drawing = new StringBuilder();
        foreach (Contour c in input.Contours)
        {
            drawing.Append('[');
            foreach (Segment2 s in c.Segments)
            {
                drawing.Append(s.IsArc ? 'A' : 'L').Append(s.Ccw ? '+' : '-')
                    .Append(N(s.Start.X)).Append(',').Append(N(s.Start.Y)).Append('>').Append(N(s.End.X)).Append(',').Append(N(s.End.Y));
                if (s.IsArc)
                {
                    drawing.Append('@').Append(N(s.Center.X)).Append(',').Append(N(s.Center.Y));
                }

                drawing.Append(';');
            }

            drawing.Append(']');
        }

        string material = input.Material is { } m
            ? string.Join('|', m.Name, m.Group, N(m.SurfaceSpeed), N(m.FeedPerTooth), N(m.DepthFactor), m.Draft)
            : "(nenhum)";
        string tools = string.Join(';', input.Tools.Select(t => string.Join('|', t.Number, N(t.Diameter), t.IsDrill, t.Name, t.Teeth)));
        return new PlanStamp(drawing.ToString(), N(input.Thickness), material, tools);
    }

    /// <summary>Nomes (em português) do que mudou de <paramref name="planned"/> para este registro.</summary>
    public IReadOnlyList<string> ChangedSince(PlanStamp planned)
    {
        var changed = new List<string>();
        if (Drawing != planned.Drawing)
        {
            changed.Add("desenho");
        }

        if (Thickness != planned.Thickness)
        {
            changed.Add("espessura");
        }

        if (Material != planned.Material)
        {
            changed.Add("material");
        }

        if (Tools != planned.Tools)
        {
            changed.Add("ferramentas");
        }

        return changed;
    }
}

/// <summary>Confere se um plano sugerido ainda pode ser aplicado ao Trabalho.</summary>
public static class PlanValidity
{
    /// <summary>
    /// Mensagem do motivo para recusar a aplicação, ou null se o plano ainda vale. Compara o registro do plano com os
    /// dados de agora e confere se cada operação aponta para um contorno que existe.
    /// </summary>
    public static string? CheckApply(PlanResult plan, PlanInput current)
    {
        if (plan.Stamp is not { } planned)
        {
            return "Este plano não tem registro dos dados de origem. Sugira o plano de novo.";
        }

        IReadOnlyList<string> changed = PlanStamp.Of(current).ChangedSince(planned);
        if (changed.Count > 0)
        {
            return "O plano está desatualizado: mudou " + string.Join(", ", changed) + " desde a sugestão. Sugira o plano de novo.";
        }

        foreach (CamOperationSpec op in plan.Operations)
        {
            bool needsContour = op.Kind is JobOpKind.Contour or JobOpKind.Pocket;
            if ((needsContour && (op.ContourIndex < 0 || op.ContourIndex >= current.Contours.Count)) || op.ContourIndex >= current.Contours.Count)
            {
                return "Uma operação do plano aponta para um contorno que não existe mais no desenho. Sugira o plano de novo.";
            }
        }

        return null;
    }
}
