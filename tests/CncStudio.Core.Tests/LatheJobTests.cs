using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

public class LatheJobTests
{
    private static LatheProfile Profile()
    {
        Point2[] pts = [new(0, 8), new(-2, 10), new(-25, 10), new(-25, 14), new(-35, 14), new(-45, 14)];
        return LatheProfile.From(Enumerable.Range(1, pts.Length - 1).Select(i => Segment2.Line(pts[i - 1], pts[i])).ToList(), out _)!;
    }

    private static LatheJob Sample()
    {
        var job = new LatheJob { Name = "EIXO TESTE", Profile = Profile() };
        foreach ((string _, LatheOpKind kind, bool isInternal) in LatheJobInfo.NewKinds.Where(k => !k.Internal))
        {
            job.Operations.Add(LatheJobInfo.NewOperation(kind, isInternal, new LatheTool((int)kind + 1, "Ferr", 200, 3000, 0)));
        }

        return job;
    }

    [Theory]
    [InlineData(CamDialect.Fanuc)]
    [InlineData(CamDialect.Siemens)]
    public void Job_gera_o_mesmo_programa_que_o_fluxo_direto(CamDialect dialect)
    {
        LatheJob job = Sample();
        job.Dialect = dialect;
        LatheBuild build = LatheJobBuilder.Build(job);
        Assert.True(build.Ok, string.Join("; ", build.Errors));

        LatheProfile p = job.Profile!;
        LatheTool Tool(int n) => new(n, "Ferr", 200, 3000, 0);
        var ops = new List<LatheOperation>
        {
            LatheOperations.Turning(p, Tool(1), 50, 40, new TurningParams(40, false, true, true, 2, 1.5, 0.5, 0.2, 0.1, 0.25, 0.1)).Operation!,
            LatheOperations.Facing(Tool(2), 50, 40, 40, 2, 0, 1, 0.15, 0, 2).Operation!,
            LatheOperations.Drilling(Tool(3), 50, 40, 0, -25, 8, 0.12, 2).Operation!,
            LatheOperations.Threading(Tool(4), 50, 40, new ThreadingParams(false, 20, 1.5, 0, -18, 600)).Operation!,
            LatheOperations.Grooving(Tool(5), 50, 40, new GroovingParams(false, 30, 22, -20, 8, 3, 2, 0.08, 0.5, 0.1, true, 2)).Operation!,
        };
        string direct = LathePost.Generate(ops, new LathePostOptions(dialect, "EIXO TESTE", 1002, 54, true));
        Assert.Equal(direct, build.Program);
    }

    [Fact]
    public void Salvar_e_abrir_devolve_o_mesmo_trabalho_e_o_mesmo_programa()
    {
        LatheJob job = Sample();
        job.Operations[0] = job.Operations[0] with { Label = "Desbastar eixo", Clearance = 3 };
        job.Operations.Add(LatheJobInfo.NewOperation(LatheOpKind.Turning, true));
        string json = LatheJobJson.ToJson(job);
        LatheLoadResult loaded = LatheJobJson.FromJson(json);
        Assert.True(loaded.Ok, loaded.Error);
        Assert.Equal(json, LatheJobJson.ToJson(loaded.Job!));
        Assert.Equal(LatheJobBuilder.Build(job).Program, LatheJobBuilder.Build(loaded.Job!).Program);
        Assert.Contains("Desbastar eixo", LatheJobBuilder.Build(loaded.Job!).Program);
    }

    [Fact]
    public void Desligar_mover_e_duplicar()
    {
        LatheJob job = Sample();
        string all = LatheJobBuilder.Build(job).Program;
        job.Operations[0] = job.Operations[0] with { Enabled = false };
        LatheBuild without = LatheJobBuilder.Build(job);
        Assert.True(without.Ok);
        Assert.Equal(4, without.Operations.Count);
        Assert.NotEqual(all, without.Program);
        Assert.True(job.MoveOperation(4, 0));
        Assert.Equal(LatheOpKind.Grooving, job.Operations[0].Kind);
        Assert.Equal(1, job.DuplicateOperation(0));
        Assert.Equal(6, job.Operations.Count);
        Assert.False(job.MoveOperation(0, 9));
        Assert.Equal(-1, job.DuplicateOperation(9));
    }

