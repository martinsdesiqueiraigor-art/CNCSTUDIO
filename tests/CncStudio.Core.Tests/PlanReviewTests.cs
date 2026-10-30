using CncStudio.Core.Assist;
using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

/// <summary>PR 3 da auditoria (A-01): profundidade suposta bloqueia a aplicação até o usuário digitar cada valor.</summary>
public class PlanReviewTests
{
    private static readonly MaterialEntry Steel = AssistLibrary.Defaults().Materials[3];

    private static List<Contour> Plate() =>
    [
        Contour.Rectangle(0, 0, 100, 60),
        Contour.Circle(new Point2(15, 15), 4),
        Contour.Circle(new Point2(85, 45), 4),
        Contour.Rectangle(30, 20, 40, 20),
    ];

    private static PlanInput Input() => new(Plate(), 10, Steel, [new PlanTool(1, 10, false, "Fresa 10"), new PlanTool(2, 8, true, "Broca 8")]);

    /// <summary>Review com a fixação já escolhida: os testes de profundidade não tratam da estratégia de fixação.</summary>
    private static PlanReviewResult Rev(PlanResult plan, PlanInput input, IReadOnlySet<int>? tools, IReadOnlyDictionary<int, double> depths, ReleaseStrategy release = ReleaseStrategy.HeldBySetup, PlanHeights? heights = null) =>
        PlanReview.Review(plan, input, tools, depths, release, heights ?? new PlanHeights(50, 2, 0));

    private static readonly PlanHeights Std = new(50, 2, 0);

    private static Dictionary<int, double> All(PlanResult plan, double? value = null) =>
        PlanReview.DepthsOf(plan).ToDictionary(d => d.Key, d => value ?? d.Presumed);

    [Fact]
    public void Profundidades_sao_agrupadas_por_tipo_e_nome_e_todas_presumidas()
    {
        PlanResult plan = AssistPlanner.Plan(Input());
        IReadOnlyList<PlanDepth> depths = PlanReview.DepthsOf(plan);
        Assert.Equal(3, depths.Count); // furos Ø8 (2 operações), bolsão, contorno
        Assert.Equal(2, depths.Single(d => d.Kind == JobOpKind.Drilling).OperationIndexes.Count);
        Assert.All(depths, d => Assert.Equal(DepthOrigin.Presumed, d.Origin));
        Assert.Equal(10, depths.Single(d => d.Kind == JobOpKind.Drilling).Presumed);
        Assert.Equal(5, depths.Single(d => d.Kind == JobOpKind.Pocket).Presumed);
        Assert.Equal(10, depths.Single(d => d.Kind == JobOpKind.Contour).Presumed);
    }

