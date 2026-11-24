using CncStudio.Core.Cam;
using CncStudio.Core.Geometry;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>CAM-RAPIDO-01: rápido (G0) no material que as operações anteriores deixaram, por operação.</summary>
public class CamRapidInStockTests
{
    /// <summary>Bloco 60 × 60 × 20; bolsão no quadrado inteiro até Z−10; furos em (15,15) e (45,45) com R em Z−6 (só vale depois do bolsão).</summary>
    private static CamJob Job(bool drillFirst)
    {
        var job = new CamJob { Name = "RAPIDO", ProgramNumber = 1001, WorkOffset = 54, Zero = JobZero.Legacy };
        job.Contours.Add(Contour.Rectangle(0, 0, 60, 60));
        job.Contours.Add(Contour.Circle(new Point2(15, 15), 4));
        job.Contours.Add(Contour.Circle(new Point2(45, 45), 4));
        job.SetStock(new JobStock(0, 0, 60, 60, 20), null);
        CamOperationSpec pocket = CamJobInfo.NewOperation(JobOpKind.Pocket, ContourSide.Inside, 0, new CamTool(1, 10, 3000, 500, 200, "T1")) with
        {
            Levels = new CamLevels(50, 2, 0, -10, 5),
        };
        CamOperationSpec drill = CamJobInfo.NewOperation(JobOpKind.Drilling, ContourSide.Inside, 1, new CamTool(2, 8, 1000, 100, 100, "T2")) with
        {
            Levels = new CamLevels(30, -6, -10, -20, 5),
            ContourIndices = [1, 2],
        };
        job.Operations.AddRange(drillFirst ? [drill, pocket] : [pocket, drill]);
        return job;
    }

    [Fact]
    public void Furo_antes_do_bolsao_acusa_rapido_no_material_na_furacao()
    {
        CamStockTimeline t = CamStockTimeline.Compute(Job(drillFirst: true), null);
        Assert.Null(t.Problem);
        Assert.True(t.States[0].RapidHits > 0);
        Assert.Equal(0, t.States[1].RapidHits);
        string note = Assert.Single(t.RapidNotes);
        Assert.StartsWith("Operação 1:", note);
        Assert.Contains("no material", note);
    }

    [Fact]
    public void Bolsao_antes_do_furo_nao_acusa()
    {
        CamStockTimeline t = CamStockTimeline.Compute(Job(drillFirst: false), null);
        Assert.Null(t.Problem);
        Assert.All(t.States, s => Assert.Equal(0, s.RapidHits));
        Assert.Empty(t.RapidNotes);
    }

    [Fact]
    public void Reaproveitado_mantem_o_aviso()
    {
        CamJob job = Job(drillFirst: true);
        CamStockTimeline first = CamStockTimeline.Compute(job, null);
        CamStockTimeline again = CamStockTimeline.Compute(job, null, previous: first);
        Assert.True(again.States[0].Reused);
        Assert.Equal(first.States[0].RapidHits, again.States[0].RapidHits);
    }
}
