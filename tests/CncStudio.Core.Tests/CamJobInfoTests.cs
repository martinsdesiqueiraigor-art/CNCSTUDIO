using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

public class CamJobInfoTests
{
    [Fact]
    public void Titulos_da_arvore()
    {
        CamOperationSpec s = CamJobInfo.NewOperation(JobOpKind.Contour, ContourSide.Inside, 0);
        Assert.Equal("3 · Contorno por dentro — T1 Ø10", CamJobInfo.OpTitle(2, s));
        Assert.Equal("1 · Meu nome — T1 Ø10", CamJobInfo.OpTitle(0, s with { Label = "Meu nome" }));
        Assert.Equal("2 · Círculo Ø20 (20 × 20 mm)", CamJobInfo.ContourTitle(Contour.Circle(new Point2(5, 5), 10), 1));
        Assert.Equal("1 · Contorno (60 × 40 mm)", CamJobInfo.ContourTitle(Contour.Rectangle(0, 0, 60, 40), 0));
        Assert.Equal("Bruto: não definido", CamJobInfo.StockTitle(null));
        Assert.Equal("Bruto 70 × 50 × 20 mm", CamJobInfo.StockTitle(new JobStock(0, 0, 70, 50, 20)));
    }

    [Fact]
    public void Toda_operacao_nova_gera_programa()
    {
        var job = new CamJob();
        job.Contours.Add(Contour.Rectangle(0, 0, 60, 40));
        job.Contours.Add(Contour.Circle(new Point2(30, 20), 12));
        foreach ((string _, JobOpKind kind, ContourSide side) in CamJobInfo.NewKinds)
        {
            job.Operations.Add(CamJobInfo.NewOperation(kind, side, kind == JobOpKind.Pocket ? 1 : 0));
        }

        Assert.Contains("perfil da máquina", CamJobBuilder.Build(job, null).Errors[^1]);
        MachineProfile machine = MachineProfile.FourAxisTableA();
        machine.Axes[0].PivotZ = -20;
        JobBuild build = CamJobBuilder.Build(job, machine);
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        Assert.Equal(CamJobInfo.NewKinds.Count, build.Operations.Count);
    }

    [Fact]
    public void Ferramentas_usadas_sem_repetir()
    {
        var job = new CamJob();
        job.Operations.Add(CamJobInfo.NewOperation(JobOpKind.Drilling, ContourSide.Outside, -1));
        job.Operations.Add(CamJobInfo.NewOperation(JobOpKind.Drilling, ContourSide.Outside, -1, new CamTool(2, 6, 4000, 400, 150)));
        job.Operations.Add(CamJobInfo.NewOperation(JobOpKind.Drilling, ContourSide.Outside, -1));
        Assert.Equal([1, 2], CamJobInfo.ToolsUsed(job).Select(t => t.Number));
    }
}
