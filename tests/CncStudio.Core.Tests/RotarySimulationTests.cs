using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Panel;
using CncStudio.Core.Parsing;
using CncStudio.Core.Simulation;

namespace CncStudio.Core.Tests;

public class RotarySimulationTests
{
    private const string Program = "G21 G90 G54\nG00 X-10 Y0 Z5\nG01 Z-2 F200\nG01 X30\nG00 Z20\nG00 A90.\nG00 X-10 Z5\nG01 Z-2 F200\nG01 X30";

    [Fact]
    public void PainelMostraOAnguloEOAvisoNaoMataSimulacao()
    {
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(Program, Dialect.Fanuc);
        Assert.DoesNotContain(analysis.ToolPath.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        int n = analysis.ToolPath.Moves.Count;
        ControlPanelState last = ControlPanel.At(analysis.ToolPath, n, Dialect.Fanuc)!;
        Assert.Contains(last.Axes, a => a.Name == "A" && Math.Abs(a.Position - 90) < 1e-9);
        ControlPanelState first = ControlPanel.At(analysis.ToolPath, 1, Dialect.Fanuc)!;
        Assert.Contains(first.Axes, a => a.Name == "A" && a.Position == 0);
    }

    [Fact]
    public void CorteComPecaGiradaNaoRetiraMaterial()
    {
        // Mesmo corte, uma vez sem giro e uma vez com A90: só o primeiro tira material
        const string Cut = "G21 G90 G54\nG00 X-10 Y0 Z5\nG01 Z-2 F200\nG01 X30\nG00 Z20";
        string flat = Cut;
        string turned = "G21 G90 G54\nG00 A90.\n" + Cut.Substring(Cut.IndexOf('\n') + 1);
        double Removed(string text)
        {
            ProgramAnalysis a = ProgramAnalyzer.Analyze(text, Dialect.Fanuc);
            MaterialSimulationParts parts = SimulationFactory.Create(a, null, text);
            parts.Simulation.RunToEnd();
            return parts.Simulation.Version;
        }

        Assert.True(Removed(turned) * 10 < Removed(flat));
    }
}
