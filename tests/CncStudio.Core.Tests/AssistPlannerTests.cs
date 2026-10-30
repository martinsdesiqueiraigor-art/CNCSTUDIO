using CncStudio.Core.Assist;
using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

public class AssistPlannerTests
{
    private static PlanInput Input(IReadOnlyList<Contour> contours, IReadOnlyList<PlanTool>? tools = null, MaterialEntry? material = null) =>
        new(contours, 10, material ?? AssistLibrary.Defaults().Materials[3], tools ?? []);

    private static List<Contour> Plate() =>
    [
        Contour.Rectangle(0, 0, 100, 60),
        Contour.Circle(new Point2(15, 15), 4),
        Contour.Circle(new Point2(85, 15), 4),
        Contour.Circle(new Point2(15, 45), 4),
        Contour.Circle(new Point2(85, 45), 4),
        Contour.Rectangle(30, 20, 40, 20),
    ];

    [Fact]
    public void Placa_com_furos_e_bolsao_gera_furar_bolsao_e_contorno_nessa_ordem()
    {
        PlanResult plan = AssistPlanner.Plan(Input(Plate()));
        Assert.True(plan.Ok);
        Assert.Equal(
            [JobOpKind.Drilling, JobOpKind.Drilling, JobOpKind.Drilling, JobOpKind.Drilling, JobOpKind.Pocket, JobOpKind.Contour],
            plan.Operations.Select(o => o.Kind).ToArray());
        Assert.All(plan.Operations.Where(o => o.Kind == JobOpKind.Drilling), o => Assert.Equal(8, o.Tool.Diameter));
        Assert.Equal(-5, plan.Operations[4].Levels.BottomZ); // bolsão até a metade
        Assert.Equal(-10, plan.Operations[5].Levels.BottomZ); // contorno até a espessura
    }

    [Fact]
    public void O_plano_vira_programa_que_o_cam_gera_nos_dois_comandos()
    {
        List<Contour> contours = Plate();
        PlanResult plan = AssistPlanner.Plan(Input(contours));
        foreach (CamDialect dialect in new[] { CamDialect.Fanuc, CamDialect.Siemens })
        {
            var job = new CamJob { Name = "PLANO", Dialect = dialect };
            job.Contours.AddRange(contours);
            job.Operations.AddRange(plan.Operations);
            JobBuild build = CamJobBuilder.Build(job, null);
            Assert.True(build.Ok, string.Join("; ", build.Errors));
        }
    }

    [Fact]
    public void Usa_ferramentas_da_biblioteca_e_numeros_livres_para_as_novas()
    {
        PlanTool[] tools = [new(1, 12, false, "Topo Ø12"), new(2, 6, false), new(5, 8, true, "Broca Ø8")];
        PlanResult plan = AssistPlanner.Plan(Input(Plate(), tools));
        Assert.All(plan.Operations.Where(o => o.Kind == JobOpKind.Drilling), o => Assert.Equal(5, o.Tool.Number));
        Assert.Equal(1, plan.Operations[^1].Tool.Number); // contorno com a maior fresa até 12 mm

        PlanResult none = AssistPlanner.Plan(Input(Plate()));
        Assert.True(none.Operations.Select(o => o.Tool.Number).Distinct().Count() >= 2);
        Assert.All(none.Operations, o => Assert.True(o.Tool.Number >= 1));
    }

    [Fact]
    public void Corte_vem_do_material_e_aviso_de_rascunho_aparece()
    {
        MaterialEntry steel = AssistLibrary.Defaults().Materials[0];
        PlanResult plan = AssistPlanner.Plan(Input(Plate(), material: steel));
        CamOperationSpec contour = plan.Operations[^1];
        (double rpm, double feed) = steel.SuggestMill(contour.Tool.Diameter, 4)!.Value;
        Assert.Equal(rpm, contour.Tool.SpindleRpm);
        Assert.Equal(feed, contour.Tool.FeedXY);
        Assert.Contains(plan.Notes, n => n.Contains("rascunho"));
        Assert.Contains(plan.Notes, n => n.Contains("no ar"));
    }

