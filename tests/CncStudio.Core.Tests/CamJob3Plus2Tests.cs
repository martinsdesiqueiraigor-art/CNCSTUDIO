using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

/// <summary>Face 3+2 no Trabalho fresa: campos opcionais no arquivo (versão 1), perfil conferido e programa com os dois giros.</summary>
public class CamJob3Plus2Tests
{
    private static readonly CamTool Tool = new(1, 10, 3000, 500, 200, "Fresa D10");
    private static readonly CamLevels Levels = new(50, 2, 0, -6, 3);
    private static readonly RotaryFaceSpec Tilted = new RotaryFaceSpec("A", 90, 25) with { Axis2 = "C", Angle2 = 45 };

    private static CamJob Job(RotaryFaceSpec? face)
    {
        var job = new CamJob { Name = "PECA 3+2", Dialect = CamDialect.Fanuc, Stock = new JobStock(-5, -5, 70, 50, 20) };
        job.Contours.Add(Contour.Rectangle(0, 0, 60, 40));
        job.Contours.Add(Contour.Circle(new Point2(30, 20), 4));
        job.Operations.Add(new CamOperationSpec(JobOpKind.Contour, Tool, Levels) { ContourIndex = 0, ContourParams = new ContourParams(ContourSide.Outside, true, false, 0, 3), Face = face });
        job.Operations.Add(new CamOperationSpec(JobOpKind.Drilling, new CamTool(2, 6, 4000, 400, 150), Levels) { ContourIndex = 1, Drill = new DrillSpec(DrillCycleKind.Peck, 2, 0), Face = face });
        return job;
    }

    private static MachineProfile TableTable()
    {
        MachineProfile m = MachineProfile.FiveAxisTableTable();
        m.Axes[0].PivotY = 20;
        m.Axes[0].PivotZ = -25;
        return m;
    }

    [Fact]
    public void Gera_os_dois_giros_e_avisa_que_nao_foi_verificado()
    {
        JobBuild build = CamJobBuilder.Build(Job(Tilted), TableTable());
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        string[] lines = build.Program.Split('\n');
        int c = Array.FindIndex(lines, l => l.Contains("C45."));
        int a = Array.FindIndex(lines, l => l.Contains("A90."));
        Assert.True(c >= 0 && a > c, build.Program);
        Assert.Contains(build.Warnings, w => w.Contains("NÃO VERIFICADO EM MÁQUINA"));
    }

    [Fact]
    public void Perfil_diferente_da_face_recusa_com_o_motivo()
    {
        JobBuild four = CamJobBuilder.Build(Job(Tilted), MachineProfile.FourAxisTableA());
        Assert.False(four.Ok);
        Assert.Contains(four.Errors, e => e.Contains("3+2") && e.Contains("A + C"));
        Assert.False(CamJobBuilder.Build(Job(Tilted), null).Ok);
        JobBuild over = CamJobBuilder.Build(Job(Tilted with { Angle = 150 }), TableTable());
        Assert.Contains(over.Errors, e => e.Contains("fora do curso"));
    }

    [Fact]
    public void Arquivo_guarda_e_le_o_segundo_eixo_na_versao_1()
    {
        CamJob job = Job(Tilted);
        string json = CamJobJson.ToJson(job);
        Assert.Contains("\"axis2\": \"C\"", json);
        Assert.Contains("\"version\": 1", json);
        JobLoadResult loaded = CamJobJson.FromJson(json);
        Assert.True(loaded.Job is not null, loaded.Error);
        Assert.Equal(Tilted, loaded.Job!.Operations[0].Face);
        Assert.Equal(json, CamJobJson.ToJson(loaded.Job));
        Assert.Equal(CamJobBuilder.Build(job, TableTable()).Program, CamJobBuilder.Build(loaded.Job, TableTable()).Program);
    }

    [Fact]
    public void Face_de_um_eixo_nao_ganha_campos_novos_no_arquivo()
    {
        string json = CamJobJson.ToJson(Job(new RotaryFaceSpec("A", 90, 25)));
        Assert.DoesNotContain("axis2", json);
        Assert.Null(CamJobJson.FromJson(json).Job!.Operations[0].Face!.Axis2);
    }

    [Fact]
    public void Segundo_eixo_invalido_no_arquivo_e_recusado()
    {
        string json = CamJobJson.ToJson(Job(Tilted));
        Assert.Null(CamJobJson.FromJson(json.Replace("\"axis2\": \"C\"", "\"axis2\": \"A\"")).Job);
        Assert.Null(CamJobJson.FromJson(json.Replace("\"axis2\": \"C\"", "\"axis2\": \"X\"")).Job);
        Assert.Null(CamJobJson.FromJson(json.Replace("\"angle2\": 45", "\"angle2\": \"x\"")).Job);
    }

    [Fact]
    public void Titulo_da_operacao_mostra_os_dois_angulos()
    {
        CamJob job = Job(Tilted);
        Assert.Contains("face A90° C45° (3+2)", CamJobInfo.OpTitle(0, job.Operations[0]));
    }
}
