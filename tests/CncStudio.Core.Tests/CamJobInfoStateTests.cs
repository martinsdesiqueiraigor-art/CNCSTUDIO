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
