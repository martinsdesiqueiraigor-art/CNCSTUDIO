using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;

namespace CncStudio.Core.Tests;

/// <summary>
/// Os exemplos de samples/exemplos (menu Exemplos da versão web) ensinam cada ciclo:
/// precisam rodar sem erro nem aviso e na máquina certa (pasta "comando-máquina").
/// </summary>
public class ExampleLibraryTests
{
    private static string Root => Path.Combine(AppContext.BaseDirectory, "samples", "exemplos");

    [Theory]
    [InlineData("fanuc-torno", Dialect.Fanuc, MachineKind.Lathe)]
    [InlineData("fanuc-fresa", Dialect.Fanuc, MachineKind.Mill)]
    [InlineData("siemens-torno", Dialect.Siemens, MachineKind.Lathe)]
    [InlineData("siemens-fresa", Dialect.Siemens, MachineKind.Mill)]
    public void ExemplosRodamLimposNaMaquinaCerta(string folder, Dialect dialect, MachineKind machine)
    {
        string[] files = Directory.GetFiles(Path.Combine(Root, folder));
        Assert.True(files.Length >= 9, $"{folder}: só {files.Length} exemplos");

        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            Assert.Equal(dialect, DialectDetector.FromFileName(file));

            ProgramAnalysis analysis = ProgramAnalyzer.Analyze(File.ReadAllText(file), dialect);
            Assert.True(analysis.ToolPath.Machine == machine, $"{name}: detectado como {analysis.ToolPath.Machine}");
            Assert.True(analysis.Diagnostics.Count == 0,
                $"{name}: " + string.Join(" | ", analysis.Diagnostics.Select(d => $"L{d.LineNumber} {d.Message}")));
            Assert.True(analysis.ToolPath.Moves.Count > 5, $"{name}: poucos movimentos");
        }
    }
}
