using CncStudio.Core.Assist;
using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

/// <summary>Lista completa do que falta para aplicar o plano (tela de revisão do Assist).</summary>
public class PlanMissingTests
{
    private static PlanInput Input() => new(
        [Contour.Rectangle(0, 0, 100, 60), Contour.Circle(new Point2(15, 15), 4), Contour.Rectangle(30, 20, 40, 20)],
        10, AssistLibrary.Defaults().Materials[3], [new PlanTool(1, 10, false, "Fresa 10"), new PlanTool(2, 8, true, "Broca 8")]);

    [Fact]
    public void Sem_nada_revisado_lista_fixacao_alturas_e_cada_profundidade()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        IReadOnlyList<string> m = PlanReview.Missing(plan, input, null, new Dictionary<int, double>());
        Assert.Contains("Fixação da peça", m);
        Assert.Contains(m, x => x.StartsWith("Alturas Z", StringComparison.Ordinal));
        Assert.Equal(PlanReview.DepthsOf(plan).Count, m.Count(x => x.StartsWith("Profundidade:", StringComparison.Ordinal)));
    }

    [Fact]
    public void Tudo_revisado_nao_falta_nada_e_review_concorda()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        Dictionary<int, double> depths = PlanReview.DepthsOf(plan).ToDictionary(d => d.Key, d => d.Presumed);
        var h = new PlanHeights(50, 2, 0);
        Assert.Empty(PlanReview.Missing(plan, input, null, depths, ReleaseStrategy.HeldBySetup, h));
        Assert.True(PlanReview.Review(plan, input, null, depths, ReleaseStrategy.HeldBySetup, h).Ok);
    }

    [Fact]
    public void Alturas_invalidas_entram_com_o_motivo()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        Dictionary<int, double> depths = PlanReview.DepthsOf(plan).ToDictionary(d => d.Key, d => d.Presumed);
        IReadOnlyList<string> m = PlanReview.Missing(plan, input, null, depths, ReleaseStrategy.HeldBySetup, new PlanHeights(1, 5, 0));
        Assert.Single(m);
    }
}
