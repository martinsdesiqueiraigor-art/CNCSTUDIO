using CncStudio.Core.Assist;
using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

public class PlanStampTests
{
    private static readonly MaterialEntry Steel = AssistLibrary.Defaults().Materials[3];

    private static List<Contour> Plate() =>
    [
        Contour.Rectangle(0, 0, 100, 60),
        Contour.Circle(new Point2(15, 15), 4),
        Contour.Circle(new Point2(85, 45), 4),
        Contour.Rectangle(30, 20, 40, 20),
    ];

    private static PlanInput Input(IReadOnlyList<Contour>? contours = null, double thickness = 10, MaterialEntry? material = null, IReadOnlyList<PlanTool>? tools = null) =>
        new(contours ?? Plate(), thickness, material ?? Steel, tools ?? []);

    [Fact]
    public void O_plano_leva_o_registro_dos_dados_de_origem()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        Assert.NotNull(plan.Stamp);
        Assert.Equal(PlanStamp.Of(input), plan.Stamp);
        Assert.Null(PlanValidity.CheckApply(plan, input));
    }

    [Fact]
    public void Plano_sem_operacoes_tambem_leva_o_registro()
    {
        PlanResult plan = AssistPlanner.Plan(Input([]));
        Assert.False(plan.Ok);
        Assert.NotNull(plan.Stamp);
    }

    [Fact]
    public void Mudar_a_espessura_invalida_o_plano()
    {
        PlanResult plan = AssistPlanner.Plan(Input());
        string? msg = PlanValidity.CheckApply(plan, Input(thickness: 12));
        Assert.NotNull(msg);
        Assert.Contains("espessura", msg);
    }

    [Fact]
    public void Mudar_o_material_invalida_o_plano()
    {
        PlanResult plan = AssistPlanner.Plan(Input());
        string? msg = PlanValidity.CheckApply(plan, Input(material: AssistLibrary.Defaults().Materials[0]));
        Assert.Contains("material", msg);
    }

    [Fact]
    public void Mudar_ou_tirar_o_material_invalida_o_plano()
    {
        PlanResult plan = AssistPlanner.Plan(Input());
        Assert.NotNull(PlanValidity.CheckApply(plan, new PlanInput(Plate(), 10, null, [])));
    }

    [Fact]
    public void Ferramenta_nova_ou_alterada_no_banco_invalida_o_plano()
    {
        PlanTool mill = new(1, 10, false, "Fresa 10", 4);
        PlanResult plan = AssistPlanner.Plan(Input(tools: [mill]));
        Assert.Null(PlanValidity.CheckApply(plan, Input(tools: [mill])));
        Assert.Contains("ferramentas", PlanValidity.CheckApply(plan, Input(tools: [mill, new PlanTool(2, 6, false, "Fresa 6", 4)])));
        Assert.Contains("ferramentas", PlanValidity.CheckApply(plan, Input(tools: [mill with { Diameter = 8 }])));
        Assert.Contains("ferramentas", PlanValidity.CheckApply(plan, Input(tools: [])));
    }

    [Fact]
    public void Desenho_com_o_mesmo_numero_de_contornos_mas_outra_geometria_invalida_o_plano()
    {
        PlanResult plan = AssistPlanner.Plan(Input());
        List<Contour> other = Plate();
        other[1] = Contour.Circle(new Point2(20, 15), 4); // furo deslocado 5 mm
        Assert.Equal(Plate().Count, other.Count);
        Assert.Contains("desenho", PlanValidity.CheckApply(plan, Input(other)));
    }

    [Fact]
    public void Trocar_a_ordem_dos_contornos_invalida_o_plano()
    {
        PlanResult plan = AssistPlanner.Plan(Input());
        List<Contour> swapped = Plate();
        (swapped[1], swapped[2]) = (swapped[2], swapped[1]);
        Assert.Contains("desenho", PlanValidity.CheckApply(plan, Input(swapped)));
    }

    [Fact]
    public void Tirar_um_contorno_invalida_o_plano()
    {
        PlanResult plan = AssistPlanner.Plan(Input());
        Assert.NotNull(PlanValidity.CheckApply(plan, Input(Plate().Take(2).ToList())));
    }

    [Fact]
    public void Voltar_aos_mesmos_dados_deixa_o_plano_valido_de_novo()
    {
        PlanResult plan = AssistPlanner.Plan(Input());
        Assert.NotNull(PlanValidity.CheckApply(plan, Input(thickness: 15)));
        Assert.Null(PlanValidity.CheckApply(plan, Input(thickness: 10)));
    }

    [Fact]
    public void Ruido_abaixo_de_um_decimo_de_micron_nao_invalida_mas_um_micron_invalida()
    {
        PlanResult plan = AssistPlanner.Plan(Input());
        List<Contour> noisy = Plate();
        noisy[1] = Contour.Circle(new Point2(15 + 1e-6, 15), 4);
        Assert.Null(PlanValidity.CheckApply(plan, Input(noisy)));
        List<Contour> moved = Plate();
        moved[1] = Contour.Circle(new Point2(15.001, 15), 4);
        Assert.NotNull(PlanValidity.CheckApply(plan, Input(moved)));
    }

    [Fact]
    public void Plano_sem_registro_nunca_e_aceito()
    {
        PlanResult plan = AssistPlanner.Plan(Input()) with { Stamp = null };
        Assert.Contains("registro", PlanValidity.CheckApply(plan, Input()));
    }

    [Fact]
    public void Operacao_apontando_para_contorno_que_nao_existe_e_recusada_mesmo_com_o_registro_igual()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        CamOperationSpec broken = plan.Operations[0] with { ContourIndex = 99 };
        PlanResult tampered = plan with { Operations = [broken, .. plan.Operations.Skip(1)] };
        Assert.Contains("não existe", PlanValidity.CheckApply(tampered, input));
    }

    private static PlanResult Tamper(PlanResult plan, Func<CamOperationSpec, bool> pick, Func<CamOperationSpec, CamOperationSpec> change) =>
        plan with { Operations = plan.Operations.Select(o => pick(o) ? change(o) : o).ToList() };

    [Theory]
    [InlineData(JobOpKind.Drilling, -1)]
    [InlineData(JobOpKind.Drilling, -5)]
    [InlineData(JobOpKind.Drilling, 4)]
    [InlineData(JobOpKind.Pocket, -1)]
    [InlineData(JobOpKind.Pocket, 4)]
    [InlineData(JobOpKind.Contour, -1)]
    [InlineData(JobOpKind.Contour, 4)]
    public void Indice_fora_do_desenho_e_recusado_em_toda_operacao_do_Assist(JobOpKind kind, int index)
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        Assert.Contains(plan.Operations, o => o.Kind == kind);
        PlanResult tampered = Tamper(plan, o => o.Kind == kind, o => o with { ContourIndex = index });
        Assert.Contains("não existe", PlanValidity.CheckApply(tampered, input));
    }

    [Fact]
    public void Todas_as_operacoes_geradas_apontam_para_um_contorno_valido()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        Assert.Contains(plan.Operations, o => o.Kind == JobOpKind.Drilling);
        Assert.Contains(plan.Operations, o => o.Kind == JobOpKind.Pocket);
        Assert.Contains(plan.Operations, o => o.Kind == JobOpKind.Contour);
        Assert.All(plan.Operations, o => Assert.InRange(o.ContourIndex, 0, input.Contours.Count - 1));
    }

    [Fact]
    public void Alterar_a_operacao_depois_da_sugestao_invalida_o_plano()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        PlanResult deeper = Tamper(plan, o => o.Kind == JobOpKind.Contour, o => o with { Levels = o.Levels with { BottomZ = o.Levels.BottomZ - 5 } });
        Assert.Contains("alteradas", PlanValidity.CheckApply(deeper, input));
        PlanResult otherTool = Tamper(plan, o => o.Kind == JobOpKind.Pocket, o => o with { Tool = o.Tool with { SpindleRpm = o.Tool.SpindleRpm + 1 } });
        Assert.Contains("alteradas", PlanValidity.CheckApply(otherTool, input));
        PlanResult removed = plan with { Operations = plan.Operations.Skip(1).ToList() };
        Assert.Contains("alteradas", PlanValidity.CheckApply(removed, input));
        PlanResult disabled = Tamper(plan, o => o.Kind == JobOpKind.Drilling, o => o with { Enabled = false });
        Assert.Contains("alteradas", PlanValidity.CheckApply(disabled, input));
    }

    [Fact]
    public void Lista_de_operacoes_entregue_nao_pode_ser_alterada_por_fora()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        int before = plan.Operations.Count;
        if (plan.Operations is IList<CamOperationSpec> list)
        {
            Assert.Throws<NotSupportedException>(() => list.RemoveAt(0));
        }

        Assert.Equal(before, plan.Operations.Count);
        Assert.Null(PlanValidity.CheckApply(plan, input));
    }

    private static PlanInput WithX(double x) =>
        Input([Contour.Rectangle(0, 0, 100, 60), Contour.Circle(new Point2(x, 15), 4), Contour.Rectangle(30, 20, 40, 20)]);

    [Fact]
    public void Diferenca_menor_que_a_grade_na_mesma_celula_e_aceita()
    {
        PlanResult plan = AssistPlanner.Plan(WithX(15.00001));
        Assert.Null(PlanValidity.CheckApply(plan, WithX(15.00004)));
    }

    [Fact]
    public void Diferenca_de_um_decimo_de_micron_ou_mais_nunca_e_aceita()
    {
        PlanResult plan = AssistPlanner.Plan(WithX(15.00004));
        Assert.NotNull(PlanValidity.CheckApply(plan, WithX(15.00014)));
        Assert.NotNull(PlanValidity.CheckApply(plan, WithX(15.0001)));
        Assert.NotNull(PlanValidity.CheckApply(plan, WithX(14.9999)));
    }

    [Fact]
    public void Cruzar_a_fronteira_de_arredondamento_com_menos_de_um_decimo_de_micron_recusa_por_seguranca()
    {
        // 15,00004 e 15,00006 distam 0,00002 (< 0,0001) mas caem em células diferentes (15,0000 e 15,0001): recusa conservadora, documentada
        PlanResult plan = AssistPlanner.Plan(WithX(15.00004));
        Assert.NotNull(PlanValidity.CheckApply(plan, WithX(15.00006)));
    }

    [Fact]
    public void Meio_decimal_e_decidido_pela_representacao_binaria_e_e_estavel()
    {
        // 0,00005 e 0,00015 não existem exatamente em binário: o lado do arredondamento é determinístico, mas não é "meio para cima"
        Assert.Equal(PlanStamp.N(0.00005), PlanStamp.N(0.00005));
        Assert.Equal("0", PlanStamp.N(0.00005));
        Assert.Equal("0.0001", PlanStamp.N(0.00015));
        Assert.Equal("0.0002", PlanStamp.N(0.00016));
        Assert.Equal("0.0001", PlanStamp.N(0.0001));
    }

    [Fact]
    public void Zero_negativo_vale_como_zero()
    {
        Assert.Equal("0", PlanStamp.N(-0.0));
        Assert.Equal("0", PlanStamp.N(-0.00001));
        Assert.Equal(PlanStamp.N(0.00001), PlanStamp.N(-0.00001));
        Assert.Equal("-0.0001", PlanStamp.N(-0.0001));
    }

    [Fact]
    public void NaN_e_infinitos_sao_diferenciados_e_estaveis()
    {
        Assert.Equal("nan", PlanStamp.N(double.NaN));
        Assert.Equal("+inf", PlanStamp.N(double.PositiveInfinity));
        Assert.Equal("-inf", PlanStamp.N(double.NegativeInfinity));
        Assert.NotEqual(PlanStamp.N(double.PositiveInfinity), PlanStamp.N(double.NegativeInfinity));
    }
}
