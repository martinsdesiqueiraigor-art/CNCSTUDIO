using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

/// <summary>
/// Rede de segurança do UX-04f: trabalhos salvos com o código do dev de 08/11/2026 (13bd900) e o programa que cada um gerava,
/// guardados como texto em <c>Cam/trabalhos-referencia</c>. Abrir, gerar e comparar linha a linha: o G-code não pode mudar.
/// Salvar de novo devolve o mesmo arquivo (versão 1, nenhum campo novo). Os arquivos nunca são regravados pelo teste.
/// </summary>
public class CamJobGoldenTests
{
    private static string Dir => Path.Combine(AppContext.BaseDirectory, "trabalhos-referencia");

    private static string Read(string file) => File.ReadAllText(Path.Combine(Dir, file)).Replace("\r\n", "\n");

    public static TheoryData<string> MillJobs => new() { "fresa-sem-bruto", "fresa-bloco-siemens", "fresa-fixacao", "fresa-3mais2", "fresa-texto-cilindro" };

    [Theory]
    [MemberData(nameof(MillJobs))]
    public void Fresa_antiga_abre_e_gera_o_mesmo_programa(string name)
    {
        string json = Read(name + ".casillas-job");
        MachineProfile? machine = null;
        if (File.Exists(Path.Combine(Dir, name + ".maquina.json")))
        {
            (machine, string? error) = MachineProfileJson.Parse(Read(name + ".maquina.json"));
            Assert.Null(error);
        }

        JobLoadResult loaded = CamJobJson.FromJson(json);
        Assert.True(loaded.Ok, loaded.Error);
        JobBuild build = CamJobBuilder.Build(loaded.Job!, machine);
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        AssertSameLines(Read(name + ".nc"), build.Program);

        // salvar e reabrir: o mesmo arquivo e o mesmo programa
        string again = CamJobJson.ToJson(loaded.Job!).Replace("\r\n", "\n");
        Assert.Equal(json, again);
        AssertSameLines(build.Program, CamJobBuilder.Build(CamJobJson.FromJson(again).Job!, machine).Program);
    }

    [Fact]
    public void Torno_antigo_abre_e_gera_o_mesmo_programa()
    {
        string json = Read("torno-eixo.casillas-job");
        LatheLoadResult loaded = LatheJobJson.FromJson(json);
        Assert.True(loaded.Ok, loaded.Error);
        LatheBuild build = LatheJobBuilder.Build(loaded.Job!);
        Assert.True(build.Ok, string.Join("; ", build.Errors));
        AssertSameLines(Read("torno-eixo.nc"), build.Program);
        Assert.Equal(json, LatheJobJson.ToJson(loaded.Job!).Replace("\r\n", "\n"));
    }

    /// <summary>Compara linha a linha e aponta a primeira diferente (mais fácil de ler que o texto inteiro).</summary>
    private static void AssertSameLines(string expected, string actual)
    {
        string[] e = expected.Replace("\r\n", "\n").Split('\n');
        string[] a = actual.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < Math.Min(e.Length, a.Length); i++)
        {
            Assert.True(e[i] == a[i], $"Linha {i + 1} diferente.\nEsperado: {e[i]}\nGerado:   {a[i]}");
        }

        Assert.Equal(e.Length, a.Length);
    }
}
