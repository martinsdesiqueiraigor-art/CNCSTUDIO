using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

/// <summary>OPS-A-01 A1/A2: entradas (rampa, hélice, mergulho reto, pré-furo) e estratégias (zigue-zague, de dentro para fora, espiral) do bolsão.</summary>
public class CamPocketOptionsTests
{
    private static readonly CamTool Tool = new(1, 10, 3000, 500, 200, "Fresa D10");
    private static readonly CamLevels Levels = new(50, 2, 0, -6, 3);
    private static readonly Contour Box = Contour.Rectangle(0, 0, 80, 60);

    private static CamOperation Ok(OperationResult r)
    {
        Assert.True(r.Ok, r.Error);
        return r.Operation!;
    }

    [Fact]
    public void Sem_escolha_o_bolsao_sai_igual_a_antes()
    {
        var plain = new PocketParams(60, 0, 2);
        Assert.False(plain.UsesOptions);
        CamOperation a = Ok(CamOperations.Pocket(Box, Tool, Levels, plain));
        CamOperation b = Ok(CamOperations.Pocket(Box, Tool, Levels, new PocketParams(60, 0, 2) { Entry = PocketEntry.Default, Strategy = PocketStrategy.Rings }));
        Assert.Equal(a.Moves, b.Moves);
    }

    [Fact]
    public void Rampa_com_angulo_nao_passa_do_angulo_digitado()
    {
        CamOperation op = Ok(CamOperations.Pocket(Box, Tool, Levels, new PocketParams(60, 0, 2) { Entry = PocketEntry.Ramp, RampAngle = 3 }));
        double tan = Math.Tan(3 * Math.PI / 180);
        for (int i = 1; i < op.Moves.Count; i++)
        {
            ToolMove a = op.Moves[i - 1], m = op.Moves[i];
            if (m.Kind == MoveKind.Feed && Math.Abs(m.Z - a.Z) > 1e-9 && a.XY.DistanceTo(m.XY) > 1e-6)
            {
                Assert.True((a.Z - m.Z) / a.XY.DistanceTo(m.XY) <= tan + 1e-6, $"mais íngreme que 3° no movimento {i}");
            }
        }

        Assert.Equal(-6, op.Moves.Min(m => m.Z), 6);
    }

