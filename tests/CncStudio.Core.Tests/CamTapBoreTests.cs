using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

/// <summary>OPS-A-01 A4/A5: macho (G84 com M29) e mandrilhar (G85/G89/G86), só Fanuc.</summary>
public class CamTapBoreTests
{
    private static readonly CamLevels Levels = new(50, 2, 0, -12, 3);

    private static CamJob Job(DrillCycleKind kind, double dwell = 0, CamDialect dialect = CamDialect.Fanuc)
    {
        var job = new CamJob { ProgramNumber = 1001, WorkOffset = 54, Zero = JobZero.Legacy, Name = "FURO", Dialect = dialect, Stock = new JobStock(-5, -5, 70, 50, 20) };
        job.Contours.Add(Contour.Circle(new Point2(20, 20), 8));
        job.Contours.Add(Contour.Circle(new Point2(40, 20), 8));
        job.Operations.Add(new CamOperationSpec(JobOpKind.Drilling, new CamTool(2, 8, 500, 750, 750, "Macho M8"), Levels) { Drill = new DrillSpec(kind, 0, dwell) });
        return job;
    }

    [Theory]
    [InlineData(DrillCycleKind.Tap, 0, "G84")]
    [InlineData(DrillCycleKind.BoreFeed, 0, "G85")]
    [InlineData(DrillCycleKind.BoreFeed, 0.5, "G89 P500")]
    [InlineData(DrillCycleKind.BoreStop, 0, "G86")]
    public void Fanuc_gera_o_ciclo_certo_e_o_simulador_executa(DrillCycleKind kind, double dwell, string code)
    {
        JobBuild build = CamJobBuilder.Build(Job(kind, dwell), null);
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        Assert.Contains(code, build.Program);
        Assert.Equal(kind == DrillCycleKind.Tap, build.Program.Contains("M29 S500", StringComparison.Ordinal));
        var analysis = ProgramAnalyzer.Analyze(build.Program, Dialects.Dialect.Fanuc);
        Assert.DoesNotContain(analysis.Diagnostics, d => d.Severity == Parsing.DiagnosticSeverity.Error);
        var parts = Simulation.SimulationFactory.Create(analysis, null, build.Program);
        parts.Simulation.RunToEnd();
        Assert.NotNull(parts.Map);
    }

    [Theory]
    [InlineData(DrillCycleKind.Tap)]
    [InlineData(DrillCycleKind.BoreFeed)]
    [InlineData(DrillCycleKind.BoreStop)]
    public void Siemens_recusa_macho_e_mandrilhar_com_aviso_claro(DrillCycleKind kind)
    {
        JobBuild build = CamJobBuilder.Build(Job(kind, 0, CamDialect.Siemens), null);
        Assert.False(build.Ok);
        Assert.Contains(build.Errors, e => e.Contains("só saem em Fanuc", StringComparison.Ordinal));
    }

    [Fact]
    public void Macho_sem_rotacao_ou_avanco_nao_gera()
    {
        OperationResult r = CamOperations.Drilling(new CamTool(2, 8, 0, 750, 750), 50, [new Point2(0, 0)], DrillCycleKind.Tap, 0, -10, 2);
        Assert.False(r.Ok);
    }

    [Fact]
    public void Arquivo_com_macho_vira_versao_7_e_volta_igual()
    {
        string json = CamJobJson.ToJson(Job(DrillCycleKind.Tap));
        Assert.Contains("\"version\": 7", json);
        Assert.Equal(DrillCycleKind.Tap, CamJobJson.FromJson(json).Job!.Operations[0].Drill!.Cycle);
    }
}
