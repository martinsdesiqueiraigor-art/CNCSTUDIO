using CncStudio.Core.Cam;
using CncStudio.Core.Geometry;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>DESK-01 A: a peça STEP vale sozinha como peça (bruto, zero e pendências) e o bruto tem de envolvê-la.</summary>
public class Desk01StepPartTests
{
    // flange 100 × 100 × 26 com o canto no zero, topo em Z 0 (como o Bolted_Flange.step do teste do Igor)
    private static CamJob StepJob()
    {
        var job = new CamJob { PartHeight = 26 };
        job.PartStep = new JobPartStep("C:/pecas/flange.step", new StepStockSummary(0, 0, -26, 100, 100, 0, 150000));
        return job;
    }

    private static JobStockShape Margins(double side = 2) =>
        new(StockShapeKind.Margins) { XMinus = side, XPlus = side, YMinus = side, YPlus = side, ZTop = 0, ZBottom = 0 };

    [Fact]
    public void SoComStepNaoFaltaAPeca()
    {
        CamJob job = StepJob();
        Assert.True(CamJobInfo.HasPart(job));
        Assert.DoesNotContain(CamJobInfo.Pending(job, CamJobBuilder.Build(job, null)), p => p.StartsWith("Falta a peça", StringComparison.Ordinal));
        Assert.Equal((0.0, 0.0, 100.0, 100.0), CamJobInfo.PartBox(job));
    }

    [Fact]
    public void BrutoPorSobraEnvolveOStepEMesmoComOFuroDoTresD()
    {
        CamJob job = StepJob();
        job.SetStock(null, Margins());
        // a furação pelo 3D cria um círculo Ø9: antes o bruto virava 13 × 13 em volta dele
        job.Contours.Add(CamJobPart.Circle(new Point2(80, 50), 4.5));
        StockCheck c = CamJobStock.Check(job);
        Assert.True(c.Complete);
        Assert.Equal((-2.0, 102.0, -2.0, 102.0, -26.0, 0.0), (c.Solid!.MinX, c.Solid.MaxX, c.Solid.MinY, c.Solid.MaxY, c.Solid.MinZ, c.Solid.MaxZ));
        Assert.Null(CamJobStock.PartFit(job, c));
    }

    [Fact]
    public void ZeroNoCentroEDaPecaStep()
    {
        CamJob job = StepJob();
        job.Contours.Add(CamJobPart.Circle(new Point2(80, 50), 4.5));
        job.Zero = new JobZero(ZeroPoint.Center, ZeroLevel.Top);
        Point3 p = CamJobZero.Position(job)!.Value;
        Assert.Equal((50.0, 50.0), (p.X, p.Y));
    }

    [Fact]
    public void BrutoQueNaoEnvolveAPecaViraPendencia()
    {
        CamJob job = StepJob();
        // bloco absoluto 100 × 100 centrado no zero: a peça está em X 0 a 100 (o caso das duas flanges deslocadas)
        job.SetStock(null, new JobStockShape(StockShapeKind.Step)
        {
            StepPath = "C:/brutos/bloco.step",
            StepSummary = new StepStockSummary(-50, -50, -26, 50, 50, 0, 260000),
        });
        StockCheck c = CamJobStock.Check(job);
        string? fit = CamJobStock.PartFit(job, c);
        Assert.NotNull(fit);
        Assert.StartsWith("Bruto não envolve a peça", fit);
        Assert.Contains(fit, CamJobInfo.Pending(job, CamJobBuilder.Build(job, null)));
        Assert.Equal("pending", CamJobStock.State(job).Kind);
    }

    [Fact]
    public void BrutoSendoOProprioStepDaPecaViraPendencia()
    {
        CamJob job = StepJob();
        job.SetStock(null, new JobStockShape(StockShapeKind.Step)
        {
            StepPath = "c:\\pecas\\flange.step",
            StepSummary = new StepStockSummary(0, 0, -26, 100, 100, 0, 150000),
        });
        string? fit = CamJobStock.PartFit(job, CamJobStock.Check(job));
        Assert.NotNull(fit);
        Assert.Contains("próprio STEP da peça", fit);
    }

    [Fact]
    public void PaletaTemNovoNasDuasMaquinas()
    {
        Assert.Equal("novo-trabalho", CamPalette.Groups.Single(g => g.Id == "arquivo").Items[0].Action);
        Assert.Equal("novo-trabalho", CamPalette.LatheGroups.Single(g => g.Id == "arquivo").Items[0].Action);
    }
}