    [Fact]
    public void Rampa_curta_avisa_as_voltas_e_gera()
    {
        CamOperation op = Ok(CamOperations.Pocket(Contour.Rectangle(0, 0, 30, 24), Tool, Levels, new PocketParams(60, 0, 2) { Entry = PocketEntry.Ramp, RampAngle = 0.3 }));
        Assert.Contains(op.Warnings, w => w.StartsWith("Rampa de", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    public void Rampa_sem_angulo_valido_nao_gera(double angle)
    {
        OperationResult r = CamOperations.Pocket(Box, Tool, Levels, new PocketParams(60, 0, 2) { Entry = PocketEntry.Ramp, RampAngle = angle });
        Assert.False(r.Ok);
        Assert.Contains("ângulo da rampa", r.Error);
    }

    [Fact]
    public void Helice_desce_em_arcos_e_chega_no_fundo_do_passe()
    {
        CamOperation op = Ok(CamOperations.Pocket(Box, Tool, Levels, new PocketParams(60, 0, 2) { Entry = PocketEntry.Helix, HelixDiameter = 16, HelixPitch = 1 }));
        var helix = op.Moves.Where(m => m.IsArc && m.Kind == MoveKind.ArcCcw).ToList();
        Assert.NotEmpty(helix);
        Assert.Equal(-3, helix.Where(m => m.Z > -3.0001).Min(m => m.Z), 6); // primeiro passe termina em −3
        Assert.DoesNotContain(op.Warnings, w => w.StartsWith("A hélice", StringComparison.Ordinal));
    }

    [Fact]
    public void Helice_que_nao_cabe_avisa_e_desce_em_rampa()
    {
        CamOperation op = Ok(CamOperations.Pocket(Contour.Rectangle(0, 0, 18, 18), Tool, Levels, new PocketParams(60, 0, 2) { Entry = PocketEntry.Helix, HelixDiameter = 20, HelixPitch = 1 }));
        Assert.Contains(op.Warnings, w => w.StartsWith("A hélice", StringComparison.Ordinal));
    }

    [Fact]
    public void Helice_menor_que_a_ferramenta_nao_gera()
    {
        OperationResult r = CamOperations.Pocket(Box, Tool, Levels, new PocketParams(60, 0, 2) { Entry = PocketEntry.Helix, HelixDiameter = 8, HelixPitch = 1 });
        Assert.False(r.Ok);
    }

    [Fact]
    public void Mergulho_reto_tem_descida_vertical_no_inicio_do_anel()
    {
        CamOperation op = Ok(CamOperations.Pocket(Box, Tool, Levels, new PocketParams(60, 0, 2) { Entry = PocketEntry.Plunge }));
        Assert.Contains(op.Moves.Zip(op.Moves.Skip(1)), t => t.Second.Kind == MoveKind.Feed && t.Second.Z < t.First.Z - 2.9 && t.Second.XY.DistanceTo(t.First.XY) < 1e-9);
    }

    [Fact]
    public void Pre_furo_mergulha_no_furo_e_sem_furo_avisa()
    {
        var job = new CamJob { ProgramNumber = 1, WorkOffset = 54, Zero = JobZero.Legacy, Name = "PF", Stock = new JobStock(-5, -5, 90, 70, 20) };
        job.Contours.Add(Box);
        job.Contours.Add(Contour.Circle(new Point2(40, 30), 3));
        var pocket = new CamOperationSpec(JobOpKind.Pocket, Tool, Levels) { ContourIndex = 0, PocketParams = new PocketParams(60, 0, 2) { Entry = PocketEntry.PreDrill } };
        job.Operations.Add(new CamOperationSpec(JobOpKind.Drilling, new CamTool(2, 6, 4000, 400, 150), Levels) { ContourIndex = 1, Drill = new DrillSpec(DrillCycleKind.Drill) });
        job.Operations.Add(pocket);

        CamOperation op = Ok(CamJobBuilder.BuildOperations(job, null)[1].Result!);
        Assert.Contains(op.Moves, m => m.Kind == MoveKind.Feed && Math.Abs(m.X - 40) < 1e-6 && Math.Abs(m.Y - 30) < 1e-6 && Math.Abs(m.Z + 3) < 1e-6);
        Assert.DoesNotContain(op.Warnings, w => w.StartsWith("Pré-furo", StringComparison.Ordinal));

        job.Operations.RemoveAt(0);
        CamOperation without = Ok(CamJobBuilder.BuildOperations(job, null)[0].Result!);
        Assert.Contains(without.Warnings, w => w.StartsWith("Pré-furo", StringComparison.Ordinal));
    }

    [Fact]
    public void Zigue_zague_limpa_com_passes_retos_e_o_contorno()
    {
        CamOperation op = Ok(CamOperations.Pocket(Box, Tool, Levels, new PocketParams(60, 0, 2) { Strategy = PocketStrategy.ZigZag }));
        Assert.Contains(op.Moves, m => m.Kind == MoveKind.Feed && Math.Abs(m.Z + 6) < 1e-6);
        Assert.Contains(op.Warnings, w => w.Contains("zigue-zague", StringComparison.Ordinal));
        // o centro da ferramenta nunca sai da área livre (parede − raio)
        Assert.All(op.Moves.Where(m => m.Kind == MoveKind.Feed), m => Assert.True(m.X >= 5 - 1e-6 && m.X <= 75 + 1e-6 && m.Y >= 5 - 1e-6 && m.Y <= 55 + 1e-6));
    }

    [Theory]
    [InlineData(PocketStrategy.Parallel)]
    [InlineData(PocketStrategy.Spiral)]
    public void De_dentro_para_fora_exige_escolher_a_entrada(PocketStrategy strategy)
    {
        OperationResult r = CamOperations.Pocket(Box, Tool, Levels, new PocketParams(60, 0, 2) { Strategy = strategy });
        Assert.False(r.Ok);
        Assert.Contains("escolha a entrada", r.Error);
    }

    [Theory]
    [InlineData(PocketStrategy.Parallel)]
    [InlineData(PocketStrategy.Spiral)]
    public void De_dentro_para_fora_termina_na_parede_e_fica_na_area_livre(PocketStrategy strategy)
    {
        CamOperation op = Ok(CamOperations.Pocket(Box, Tool, Levels, new PocketParams(60, 0, 2) { Strategy = strategy, Entry = PocketEntry.Plunge }));
        Assert.All(op.Moves.Where(m => m.Kind == MoveKind.Feed && m.Z <= -3 + 1e-6),
            m => Assert.True(m.X >= 5 - 1e-6 && m.X <= 75 + 1e-6 && m.Y >= 5 - 1e-6 && m.Y <= 55 + 1e-6));
        // a última volta de cada passe anda pela parede (centro da ferramenta a 5 mm dela)
        Assert.Contains(op.Moves, m => m.Kind == MoveKind.Feed && Math.Abs(m.Z + 6) < 1e-6 && (Math.Abs(m.X - 5) < 1e-6 || Math.Abs(m.Y - 5) < 1e-6));
    }

    [Fact]
    public void Estrategia_de_dentro_para_fora_com_ilhas_avisa_e_gera()
    {
        Contour island = Contour.Circle(new Point2(40, 30), 8);
        CamOperation op = Ok(CamOperations.PocketWithIslands(Box, [island], Tool, Levels, new PocketParams(60, 0, 2) { Strategy = PocketStrategy.Spiral, Entry = PocketEntry.Ramp, RampAngle = 5 }));
        Assert.Contains(op.Warnings, w => w.Contains("não vale no bolsão com ilhas", StringComparison.Ordinal));
    }

    [Fact]
    public void Arquivo_so_vira_versao_7_quando_usa_a_escolha_nova_e_volta_igual()
    {
        CamJob job = new() { ProgramNumber = 1, WorkOffset = 54, Zero = JobZero.Legacy, Name = "V7", Stock = new JobStock(-5, -5, 90, 70, 20) };
        job.Contours.Add(Box);
        job.Operations.Add(new CamOperationSpec(JobOpKind.Pocket, Tool, Levels) { ContourIndex = 0, PocketParams = new PocketParams(60, 0.2, 2) });
        Assert.Equal(1, System.Text.Json.JsonDocument.Parse(CamJobJson.ToJson(job)).RootElement.GetProperty("version").GetInt32());

        job.Operations[0] = job.Operations[0] with
        {
            PocketParams = new PocketParams(60, 0.2, 2) { Entry = PocketEntry.Helix, HelixDiameter = 16, HelixPitch = 1.5, Strategy = PocketStrategy.Spiral },
        };
        string json = CamJobJson.ToJson(job);
        Assert.Equal(7, System.Text.Json.JsonDocument.Parse(json).RootElement.GetProperty("version").GetInt32());
        JobLoadResult back = CamJobJson.FromJson(json);
        Assert.True(back.Job is not null, back.Error);
        PocketParams p = back.Job!.Operations[0].PocketParams!;
        Assert.Equal((PocketEntry.Helix, PocketStrategy.Spiral, 16.0, 1.5), (p.Entry, p.Strategy, p.HelixDiameter, p.HelixPitch));
    }

    [Fact]
    public void Campo_da_escolha_so_conta_quando_a_entrada_o_usa()
    {
        CamOperationSpec s = new CamOperationSpec(JobOpKind.Pocket, Tool, Levels) { PocketParams = new PocketParams() { Entry = PocketEntry.Plunge } } with { MissingFields = [CamJobInfo.RampAngleField] };
        Assert.Empty(CamJobInfo.MissingFields(s));
        s = s with { PocketParams = new PocketParams() { Entry = PocketEntry.Ramp } };
        Assert.Equal([CamJobInfo.RampAngleField], CamJobInfo.MissingFields(s));
    }
}
