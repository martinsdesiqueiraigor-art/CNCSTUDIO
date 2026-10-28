using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

public class CamJobTests
{
    private static readonly CamTool Tool = new(1, 10, 3000, 500, 200, "Fresa D10");
    private static readonly CamLevels Levels = new(50, 2, 0, -6, 3);

    private static CamJob Sample()
    {
        var job = new CamJob { Name = "PECA TESTE", Dialect = CamDialect.Fanuc, Stock = new JobStock(-5, -5, 70, 50, 20) };
        job.Contours.Add(Contour.Rectangle(0, 0, 60, 40));
        job.Contours.Add(Contour.Circle(new Point2(30, 20), 12));
        job.Operations.Add(new CamOperationSpec(JobOpKind.Contour, Tool, Levels) { ContourIndex = 0, ContourParams = new ContourParams(ContourSide.Outside, true, false, 0, 3) });
        job.Operations.Add(new CamOperationSpec(JobOpKind.Pocket, Tool, Levels) { ContourIndex = 1, PocketParams = new PocketParams(60, 0.2, 2) });
        job.Operations.Add(new CamOperationSpec(JobOpKind.Drilling, new CamTool(2, 6, 4000, 400, 150), Levels) { Drill = new DrillSpec(DrillCycleKind.Peck, 2, 0) });
        return job;
    }

    private static MachineProfile Machine()
    {
        MachineProfile m = MachineProfile.FourAxisTableA();
        m.Axes[0].PivotY = 20;
        m.Axes[0].PivotZ = -25;
        return m;
    }

    [Theory]
    [InlineData(CamDialect.Fanuc)]
    [InlineData(CamDialect.Siemens)]
    public void Job_gera_o_mesmo_programa_que_o_fluxo_direto(CamDialect dialect)
    {
        CamJob job = Sample();
        job.Dialect = dialect;
        JobBuild build = CamJobBuilder.Build(job, null);
        Assert.True(build.Ok, string.Join("; ", build.Errors));

        // O mesmo caminho que a tela do CAM seguia: operações geradas uma a uma e entregues ao pós
        var ops = new List<CamOperation>
        {
            CamOperations.Contour(job.Contours[0], Tool, Levels, new ContourParams(ContourSide.Outside, true, false, 0, 3)).Operation!,
            CamOperations.Pocket(job.Contours[1], Tool, Levels, new PocketParams(60, 0.2, 2)).Operation!,
            CamOperations.Drilling(new CamTool(2, 6, 4000, 400, 150), Levels.SafeZ, [new Point2(30, 20)], DrillCycleKind.Peck, 0, -6, 2, 2).Operation!,
        };
        string direct = CamPost.Generate(ops, new PostOptions(dialect, "PECA TESTE", 1001, 54, true));
        Assert.Equal(direct, build.Program);
    }

    [Fact]
    public void Salvar_e_abrir_devolve_o_mesmo_job_e_o_mesmo_programa()
    {
        CamJob job = Sample();
        job.Contours.Add(new Contour([
            Segment2.Line(new Point2(0, 0), new Point2(20, 0)),
            Segment2.Arc(new Point2(20, 0), new Point2(20, 10), new Point2(20, 5), true),
            Segment2.Line(new Point2(20, 10), new Point2(0, 10)),
            Segment2.Line(new Point2(0, 10), new Point2(0, 0)),
        ]));
        job.Operations.Add(new CamOperationSpec(JobOpKind.Facing, Tool, Levels) { ContourIndex = 0, FacingStepOverPercent = 70, Label = "Facear topo" });
        string json = CamJobJson.ToJson(job);

        JobLoadResult loaded = CamJobJson.FromJson(json);
        Assert.True(loaded.Ok, loaded.Error);
        Assert.Equal(json, CamJobJson.ToJson(loaded.Job!));
        Assert.Equal(CamJobBuilder.Build(job, null).Program, CamJobBuilder.Build(loaded.Job!, null).Program);
        Assert.Contains("Facear topo", CamJobBuilder.Build(loaded.Job!, null).Program);
        Assert.Equal(new JobStock(-5, -5, 70, 50, 20), loaded.Job!.Stock);
        Assert.True(loaded.Job.Contours[2].Segments[1].IsArc);
    }

    [Fact]
    public void Salvar_e_abrir_com_face_girada_e_texto_no_cilindro()
    {
        CamJob job = Sample();
        job.Operations.Add(new CamOperationSpec(JobOpKind.Contour, Tool, Levels) { ContourIndex = 0, ContourParams = new ContourParams(ContourSide.OnLine), Face = new RotaryFaceSpec("A", 90, 25) });
        job.Operations.Add(new CamOperationSpec(JobOpKind.WrapText, Tool, Levels) { Wrap = new WrapSpec("CASILLAS", 6, "A", new WrapSetup(40, 5, 0, 0.3, 0.15, 3, 200, 60)) });
        JobLoadResult loaded = CamJobJson.FromJson(CamJobJson.ToJson(job));
        Assert.True(loaded.Ok, loaded.Error);
        Assert.Equal(job.Operations[3].Face, loaded.Job!.Operations[3].Face);
        Assert.Equal(job.Operations[4].Wrap, loaded.Job.Operations[4].Wrap);
        Assert.Equal(CamJobBuilder.Build(job, Machine()).Program, CamJobBuilder.Build(loaded.Job, Machine()).Program);
    }

