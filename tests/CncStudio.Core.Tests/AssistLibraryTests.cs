using CncStudio.Core.Assist;

namespace CncStudio.Core.Tests;

public class AssistLibraryTests
{
    [Fact]
    public void Itens_iniciais_sao_validos_e_todos_rascunho()
    {
        AssistLibrary lib = AssistLibrary.Defaults();
        Assert.Empty(lib.Validate());
        Assert.NotEmpty(lib.Materials);
        Assert.All(lib.Materials, m => Assert.True(m.Draft));
        Assert.All(lib.Vises, v => Assert.True(v.Draft));
        Assert.All(lib.Jaws, j => Assert.True(j.Draft));
    }

    [Fact]
    public void Material_sugere_rotacao_e_avanco_da_fresa()
    {
        var m = new MaterialEntry("Teste", MaterialGroup.Steel, 100, 0.05, 0.5);
        (double rpm, double feed) = m.SuggestMill(10, 4)!.Value;
        Assert.Equal(3183, rpm);
        Assert.Equal(Math.Round(3183 * 0.05 * 4), feed);
        Assert.Equal(5, m.SuggestDepth(10));
        Assert.Null(m.SuggestMill(0, 4));
        Assert.Null(m.SuggestMill(10, 0));
    }

    [Fact]
    public void Morsa_confere_abertura_mandibula_e_altura()
    {
        var v = new ViseEntry("M", 100, 30, 120, 150, 50);
        Assert.True(v.Check(80, 20).Fits);
        Assert.Contains("abertura", v.Check(110, 20).Reason);
        Assert.Contains("baixa", v.Check(80, 2).Reason);
        Assert.False(v.Check(0, 20).Fits);
        Assert.Contains("Mandíbula", new ViseEntry("M", 100, 30, 70, 150, 50).Check(80, 20).Reason);
    }

    [Fact]
    public void Busca_de_morsa_ordena_da_mais_justa()
    {
        AssistLibrary lib = AssistLibrary.Defaults();
        IReadOnlyList<ViseEntry> found = lib.FindVises(90, 20);
        Assert.Equal(3, found.Count);
        Assert.Equal(100, found[0].MaxOpening);
        Assert.Single(lib.FindVises(140, 20));
        Assert.Empty(lib.FindVises(200, 20));
    }

    [Fact]
    public void Castanha_pega_por_fora_ou_por_dentro_conforme_a_faixa()
    {
        AssistLibrary lib = AssistLibrary.Defaults();
        Assert.Equal(2, lib.FindJaws(50, external: true).Count);
        Assert.Empty(lib.FindJaws(300, external: true));
        ChuckJawEntry soft = Assert.Single(lib.FindJaws(100, external: false));
        Assert.Contains("moles", soft.Name);
        Assert.False(soft.Grips(100, external: true));
    }

    [Fact]
    public void Salvar_e_abrir_devolve_a_mesma_biblioteca()
    {
        AssistLibrary lib = AssistLibrary.Defaults();
        lib.Materials[0] = lib.Materials[0] with { Draft = false };
        string json = AssistLibraryJson.ToJson(lib);
        AssistLoadResult back = AssistLibraryJson.FromJson(json);
        Assert.True(back.Ok, back.Error);
        Assert.Equal(json, AssistLibraryJson.ToJson(back.Library!));
        Assert.False(back.Library!.Materials[0].Draft);
    }

    [Theory]
    [InlineData("isto não é json")]
    [InlineData("{}")]
    [InlineData("{\"format\":\"outro\",\"version\":1}")]
    public void Arquivo_invalido_nao_abre(string text)
    {
        AssistLoadResult r = AssistLibraryJson.FromJson(text);
        Assert.False(r.Ok);
        Assert.False(string.IsNullOrEmpty(r.Error));
    }

    [Fact]
    public void Versao_nova_grupo_estranho_e_nome_repetido_sao_recusados()
    {
        string json = AssistLibraryJson.ToJson(AssistLibrary.Defaults());
        Assert.Contains("mais nova", AssistLibraryJson.FromJson(json.Replace("\"version\": 1", "\"version\": 2")).Error);
        Assert.False(AssistLibraryJson.FromJson(json.Replace("\"Steel\"", "\"Qualquer\"")).Ok);
        AssistLibrary dup = AssistLibrary.Defaults();
        dup.Materials.Add(dup.Materials[0]);
        Assert.Contains("repetido", Assert.Single(dup.Validate()));
        Assert.False(AssistLibraryJson.FromJson(AssistLibraryJson.ToJson(dup)).Ok);

        var bad = new AssistLibrary();
        bad.Jaws.Add(new ChuckJawEntry("X", 160, 50, 20, 0, 0, 20));
        Assert.NotEmpty(bad.Validate());
    }
}
