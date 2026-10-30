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

/// <summary>
/// Alturas Z do plano, informadas pelo usuário (auditoria A-05). Todas na coordenada do Trabalho (a mesma do programa):
/// <see cref="TopZ"/> é onde fica o topo da peça (referência Z; o plano supõe 0), <see cref="ClearanceZ"/> o plano de aproximação
/// (rápido até ele, depois avanço) e <see cref="SafeZ"/> a altura de deslocamento entre operações. Precisa valer
/// <c>SafeZ &gt; ClearanceZ &gt; TopZ</c>. Altura acima da peça não prova ausência de colisão com morsa, grampos ou castanhas.
/// </summary>
public sealed record PlanHeights(double SafeZ, double ClearanceZ, double TopZ)
{
    /// <summary>Motivo se as alturas são inconsistentes; null se estão coerentes entre si.</summary>
    public string? Validate()
    {
        if (!double.IsFinite(SafeZ) || !double.IsFinite(ClearanceZ) || !double.IsFinite(TopZ))
        {
            return "Alturas Z: digite números válidos.";
        }

        if (ClearanceZ <= TopZ)
        {
            return $"Alturas Z: a aproximação ({ClearanceZ:0.##}) precisa ficar acima do topo da peça ({TopZ:0.##}).";
        }

        return SafeZ <= ClearanceZ ? $"Alturas Z: o Z seguro ({SafeZ:0.##}) precisa ficar acima da aproximação ({ClearanceZ:0.##})." : null;
    }
}

/// <summary>Ferramenta do plano cuja rotação calculada passa do limite do fuso do perfil da máquina.</summary>
public sealed record PlanRpmIssue(int ToolNumber, string Name, double Diameter, double Calculated, double Max);

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

    /// <summary>
    /// Ferramentas do plano (por número) cuja rotação calculada passa do limite do fuso informado no perfil da máquina.
    /// Sem limite informado, não há como dizer: devolve vazio e a rotação fica "não verificada". Nenhum limite é inventado.
    /// </summary>
    public static IReadOnlyList<PlanRpmIssue> RpmIssues(PlanResult plan, PlanInput current) =>
        current.MaxSpindleRpm is not { } max
            ? []
            : plan.Operations.Select(o => o.Tool).Where(t => t.SpindleRpm > max + 1e-9).DistinctBy(t => t.Number).OrderBy(t => t.Number)
                .Select(t => new PlanRpmIssue(t.Number, t.Name, t.Diameter, t.SpindleRpm, max)).ToList();

    /// <summary>Faltam profundidades: devolve o nome da primeira ainda sem valor, ou null se todas têm.</summary>
    public static string? FirstPending(PlanResult plan, IReadOnlyDictionary<int, double> depths) =>
        DepthsOf(plan).FirstOrDefault(d => !depths.ContainsKey(d.Key))?.Label;

    /// <summary>
    /// Confere o plano (desatualização, ferramentas novas) e as profundidades digitadas e devolve as operações a aplicar, com
    /// as profundidades confirmadas no lugar das presumidas. O plano em si não é alterado.
    /// </summary>
    public static PlanReviewResult Review(PlanResult plan, PlanInput current, IReadOnlySet<int>? confirmedNewTools, IReadOnlyDictionary<int, double> depths,
        ReleaseStrategy release = ReleaseStrategy.NotReviewed, PlanHeights? heights = null,
        IReadOnlyDictionary<int, double>? rpmAdjust = null)
    {
        if (PlanValidity.CheckApply(plan, current, confirmedNewTools) is { } stale)
        {
            return new PlanReviewResult(null, stale);
        }

        // Rotação acima do limite do fuso (perfil da máquina): bloqueia até o usuário digitar uma rotação dentro do limite
        var rpmFor = new Dictionary<int, double>();
        foreach (PlanRpmIssue issue in RpmIssues(plan, current))
        {
            if (rpmAdjust is null || !rpmAdjust.TryGetValue(issue.ToolNumber, out double newRpm))
            {
                return new PlanReviewResult(null, $"T{issue.ToolNumber} ({issue.Name}): a rotação calculada ({issue.Calculated:0} rpm) passa do limite do fuso do perfil da máquina ({issue.Max:0} rpm). Digite uma rotação até {issue.Max:0}.");
            }

            if (!double.IsFinite(newRpm) || newRpm <= 0 || newRpm > issue.Max + 1e-9)
            {
                return new PlanReviewResult(null, $"T{issue.ToolNumber}: a rotação precisa ser maior que zero e no máximo {issue.Max:0} rpm (limite do perfil da máquina).");
            }

            rpmFor[issue.ToolNumber] = newRpm;
        }

        int releaseIndex = ReleaseOperationIndex(plan);
        if (releaseIndex >= 0 && release == ReleaseStrategy.NotReviewed)
        {
            return new PlanReviewResult(null, "Revise a fixação: o contorno da peça é cortado por inteiro, sem pontes, e a peça pode soltar. Escolha como ela fica presa ou se o contorno sai do plano.");
        }

        if (plan.Operations.Count > 0)
        {
            if (heights is null)
            {
                return new PlanReviewResult(null, "Faltam as alturas Z: digite o Z seguro, a aproximação e o topo da peça (referência Z do seu zero-peça).");
            }

            if (heights.Validate() is { } badHeights)
            {
                return new PlanReviewResult(null, badHeights);
            }
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
                CamLevels levels = new(heights!.SafeZ, heights.ClearanceZ, heights.TopZ, heights.TopZ - value, Math.Min(op.Levels.StepDown, value));
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

        // Rotação ajustada pelo usuário: o avanço acompanha na mesma proporção (mesmo avanço por dente)
        foreach ((int toolNumber, double newRpm) in rpmFor)
        {
            for (int i = 0; i < ops.Count; i++)
            {
                CamTool t = ops[i].Tool;
                if (t.Number != toolNumber)
                {
                    continue;
                }

                double k = newRpm / t.SpindleRpm;
                CamTool adjusted = t with { SpindleRpm = newRpm, FeedXY = Math.Max(1, Math.Round(t.FeedXY * k)), FeedZ = Math.Max(1, Math.Round(t.FeedZ * k)) };
                if (adjusted.Validate() is { } toolError)
                {
                    return new PlanReviewResult(null, $"T{toolNumber}: {toolError}");
                }

                ops[i] = ops[i] with { Tool = adjusted };
            }
        }

        var notes = new List<string>();
        if (ops.Count > 0)
        {
            notes.Add(current.MaxSpindleRpm is { } limit
                ? (rpmFor.Count > 0
                    ? $"Rotação de {string.Join(", ", rpmFor.Keys.Select(n => "T" + n))} ajustada por você ao limite do fuso do perfil ({limit:0} rpm); avanço reduzido na mesma proporção. Confira no catálogo da ferramenta."
                    : $"Rotações dentro do limite do fuso do perfil da máquina ({limit:0} rpm). É só o limite do fuso, não prova que a ferramenta ou o corte aguentam.")
                : "Limite de rotação do fuso não informado no perfil da máquina: rotação NÃO VERIFICADA. Informe a rotação máxima em CAM › Máquina.");
        }

        if (heights is not null && ops.Count > 0)
        {
            notes.Add($"Alturas Z informadas pelo usuário (seguro {heights.SafeZ:0.##}, aproximação {heights.ClearanceZ:0.##}, topo da peça {heights.TopZ:0.##}). Estar acima da peça não garante estar livre de morsa, grampos ou castanhas: não verificado.");
        }

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
