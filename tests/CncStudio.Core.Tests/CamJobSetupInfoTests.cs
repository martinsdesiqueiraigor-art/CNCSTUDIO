using CncStudio.Core.Cam;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>Bruto, origem, máquina e limite do fuso no Trabalho fresa (UX-01 rodada 3).</summary>
public class CamJobSetupInfoTests
{
    [Fact]
    public void StockError_pede_as_tres_medidas_maiores_que_zero()
    {
        Assert.Null(CamJobInfo.StockError(100, 60, 10));
        Assert.NotNull(CamJobInfo.StockError(0, 60, 10));
        Assert.NotNull(CamJobInfo.StockError(100, -1, 10));
        Assert.Contains("altura", CamJobInfo.StockError(100, 60, 0));
        Assert.NotNull(CamJobInfo.StockError(100, 60, double.NaN));
    }

    [Fact]
    public void DrawingBox_envolve_todos_os_contornos()
    {
        Assert.Null(CamJobInfo.DrawingBox([]));
        var box = CamJobInfo.DrawingBox(
        [
            Contour.FromPoints([new(10, 5), new(60, 5), new(60, 35), new(10, 35)]),
            Contour.FromPoints([new(-5, 0), new(0, 0), new(0, 40), new(-5, 40)]),
        ]);
        Assert.Equal((-5d, 0d, 65d, 40d), box);
    }

    [Fact]
    public void OriginTitle_mostra_o_topo_das_operacoes_ligadas()
    {
        var job = new CamJob();
        Assert.Equal("Origem G54: X0 Y0 do desenho, topo Z0", CamJobInfo.OriginTitle(job));

        job.Operations.Add(CamJobInfo.NewOperation(JobOpKind.Pocket, ContourSide.Outside, 0));
        CamOperationSpec raised = CamJobInfo.NewOperation(JobOpKind.Contour, ContourSide.Outside, 0);
        job.Operations.Add(raised with { Levels = raised.Levels with { TopZ = 2 } });
        Assert.EndsWith("topo Z varia por operação", CamJobInfo.OriginTitle(job));

        job.Operations[0] = job.Operations[0] with { Enabled = false };
        job.WorkOffset = 55;
        Assert.Equal("Origem G55: X0 Y0 do desenho, topo Z2", CamJobInfo.OriginTitle(job));
        Assert.Equal(("pending", "não verificada"), CamJobInfo.OriginState());
    }

    [Fact]
    public void Machine_so_fica_OK_com_o_fuso_informado()
    {
        Assert.Equal("pending", CamJobInfo.MachineState(null).Kind);
        Assert.Equal("Máquina: sem perfil ativo", CamJobInfo.MachineTitle(null));

        var m = new MachineProfile { Name = "VMC" };
        Assert.Equal(("pending", "fuso não informado"), CamJobInfo.MachineState(m));
        Assert.Equal("Máquina: VMC · fuso não informado", CamJobInfo.MachineTitle(m));

        m.MaxSpindleRpm = 8000;
        Assert.Equal("ok", CamJobInfo.MachineState(m).Kind);
        Assert.Equal("Máquina: VMC · fuso até 8000 rpm", CamJobInfo.MachineTitle(m));
    }

    [Fact]
    public void SpindleWarning_so_acima_do_limite_informado()
    {
        Assert.Null(CamJobInfo.SpindleWarning(12000, null));
        Assert.Null(CamJobInfo.SpindleWarning(8000, 8000));
        Assert.Null(CamJobInfo.SpindleWarning(3000, 8000));
        Assert.Equal("Acima do limite do fuso (8000 rpm). Digite até 8000; o avanço cai na mesma proporção.", CamJobInfo.SpindleWarning(9000, 8000));
    }

    [Fact]
    public void SpindleWarning_nao_muda_o_programa()
    {
        // Só conferência: a geração usa a rotação digitada, com ou sem perfil de máquina
        var job = new CamJob();
        job.Contours.Add(Contour.FromPoints([new(0, 0), new(100, 0), new(100, 60), new(0, 60)]));
        CamOperationSpec op = CamJobInfo.NewOperation(JobOpKind.Contour, ContourSide.Outside, 0);
        job.Operations.Add(op with { Tool = op.Tool with { SpindleRpm = 9000 } });
        string withLimit = CamJobBuilder.Build(job, new MachineProfile { MaxSpindleRpm = 8000 }).Program;
        Assert.Contains("S9000", withLimit);
        Assert.Equal(CamJobBuilder.Build(job, null).Program, withLimit);
    }
}
