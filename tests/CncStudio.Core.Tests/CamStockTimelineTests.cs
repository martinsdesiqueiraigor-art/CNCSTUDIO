using System.Diagnostics;
using CncStudio.Core.Cam;
using CncStudio.Core.Dialects;
using CncStudio.Core.Parsing;
using CncStudio.Core.Simulation;
using Xunit.Abstractions;

namespace CncStudio.Core.Tests;

/// <summary>CAM-STOCK-01: bruto depois de cada operação de fresa, calculado no Core e nunca gravado.</summary>
public class CamStockTimelineTests(ITestOutputHelper output)
{
    private static string Dir => Path.Combine(AppContext.BaseDirectory, "trabalhos-referencia");

    private static string Read(string file) => File.ReadAllText(Path.Combine(Dir, file)).Replace("\r\n", "\n");

    private static CamJob Load(string name)
    {
        JobLoadResult loaded = CamJobJson.FromJson(Read(name + ".casillas-job"));
        Assert.True(loaded.Ok, loaded.Error);
        return loaded.Job!;
    }

    private static MachineProfile? Machine(string name)
    {
        if (!File.Exists(Path.Combine(Dir, name + ".maquina.json")))
        {
            return null;
        }

        (MachineProfile? machine, string? error) = MachineProfileJson.Parse(Read(name + ".maquina.json"));
        Assert.Null(error);
        return machine;
    }

    [Fact]
    public void Cada_operacao_tira_material_e_o_volume_fecha()
    {
        CamJob job = Load("fresa-bloco-siemens");
        CamStockTimeline t = CamStockTimeline.Compute(job, null);
        Assert.Null(t.Problem);
        Assert.False(t.UsesVoxels);
        Assert.All(t.States, s => Assert.Equal(CamStockStatus.Ready, s.Status));
        Assert.True(t.States[0].RemovedVolume > 0);
        double removed = t.States.Sum(s => s.RemovedVolume);
        Assert.Equal(t.InitialVolume - t.States[^1].Volume, removed, 3);
        Assert.True(t.States[^1].Volume < t.InitialVolume);
    }

    [Theory]
    [InlineData("fresa-bloco-siemens")]
    [InlineData("fresa-zero-g55-canto")]
    [InlineData("fresa-fixacao")]
    public void Bruto_depois_da_ultima_igual_ao_programa_inteiro(string name)
    {
        // operação por operação (bruto passado de uma para a outra) = o programa inteiro simulado de uma vez, na mesma grade
        CamJob job = Load(name);
        CamStockTimeline t = CamStockTimeline.Compute(job, null);
        Assert.Null(t.Problem);
        Assert.True(t.ShowAfter(t.States.Count - 1));
        HeightMap chained = t.Map!;

        JobBuild build = CamJobBuilder.Build(job, null);
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        Dialect dialect = job.Dialect == CamDialect.Siemens ? Dialect.Siemens : Dialect.Fanuc;
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(build.Program, dialect);
        var whole = new HeightMap(chained.OriginX, chained.OriginY, chained.Columns, chained.Rows, chained.CellSize, chained.TopZ, chained.BottomZ);
        var sim = new MaterialSimulator(whole, analysis.ToolPath.Moves, SimulationFactory.MillProfiles(null, analysis.ToolDiameterOf));
        sim.RunToEnd();

        int different = 0;
        for (int k = 0; k < whole.Heights.Length; k++)
        {
            if (Math.Abs(whole.Heights[k] - chained.Heights[k]) > 1e-4)
            {
                different++;
            }
        }

        Assert.Equal(0, different);
    }

    [Fact]
    public void Operacao_incompleta_para_o_calculo_sem_pular()
    {
        CamJob job = Load("fresa-bloco-siemens");
        job.Operations[1] = job.Operations[1] with { Tool = job.Operations[1].Tool with { Diameter = 0 } };
        CamStockTimeline t = CamStockTimeline.Compute(job, null);
        Assert.Equal(CamStockStatus.Ready, t.States[0].Status);
        Assert.Equal(CamStockStatus.NoState, t.States[1].Status);
        Assert.Contains("Diâmetro", t.States[1].Reason);
        for (int i = 2; i < t.States.Count; i++)
        {
            Assert.Equal(CamStockStatus.NoState, t.States[i].Status);
            Assert.Equal("Operação 2 anterior sem estado.", t.States[i].Reason);
        }

        Assert.False(t.ShowAfter(2));
        Assert.True(t.ShowAfter(0));
    }

