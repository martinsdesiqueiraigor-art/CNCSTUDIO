using CncStudio.Core.Cam;
using Xunit;

namespace CncStudio.Core.Tests;

public class CamJobInfoStateTests
{
    [Fact]
    public void OpState_cobre_os_casos()
    {
        Assert.Equal("pending", CamJobInfo.OpState(null).Kind);
        Assert.Equal("off", CamJobInfo.OpState(new JobOpResult(0, true, null)).Kind);
        Assert.Equal("bad", CamJobInfo.OpState(new JobOpResult(0, false, null)).Kind);
    }

    [Fact]
    public void StockState_sem_bruto_e_pendente() => Assert.Equal("bad", CamJobInfo.StockState(null).Kind);
}

public class CamJobPendingTests
{
    [Fact]
    public void Pending_job_vazio_lista_peca_bruto_e_operacao()
    {
        var job = new CamJob();
        var p = CamJobInfo.Pending(job, CamJobBuilder.Build(job, null));
        Assert.Contains(p, x => x.Contains("peça"));
        Assert.Contains(p, x => x.Contains("Bruto"));
        Assert.Contains(p, x => x.Contains("operação"));
    }
}

public class LatheJobPendingTests
{
    [Fact]
    public void Pending_trabalho_vazio_lista_perfil_e_operacao()
    {
        var job = new LatheJob();
        var p = LatheJobInfo.Pending(job, LatheJobBuilder.Build(job));
        Assert.Contains(p, x => x.Contains("perfil"));
        Assert.Contains(p, x => x.Contains("operação"));
    }
}