    [Fact]
    public void Contorno_dentro_do_bolsao_vira_ilha_e_nao_operacao()
    {
        var contours = new List<Contour>
        {
            Contour.Rectangle(0, 0, 100, 80),
            Contour.Rectangle(15, 15, 70, 50),
            Contour.Circle(new Point2(50, 40), 8),
        };
        PlanResult plan = AssistPlanner.Plan(Input(contours));
        CamOperationSpec pocket = Assert.Single(plan.Operations, o => o.Kind == JobOpKind.Pocket);
        Assert.True(pocket.Islands);
        Assert.DoesNotContain(plan.Operations, o => o.Kind == JobOpKind.Drilling);
        var job = new CamJob();
        job.Contours.AddRange(contours);
        job.Operations.AddRange(plan.Operations);
        Assert.True(CamJobBuilder.Build(job, null).Ok);
    }

    [Fact]
    public void Bolsao_que_nenhuma_fresa_faz_vira_aviso_e_nao_operacao()
    {
        var contours = new List<Contour> { Contour.Rectangle(0, 0, 100, 60), Contour.Rectangle(40, 25, 3, 3) };
        PlanTool[] tools = [new(1, 10, false)];
        PlanResult plan = AssistPlanner.Plan(Input(contours, tools));
        Assert.DoesNotContain(plan.Operations, o => o.Kind == JobOpKind.Pocket);
        Assert.Contains(plan.Notes, n => n.StartsWith("Bolsão 1", StringComparison.Ordinal));
        Assert.Contains(plan.Operations, o => o.Kind == JobOpKind.Contour);
    }

    [Fact]
    public void Entradas_ruins_explicam()
    {
        Assert.False(AssistPlanner.Plan(Input([])).Ok);
        Assert.Contains("fechado", AssistPlanner.Plan(Input([])).Notes[0]);
        PlanResult noThickness = AssistPlanner.Plan(new PlanInput(Plate(), 0, null, []));
        Assert.False(noThickness.Ok);
        Assert.Contains("espessura", noThickness.Notes[0]);
    }

    [Fact]
    public void Contornos_fora_da_peca_sao_ignorados_com_aviso()
    {
        var contours = new List<Contour> { Contour.Rectangle(0, 0, 100, 60), Contour.Circle(new Point2(200, 200), 5) };
        PlanResult plan = AssistPlanner.Plan(Input(contours));
        Assert.Contains(plan.Notes, n => n.Contains("ignorados"));
        Assert.Single(plan.Operations);
    }
}

public class PlanCriteriaNotesTests
{
    private static PlanInput Input(IReadOnlyList<Contour> contours, IReadOnlyList<PlanTool> tools) =>
        new(contours, 10, AssistLibrary.Defaults().Materials[3], tools);

    private static List<Contour> Plate() =>
    [
        Contour.Rectangle(0, 0, 100, 60),
        Contour.Circle(new Point2(15, 15), 4),
        Contour.Rectangle(30, 20, 40, 20),
    ];

    [Fact]
    public void BrocaDoBancoMostraDiferencaECriterio()
    {
        PlanResult plan = AssistPlanner.Plan(Input(Plate(), [new PlanTool(1, 10, false, "Fresa 10"), new PlanTool(2, 8.03, true, "Broca 8,03")]));
        string note = Assert.Single(plan.Notes, n => n.StartsWith("Broca para furo"));
        Assert.Contains("T2", note);
        Assert.Contains("+0,03", note);
        Assert.Contains("0,05", note);
    }

    [Fact]
    public void FresaDoBancoMostraQueOCriterioEhSoGeometria()
    {
        PlanResult plan = AssistPlanner.Plan(Input(Plate(), [new PlanTool(1, 10, false, "Fresa 10"), new PlanTool(2, 8, true, "Broca 8")]));
        Assert.Contains(plan.Notes, n => n.StartsWith("Fresa T1") && n.Contains("material") && n.Contains("rigidez"));
    }

    [Fact]
    public void CadaFerramentaTemUmaNotaSo()
    {
        PlanResult plan = AssistPlanner.Plan(Input(Plate(), [new PlanTool(1, 10, false, "Fresa 10"), new PlanTool(2, 8, true, "Broca 8")]));
        Assert.Single(plan.Notes, n => n.StartsWith("Fresa T1"));
    }

    [Fact]
    public void FerramentaNovaNaoGeraNotaDeBanco()
    {
        PlanResult plan = AssistPlanner.Plan(Input(Plate(), []));
        Assert.DoesNotContain(plan.Notes, n => n.StartsWith("Broca para furo") || n.StartsWith("Fresa T"));
    }
}
