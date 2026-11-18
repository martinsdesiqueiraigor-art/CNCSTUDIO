using CncStudio.Core.Cam;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Parsing;

namespace CncStudio.Core.Tests;

/// <summary>
/// Exemplos novos do torno em <c>exemplos/torno</c> (TORNO-T2 em diante): cada trabalho abre, gera exatamente o programa guardado ao
/// lado e o simulador do Code lê esse programa. Os arquivos nunca são regravados pelo teste. Conteúdo técnico em rascunho.
/// </summary>
public class LatheExamplesTests
{
    private static string Dir => Path.Combine(AppContext.BaseDirectory, "exemplos-torno");

    private static IEnumerable<string> Names => Directory.GetFiles(Dir, "*.casillas-job").Select(f => Path.GetFileNameWithoutExtension(f)!).Order();

    public static TheoryData<string> Jobs
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (string name in Names)
            {
                data.Add(name);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Jobs))]
    public void Exemplo_abre_gera_o_programa_guardado_e_simula(string name)
    {
        string json = File.ReadAllText(Path.Combine(Dir, name + ".casillas-job")).Replace("\r\n", "\n");
        LatheLoadResult loaded = LatheJobJson.FromJson(json);
        Assert.True(loaded.Ok, loaded.Error);
        LatheJob job = loaded.Job!;
        Assert.Equal(json, LatheJobJson.ToJson(job).Replace("\r\n", "\n"));

        for (int setup = 0; setup < SetupCount(job); setup++)
        {
            LatheBuild build = Build(job, setup);
            Assert.True(build.Ok, string.Join("; ", build.Errors));
            string suffix = SetupCount(job) > 1 ? $"-fixacao{setup + 1}" : "";
            string file = Path.Combine(Dir, name + suffix + (job.Dialect == CamDialect.Siemens ? ".mpf" : ".nc"));
            Assert.Equal(File.ReadAllText(file).Replace("\r\n", "\n"), build.Program.Replace("\r\n", "\n"));
            Assert.Contains("SIMULE ANTES DE RODAR NA MAQUINA", build.Program);

            var path = MachineExecutor.Execute(GCodeParser.Parse(build.Program, job.Dialect == CamDialect.Siemens ? Dialect.Siemens : Dialect.Fanuc), new MachineSettings { Machine = MachineKind.Lathe });
            Assert.Contains(path.Moves, m => !m.IsRapid);
        }
    }

    [Fact]
    public void Ha_exemplo_de_canal_de_face_nos_dois_controles()
    {
        Assert.Contains("canal-de-face-fanuc", Names);
        Assert.Contains("canal-de-face-siemens", Names);
    }

    private static int SetupCount(LatheJob job) => 1;

    private static LatheBuild Build(LatheJob job, int setup) => LatheJobBuilder.Build(job);
}
