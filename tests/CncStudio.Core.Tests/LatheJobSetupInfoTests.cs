using CncStudio.Core.Cam;
using Xunit;

namespace CncStudio.Core.Tests;

public class LatheJobSetupInfoTests
{
    private static LatheJob FacingJob(LatheTool tool)
    {
        var job = new LatheJob();
        job.Operations.Add(LatheJobInfo.NewOperation(LatheOpKind.Facing, false, tool));
        return job;
    }

    [Fact]
    public void OriginTitle_mostra_o_deslocamento()
    {
        var job = new LatheJob { WorkOffset = 55 };
        Assert.Equal("Origem G55: X0 no centro da peça, Z0 do perfil", LatheJobInfo.OriginTitle(job));
    }

    [Fact]
    public void SpindleRpm_usa_a_fixa_ou_o_G50()
    {
        Assert.Equal(1200, LatheJobInfo.SpindleRpm(new LatheTool(1, Rpm: 1200, MaxRpm: 4000)));
        Assert.Equal(4000, LatheJobInfo.SpindleRpm(new LatheTool(1, Rpm: 0, MaxRpm: 4000)));
    }

    [Fact]
    public void SpindleWarning_so_acima_do_limite_informado()
    {
        var g96 = new LatheTool(1, MaxRpm: 4000);
        Assert.Null(LatheJobInfo.SpindleWarning(g96, null));
        Assert.Null(LatheJobInfo.SpindleWarning(g96, 4000));
        Assert.Equal("Rotação máxima (G50) acima do limite do fuso (3500 rpm). Digite até 3500.", LatheJobInfo.SpindleWarning(g96, 3500));
        Assert.Equal("Rotação fixa acima do limite do fuso (800 rpm). Digite até 800.", LatheJobInfo.SpindleWarning(new LatheTool(1, Rpm: 900), 800));
    }

    [Fact]
    public void Pending_e_estado_marcam_a_operacao_acima_do_fuso()
    {
        LatheJob job = FacingJob(new LatheTool(1, MaxRpm: 4000));
        LatheBuild build = LatheJobBuilder.Build(job);
        Assert.True(build.Ok);
        // o trabalho sem perfil já tem a pendência do perfil; aqui só importa a do fuso
        Assert.DoesNotContain(LatheJobInfo.Pending(job, build), x => x.Contains("fuso"));
        Assert.DoesNotContain(LatheJobInfo.Pending(job, build, 4000), x => x.Contains("fuso"));
        Assert.Equal("rotação acima do fuso na operação 1", LatheJobInfo.Pending(job, build, 3000)[^1]);
        Assert.Equal(("pending", "aviso: acima do fuso"), LatheJobInfo.OpState(build.Items[0], job.Operations[0], 3000));
        Assert.Equal(("ok", "OK"), LatheJobInfo.OpState(build.Items[0], job.Operations[0], null));
    }

    [Fact]
    public void Operacao_desligada_nao_vira_pendencia_de_fuso()
    {
        LatheJob job = FacingJob(new LatheTool(1, MaxRpm: 4000));
        job.Operations[0] = job.Operations[0] with { Enabled = false };
        Assert.Empty(LatheJobInfo.OverSpindle(job, 3000));
        Assert.Equal("off", LatheJobInfo.OpState(new LatheOpResult(0, true, null), job.Operations[0], 3000).Kind);
    }

    [Fact]
    public void SpindleWarning_nao_muda_o_programa()
    {
        LatheJob job = FacingJob(new LatheTool(1, MaxRpm: 4000));
        string before = LatheJobBuilder.Build(job).Program;
        _ = LatheJobInfo.Pending(job, LatheJobBuilder.Build(job), 3000);
        Assert.Equal(before, LatheJobBuilder.Build(job).Program);
        Assert.Contains("4000", before);
    }
}
