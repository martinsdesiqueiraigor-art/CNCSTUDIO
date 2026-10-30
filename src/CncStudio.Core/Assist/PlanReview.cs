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

/// <summary>
/// Decisão do usuário sobre o contorno externo, que corta a peça por inteiro, sem pontes, e pode soltá-la (auditoria A-02).
/// Não há valor padrão: <see cref="NotReviewed"/> bloqueia a aplicação. O Assist não verifica fixação nem colisão em nenhuma das opções.
/// </summary>
public enum ReleaseStrategy
{
    /// <summary>Ainda não escolhido: bloqueia.</summary>
    NotReviewed,

    /// <summary>O usuário diz que a peça fica presa por outro meio depois do corte (grampos, vácuo, mesa de sacrifício, calço…).</summary>
    HeldBySetup,

    /// <summary>O contorno externo sai do plano; o usuário faz depois, fora do Assist.</summary>
    OmitContour,

    /// <summary>O usuário aceita que a peça solte no fim e a retira com a máquina parada.</summary>
    AcceptRelease,
}

/// <summary>Resultado da revisão: operações com as profundidades confirmadas, ou o motivo de não poder aplicar.</summary>
public sealed record PlanReviewResult(IReadOnlyList<CamOperationSpec>? Operations, string? Error)
{
    public bool Ok => Operations is not null;

    /// <summary>Avisos que acompanham a aplicação (fixação não verificada etc.).</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];
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

    /// <summary>Índice da operação que corta o contorno externo da peça (contorno por fora), ou -1 se o plano não tem.</summary>
    public static int ReleaseOperationIndex(PlanResult plan)
    {
        for (int i = 0; i < plan.Operations.Count; i++)
        {
            CamOperationSpec o = plan.Operations[i];
            if (o.Kind == JobOpKind.Contour && o.ContourParams is { Side: ContourSide.Outside })
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Faltam profundidades: devolve o nome da primeira ainda sem valor, ou null se todas têm.</summary>
    public static string? FirstPending(PlanResult plan, IReadOnlyDictionary<int, double> depths) =>
        DepthsOf(plan).FirstOrDefault(d => !depths.ContainsKey(d.Key))?.Label;

    /// <summary>
    /// Confere o plano (desatualização, ferramentas novas) e as profundidades digitadas e devolve as operações a aplicar, com
    /// as profundidades confirmadas no lugar das presumidas. O plano em si não é alterado.
    /// </summary>
    public static PlanReviewResult Review(PlanResult plan, PlanInput current, IReadOnlySet<int>? confirmedNewTools, IReadOnlyDictionary<int, double> depths,
        ReleaseStrategy release = ReleaseStrategy.NotReviewed)
    {
        if (PlanValidity.CheckApply(plan, current, confirmedNewTools) is { } stale)
        {
            return new PlanReviewResult(null, stale);
        }

        int releaseIndex = ReleaseOperationIndex(plan);
        if (releaseIndex >= 0 && release == ReleaseStrategy.NotReviewed)
        {
            return new PlanReviewResult(null, "Revise a fixação: o contorno da peça é cortado por inteiro, sem pontes, e a peça pode soltar. Escolha como ela fica presa ou se o contorno sai do plano.");
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

        var notes = new List<string>();
        if (releaseIndex >= 0)
        {
            string label = ops[releaseIndex].Label;
            switch (release)
            {
                case ReleaseStrategy.OmitContour:
                    ops.RemoveAt(releaseIndex);
                    notes.Add($"\"{label}\" ficou fora do plano: faça o contorno depois, com a fixação definida por você.");
                    break;
                case ReleaseStrategy.HeldBySetup:
                    notes.Add("Fixação informada pelo usuário e não verificada: o Assist não confere morsa, grampos, vácuo nem colisão.");
                    break;
                default:
                    notes.Add("O usuário aceitou que a peça solte no fim do contorno, sem pontes. Retire com a máquina parada. Fixação não verificada.");
                    break;
            }
        }

        return new PlanReviewResult(ops, null) { Notes = notes };
    }
}
