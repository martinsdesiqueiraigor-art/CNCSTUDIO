using CncStudio.Core.Cam;

namespace CncStudio.Core.Assist;

/// <summary>Estado de uma profundidade do plano.</summary>
public enum DepthOrigin
{
    /// <summary>Veio do usuário ou do desenho (o plano 2D não tem nenhuma hoje).</summary>
    Provided,

    /// <summary>Suposta pelo planejador (o 2D não diz a profundidade). Bloqueia a aplicação até ser revisada.</summary>
    Presumed,

    /// <summary>O usuário digitou o valor depois de ver o presumido.</summary>
    Confirmed,
}

/// <summary>
/// Uma profundidade do plano a revisar. Operações iguais (mesmo tipo e nome, como os furos de um mesmo Ø) formam um grupo e
/// recebem o mesmo valor. <see cref="Key"/> é o índice da primeira operação do grupo.
/// </summary>
public sealed record PlanDepth(int Key, string Label, JobOpKind Kind, double Presumed, IReadOnlyList<int> OperationIndexes)
{
    public DepthOrigin Origin => DepthOrigin.Presumed;
}

/// <summary>Resultado da revisão: operações com as profundidades confirmadas, ou o motivo de não poder aplicar.</summary>
public sealed record PlanReviewResult(IReadOnlyList<CamOperationSpec>? Operations, string? Error)
{
    public bool Ok => Operations is not null;
}

/// <summary>
/// Revisão do plano antes de aplicar (auditoria A-01): toda profundidade do plano 2D é presumida e só vale depois de o usuário
/// digitar o valor. Não há "confirmo tudo": cada profundidade exige um número.
/// </summary>
public static class PlanReview
{
    /// <summary>Profundidades presumidas do plano, agrupadas por tipo e nome da operação, na ordem em que aparecem.</summary>
    public static IReadOnlyList<PlanDepth> DepthsOf(PlanResult plan) =>
        plan.Operations.Select((o, i) => (Op: o, Index: i)).GroupBy(x => (x.Op.Kind, x.Op.Label))
            .Select(g => new PlanDepth(g.First().Index, g.Key.Label, g.Key.Kind, Depth(g.First().Op), g.Select(x => x.Index).ToList()))
            .OrderBy(d => d.Key).ToList();

    private static double Depth(CamOperationSpec op) => Math.Round(op.Levels.TopZ - op.Levels.BottomZ, 4);

    /// <summary>Faltam profundidades: devolve o nome da primeira ainda sem valor, ou null se todas têm.</summary>
    public static string? FirstPending(PlanResult plan, IReadOnlyDictionary<int, double> depths) =>
        DepthsOf(plan).FirstOrDefault(d => !depths.ContainsKey(d.Key))?.Label;

    /// <summary>
    /// Confere o plano (desatualização, ferramentas novas) e as profundidades digitadas e devolve as operações a aplicar, com
    /// as profundidades confirmadas no lugar das presumidas. O plano em si não é alterado.
    /// </summary>
    public static PlanReviewResult Review(PlanResult plan, PlanInput current, IReadOnlySet<int>? confirmedNewTools, IReadOnlyDictionary<int, double> depths)
    {
        if (PlanValidity.CheckApply(plan, current, confirmedNewTools) is { } stale)
        {
            return new PlanReviewResult(null, stale);
        }

        var ops = new List<CamOperationSpec>(plan.Operations);
        foreach (PlanDepth d in DepthsOf(plan))
        {
            if (!depths.TryGetValue(d.Key, out double value))
            {
                return new PlanReviewResult(null, $"Falta revisar a profundidade de \"{d.Label}\" (suposta: {d.Presumed:0.##} mm). Digite o valor.");
            }

            if (!double.IsFinite(value) || value <= 0)
            {
                return new PlanReviewResult(null, $"Profundidade de \"{d.Label}\": digite um valor maior que zero.");
            }

            if (d.Kind == JobOpKind.Pocket && value > current.Thickness + 1e-9)
            {
                return new PlanReviewResult(null, $"Bolsão \"{d.Label}\": a profundidade ({value:0.##} mm) passa da espessura do bruto ({current.Thickness:0.##} mm).");
            }

            foreach (int i in d.OperationIndexes)
            {
                CamOperationSpec op = ops[i];
                CamLevels levels = op.Levels with { BottomZ = op.Levels.TopZ - value, StepDown = Math.Min(op.Levels.StepDown, value) };
                if (levels.Validate() is { } invalid)
                {
                    return new PlanReviewResult(null, $"\"{d.Label}\": {invalid}");
                }

                ops[i] = op with { Levels = levels };
                if (op.Kind == JobOpKind.Drilling && op.Drill is { } drill)
                {
                    // Mesma regra do planejador: bicada só se o furo for mais fundo que 3 diâmetros
                    ops[i] = ops[i] with { Drill = value > 3 * op.Tool.Diameter ? new DrillSpec(DrillCycleKind.Peck, Math.Round(op.Tool.Diameter, 1), drill.DwellSeconds) : new DrillSpec(DrillCycleKind.Drill, 0, drill.DwellSeconds) };
                }
            }
        }

        return new PlanReviewResult(ops, null);
    }
}
