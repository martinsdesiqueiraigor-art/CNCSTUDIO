using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Parsing;
using CncStudio.Core.Reports;

namespace CncStudio.Core.Tests;

public class ToolCommentDiameterTests
{
    [Theory]
    [InlineData("FRESA DE TOPO D10", 10.0)]
    [InlineData("BROCA Ø6,8", 6.8)]
    [InlineData("MANDRIL D30", 30.0)]
    [InlineData("MACHO M8", null)]
    [InlineData("DESBASTE", null)]
    public void LeODiametroDoComentario(string comment, double? expected)
    {
        Assert.Equal(expected, ToolCommentDiameters.FromComment(comment));
    }

    [Fact]
    public void CompensacaoUsaODiametroDoComentarioSemTabela()
    {
        const string program = "T8 M06 (FRESA D10)\nG00 X-20 Y-10 Z2\nG01 Z-5 F100\nG41 X0 Y0\nY40\nG40 X-20";
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(program, Dialect.Fanuc, MachineSettings.Default);

        Assert.DoesNotContain(analysis.Diagnostics, d => d.Message.Contains("sem diâmetro"));
        // G41 à esquerda subindo em Y: o centro da fresa fica em X = −5
        Assert.Contains(analysis.ToolPath.Moves, m => Math.Abs(m.End.X + 5) < 1e-6 && m.End.Y > 30);
    }

    [Fact]
    public void TabelaTemPrioridadeSobreOComentario()
    {
        const string program = "T8 M06 (FRESA D10)\nG00 X-20 Y-10 Z2\nG01 Z-5 F100\nG41 X0 Y0\nY40\nG40 X-20";
        var settings = new MachineSettings { ToolDiameterOf = _ => 4 };
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(program, Dialect.Fanuc, settings);

        Assert.Contains(analysis.ToolPath.Moves, m => Math.Abs(m.End.X + 2) < 1e-6 && m.End.Y > 30);
    }

    [Fact]
    public void TornoFanucLeODiametroDaBrocaPeloNumeroDaFerramenta()
    {
        // T0202 é a ferramenta 2 (corretor 02): o diâmetro do comentário tem de chegar na ferramenta 2
        const string program = "G21 G18 G40 G99\nT0101 (DESBASTE EXTERNO R0.8)\nG00 X52. Z2.\nT0202 (BROCA D16)\nG97 S800 M03\nG00 X0. Z3.\nG01 Z-20. F0.1\nG00 Z100.";
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(program, Dialect.Fanuc, MachineSettings.Default);

        Assert.Equal(MachineKind.Lathe, analysis.ToolPath.Machine);
        Assert.Equal(16, analysis.ToolDiameterOf(2));
    }
}
