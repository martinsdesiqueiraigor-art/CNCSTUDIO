using CncStudio.Core.Assist;
using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

/// <summary>PR 6 da auditoria (R-01): o limite de rpm vem do perfil da máquina; nunca é inventado.</summary>
public class PlanRpmTests
{
    private static readonly MaterialEntry Steel = AssistLibrary.Defaults().Materials[3];
    private static readonly PlanHeights Z = new(50, 2, 0);

    private static List<Contour> Plate() =>
    [
        Contour.Rectangle(0, 0, 100, 60),
        Contour.Circle(new Point2(15, 15), 4),
        Contour.Circle(new Point2(85, 45), 4),
        Contour.Rectangle(30, 20, 40, 20),
    ];

    // Material de teste (Vc 300 m/min): fresa Ø10 → 9549 rpm; broca Ø8 → 11937 rpm
    private static PlanInput Input(double? maxRpm) =>
        new(Plate(), 10, Steel, [new PlanTool(1, 10, false, "Fresa 10"), new PlanTool(2, 8, true, "Broca 8")], MaxSpindleRpm: maxRpm);

    private static Dictionary<int, double> Depths(PlanResult plan) => PlanReview.DepthsOf(plan).ToDictionary(d => d.Key, d => d.Presumed);

    private static PlanReviewResult Review(PlanResult plan, PlanInput input, Dictionary<int, double>? rpm = null) =>
        PlanReview.Review(plan, input, null, Depths(plan), ReleaseStrategy.HeldBySetup, Z, rpm);

    [Fact]
    public void Perfil_novo_nao_tem_limite_e_perfil_antigo_sem_o_campo_continua_valido()
    {
        Assert.Null(new MachineProfile().MaxSpindleRpm);
        (MachineProfile? profile, string? error) = MachineProfileJson.Parse("{\"Name\":\"Antiga\",\"Controller\":\"Fanuc\",\"Axes\":[]}");
        Assert.Null(error);
        Assert.Null(profile!.MaxSpindleRpm);
    }

