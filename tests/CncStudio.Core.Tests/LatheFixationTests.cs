using CncStudio.Core.Cam;
using Xunit;

namespace CncStudio.Core.Tests;

public class LatheFixationTests
{
    [Fact]
    public void Title_e_estado()
    {
        Assert.Equal("Fixação: não informada", LatheFixation.Title(null));
        Assert.Equal(("pending", "não verificada"), LatheFixation.State(null));
        var f = new LatheFixation(LatheFixationKind.Jaws, 40, 20, 60.5);
        Assert.Equal("Fixação: castanhas · Ø40 · preso 20 mm · balanço 60,5 mm", LatheFixation.Title(f));
        Assert.Equal(("pending", "não verificada"), LatheFixation.State(f));
        Assert.Equal("Fixação: entre pontas", LatheFixation.Title(new LatheFixation(LatheFixationKind.Center)));
    }

    [Fact]
    public void Medida_zero_ou_negativa_e_invalida()
    {
        Assert.Null(new LatheFixation(LatheFixationKind.Faceplate).Validate());
        Assert.NotNull(new LatheFixation(LatheFixationKind.Jaws, GripDiameter: 0).Validate());
        Assert.NotNull(new LatheFixation(LatheFixationKind.Jaws, StickOut: -5).Validate());
        Assert.Equal("bad", LatheFixation.State(new LatheFixation(LatheFixationKind.Jaws, GripLength: 0)).Kind);
    }

    [Fact]
    public void Salva_e_abre_com_a_fixacao()
    {
        var job = new LatheJob { Fixation = new LatheFixation(LatheFixationKind.Center, 30, null, 120) };
        string json = LatheJobJson.ToJson(job);
        Assert.Contains("\"ponto\"", json);
        Assert.DoesNotContain("gripLength", json);
        LatheLoadResult back = LatheJobJson.FromJson(json);
        Assert.True(back.Ok, back.Error);
        Assert.Equal(job.Fixation, back.Job!.Fixation);
    }

    [Fact]
    public void Arquivo_antigo_sem_fixacao_abre_como_nao_informada()
    {
        var job = new LatheJob();
        string json = LatheJobJson.ToJson(job);
        Assert.DoesNotContain("fixation", json);
        Assert.Equal(1, LatheJob.CurrentVersion);
        LatheLoadResult back = LatheJobJson.FromJson(json);
        Assert.True(back.Ok, back.Error);
        Assert.Null(back.Job!.Fixation);
    }

    [Fact]
    public void Fixacao_desconhecida_ou_invalida_abre_com_aviso_como_na_fresa()
    {
        // decisão do Igor (02/11): igual à fresa; o trabalho abre e a fixação fica "não informada", com aviso legível
        string json = LatheJobJson.ToJson(new LatheJob { Fixation = new LatheFixation(LatheFixationKind.Jaws) });
        LatheLoadResult unknown = LatheJobJson.FromJson(json.Replace("\"castanhas\"", "\"morsa\""));
        Assert.True(unknown.Ok);
        Assert.Null(unknown.Job!.Fixation);
        Assert.Contains("morsa", Assert.Single(unknown.Warnings));

        string bad = LatheJobJson.ToJson(new LatheJob { Fixation = new LatheFixation(LatheFixationKind.Jaws, GripDiameter: -1) });
        LatheLoadResult invalid = LatheJobJson.FromJson(bad);
        Assert.True(invalid.Ok);
        Assert.Null(invalid.Job!.Fixation);
        Assert.Contains("não informada", Assert.Single(invalid.Warnings));

        LatheLoadResult good = LatheJobJson.FromJson(json);
        Assert.Empty(good.Warnings);
        Assert.NotNull(good.Job!.Fixation);
    }

    [Fact]
    public void Fixacao_nao_muda_o_programa()
    {
        var job = new LatheJob();
        job.Operations.Add(LatheJobInfo.NewOperation(LatheOpKind.Facing, false));
        string before = LatheJobBuilder.Build(job).Program;
        job.Fixation = new LatheFixation(LatheFixationKind.Jaws, 40, 20, 60);
        Assert.Equal(before, LatheJobBuilder.Build(job).Program);
    }
}