    [Fact]
    public void Operacao_desligada_passa_o_bruto_igual()
    {
        CamJob job = Load("fresa-bloco-siemens");
        job.Operations[1] = job.Operations[1] with { Enabled = false };
        CamStockTimeline t = CamStockTimeline.Compute(job, null);
        Assert.Equal(CamStockStatus.Disabled, t.States[1].Status);
        Assert.Equal(t.States[0].Volume, t.States[1].Volume);
        Assert.True(t.ShowAfter(0));
        float[] after0 = t.Map!.Heights.ToArray();
        Assert.True(t.ShowAfter(1));
        Assert.Equal(after0, t.Map.Heights.ToArray());
        Assert.Equal(CamStockStatus.Ready, t.States[2].Status);
    }

    [Fact]
    public void Sem_bruto_fica_tudo_sem_estado()
    {
        CamJob job = Load("fresa-sem-bruto");
        CamStockTimeline t = CamStockTimeline.Compute(job, null);
        Assert.Equal(CamJobInfo.StockMissing + ".", t.Problem);
        Assert.All(t.States, s => Assert.Equal(CamStockStatus.NoState, s.Status));
        Assert.Null(t.Map);
        Assert.False(t.ShowAfter(0));
    }

    [Fact]
    public void Bruto_incompleto_fica_sem_estado()
    {
        CamJob job = Load("fresa-bloco-siemens");
        job.SetStock(null, new JobStockShape(StockShapeKind.Cylinder) { Diameter = null, Length = 30 });
        CamStockTimeline t = CamStockTimeline.Compute(job, null);
        Assert.StartsWith(CamJobStock.IncompletePrefix, t.Problem);
        Assert.All(t.States, s => Assert.Equal(CamStockStatus.NoState, s.Status));
    }

    [Fact]
    public void Bruto_STEP_fica_sem_estado()
    {
        CamJob job = Load("fresa-bloco-siemens");
        job.SetStock(null, new JobStockShape(StockShapeKind.Step)
        {
            StepPath = "C:/bruto.step", StepSummary = new StepStockSummary(-5, -5, -40, 200, 200, 2, 1000), OffsetX = null, OffsetY = null, OffsetZ = null,
        });
        CamStockTimeline t = CamStockTimeline.Compute(job, null);
        Assert.StartsWith("Bruto STEP", t.Problem);
        Assert.All(t.States, s => Assert.Equal(CamStockStatus.NoState, s.Status));
    }

    [Fact]
    public void Sem_zero_fica_sem_estado()
    {
        CamJob job = Load("fresa-zero-g55-canto");
        job.Zero = new JobZero(null, ZeroLevel.Top);
        CamStockTimeline t = CamStockTimeline.Compute(job, null);
        Assert.StartsWith("Zero da peça incompleto", t.Problem);
        Assert.All(t.States, s => Assert.Equal(CamStockStatus.NoState, s.Status));
    }

    [Theory]
    [InlineData("fresa-3mais2")]
    [InlineData("fresa-texto-cilindro")]
    public void Peca_girada_usa_voxels(string name)
    {
        CamJob job = Load(name);
        CamStockTimeline t = CamStockTimeline.Compute(job, Machine(name), maxCellsPerSide: SimulationFactory.PhoneCells);
        Assert.Null(t.Problem);
        Assert.True(t.UsesVoxels);
        Assert.Null(t.Map);
        Assert.All(t.States, s => Assert.Equal(CamStockStatus.Ready, s.Status));
        Assert.True(t.States[^1].Volume < t.InitialVolume);
    }

