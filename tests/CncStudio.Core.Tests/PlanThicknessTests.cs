using CncStudio.Core.Assist;
using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

/// <summary>Adendo 3 do UX-04f: o campo "espessura do bruto" do Assist acompanha o Trabalho fresa sem supor nada.</summary>
public class PlanThicknessTests
{
    private static CamJob Job()
    {
        var job = new CamJob { Name = "ASSIST" };
        job.Contours.Add(Contour.Rectangle(0, 0, 60, 40));
        job.PartHeight = 20;
        job.SetStock(null, new JobStockShape(StockShapeKind.Margins) { XMinus = 2, XPlus = 3, YMinus = 4, YPlus = 5, ZTop = 1, ZBottom = 6 });
        return job;
    }

    private static double? Thick(CamJob job) => CamJobStock.BlockBox(job)?.Thickness;

    [Fact]
    public void Bloco_por_sobra_preenche_e_cilindro_esvazia()
    {
        CamJob job = Job();
        (string text, bool fromJob) = PlanThickness.Sync("10", false, Thick(job));
        Assert.Equal(("26", true), (text, fromJob));

        job.SetStock(null, new JobStockShape(StockShapeKind.Cylinder) { Diameter = 80, Length = 30 });
        Assert.Equal(("", false), PlanThickness.Sync(text, fromJob, Thick(job)));
    }

    [Fact]
    public void Bloco_todo_acima_de_Z0_ou_incompleto_esvazia_o_que_veio_do_trabalho()
    {
        CamJob job = Job();
        job.StockShape = job.StockShape! with { OffsetZ = 30 };
        Assert.Equal(("", false), PlanThickness.Sync("26", true, Thick(job)));

        job.SetStock(null, new JobStockShape(StockShapeKind.Tube) { Diameter = 80 });
        Assert.Equal(("", false), PlanThickness.Sync("26", true, Thick(job)));
    }

    [Fact]
    public void Valor_digitado_a_mao_fica()
    {
        CamJob job = Job();
        job.SetStock(null, new JobStockShape(StockShapeKind.Cylinder) { Diameter = 80, Length = 30 });
        Assert.Equal(("15", false), PlanThickness.Sync("15", false, Thick(job)));
        Assert.Equal(("15", false), PlanThickness.Sync("15", false, Thick(job), fill: false));
    }

    [Fact]
    public void Abrir_outro_trabalho_nao_herda_o_valor_anterior()
    {
        // ao abrir o módulo (fill: false): o valor do trabalho anterior some se o novo não informa, ou vira o do novo
        var other = new CamJob { Name = "SEM BRUTO" };
        other.Contours.Add(Contour.Rectangle(0, 0, 60, 40));
        Assert.Equal(("", false), PlanThickness.Sync("26", true, Thick(other), fill: false));

        other.Stock = new JobStock(0, 0, 60, 40, 12);
        Assert.Equal(("12", true), PlanThickness.Sync("26", true, Thick(other), fill: false));

        // o valor inicial "10" (não veio do trabalho) não é trocado ao abrir o módulo
        Assert.Equal(("10", false), PlanThickness.Sync("10", false, Thick(other), fill: false));
    }
}