    [Fact]
    public void Limite_do_perfil_vai_e_volta_no_json()
    {
        var profile = new MachineProfile { Name = "Fresadora", MaxSpindleRpm = 8000 };
        (MachineProfile? back, string? error) = MachineProfileJson.Parse(MachineProfileJson.Serialize(profile));
        Assert.Null(error);
        Assert.Equal(8000, back!.MaxSpindleRpm);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Limite_invalido_no_perfil_e_recusado(double value)
    {
        var profile = new MachineProfile { MaxSpindleRpm = value };
        Assert.Contains(profile.Validate(), e => e.Contains("rotação máxima"));
    }

    [Fact]
    public void Sem_limite_informado_a_rotacao_fica_nao_verificada_e_nada_e_inventado()
    {
        PlanInput input = Input(null);
        PlanResult plan = AssistPlanner.Plan(input);
        Assert.Empty(PlanReview.RpmIssues(plan, input));
        PlanReviewResult r = Review(plan, input);
        Assert.True(r.Ok, r.Error);
        Assert.Contains(r.Notes, n => n.Contains("NÃO VERIFICADA"));
        Assert.Contains(r.Operations!, o => o.Tool.SpindleRpm > 11000); // sem limite, a rotação calculada segue como está
    }

    [Fact]
    public void Limite_acima_de_todas_as_rotacoes_libera_e_diz_que_e_so_o_limite_do_fuso()
    {
        PlanInput input = Input(15000);
        PlanResult plan = AssistPlanner.Plan(input);
        PlanReviewResult r = Review(plan, input);
        Assert.True(r.Ok, r.Error);
        Assert.Contains(r.Notes, n => n.Contains("dentro do limite") && n.Contains("15000"));
        Assert.DoesNotContain(r.Notes, n => n.Contains("NÃO VERIFICADA"));
    }

    [Fact]
    public void Rotacao_acima_do_limite_bloqueia_ate_o_usuario_digitar_uma_valida()
    {
        PlanInput input = Input(10000);
        PlanResult plan = AssistPlanner.Plan(input);
        IReadOnlyList<PlanRpmIssue> issues = PlanReview.RpmIssues(plan, input);
        PlanRpmIssue issue = Assert.Single(issues);
        Assert.Equal(2, issue.ToolNumber);
        Assert.Equal(11937, issue.Calculated);
        PlanReviewResult blocked = Review(plan, input);
        Assert.False(blocked.Ok);
        Assert.Contains("T2", blocked.Error);
        Assert.Contains("10000", blocked.Error);
    }

    [Fact]
    public void Rotacao_digitada_dentro_do_limite_vale_e_o_avanco_acompanha()
    {
        PlanInput input = Input(10000);
        PlanResult plan = AssistPlanner.Plan(input);
        CamTool before = plan.Operations.First(o => o.Tool.Number == 2).Tool;
        PlanReviewResult r = Review(plan, input, new Dictionary<int, double> { [2] = 8000 });
        Assert.True(r.Ok, r.Error);
        Assert.All(r.Operations!.Where(o => o.Tool.Number == 2), o =>
        {
            Assert.Equal(8000, o.Tool.SpindleRpm);
            Assert.Equal(Math.Round(before.FeedXY * 8000 / 11937), o.Tool.FeedXY);
            Assert.Equal(Math.Round(before.FeedZ * 8000 / 11937), o.Tool.FeedZ);
            Assert.Null(o.Tool.Validate());
        });
        Assert.All(r.Operations!.Where(o => o.Tool.Number != 2), o => Assert.Equal(9549, o.Tool.SpindleRpm)); // as outras não mudam
        Assert.Contains(r.Notes, n => n.Contains("T2") && n.Contains("ajustada por você"));
        Assert.Equal(11937, plan.Operations.First(o => o.Tool.Number == 2).Tool.SpindleRpm); // o plano sugerido não muda
    }

    [Theory]
    [InlineData(10001)]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    public void Rotacao_digitada_acima_do_limite_ou_invalida_e_recusada(double value)
    {
        PlanInput input = Input(10000);
        PlanResult plan = AssistPlanner.Plan(input);
        PlanReviewResult r = Review(plan, input, new Dictionary<int, double> { [2] = value });
        Assert.False(r.Ok);
        Assert.Contains("limite", r.Error);
    }

    [Fact]
    public void Rotacao_exatamente_no_limite_vale()
    {
        PlanInput input = Input(11937);
        PlanResult plan = AssistPlanner.Plan(input);
        Assert.Empty(PlanReview.RpmIssues(plan, input));
        Assert.True(Review(plan, input).Ok);
    }

    [Fact]
    public void Varias_ferramentas_acima_do_limite_pedem_cada_uma()
    {
        PlanInput input = Input(5000);
        PlanResult plan = AssistPlanner.Plan(input);
        Assert.Equal([1, 2], PlanReview.RpmIssues(plan, input).Select(i => i.ToolNumber).ToArray());
        Assert.Contains("T1", Review(plan, input).Error);
        Assert.Contains("T2", Review(plan, input, new Dictionary<int, double> { [1] = 5000 }).Error);
        PlanReviewResult ok = Review(plan, input, new Dictionary<int, double> { [1] = 2500, [2] = 4000 });
        Assert.True(ok.Ok, ok.Error);
        Assert.All(ok.Operations!.Where(o => o.Tool.Number == 1), o => Assert.Equal(2500, o.Tool.SpindleRpm));
    }

    [Fact]
    public void Mudar_o_limite_do_perfil_depois_de_sugerir_invalida_o_plano()
    {
        PlanResult plan = AssistPlanner.Plan(Input(8000));
        string? msg = PlanValidity.CheckApply(plan, Input(4000));
        Assert.NotNull(msg);
        Assert.Contains("limite de rotação", msg);
        Assert.NotNull(PlanValidity.CheckApply(plan, Input(null)));
        Assert.Null(PlanValidity.CheckApply(plan, Input(8000)));
    }
}
