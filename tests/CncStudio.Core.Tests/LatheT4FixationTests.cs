using CncStudio.Core.Cam;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>TORNO-T4: castanha dura, castanha mole, contraponto e luneta (informativos).</summary>
public class LatheT4FixationTests
{
    [Fact]
    public void Tipos_novos_tem_nome_na_tela_e_no_arquivo()
    {
        Assert.Equal(Enum.GetValues<LatheFixationKind>().Length, LatheFixation.KindNames.Count);
        foreach (LatheFixationKind k in Enum.GetValues<LatheFixationKind>())
        {
            Assert.Equal(k, LatheFixation.FromFileName(LatheFixation.FileName(k)));
        }

        // nomes antigos continuam os mesmos
        Assert.Equal("castanhas", LatheFixation.FileName(LatheFixationKind.Jaws));
        Assert.Equal("placa", LatheFixation.FileName(LatheFixationKind.Faceplate));
        Assert.Equal("ponto", LatheFixation.FileName(LatheFixationKind.Center));
        Assert.Equal("Fixação: castanha mole · Ø30", LatheFixation.Title(new LatheFixation(LatheFixationKind.SoftJaws, 30)));
    }

    [Fact]
    public void Contraponto_e_luneta_aparecem_no_titulo_e_vao_e_voltam_do_arquivo()
    {
        var f = new LatheFixation(LatheFixationKind.HardJaws, 40) { Tailstock = true, SteadyRest = true };
        Assert.Equal("Fixação: castanha dura · Ø40 · contraponto · luneta", LatheFixation.Title(f));
        var job = new LatheJob { Fixation = f };
        string json = LatheJobJson.ToJson(job);
        Assert.Contains("\"castanha-dura\"", json);
        Assert.Contains("\"tailstock\": true", json);
        Assert.Contains("\"steadyRest\": true", json);
        LatheLoadResult back = LatheJobJson.FromJson(json);
        Assert.True(back.Ok, back.Error);
        Assert.Equal(f, back.Job!.Fixation);
        Assert.Equal(json, LatheJobJson.ToJson(back.Job));
    }

    [Fact]
    public void Fixacao_sem_apoio_nao_ganha_chave_nova()
    {
        var job = new LatheJob { Fixation = new LatheFixation(LatheFixationKind.Jaws, 40, 20) };
        string json = LatheJobJson.ToJson(job);
        Assert.DoesNotContain("tailstock", json);
        Assert.DoesNotContain("steadyRest", json);
        LatheLoadResult back = LatheJobJson.FromJson(json);
        Assert.False(back.Job!.Fixation!.Tailstock);
        Assert.False(back.Job.Fixation.SteadyRest);
    }
}
