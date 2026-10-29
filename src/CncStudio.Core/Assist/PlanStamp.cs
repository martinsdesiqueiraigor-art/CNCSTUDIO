using System.Globalization;
using System.Text;
using CncStudio.Core.Cam;

namespace CncStudio.Core.Assist;

/// <summary>
/// Registro do que o plano sugerido usou: desenho (geometria e ordem dos contornos), espessura, material e ferramentas.
/// Guardado junto do plano, serve para recusar a aplicação quando qualquer um desses dados mudou depois da sugestão:
/// as operações apontam para contornos pela posição na lista, e uma posição pode passar a apontar para outra geometria.
/// <para>
/// <b>Precisão:</b> cada número é quantizado em uma grade de 0,0001 mm (arredondado para 4 casas, meio para o par) e as
/// grades são comparadas. Não é uma tolerância absoluta: dois valores a menos de 0,0001 de distância podem cair em células
/// vizinhas e contar como diferentes (ex.: 0,00004999 e 0,00005001), mas dois valores a 0,0001 ou mais nunca caem na mesma
/// célula. O erro vai sempre para o lado seguro: pode recusar um plano à toa, nunca aceita uma diferença de 0,0001 mm ou mais.
/// Zero negativo vale como zero; NaN e infinitos são diferenciados entre si.
/// </para>
/// </summary>
public sealed record PlanStamp(string Drawing, string Thickness, string Material, string Tools)
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Quantiza em 0,0001 (ver a nota de precisão da classe). O "+ 0.0" troca -0 por 0.</summary>
    internal static string N(double v) =>
        double.IsNaN(v) ? "nan" : double.IsInfinity(v) ? (v > 0 ? "+inf" : "-inf") : (Math.Round(v, 4) + 0.0).ToString("0.####", Inv);

    /// <summary>
    /// Impressão digital das operações do plano (todos os campos de cada operação). Guardada na sugestão e conferida na aplicação:
    /// qualquer alteração posterior das operações (ferramenta, níveis, índice do contorno…) invalida o plano.
    /// </summary>
    public static string OfOperations(IEnumerable<CamOperationSpec> operations) =>
        string.Join("\n", operations.Select(o => o.ToString()));

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
    /// dados de agora, confere se cada operação aponta para um contorno que existe (0 ≤ índice &lt; quantidade, vale para furo,
    /// bolsão e contorno) e se as operações continuam as mesmas da sugestão.
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

        // O Assist sempre aponta cada operação (furo, bolsão ou contorno) para um contorno do desenho: não existe "todos os círculos" (-1)
        foreach (CamOperationSpec op in plan.Operations)
        {
            if (op.ContourIndex < 0 || op.ContourIndex >= current.Contours.Count)
            {
                return "Uma operação do plano aponta para um contorno que não existe mais no desenho. Sugira o plano de novo.";
            }
        }

        if (plan.OperationsStamp != PlanStamp.OfOperations(plan.Operations))
        {
            return "As operações do plano foram alteradas depois da sugestão. Sugira o plano de novo.";
        }

        return null;
    }
}