    [Fact]
    public void Sem_revisar_nenhuma_profundidade_nao_aplica()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        PlanReviewResult r = Rev(plan, input, null, new Dictionary<int, double>());
        Assert.False(r.Ok);
        Assert.Contains("Falta revisar", r.Error);
        Assert.NotNull(PlanReview.FirstPending(plan, new Dictionary<int, double>()));
    }

    [Fact]
    public void Revisar_so_algumas_ainda_bloqueia_e_diz_qual_falta()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        Dictionary<int, double> depths = All(plan);
        PlanDepth last = PlanReview.DepthsOf(plan).Last();
        depths.Remove(last.Key);
        PlanReviewResult r = Rev(plan, input, null, depths);
        Assert.False(r.Ok);
        Assert.Contains(last.Label, r.Error);
        Assert.Equal(last.Label, PlanReview.FirstPending(plan, depths));
    }

    [Fact]
    public void Todas_digitadas_aplica_com_os_valores_do_usuario_e_sem_alterar_o_plano()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        string before = PlanStamp.OfOperations(plan.Operations);
        Dictionary<int, double> depths = PlanReview.DepthsOf(plan).ToDictionary(d => d.Key, d => d.Kind switch { JobOpKind.Drilling => 12.0, JobOpKind.Pocket => 3.0, _ => 11.5 });
        PlanReviewResult r = Rev(plan, input, null, depths);
        Assert.True(r.Ok, r.Error);
        Assert.All(r.Operations!.Where(o => o.Kind == JobOpKind.Drilling), o => Assert.Equal(-12, o.Levels.BottomZ));
        Assert.Equal(-3, r.Operations!.Single(o => o.Kind == JobOpKind.Pocket).Levels.BottomZ);
        Assert.Equal(-11.5, r.Operations!.Single(o => o.Kind == JobOpKind.Contour).Levels.BottomZ);
        Assert.Equal(before, PlanStamp.OfOperations(plan.Operations)); // o plano sugerido continua igual
        Assert.Equal(plan.Operations.Count, r.Operations!.Count);
    }

    [Fact]
    public void Digitar_o_mesmo_valor_presumido_tambem_vale_mas_precisa_ser_digitado()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        Assert.True(Rev(plan, input, null, All(plan)).Ok);
        Assert.False(Rev(plan, input, null, new Dictionary<int, double>()).Ok);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Valor_invalido_e_recusado(double value)
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        Dictionary<int, double> depths = All(plan);
        int key = PlanReview.DepthsOf(plan)[0].Key;
        depths[key] = value;
        PlanReviewResult r = Rev(plan, input, null, depths);
        Assert.False(r.Ok);
        Assert.Contains("maior que zero", r.Error);
    }

    [Fact]
    public void Bolsao_mais_fundo_que_o_bruto_e_recusado_mas_furo_e_contorno_podem_passar_para_calco()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        Dictionary<int, double> depths = PlanReview.DepthsOf(plan).ToDictionary(d => d.Key, d => d.Kind == JobOpKind.Pocket ? 10.5 : 11.0);
        PlanReviewResult r = Rev(plan, input, null, depths);
        Assert.False(r.Ok);
        Assert.Contains("passa da espessura", r.Error);
        depths[PlanReview.DepthsOf(plan).Single(d => d.Kind == JobOpKind.Pocket).Key] = 10;
        Assert.True(Rev(plan, input, null, depths).Ok);
    }

    [Fact]
    public void Furo_raso_vira_furacao_simples_e_furo_fundo_vira_bicada()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        int key = PlanReview.DepthsOf(plan).Single(d => d.Kind == JobOpKind.Drilling).Key;
        Dictionary<int, double> depths = All(plan);
        depths[key] = 30; // 30 > 3 × 8
        Assert.All(Rev(plan, input, null, depths).Operations!.Where(o => o.Kind == JobOpKind.Drilling), o => Assert.Equal(DrillCycleKind.Peck, o.Drill!.Cycle));
        depths[key] = 5; // 5 ≤ 3 × 8
        Assert.All(Rev(plan, input, null, depths).Operations!.Where(o => o.Kind == JobOpKind.Drilling), o => Assert.Equal(DrillCycleKind.Drill, o.Drill!.Cycle));
    }

    [Fact]
    public void Passe_em_Z_nunca_passa_da_profundidade()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        PlanReviewResult r = Rev(plan, input, null, All(plan, 0.2));
        Assert.True(r.Ok, r.Error);
        Assert.All(r.Operations!, o => Assert.True(o.Levels.StepDown <= 0.2 + 1e-9));
        Assert.All(r.Operations!, o => Assert.Null(o.Levels.Validate()));
    }

    [Fact]
    public void Plano_obsoleto_e_ferramenta_nova_sem_confirmar_continuam_bloqueando_a_revisao()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        PlanInput changed = new(Plate(), 12, Steel, input.Tools);
        Assert.Contains("desatualizado", Rev(plan, changed, null, All(plan)).Error);
        PlanInput empty = new(Plate(), 10, Steel, []);
        PlanResult withNew = AssistPlanner.Plan(empty);
        Assert.Contains("ferramenta nova", Rev(withNew, empty, null, All(withNew)).Error);
        Assert.True(Rev(withNew, empty, PlanValidity.NewToolNumbers(withNew, empty), All(withNew)).Ok);
    }

    [Fact]
    public void Plano_sem_operacoes_nao_tem_profundidades()
    {
        PlanInput input = new([], 10, Steel, []);
        PlanResult plan = AssistPlanner.Plan(input);
        Assert.Empty(PlanReview.DepthsOf(plan));
    }

    [Fact]
    public void Contorno_externo_sem_escolher_a_fixacao_bloqueia()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        Assert.True(PlanReview.ReleaseOperationIndex(plan) >= 0);
        PlanReviewResult r = PlanReview.Review(plan, input, null, All(plan), ReleaseStrategy.NotReviewed, Std);
        Assert.False(r.Ok);
        Assert.Contains("Revise a fixação", r.Error);
        Assert.Contains("sem pontes", r.Error);
        Assert.False(PlanReview.Review(plan, input, null, All(plan), ReleaseStrategy.NotReviewed, Std).Ok);
    }

    [Fact]
    public void Presa_por_outro_meio_aplica_tudo_e_avisa_que_a_fixacao_nao_e_verificada()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        PlanReviewResult r = PlanReview.Review(plan, input, null, All(plan), ReleaseStrategy.HeldBySetup, Std);
        Assert.True(r.Ok, r.Error);
        Assert.Equal(plan.Operations.Count, r.Operations!.Count);
        Assert.Contains(r.Notes, n => n.Contains("não verificada"));
        Assert.DoesNotContain(r.Notes, n => n.Contains("segura", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Aceitar_que_solte_aplica_tudo_e_avisa()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        PlanReviewResult r = PlanReview.Review(plan, input, null, All(plan), ReleaseStrategy.AcceptRelease, Std);
        Assert.True(r.Ok, r.Error);
        Assert.Equal(plan.Operations.Count, r.Operations!.Count);
        Assert.Contains(r.Notes, n => n.Contains("solte") && n.Contains("não verificada"));
    }

    [Fact]
    public void Tirar_o_contorno_do_plano_remove_so_essa_operacao()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        PlanReviewResult r = PlanReview.Review(plan, input, null, All(plan), ReleaseStrategy.OmitContour, Std);
        Assert.True(r.Ok, r.Error);
        Assert.Equal(plan.Operations.Count - 1, r.Operations!.Count);
        Assert.DoesNotContain(r.Operations!, o => o.Kind == JobOpKind.Contour);
        Assert.Contains(r.Operations!, o => o.Kind == JobOpKind.Pocket);
        Assert.Contains(r.Operations!, o => o.Kind == JobOpKind.Drilling);
        Assert.Contains(plan.Operations, o => o.Kind == JobOpKind.Contour); // o plano sugerido não muda
        Assert.Contains(r.Notes, n => n.Contains("fora do plano"));
    }

    [Fact]
    public void Plano_sem_operacoes_nao_tem_contorno_para_soltar()
    {
        PlanResult plan = AssistPlanner.Plan(new PlanInput([], 10, Steel, []));
        Assert.Equal(-1, PlanReview.ReleaseOperationIndex(plan));
    }

    [Fact]
    public void Sem_as_alturas_Z_nao_aplica()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        PlanReviewResult r = PlanReview.Review(plan, input, null, All(plan), ReleaseStrategy.HeldBySetup, null);
        Assert.False(r.Ok);
        Assert.Contains("Faltam as alturas Z", r.Error);
    }

    [Theory]
    [InlineData(50, 2, 2)]   // aproximação no topo
    [InlineData(50, 0, 0)]   // aproximação no topo (igual)
    [InlineData(50, -1, 0)]  // aproximação abaixo do topo
    [InlineData(2, 2, 0)]    // seguro igual à aproximação
    [InlineData(1, 2, 0)]    // seguro abaixo da aproximação
    [InlineData(double.NaN, 2, 0)]
    [InlineData(50, double.PositiveInfinity, 0)]
    [InlineData(50, 2, double.NaN)]
    public void Alturas_incoerentes_sao_recusadas(double safe, double clearance, double top)
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        PlanReviewResult r = PlanReview.Review(plan, input, null, All(plan), ReleaseStrategy.HeldBySetup, new PlanHeights(safe, clearance, top));
        Assert.False(r.Ok);
        Assert.Contains("Alturas Z", r.Error);
    }

    [Fact]
    public void Alturas_do_usuario_vao_para_todas_as_operacoes_e_o_fundo_acompanha_o_topo()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        Dictionary<int, double> depths = PlanReview.DepthsOf(plan).ToDictionary(d => d.Key, d => d.Kind == JobOpKind.Pocket ? 4.0 : 10.0);
        PlanReviewResult r = PlanReview.Review(plan, input, null, depths, ReleaseStrategy.HeldBySetup, new PlanHeights(120, 30, 25));
        Assert.True(r.Ok, r.Error);
        Assert.All(r.Operations!, o => { Assert.Equal(120, o.Levels.SafeZ); Assert.Equal(30, o.Levels.ClearanceZ); Assert.Equal(25, o.Levels.TopZ); Assert.Null(o.Levels.Validate()); });
        Assert.Equal(15, r.Operations!.Single(o => o.Kind == JobOpKind.Contour).Levels.BottomZ);   // 25 - 10
        Assert.Equal(21, r.Operations!.Single(o => o.Kind == JobOpKind.Pocket).Levels.BottomZ);    // 25 - 4
        Assert.Contains(r.Notes, n => n.Contains("não verificado") && n.Contains("120"));
        Assert.Equal(0, plan.Operations[0].Levels.TopZ); // o plano sugerido não muda
    }

    [Fact]
    public void Topo_negativo_com_seguro_e_aproximacao_coerentes_vale()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        PlanReviewResult r = PlanReview.Review(plan, input, null, All(plan), ReleaseStrategy.HeldBySetup, new PlanHeights(10, -10, -20));
        Assert.True(r.Ok, r.Error);
        Assert.Equal(-30, r.Operations!.Single(o => o.Kind == JobOpKind.Contour).Levels.BottomZ); // -20 - 10
    }

    [Fact]
    public void Plano_sem_operacoes_nao_exige_alturas()
    {
        PlanInput input = new([], 10, Steel, []);
        PlanResult plan = AssistPlanner.Plan(input);
        PlanReviewResult r = PlanReview.Review(plan, input, null, new Dictionary<int, double>(), ReleaseStrategy.NotReviewed, null);
        Assert.True(r.Ok);
        Assert.Empty(r.Operations!);
    }
}