    [Fact]
    public void Torneamento_sem_perfil_e_ferramenta_ruim_apontam_a_operacao()
    {
        var job = new LatheJob();
        job.Operations.Add(LatheJobInfo.NewOperation(LatheOpKind.Turning, false));
        LatheBuild noProfile = LatheJobBuilder.Build(job);
        Assert.False(noProfile.Ok);
        Assert.Equal("", noProfile.Program);
        Assert.Contains("perfil", noProfile.Errors[0]);

        job = new LatheJob();
        job.Operations.Add(LatheJobInfo.NewOperation(LatheOpKind.Facing, false, new LatheTool(0)));
        Assert.Contains("Operação 1", LatheJobBuilder.Build(job).Errors[0]);

        Assert.Contains("ligada", LatheJobBuilder.Build(new LatheJob()).Errors[0]);
    }

    [Fact]
    public void Toda_operacao_nova_gera_programa()
    {
        var job = new LatheJob { Profile = Profile() };
        foreach ((string _, LatheOpKind kind, bool isInternal) in LatheJobInfo.NewKinds)
        {
            job.Operations.Add(LatheJobInfo.NewOperation(kind, isInternal));
        }

        LatheBuild build = LatheJobBuilder.Build(job);
        // O torneamento interno padrão (pré-furo Ø14) não cabe no perfil de exemplo, que é externo: só ele pode falhar
        Assert.All(build.Errors, e => Assert.StartsWith("Operação 2:", e));
        job.Operations.RemoveAt(1);
        LatheBuild rest = LatheJobBuilder.Build(job);
        Assert.True(rest.Ok, string.Join("; ", rest.Errors));
    }

    [Fact]
    public void Titulos()
    {
        LatheOperationSpec s = LatheJobInfo.NewOperation(LatheOpKind.Threading, true);
        Assert.Equal("3 · Rosca interna — T1", LatheJobInfo.OpTitle(2, s));
        Assert.Equal("1 · Meu corte — T1", LatheJobInfo.OpTitle(0, s with { Label = "Meu corte" }));
        Assert.Equal("Perfil: não definido", LatheJobInfo.ProfileTitle(null));
        Assert.Equal("Perfil: 5 trecho(s), Z 0 a -45, Ø 16 a 28", LatheJobInfo.ProfileTitle(Profile()));
    }

    [Theory]
    [InlineData("isto não é json")]
    [InlineData("[]")]
    [InlineData("{}")]
    public void Arquivo_invalido_nao_abre(string text)
    {
        LatheLoadResult r = LatheJobJson.FromJson(text);
        Assert.False(r.Ok);
        Assert.False(string.IsNullOrEmpty(r.Error));
    }

    [Fact]
    public void Fresa_e_torno_se_reconhecem_e_versao_nova_e_recusada()
    {
        string lathe = LatheJobJson.ToJson(Sample());
        JobLoadResult asMill = CamJobJson.FromJson(lathe);
        Assert.False(asMill.Ok);
        Assert.Contains("torno", asMill.Error);

        string mill = CamJobJson.ToJson(new CamJob());
        LatheLoadResult asLathe = LatheJobJson.FromJson(mill);
        Assert.False(asLathe.Ok);
        Assert.Contains("fresa", asLathe.Error);

        LatheLoadResult newer = LatheJobJson.FromJson(lathe.Replace("\"version\": 1", "\"version\": 2"));
        Assert.Contains("mais nova", newer.Error);
        Assert.False(LatheJobJson.FromJson(lathe.Replace("\"Threading\"", "\"Qualquer\"")).Ok);
    }
}
