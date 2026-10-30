using CncStudio.Core.Assist;
using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

/// <summary>PR 2 da auditoria (A-04): ferramenta do banco × ferramenta criada pelo planejador, e a confirmação das novas.</summary>
public class PlanToolOriginTests
{
    private static readonly MaterialEntry Steel = AssistLibrary.Defaults().Materials[3];

    private static List<Contour> Plate() =>
    [
        Contour.Rectangle(0, 0, 100, 60),
        Contour.Circle(new Point2(15, 15), 4),
        Contour.Circle(new Point2(85, 45), 4),
        Contour.Rectangle(30, 20, 40, 20),
    ];

    private static readonly PlanTool BankMill = new(1, 10, false, "Fresa 10");
    private static readonly PlanTool BankDrill = new(2, 8, true, "Broca 8");

    private static PlanInput Input(params PlanTool[] tools) => new(Plate(), 10, Steel, tools);

    [Fact]
    public void Banco_completo_nao_cria_ferramenta_nova()
    {
        PlanInput input = Input(BankMill, BankDrill);
        PlanResult plan = AssistPlanner.Plan(input);
        IReadOnlyList<PlannedTool> tools = PlanValidity.ToolsOf(plan, input);
        Assert.NotEmpty(tools);
        Assert.All(tools, t => Assert.Equal(ToolOrigin.Bank, t.Origin));
        Assert.Null(PlanValidity.CheckApply(plan, input));
    }

    [Fact]
    public void Banco_vazio_cria_ferramentas_novas_e_bloqueia_ate_confirmar()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        IReadOnlyList<PlannedTool> tools = PlanValidity.ToolsOf(plan, input);
        Assert.All(tools, t => Assert.Equal(ToolOrigin.New, t.Origin));
        string? msg = PlanValidity.CheckApply(plan, input);
        Assert.NotNull(msg);
        Assert.Contains("ferramenta nova", msg);
        foreach (PlannedTool t in tools)
        {
            Assert.Contains("T" + t.Number, msg);
        }
    }

    [Fact]
    public void Confirmar_todas_as_novas_libera_e_confirmar_so_algumas_nao()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        List<int> numbers = PlanValidity.NewToolNumbers(plan, input).OrderBy(n => n).ToList();
        Assert.True(numbers.Count >= 2);
        Assert.Null(PlanValidity.CheckApply(plan, input, numbers.ToHashSet()));
        string? partial = PlanValidity.CheckApply(plan, input, numbers.Take(1).ToHashSet());
        Assert.NotNull(partial);
        Assert.DoesNotContain("T" + numbers[0] + ",", partial);
        Assert.Contains("T" + numbers[1], partial);
    }

    [Fact]
    public void Banco_parcial_mistura_banco_e_novas()
    {
        PlanInput input = Input(BankMill);
        PlanResult plan = AssistPlanner.Plan(input);
        IReadOnlyList<PlannedTool> tools = PlanValidity.ToolsOf(plan, input);
        Assert.Contains(tools, t => t.Origin == ToolOrigin.Bank && t.Number == 1);
        Assert.Contains(tools, t => t.Origin == ToolOrigin.New);
        Assert.All(tools.Where(t => t.Origin == ToolOrigin.New), t => Assert.True(t.Number > 1));
        Assert.NotNull(PlanValidity.CheckApply(plan, input));
    }

    [Fact]
    public void Numero_novo_nunca_repete_numero_do_banco()
    {
        PlanInput input = Input(new PlanTool(7, 20, false, "Fresa 20"), new PlanTool(9, 3, true, "Broca 3"));
        PlanResult plan = AssistPlanner.Plan(input);
        HashSet<int> bank = [7, 9];
        Assert.All(PlanValidity.ToolsOf(plan, input).Where(t => t.Origin == ToolOrigin.New), t => Assert.DoesNotContain(t.Number, bank));
    }

    [Fact]
    public void Ferramenta_do_banco_com_mesmo_numero_mas_outro_diametro_conta_como_nova()
    {
        PlanInput planned = Input(BankMill, BankDrill);
        PlanResult plan = AssistPlanner.Plan(planned);
        PlanInput changed = Input(BankMill with { Diameter = 10.5 }, BankDrill);
        IReadOnlyList<PlannedTool> tools = PlanValidity.ToolsOf(plan, changed);
        Assert.Contains(tools, t => t.Number == 1 && t.Origin == ToolOrigin.New);
        Assert.Contains("ferramentas", PlanValidity.CheckApply(plan, changed)); // o registro também acusa a mudança do banco
    }

    [Fact]
    public void Cadastrar_a_ferramenta_depois_exige_sugerir_de_novo_e_ai_vira_banco()
    {
        PlanInput empty = Input();
        PlanResult first = AssistPlanner.Plan(empty);
        List<PlannedTool> news = PlanValidity.ToolsOf(first, empty).ToList();
        PlanInput registered = Input(news.Select(t => new PlanTool(t.Number, t.Diameter, first.Operations.First(o => o.Tool.Number == t.Number).Kind == JobOpKind.Drilling, t.Name)).ToArray());
        // o banco mudou depois da sugestão: o plano antigo não vale mais
        Assert.Contains("ferramentas", PlanValidity.CheckApply(first, registered, PlanValidity.NewToolNumbers(first, registered)));
        PlanResult second = AssistPlanner.Plan(registered);
        Assert.All(PlanValidity.ToolsOf(second, registered), t => Assert.Equal(ToolOrigin.Bank, t.Origin));
        Assert.Null(PlanValidity.CheckApply(second, registered));
    }

    [Fact]
    public void Plano_sem_operacoes_nao_tem_ferramentas()
    {
        PlanInput input = new([], 10, Steel, []);
        PlanResult plan = AssistPlanner.Plan(input);
        Assert.Empty(PlanValidity.ToolsOf(plan, input));
    }
}