    [Fact]
    public void Mudar_a_terceira_nao_recalcula_as_anteriores()
    {
        CamJob job = Load("fresa-bloco-siemens");
        CamStockTimeline first = CamStockTimeline.Compute(job, null);
        Assert.Equal(job.Operations.Count, first.Simulated);

        CamOperationSpec third = job.Operations[2];
        job.Operations[2] = third with { Tool = third.Tool with { SpindleRpm = third.Tool.SpindleRpm + 100 } };
        CamStockTimeline second = CamStockTimeline.Compute(job, null, previous: first);
        Assert.True(second.States[0].Reused);
        Assert.True(second.States[1].Reused);
        Assert.False(second.States[2].Reused);
        Assert.Equal(job.Operations.Count - 2, second.Simulated);
        Assert.Equal(first.States[^1].Volume, second.States[^1].Volume, 6);

        // nada mudou: tudo reaproveitado
        CamStockTimeline third2 = CamStockTimeline.Compute(job, null, previous: second);
        Assert.Equal(0, third2.Simulated);
    }

    [Fact]
    public void Cilindro_em_pe_nao_tem_material_fora_do_circulo()
    {
        CamJob job = Load("fresa-bloco-siemens");
        job.PartHeight ??= 20;
        job.SetStock(null, new JobStockShape(StockShapeKind.Cylinder) { Diameter = 150, Length = 30 });
        CamStockTimeline t = CamStockTimeline.Compute(job, null);
        Assert.Null(t.Problem);
        Assert.False(t.UsesVoxels);
        Assert.True(t.Map!.HasEmptyCells);
        Assert.True(t.ShowAfter(-1));
        Assert.True(t.Map.IsEmptyCell(0, 0));
        Assert.Equal(t.Map.BottomZ, t.Map[0, 0], 4);
        double exact = Math.PI * 75 * 75 * 30;
        Assert.True(Math.Abs(t.InitialVolume - exact) / exact < 0.02, $"{t.InitialVolume} x {exact}");
    }

    [Fact]
    public void Cilindro_deitado_usa_voxels()
    {
        CamJob job = Load("fresa-bloco-siemens");
        job.PartHeight ??= 20;
        job.SetStock(null, new JobStockShape(StockShapeKind.Cylinder) { Axis = StockAxis.X, Diameter = 90, Length = 160 });
        CamStockTimeline t = CamStockTimeline.Compute(job, null, maxCellsPerSide: SimulationFactory.PhoneCells);
        Assert.Null(t.Problem);
        Assert.True(t.UsesVoxels);
    }

    [Fact]
    public void Medida_de_tempo_e_memoria()
    {
        // trabalho maior: as operações do bloco repetidas com passe menor (mais movimentos); só mede e escreve na saída
        CamJob job = Load("fresa-bloco-siemens");
        int n = job.Operations.Count;
        for (int r = 0; r < 3; r++)
        {
            for (int i = 0; i < n; i++)
            {
                CamOperationSpec s = job.Operations[i];
                job.Operations.Add(s with { Levels = s.Levels with { StepDown = Math.Max(0.25, s.Levels.StepDown / 8) } });
            }
        }

        var watch = Stopwatch.StartNew();
        CamStockTimeline t = CamStockTimeline.Compute(job, null);
        watch.Stop();
        int moves = t.States.Where(s => s.Path is not null).Sum(s => s.Path!.Moves.Count);
        long bytes = (t.Map!.Heights.Length * sizeof(float)) + (t.Map.Heights.Length * sizeof(ushort));
        output.WriteLine($"operações {t.States.Count}, movimentos {moves}, grade {t.Map.Columns}×{t.Map.Rows}, tempo {watch.ElapsedMilliseconds} ms, instantâneo {bytes / 1024} KB");
        Assert.All(t.States, s => Assert.Equal(CamStockStatus.Ready, s.Status));

        // voxels na resolução do PC (3+2)
        CamJob rotary = Load("fresa-3mais2");
        watch.Restart();
        CamStockTimeline v = CamStockTimeline.Compute(rotary, Machine("fresa-3mais2"));
        watch.Stop();
        int vmoves = v.States.Where(s => s.Path is not null).Sum(s => s.Path!.Moves.Count);
        output.WriteLine($"voxels: operações {v.States.Count}, movimentos {vmoves}, grade {v.Voxels!.Nx}×{v.Voxels.Ny}×{v.Voxels.Nz} (voxel {v.Voxels.VoxelSize:0.###} mm), tempo {watch.ElapsedMilliseconds} ms, instantâneo {v.Voxels.TakeSnapshot().Length * 8 / 1024} KB");
    }
}
