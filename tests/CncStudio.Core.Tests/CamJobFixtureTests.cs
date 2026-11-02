using System.Text.Json.Nodes;
using CncStudio.Core.Cam;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>Fixação no Trabalho fresa (UX-01 rodada 3, PR 4): ida e volta no arquivo, arquivo antigo, campo inválido, pendência.</summary>
public class CamJobFixtureTests
{
    private static CamJob Job()
    {
        var job = new CamJob { Name = "PLACA", Stock = new JobStock(0, 0, 100, 60, 10) };
        job.Contours.Add(Contour.FromPoints([new(0, 0), new(100, 0), new(100, 60), new(0, 60)]));
        job.Operations.Add(CamJobInfo.NewOperation(JobOpKind.Contour, ContourSide.Outside, 0));
        return job;
    }

    [Fact]
    public void Salvar_e_abrir_preserva_a_fixacao()
    {
        CamJob job = Job();
        job.Fixture = new JobFixture("morsa", "morsa de 150 mm, mordente liso");
        string json = CamJobJson.ToJson(job);
        JobLoadResult loaded = CamJobJson.FromJson(json);
        Assert.True(loaded.Ok);
        Assert.Empty(loaded.Warnings);
        Assert.Equal(job.Fixture, loaded.Job!.Fixture);
        Assert.Equal(json, CamJobJson.ToJson(loaded.Job));
    }

    [Fact]
    public void Arquivo_antigo_sem_fixacao_abre_como_nao_definida()
    {
        JsonObject old = JsonNode.Parse(CamJobJson.ToJson(Job()))!.AsObject();
        Assert.Null(old["fixture"]);
        JobLoadResult loaded = CamJobJson.FromJson(old.ToJsonString());
        Assert.True(loaded.Ok);
        Assert.Null(loaded.Job!.Fixture);
        Assert.Empty(loaded.Warnings);
        Assert.Equal("Fixação: não definida", CamJobInfo.FixtureTitle(loaded.Job.Fixture));
        Assert.Equal(("pending", "não definida"), CamJobInfo.FixtureState(null));
        Assert.Contains("Fixação não definida", CamJobInfo.Pending(loaded.Job, CamJobBuilder.Build(loaded.Job, null)));
    }

    [Theory]
    [InlineData("""{"kind":"magnetica","description":""}""", "magnetica")]
    [InlineData("""{"kind":"outro","description":""}""", "descreva")]
    [InlineData("\"morsa\"", "formato inválido")]
    public void Fixacao_invalida_abre_com_aviso_e_fica_nao_definida(string fixture, string expected)
    {
        JsonObject o = JsonNode.Parse(CamJobJson.ToJson(Job()))!.AsObject();
        o["fixture"] = JsonNode.Parse(fixture);
        JobLoadResult loaded = CamJobJson.FromJson(o.ToJsonString());
        Assert.True(loaded.Ok);
        Assert.Null(loaded.Job!.Fixture);
        string warning = Assert.Single(loaded.Warnings);
        Assert.Contains(expected, warning);
        Assert.Contains("não definida", warning);
    }

    [Fact]
    public void Fixacao_definida_sai_da_pendencia_mas_continua_nao_verificada()
    {
        CamJob job = Job();
        job.Fixture = new JobFixture("grampos", "4 grampos nas abas");
        Assert.Empty(CamJobInfo.Pending(job, CamJobBuilder.Build(job, null)));
        Assert.Equal("Fixação: Grampos · 4 grampos nas abas", CamJobInfo.FixtureTitle(job.Fixture));
        Assert.Contains("não verificada", CamJobInfo.FixtureState(job.Fixture).Text);
        Assert.Null(job.Fixture.Validate());
        Assert.NotNull(new JobFixture("morsa", new string('x', JobFixture.MaxDescription + 1)).Validate());
    }

    [Fact]
    public void Fixacao_nao_definida_avisa_mas_nao_trava()
    {
        CamJob job = Job();
        IReadOnlyList<string> pending = CamJobInfo.Pending(job, CamJobBuilder.Build(job, null));
        Assert.Equal([CamJobInfo.FixtureMissing], pending);
        Assert.False(CamJobInfo.Blocks(pending[0]));

        job.Stock = null;
        job.Operations.Clear();
        Assert.All(CamJobInfo.Pending(job, CamJobBuilder.Build(job, null)).Where(p => p != CamJobInfo.FixtureMissing), p => Assert.True(CamJobInfo.Blocks(p)));
        Assert.True(CamJobInfo.Blocks("rotação acima do fuso na operação 1"));
    }

    [Fact]
    public void Fixacao_nao_muda_o_programa()
    {
        CamJob job = Job();
        string before = CamJobBuilder.Build(job, null).Program;
        job.Fixture = new JobFixture("vacuo");
        Assert.Equal(before, CamJobBuilder.Build(job, null).Program);
    }
}
