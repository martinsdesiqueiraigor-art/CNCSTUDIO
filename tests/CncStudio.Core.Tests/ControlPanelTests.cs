using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Panel;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

public class ControlPanelTests
{
    private static ToolPath Build(Dialect dialect, params string[] lines) =>
        MachineExecutor.Execute(GCodeParser.Parse(string.Join("\n", lines), dialect));

    private static string AxisText(ControlPanelState s) =>
        string.Join(" ", s.Axes.Select(a => $"{a.Name}{a.Position:0.###}/{a.DistanceToGo:0.###}"));

    [Fact]
    public void MostraPosicaoAbsolutaEDistanciaAPercorrerSemOZeroDePeca()
    {
        MachineSettings settings = new() { WorkOffsets = new Dictionary<int, Core.Geometry.Point3> { [54] = new(100, 50, 0) } };
        ToolPath path = MachineExecutor.Execute(GCodeParser.Parse("G54 G90\nG01 X10 Y20 Z-5 F200\nG01 X30 Y20", Dialect.Fanuc), settings);

        ControlPanelState first = ControlPanel.At(path, 1, Dialect.Fanuc)!;
        // Absoluta do programa (10,20,-5), não de máquina (110,70,-5); falta 20 em X até o próximo bloco
        Assert.Equal("X10/20 Y20/0 Z-5/0", AxisText(first));
        Assert.Equal("G54", first.WorkOffset);
        Assert.Equal(200, first.Feed);

        ControlPanelState last = ControlPanel.At(path, 2, Dialect.Fanuc)!;
        Assert.Equal("X30/0 Y20/0 Z-5/0", AxisText(last));
    }

    [Fact]
    public void ListaOsGAtivosPorGrupoETrocaOZeroDePeca()
    {
        ToolPath path = Build(Dialect.Fanuc,
            "G21 G17 G90 G54", "T1 M06", "G01 X10 F100", "G55 G91 G41", "G02 X5 Y5 R5");

        ControlPanelState a = ControlPanel.At(path, 1, Dialect.Fanuc)!;
        Assert.Equal(["G01", "G17", "G90", "G21", "G40", "G94", "G80"], a.GCodes);
        Assert.Equal("G54", a.WorkOffset);
        Assert.Equal(1, a.Tool);

        ControlPanelState b = ControlPanel.At(path, 2, Dialect.Fanuc)!;
        Assert.Equal("G02", b.GCodes[0]);
        Assert.Equal("G91", b.GCodes[2]);
        Assert.Equal("G41", b.GCodes[4]);
        Assert.Equal("G55", b.WorkOffset);
    }

    [Fact]
    public void CicloDeFuracaoAparecePeloNumeroEG80Cancela()
    {
        ToolPath path = Build(Dialect.Fanuc,
            "G90 G54", "G00 X0 Y0 Z5", "G83 X10 Y10 Z-20 R2 Q5 F100", "G80", "G00 Z50");

        int drilling = path.Moves.ToList().FindIndex(m => !m.IsRapid) + 1;
        Assert.Contains("G83", ControlPanel.At(path, drilling, Dialect.Fanuc)!.GCodes);
        Assert.Contains("G80", ControlPanel.At(path, path.Moves.Count, Dialect.Fanuc)!.GCodes);
    }

    [Fact]
    public void TornoMostraXEmDiametroSemYEGModosDeTorno()
    {
        ToolPath path = Build(Dialect.Fanuc,
            "G21 G18 G40 G99", "G96 S180", "T0101", "G00 X52. Z2.", "G01 X40. Z-10. F0.2");

        Assert.Equal(MachineKind.Lathe, path.Machine);
        ControlPanelState s = ControlPanel.At(path, 2, Dialect.Fanuc)!;
        Assert.Equal(["X", "Z"], s.Axes.Select(a => a.Name));
        Assert.Equal(40, s.Axes[0].Position, 3);
        Assert.Equal(-10, s.Axes[1].Position, 3);
        Assert.Contains("G99", s.GCodes);
        Assert.Contains("G96", s.GCodes);
        Assert.True(s.SurfaceSpeed);
        Assert.Equal(180, s.Spindle);
    }

    [Fact]
    public void ProgramaSemMovimentoNaoTemPainel()
    {
        Assert.Null(ControlPanel.At(Build(Dialect.Fanuc, "G21", "M30"), 0, Dialect.Fanuc));
    }

    [Fact]
    public void ResultadoViraJsonComOsCamposDaTela()
    {
        ToolPath path = Build(Dialect.Fanuc, "G54 G90", "G01 X10 Y5 Z-1 F150", "G01 X20");
        string json = ControlPanel.At(path, 1, Dialect.Fanuc)!.ToJson();
        Assert.Contains("\"n\":\"X\",\"p\":10,\"d\":10", json);
        Assert.Contains("\"wcs\":\"G54\"", json);
        Assert.Contains("\"feed\":150", json);
    }
}
