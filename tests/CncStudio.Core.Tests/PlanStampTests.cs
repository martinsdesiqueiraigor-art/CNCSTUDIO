using System.Globalization;
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

    /// <summary>CheckApply com todas as ferramentas novas já confirmadas: os testes de validade do desenho não tratam de confirmação.</summary>
    private static string? Check(PlanResult plan, PlanInput current) => PlanValidity.CheckApply(plan, current, PlanValidity.NewToolNumbers(plan, current));

    private static PlanInput Input(IReadOnlyList<Contour>? contours = null, double thickness = 10, MaterialEntry? material = null, IReadOnlyList<PlanTool>? tools = null) =>
        new(contours ?? Plate(), thickness, material ?? Steel, tools ?? []);

    [Fact]
    public void O_plano_leva_o_registro_dos_dados_de_origem()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        Assert.NotNull(plan.Stamp);
        Assert.Equal(PlanStamp.Of(input), plan.Stamp);
        Assert.Null(Check(plan, input));
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
        string? msg = Check(plan, Input(thickness: 12));
        Assert.NotNull(msg);
        Assert.Contains("espessura", msg);
    }

    [Fact]
    public void Mudar_o_material_invalida_o_plano()
    {
        PlanResult plan = AssistPlanner.Plan(Input());
        string? msg = Check(plan, Input(material: AssistLibrary.Defaults().Materials[0]));
        Assert.Contains("material", msg);
    }

    [Fact]
    public void Mudar_ou_tirar_o_material_invalida_o_plano()
    {
        PlanResult plan = AssistPlanner.Plan(Input());
        Assert.NotNull(Check(plan, new PlanInput(Plate(), 10, null, [])));
    }

    [Fact]
    public void Ferramenta_nova_ou_alterada_no_banco_invalida_o_plano()
    {
        PlanTool mill = new(1, 10, false, "Fresa 10", 4);
        PlanResult plan = AssistPlanner.Plan(Input(tools: [mill]));
        Assert.Null(Check(plan, Input(tools: [mill])));
        Assert.Contains("ferramentas", Check(plan, Input(tools: [mill, new PlanTool(2, 6, false, "Fresa 6", 4)])));
        Assert.Contains("ferramentas", Check(plan, Input(tools: [mill with { Diameter = 8 }])));
        Assert.Contains("ferramentas", Check(plan, Input(tools: [])));
    }

    [Fact]
    public void Desenho_com_o_mesmo_numero_de_contornos_mas_outra_geometria_invalida_o_plano()
    {
        PlanResult plan = AssistPlanner.Plan(Input());
        List<Contour> other = Plate();
        other[1] = Contour.Circle(new Point2(20, 15), 4); // furo deslocado 5 mm
        Assert.Equal(Plate().Count, other.Count);
        Assert.Contains("desenho", Check(plan, Input(other)));
    }

    [Fact]
    public void Trocar_a_ordem_dos_contornos_invalida_o_plano()
    {
        PlanResult plan = AssistPlanner.Plan(Input());
        List<Contour> swapped = Plate();
        (swapped[1], swapped[2]) = (swapped[2], swapped[1]);
        Assert.Contains("desenho", Check(plan, Input(swapped)));
    }

    [Fact]
    public void Tirar_um_contorno_invalida_o_plano()
    {
        PlanResult plan = AssistPlanner.Plan(Input());
        Assert.NotNull(Check(plan, Input(Plate().Take(2).ToList())));
    }

    [Fact]
    public void Voltar_aos_mesmos_dados_deixa_o_plano_valido_de_novo()
    {
        PlanResult plan = AssistPlanner.Plan(Input());
        Assert.NotNull(Check(plan, Input(thickness: 15)));
        Assert.Null(Check(plan, Input(thickness: 10)));
    }

    [Fact]
    public void Ruido_abaixo_de_um_decimo_de_micron_nao_invalida_mas_um_micron_invalida()
    {
        PlanResult plan = AssistPlanner.Plan(Input());
        List<Contour> noisy = Plate();
        noisy[1] = Contour.Circle(new Point2(15 + 1e-6, 15), 4);
        Assert.Null(Check(plan, Input(noisy)));
        List<Contour> moved = Plate();
        moved[1] = Contour.Circle(new Point2(15.001, 15), 4);
        Assert.NotNull(Check(plan, Input(moved)));
    }

    [Fact]
    public void Plano_sem_registro_nunca_e_aceito()
    {
        PlanResult plan = AssistPlanner.Plan(Input()) with { Stamp = null };
        Assert.Contains("registro", Check(plan, Input()));
    }

    [Fact]
    public void Operacao_apontando_para_contorno_que_nao_existe_e_recusada_mesmo_com_o_registro_igual()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        CamOperationSpec broken = plan.Operations[0] with { ContourIndex = 99 };
        PlanResult tampered = plan with { Operations = [broken, .. plan.Operations.Skip(1)] };
        Assert.Contains("não existe", Check(tampered, input));
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
        Assert.Contains("não existe", Check(tampered, input));
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
        Assert.Contains("alteradas", Check(deeper, input));
        PlanResult otherTool = Tamper(plan, o => o.Kind == JobOpKind.Pocket, o => o with { Tool = o.Tool with { SpindleRpm = o.Tool.SpindleRpm + 1 } });
        Assert.Contains("alteradas", Check(otherTool, input));
        PlanResult removed = plan with { Operations = plan.Operations.Skip(1).ToList() };
        Assert.Contains("alteradas", Check(removed, input));
        PlanResult disabled = Tamper(plan, o => o.Kind == JobOpKind.Drilling, o => o with { Enabled = false });
        Assert.Contains("alteradas", Check(disabled, input));
    }

    [Fact]
    public void Lista_de_operacoes_entregue_e_somente_leitura_de_verdade()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        int before = plan.Operations.Count;
        CamOperationSpec first = plan.Operations[0];
        var list = Assert.IsAssignableFrom<IList<CamOperationSpec>>(plan.Operations);
        Assert.True(list.IsReadOnly);
        Assert.Throws<NotSupportedException>(() => list.RemoveAt(0));
        Assert.Throws<NotSupportedException>(() => list.Add(first));
        Assert.Throws<NotSupportedException>(() => list[0] = first with { Enabled = false });
        Assert.False(plan.Operations is CamOperationSpec[]);
        Assert.Equal(before, plan.Operations.Count);
        Assert.Same(first, plan.Operations[0]);
        Assert.Null(Check(plan, input));
    }

    [Fact]
    public void Plano_nao_e_afetado_por_alterar_a_lista_de_origem()
    {
        PlanInput input = Input();
        PlanResult plan = AssistPlanner.Plan(input);
        PlanResult copy = plan with { };
        Assert.Same(plan.Operations, copy.Operations);
        Assert.Null(Check(copy, input));
    }

    private static CamOperationSpec Full() =>
        new(JobOpKind.Pocket, new CamTool(3, 6, 8000, 800, 300, "F6"), new CamLevels(50, 2, 0, -5, 1))
        {
            Label = "x", ContourIndex = 1, ContourParams = new ContourParams(ContourSide.Outside, true, false, 0.1, 3),
            PocketParams = new PocketParams(60, 0.1, 2), Islands = true, FacingStepOverPercent = 55,
            Drill = new DrillSpec(DrillCycleKind.Peck, 2, 0.5), Wrap = new WrapSpec("AB", 3, "A", new WrapSetup(40)),
            Face = new RotaryFaceSpec("A", 90, 20),
        };

    public static TheoryData<string, Func<CamOperationSpec, CamOperationSpec>> Changes() => new()
    {
        { "Kind", o => o with { Kind = JobOpKind.Contour } },
        { "Enabled", o => o with { Enabled = false } },
        { "Label", o => o with { Label = "y" } },
        { "ContourIndex", o => o with { ContourIndex = 2 } },
        { "Islands", o => o with { Islands = false } },
        { "FacingStepOver", o => o with { FacingStepOverPercent = 56 } },
        { "Tool.Number", o => o with { Tool = o.Tool with { Number = 4 } } },
        { "Tool.Diameter", o => o with { Tool = o.Tool with { Diameter = 6.001 } } },
        { "Tool.Rpm", o => o with { Tool = o.Tool with { SpindleRpm = 8001 } } },
        { "Tool.FeedXY", o => o with { Tool = o.Tool with { FeedXY = 801 } } },
        { "Tool.FeedZ", o => o with { Tool = o.Tool with { FeedZ = 301 } } },
        { "Tool.Name", o => o with { Tool = o.Tool with { Name = "G" } } },
        { "Levels.SafeZ", o => o with { Levels = o.Levels with { SafeZ = 51 } } },
        { "Levels.ClearanceZ", o => o with { Levels = o.Levels with { ClearanceZ = 3 } } },
        { "Levels.TopZ", o => o with { Levels = o.Levels with { TopZ = 0.5 } } },
        { "Levels.BottomZ", o => o with { Levels = o.Levels with { BottomZ = -5.0001 } } },
        { "Levels.StepDown", o => o with { Levels = o.Levels with { StepDown = 1.5 } } },
        { "Contour.Side", o => o with { ContourParams = o.ContourParams! with { Side = ContourSide.Inside } } },
        { "Contour.Climb", o => o with { ContourParams = o.ContourParams! with { Climb = false } } },
        { "Contour.Comp", o => o with { ContourParams = o.ContourParams! with { ControllerComp = true } } },
        { "Contour.Allowance", o => o with { ContourParams = o.ContourParams! with { Allowance = 0.2 } } },
        { "Contour.Lead", o => o with { ContourParams = o.ContourParams! with { LeadRadius = 4 } } },
        { "Contour.Null", o => o with { ContourParams = null } },
        { "Pocket.StepOver", o => o with { PocketParams = o.PocketParams! with { StepOverPercent = 61 } } },
        { "Pocket.Finish", o => o with { PocketParams = o.PocketParams! with { FinishAllowance = 0.2 } } },
        { "Pocket.Lead", o => o with { PocketParams = o.PocketParams! with { LeadRadius = 1 } } },
        { "Drill.Cycle", o => o with { Drill = o.Drill! with { Cycle = DrillCycleKind.Drill } } },
        { "Drill.Peck", o => o with { Drill = o.Drill! with { Peck = 3 } } },
        { "Drill.Dwell", o => o with { Drill = o.Drill! with { DwellSeconds = 1 } } },
        { "Wrap.Text", o => o with { Wrap = o.Wrap! with { Text = "AC" } } },
        { "Wrap.Height", o => o with { Wrap = o.Wrap! with { LetterHeight = 4 } } },
        { "Wrap.Axis", o => o with { Wrap = o.Wrap! with { Axis = "B" } } },
        { "Wrap.Setup.Diameter", o => o with { Wrap = o.Wrap! with { Setup = o.Wrap!.Setup with { Diameter = 41 } } } },
        { "Wrap.Setup.PlungeFeed", o => o with { Wrap = o.Wrap! with { Setup = o.Wrap!.Setup with { PlungeFeed = 61 } } } },
        { "Face.Axis", o => o with { Face = o.Face! with { Axis = "B" } } },
        { "Face.Angle", o => o with { Face = o.Face! with { Angle = 91 } } },
        { "Face.AxisToFace", o => o with { Face = o.Face! with { AxisToFace = 21 } } },
        { "Face.Null", o => o with { Face = null } },
    };

    [Theory]
    [MemberData(nameof(Changes))]
    public void Qualquer_campo_da_operacao_muda_a_impressao_digital(string field, Func<CamOperationSpec, CamOperationSpec> change)
    {
        CamOperationSpec baseline = Full();
        Assert.Equal(PlanStamp.OfOperations([baseline]), PlanStamp.OfOperations([Full()]));
        Assert.NotEqual(PlanStamp.OfOperations([baseline]), PlanStamp.OfOperations([change(baseline)]));
        Assert.False(string.IsNullOrEmpty(field));
    }

    [Fact]
    public void Impressao_digital_nao_depende_da_cultura()
    {
        CultureInfo old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
            string br = PlanStamp.OfOperations([Full()]);
            CultureInfo.CurrentCulture = new CultureInfo("en-US");
            Assert.Equal(br, PlanStamp.OfOperations([Full()]));
            Assert.Contains("Diameter=6;", br);
        }
        finally
        {
            CultureInfo.CurrentCulture = old;
        }
    }

    [Fact]
    public void Ordem_das_operacoes_conta()
    {
        CamOperationSpec a = Full(), b = Full() with { Label = "b" };
        Assert.NotEqual(PlanStamp.OfOperations([a, b]), PlanStamp.OfOperations([b, a]));
    }

    private static PlanInput WithX(double x) =>
        Input([Contour.Rectangle(0, 0, 100, 60), Contour.Circle(new Point2(x, 15), 4), Contour.Rectangle(30, 20, 40, 20)]);

    [Fact]
    public void Diferenca_menor_que_a_grade_na_mesma_celula_e_aceita()
    {
        PlanResult plan = AssistPlanner.Plan(WithX(15.00001));
        Assert.Null(Check(plan, WithX(15.00004)));
    }

    [Fact]
    public void Diferenca_de_um_decimo_de_micron_ou_mais_nunca_e_aceita()
    {
        PlanResult plan = AssistPlanner.Plan(WithX(15.00004));
        Assert.NotNull(Check(plan, WithX(15.00014)));
        Assert.NotNull(Check(plan, WithX(15.0001)));
        Assert.NotNull(Check(plan, WithX(14.9999)));
    }

    [Fact]
    public void Cruzar_a_fronteira_de_arredondamento_com_menos_de_um_decimo_de_micron_recusa_por_seguranca()
    {
        // 15,00004 e 15,00006 distam 0,00002 (< 0,0001) mas caem em células diferentes (15,0000 e 15,0001): recusa conservadora, documentada
        PlanResult plan = AssistPlanner.Plan(WithX(15.00004));
        Assert.NotNull(Check(plan, WithX(15.00006)));
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
