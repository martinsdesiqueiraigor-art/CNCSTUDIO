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
    /// Impressão digital das operações do plano. Percorre por reflexão todas as propriedades públicas de cada operação e dos
    /// registros aninhados (ferramenta, níveis, parâmetros, furação, texto, face…), em ordem de nome, com cultura invariante,
    /// números com precisão total (sem quantizar), enums pelo nome e nulo explícito. Campo novo entra sozinho. Não depende de
    /// <c>ToString()</c>, que varia com a cultura e não é um contrato.
    /// </summary>
    public static string OfOperations(IEnumerable<CamOperationSpec> operations)
    {
        var sb = new StringBuilder();
        foreach (CamOperationSpec o in operations)
        {
            Describe(sb, o);
            sb.Append('\n');
        }

        return sb.ToString();
    }

    private static void Describe(StringBuilder sb, object? v)
    {
        switch (v)
        {
            case null: sb.Append("null"); break;
            case double d: sb.Append(d.ToString("R", Inv)); break;
            case float f: sb.Append(f.ToString("R", Inv)); break;
            case bool or int or long or Enum: sb.Append(Convert.ToString(v, Inv)); break;
            case string str: sb.Append('"').Append(str.Replace("\"", "\\\"")).Append('"'); break;
            case System.Collections.IEnumerable items:
                sb.Append('[');
                foreach (object? item in items)
                {
                    Describe(sb, item);
                    sb.Append(',');
                }

                sb.Append(']');
                break;
            default:
                sb.Append(v.GetType().Name).Append('{');
                foreach (System.Reflection.PropertyInfo p in v.GetType().GetProperties().Where(p => p.GetIndexParameters().Length == 0).OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    sb.Append(p.Name).Append('=');
                    Describe(sb, p.GetValue(v));
                    sb.Append(';');
                }

                sb.Append('}');
                break;
        }
    }

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

/// <summary>De onde vem a ferramenta de uma operação do plano.</summary>
public enum ToolOrigin
{
    /// <summary>Está no banco de ferramentas do usuário (número e diâmetro iguais).</summary>
    Bank,

    /// <summary>Não está no banco: o planejador inventou um número e um diâmetro. Precisa de confirmação antes de aplicar.</summary>
    New,
}

/// <summary>Ferramenta usada pelo plano, com a procedência.</summary>
public sealed record PlannedTool(int Number, double Diameter, string Name, ToolOrigin Origin);

/// <summary>Confere se um plano sugerido ainda pode ser aplicado ao Trabalho.</summary>
public static class PlanValidity
{
    /// <summary>
    /// Ferramentas distintas das operações do plano (por número e diâmetro), em ordem de número, com a procedência:
    /// <see cref="ToolOrigin.Bank"/> se o banco atual tem uma ferramenta com o mesmo número e diâmetro; senão <see cref="ToolOrigin.New"/>.
    /// Calculado a cada chamada, sem estado guardado: não há como a procedência ficar velha.
    /// </summary>
    public static IReadOnlyList<PlannedTool> ToolsOf(PlanResult plan, PlanInput current) =>
        plan.Operations.Select(o => o.Tool).DistinctBy(t => (t.Number, t.Diameter)).OrderBy(t => t.Number)
            .Select(t => new PlannedTool(t.Number, t.Diameter, t.Name,
                current.Tools.Any(b => b.Number == t.Number && b.Diameter == t.Diameter) ? ToolOrigin.Bank : ToolOrigin.New))
            .ToList();

    /// <summary>Números das ferramentas novas (fora do banco) do plano.</summary>
    public static IReadOnlySet<int> NewToolNumbers(PlanResult plan, PlanInput current) =>
        ToolsOf(plan, current).Where(t => t.Origin == ToolOrigin.New).Select(t => t.Number).ToHashSet();

    /// <summary>
    /// Mensagem do motivo para recusar a aplicação, ou null se o plano ainda vale. Compara o registro do plano com os
    /// dados de agora, confere se cada operação aponta para um contorno que existe (0 ≤ índice &lt; quantidade, vale para furo,
    /// bolsão e contorno) e se as operações continuam as mesmas da sugestão.
    /// </summary>
    public static string? CheckApply(PlanResult plan, PlanInput current, IReadOnlySet<int>? confirmedNewTools = null)
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

        // Ferramenta que o planejador criou (não está no banco) só vale com confirmação explícita, uma a uma
        List<PlannedTool> pending = ToolsOf(plan, current).Where(t => t.Origin == ToolOrigin.New && confirmedNewTools?.Contains(t.Number) != true).ToList();
        if (pending.Count > 0)
        {
            return "Há ferramenta nova, que não está no banco, sem confirmação: " + string.Join(", ", pending.Select(t => "T" + t.Number)) + ". Confirme ou cadastre no banco.";
        }

        return null;
    }
}
