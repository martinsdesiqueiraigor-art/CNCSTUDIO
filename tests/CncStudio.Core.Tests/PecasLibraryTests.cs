using System.Text.RegularExpressions;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Setup;
using CncStudio.Core.Simulation;

namespace CncStudio.Core.Tests;

/// <summary>
/// As peças completas de samples/pecas (T01) são demonstração e base das aulas: precisam rodar limpas
/// na máquina certa, trazer o bruto no comentário e não ter rápido dentro do material.
/// </summary>
public class PecasLibraryTests
{
    private static string Root => Path.Combine(AppContext.BaseDirectory, "samples", "pecas");

    [Theory]
    [InlineData("fanuc-fresa", Dialect.Fanuc, MachineKind.Mill, 4)]
    [InlineData("fanuc-torno", Dialect.Fanuc, MachineKind.Lathe, 4)]
    [InlineData("siemens-fresa", Dialect.Siemens, MachineKind.Mill, 2)]
    [InlineData("siemens-torno", Dialect.Siemens, MachineKind.Lathe, 2)]
    public void PecasRodamLimpasNaMaquinaCerta(string folder, Dialect dialect, MachineKind machine, int expected)
    {
        string[] files = Directory.GetFiles(Path.Combine(Root, folder));
        Assert.Equal(expected, files.Length);

        foreach (string file in files)
        {
            string name = Path.GetFileName(file);
            string text = File.ReadAllText(file);
            Assert.Equal(dialect, DialectDetector.FromFileName(file));

            ProgramAnalysis analysis = ProgramAnalyzer.Analyze(text, dialect);
            Assert.True(analysis.ToolPath.Machine == machine, $"{name}: detectado como {analysis.ToolPath.Machine}");
            Assert.True(analysis.Diagnostics.Count == 0,
                $"{name}: " + string.Join(" | ", analysis.Diagnostics.Select(d => $"L{d.LineNumber} {d.Message}")));
            Assert.True(analysis.ToolPath.Moves.Count > 20, $"{name}: poucos movimentos");

            // Bruto escrito no comentário (BRUTO 100 X 60 X 20 / BRUTO D50 X 70) e lido pelo programa
            Stock? stock = SetupBinding.FromProgramComments(text, machine, analysis.ToolPath.CuttingBounds);
            Assert.True(stock is { IsValid: true }, $"{name}: sem BRUTO no comentário");

            // Nenhuma ferramenta abaixo do fundo do bruto
            Assert.True(analysis.ToolPath.Bounds.Min.Z >= stock!.MinZ - 1e-6, $"{name}: ferramenta abaixo do fundo do bruto");

            // Toda ferramenta traz o diâmetro/identificação no comentário da troca
            foreach (int tool in analysis.ToolPath.Tools)
            {
                Assert.True(analysis.ToolDiameterOf(tool) > 0, $"{name}: T{tool} sem diâmetro");
            }

            // Fim de programa
            Assert.Contains("M30", text);
        }
    }

    [Theory]
    [InlineData("fanuc-fresa")]
    [InlineData("fanuc-torno")]
    [InlineData("siemens-fresa")]
    [InlineData("siemens-torno")]
    public void SemRapidoNoMaterial(string folder)
    {
        foreach (string file in Directory.GetFiles(Path.Combine(Root, folder)))
        {
            string name = Path.GetFileName(file);
            string text = File.ReadAllText(file);
            ProgramAnalysis analysis = ProgramAnalyzer.Analyze(text, DialectDetector.FromFileName(file));

            // Os ciclos de desbaste (G71, CYCLE95...) geram os próprios rápidos de afastamento,
            // que o simulador (ferramenta pontual) às vezes marca; nesses vale só o resto do arquivo.
            if (Regex.IsMatch(text, @"\bG7[123]\b|CYCLE9[57]"))
            {
                continue;
            }

            MaterialSimulationParts parts = SimulationFactory.Create(analysis, null, text);
            parts.Simulation.RunToEnd();
            Assert.True(parts.Simulation.RapidCollisions.Count == 0,
                $"{name}: rápido no material nas linhas {string.Join(",", parts.Simulation.RapidCollisions.Select(i => analysis.ToolPath.Moves[i].LineNumber).Distinct())}");
        }
    }
}
