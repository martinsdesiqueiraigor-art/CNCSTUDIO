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
}