    [Fact]
    public void Operacao_desligada_nao_entra_no_programa_e_a_ordem_vale()
    {
        CamJob job = Sample();
        string all = CamJobBuilder.Build(job, null).Program;
        job.Operations[1] = job.Operations[1] with { Enabled = false };
        JobBuild without = CamJobBuilder.Build(job, null);
        Assert.True(without.Ok);
        Assert.Equal(2, without.Operations.Count);
        Assert.True(without.Items[1].Skipped);
        Assert.NotEqual(all, without.Program);

        job.Operations[1] = job.Operations[1] with { Enabled = true };
        Assert.True(job.MoveOperation(2, 0));
        string moved = CamJobBuilder.Build(job, null).Program;
        Assert.True(moved.IndexOf("G83", StringComparison.Ordinal) < moved.IndexOf("G1 ", StringComparison.Ordinal));
        Assert.False(job.MoveOperation(0, 9));
    }

    [Fact]
    public void Duplicar_copia_logo_abaixo()
    {
        CamJob job = Sample();
        Assert.Equal(1, job.DuplicateOperation(0));
        Assert.Equal(4, job.Operations.Count);
        Assert.Equal(job.Operations[0], job.Operations[1]);
        Assert.Equal(-1, job.DuplicateOperation(9));
    }

    [Fact]
    public void Face_girada_pelo_job_gira_uma_vez_e_precisa_do_perfil_da_maquina()
    {
        CamJob job = Sample();
        job.Operations[0] = job.Operations[0] with { Face = new RotaryFaceSpec("A", 90, 25) };
        Assert.False(CamJobBuilder.Build(job, null).Ok);
        JobBuild build = CamJobBuilder.Build(job, Machine());
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        Assert.Contains("A90.", build.Program);
    }

    [Fact]
    public void Erros_apontam_a_operacao_e_nao_geram_programa()
    {
        CamJob job = Sample();
        job.Operations[0] = job.Operations[0] with { ContourIndex = 7 };
        JobBuild structure = CamJobBuilder.Build(job, null);
        Assert.False(structure.Ok);
        Assert.Equal("", structure.Program);
        Assert.Contains("Operação 1", structure.Errors[0]);

        job = Sample();
        job.Operations[1] = job.Operations[1] with { Tool = Tool with { Diameter = 0 } };
        JobBuild tool = CamJobBuilder.Build(job, null);
        Assert.False(tool.Ok);
        Assert.Contains("Operação 2", tool.Errors[0]);

        job = Sample();
        foreach (int i in Enumerable.Range(0, job.Operations.Count))
        {
            job.Operations[i] = job.Operations[i] with { Enabled = false };
        }

        Assert.Contains("ligada", CamJobBuilder.Build(job, null).Errors[0]);
    }

    [Fact]
    public void Furacao_sem_circulo_avisa()
    {
        var job = new CamJob();
        job.Contours.Add(Contour.Rectangle(0, 0, 10, 10));
        job.Operations.Add(new CamOperationSpec(JobOpKind.Drilling, Tool, Levels) { Drill = new DrillSpec() });
        JobBuild build = CamJobBuilder.Build(job, null);
        Assert.False(build.Ok);
        Assert.Contains("círculo", build.Errors[0]);
    }

    [Fact]
    public void Texto_no_cilindro_pede_perfil_e_texto_valido()
    {
        var job = new CamJob();
        job.Operations.Add(new CamOperationSpec(JobOpKind.WrapText, Tool, Levels) { Wrap = new WrapSpec("ab#", 6, "A", new WrapSetup(40)) });
        Assert.False(CamJobBuilder.Build(job, null).Ok);
        JobBuild bad = CamJobBuilder.Build(job, Machine());
        Assert.False(bad.Ok);
        Assert.Contains("caractere", bad.Errors[0]);
    }

    [Theory]
    [InlineData("isto não é json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"format\":\"outro\",\"version\":1}")]
    public void Arquivo_invalido_nao_abre(string text)
    {
        JobLoadResult r = CamJobJson.FromJson(text);
        Assert.False(r.Ok);
        Assert.False(string.IsNullOrEmpty(r.Error));
    }

    [Fact]
    public void Versao_mais_nova_ou_dado_estranho_sao_recusados()
    {
        string json = CamJobJson.ToJson(Sample());
        JobLoadResult newer = CamJobJson.FromJson(json.Replace("\"version\": 1", "\"version\": 2"));
        Assert.False(newer.Ok);
        Assert.Contains("mais nova", newer.Error);

        JobLoadResult kind = CamJobJson.FromJson(json.Replace("\"Pocket\"", "\"Qualquer\""));
        Assert.False(kind.Ok);

        JobLoadResult index = CamJobJson.FromJson(json.Replace("\"contour\": 1", "\"contour\": 9"));
        Assert.False(index.Ok);
        Assert.Contains("Operação", index.Error);
    }
}
