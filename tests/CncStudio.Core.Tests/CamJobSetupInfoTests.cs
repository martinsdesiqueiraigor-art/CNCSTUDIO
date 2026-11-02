using CncStudio.Core.Cam;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>Bruto, origem, máquina e limite do fuso no Trabalho fresa (UX-01 rodada 3).</summary>
public class CamJobSetupInfoTests
{
    [Fact]
    public void BankToolError_mesma_regra_da_aba_antiga()
    {
        Assert.Null(CamJobInfo.BankToolError(1, 10));
        Assert.Null(CamJobInfo.BankToolError(9999, 0.5));
        Assert.Contains("1 a 9999", CamJobInfo.BankToolError(0, 10));
        Assert.Contains("1 a 9999", CamJobInfo.BankToolError(10000, 10));
        Assert.Contains("diâmetro", CamJobInfo.BankToolError(3, 0));
        Assert.Contains("diâmetro", CamJobInfo.BankToolError(3, double.NaN));
    }

    [Fact]
    public void BankDiameterWarnings_avisa_so_o_T_do_banco_com_outro_diametro()
    {
        CamTool[] used = [new(1, 10, 3000, 500, 200), new(1, 10, 3000, 500, 200), new(2, 6, 3000, 500, 200), new(3, 4.5, 3000, 500, 200)];
        IReadOnlyList<string> w = CamJobInfo.BankDiameterWarnings(used, [(1, 12), (2, 6), (9, 3)]);

        // T1 com outro Ø avisa uma vez; T2 igual e T3 fora do banco não avisam
        string only = Assert.Single(w);
        Assert.Equal("T1 no banco do Code tem Ø12, mas o CAM usou Ø10: o simulador do Code usa o do banco.", only);
        Assert.Contains("Ø4,5", CamJobInfo.BankDiameterWarnings(used, [(3, 4)])[0]);
        Assert.Empty(CamJobInfo.BankDiameterWarnings(used, []));
    }

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
    public void Rotacao_acima_do_fuso_vira_pendencia_e_aviso_na_arvore()
    {
        var job = new CamJob { Stock = new JobStock(0, 0, 100, 60, 10), Fixture = new JobFixture("morsa") };
        job.Contours.Add(Contour.FromPoints([new(0, 0), new(100, 0), new(100, 60), new(0, 60)]));
        CamOperationSpec op = CamJobInfo.NewOperation(JobOpKind.Contour, ContourSide.Outside, 0);
        CamOperationSpec fast = op with { Tool = op.Tool with { SpindleRpm = 9000 } };
        job.Operations.Add(op);
        job.Operations.Add(fast);
        JobBuild build = CamJobBuilder.Build(job, null);

        Assert.Equal(["rotação acima do fuso na operação 2"], CamJobInfo.Pending(job, build, 8000));
        Assert.Equal([1], CamJobInfo.OverSpindle(job, 8000));
        Assert.Equal(("pending", "aviso: acima do fuso"), CamJobInfo.OpState(build.Items[1], fast, 8000));
        Assert.Equal(("ok", "OK"), CamJobInfo.OpState(build.Items[0], op, 8000));

        // Sem limite informado: nada é suposto (sem pendência, operação OK)
        Assert.Empty(CamJobInfo.Pending(job, build, null));
        Assert.Equal(("ok", "OK"), CamJobInfo.OpState(build.Items[1], fast, null));

        // Operação desligada não conta
        job.Operations[1] = fast with { Enabled = false };
        Assert.Empty(CamJobInfo.OverSpindle(job, 8000));
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
