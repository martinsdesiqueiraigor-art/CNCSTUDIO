using CncStudio.Core.Assist;
using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

public class ContourAuditTests
{
    private static PlanInput Input(IReadOnlyList<Contour> contours) =>
        new(contours, 10, AssistLibrary.Defaults().Materials[3], []);

    private static Contour Open(params (double X, double Y)[] pts) =>
        new([.. pts.Zip(pts.Skip(1), (a, b) => Segment2.Line(new Point2(a.X, a.Y), new Point2(b.X, b.Y)))]);

    [Fact]
    public void DesenhoNormalNaoTemAchados()
    {
        ContourFindings f = ContourAudit.Check([Contour.Rectangle(0, 0, 100, 60), Contour.Circle(new Point2(15, 15), 4), Contour.Rectangle(30, 20, 40, 20)]);
        Assert.Empty(f.Errors);
        Assert.Empty(f.Warnings);
    }

    [Fact]
    public void ContornosQueSeCruzamSaoRecusados()
    {
        ContourFindings f = ContourAudit.Check([Contour.Rectangle(0, 0, 100, 60), Contour.Rectangle(80, 30, 40, 20)]);
        Assert.Contains(f.Errors, e => e.Contains("se cruzam"));
    }

    [Fact]
    public void FuroQueCruzaABordaDaPecaEhRecusado()
    {
        ContourFindings f = ContourAudit.Check([Contour.Rectangle(0, 0, 100, 60), Contour.Circle(new Point2(98, 30), 5)]);
        Assert.Contains(f.Errors, e => e.Contains("se cruzam"));
    }

    [Fact]
    public void ContornoEmOitoEhRecusado()
    {
        var eight = Contour.FromPoints([new(0, 0), new(40, 40), new(40, 0), new(0, 40)]);
        ContourFindings f = ContourAudit.Check([Contour.Rectangle(-10, -10, 80, 80), eight]);
        Assert.Contains(f.Errors, e => e.Contains("cruza a si mesmo"));
    }

    [Fact]
    public void ContornoAbertoQueEnvolveAPecaEhRecusado()
    {
        // A peça "de verdade" está aberta (3 lados); só o retângulo menor ficou fechado
        ContourFindings f = ContourAudit.Check([Open((0, 0), (100, 0), (100, 60), (0, 60)), Contour.Rectangle(30, 20, 40, 20)]);
        Assert.Contains(f.Errors, e => e.Contains("aberto"));
    }

    [Fact]
    public void ContornoAbertoPequenoSoAvisa()
    {
        ContourFindings f = ContourAudit.Check([Contour.Rectangle(0, 0, 100, 60), Open((10, 10), (20, 10), (20, 20))]);
        Assert.Empty(f.Errors);
        Assert.Contains(f.Warnings, w => w.Contains("aberto"));
    }

    [Fact]
    public void DoisContornosGrandesSeparadosSaoAmbiguos()
    {
        ContourFindings f = ContourAudit.Check([Contour.Rectangle(0, 0, 100, 60), Contour.Rectangle(150, 0, 80, 60)]);
        Assert.Contains(f.Errors, e => e.Contains("dois contornos grandes"));
    }

    [Fact]
    public void ContornoPequenoSeparadoNaoEhAmbiguo()
    {
        ContourFindings f = ContourAudit.Check([Contour.Rectangle(0, 0, 100, 60), Contour.Rectangle(150, 0, 10, 10)]);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void ContornosDuplicadosAvisam()
    {
        ContourFindings f = ContourAudit.Check([Contour.Rectangle(0, 0, 100, 60), Contour.Rectangle(0, 0, 100, 60)]);
        Assert.Contains(f.Warnings, w => w.Contains("idênticos"));
    }

    [Fact]
    public void PlanoNaoSugereNadaQuandoODesenhoEhAmbiguo()
    {
        PlanResult plan = AssistPlanner.Plan(Input([Contour.Rectangle(0, 0, 100, 60), Contour.Rectangle(150, 0, 80, 60)]));
        Assert.Empty(plan.Operations);
        Assert.False(plan.Ok);
        Assert.Contains(plan.Notes, n => n.Contains("dois contornos grandes"));
    }

    [Fact]
    public void PlanoNaoSugereNadaQuandoOsContornosSeCruzam()
    {
        PlanResult plan = AssistPlanner.Plan(Input([Contour.Rectangle(0, 0, 100, 60), Contour.Circle(new Point2(98, 30), 5)]));
        Assert.Empty(plan.Operations);
        Assert.Contains(plan.Notes, n => n.Contains("se cruzam"));
    }

    [Fact]
    public void PlanoComAvisoAindaSugereEMostraOAviso()
    {
        PlanResult plan = AssistPlanner.Plan(Input([Contour.Rectangle(0, 0, 100, 60), Open((10, 10), (20, 10), (20, 20))]));
        Assert.True(plan.Ok);
        Assert.Contains(plan.Notes, n => n.Contains("aberto"));
    }
}
