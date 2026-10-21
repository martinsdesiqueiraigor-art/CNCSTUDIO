using System.Globalization;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Parsing;
using CncStudio.Core.Scene;
using CncStudio.Core.Simulation;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

/// <summary>CODE-06: marcas de troca de ferramenta, distância total e material transparente.</summary>
public class PathMarksTests
{
    private static ToolPath Fanuc(string program) =>
        MachineExecutor.Execute(GCodeParser.Parse(program, Dialect.Fanuc));

    [Fact]
    public void TrocasDeFerramentaViramMarcasEmOrdem()
    {
        ToolPath path = Fanuc("T1 M06\nG01 X10 F100\nX20\nT2 M06\nX30\nX40\nT1 M06\nX50");

        IReadOnlyList<ToolChange> changes = path.ToolChanges;

        Assert.Equal([1, 2, 1], changes.Select(c => c.Tool));
        Assert.Equal([0, 1, 2], changes.Select(c => c.PreviousTool));
        Assert.Equal([0, 2, 4], changes.Select(c => c.MoveIndex));
        Assert.Equal(path.Moves[2].LineNumber, changes[1].LineNumber);
    }

    [Fact]
    public void RepetirAMesmaFerramentaNaoGeraMarca()
    {
        ToolPath path = Fanuc("T1 M06\nG01 X10 F100\nT1 M06\nX20");

        Assert.Single(path.ToolChanges);
    }

    [Fact]
    public void SemFerramentaNaoHaMarcas()
    {
        ToolPath path = Fanuc("G01 X10 F100\nX20");

        Assert.Empty(path.ToolChanges);
        Assert.Equal(-1, PathMarks.ChangeAtOrBefore(path.ToolChanges, 1));
    }

    [Fact]
    public void ChangeAtOrBeforeAchaATrocaVigente()
    {
        ToolPath path = Fanuc("T1 M06\nG01 X10 F100\nX20\nT2 M06\nX30\nX40");
        IReadOnlyList<ToolChange> changes = path.ToolChanges;

        Assert.Equal(0, PathMarks.ChangeAtOrBefore(changes, 0));
        Assert.Equal(0, PathMarks.ChangeAtOrBefore(changes, 1));
        Assert.Equal(1, PathMarks.ChangeAtOrBefore(changes, changes[1].MoveIndex));
        Assert.Equal(1, PathMarks.ChangeAtOrBefore(changes, 99));
    }

    [Fact]
    public void ComprimentoTotalSomaTodosOsMovimentos()
    {
        ToolPath path = Fanuc("G00 X0 Y0\nG01 X30 F100\nY40\nG02 X0 Y40 I-15 J0");

        double expected = path.Moves.Sum(m => m.Length);

        Assert.Equal(expected, path.TotalLength, 9);
        Assert.True(path.TotalLength > 30 + 40); // reta + reta + meia volta
    }

    [Fact]
    public void DistanciaPercorridaTerminaNoComprimentoTotal()
    {
        ToolPath path = Fanuc("T1 M06\nG00 X0 Y0 Z5\nG01 Z-1 F100\nX30\nY20\nG02 X0 Y20 I-15 J0\nG00 Z5");
        var map = HeightMap.CreateForToolPath(path, toolDiameter: 6, margin: 5, maxCellsPerSide: 200);
        var simulation = new MaterialSimulator(map, path.Moves, _ => ToolProfile.Flat(6));

        simulation.RunToEnd();

        // Arcos são percorridos por cordas: a diferença é de centésimos de milímetro
        Assert.InRange(simulation.Travelled / path.TotalLength, 0.995, 1.0);
        Assert.Equal(path.TotalLength, PathMarks.DisplayTravelled(simulation.Travelled, path.TotalLength, simulation.IsFinished), 9);
    }

    [Fact]
    public void DistanciaMostradaNuncaPassaDoTotal()
    {
        Assert.Equal(100, PathMarks.DisplayTravelled(100.4, 100, finished: false));
        Assert.Equal(0, PathMarks.DisplayTravelled(-1, 100, finished: false));
        Assert.Equal(42, PathMarks.DisplayTravelled(42, 100, finished: false));
    }

    [Fact]
    public void FormataDistanciaEmMmOuMetros()
    {
        CultureInfo pt = new("pt-BR");

        Assert.Equal("523,4 mm", PathMarks.FormatDistance(523.4, pt));
        Assert.Equal("12,35 m", PathMarks.FormatDistance(12_345, pt));
    }

    [Fact]
    public void MaterialTransparenteBaixaAOpacidadeSoDoMaterial()
    {
        ToolPath path = Fanuc("T1 M06\nG01 X10 Y10 Z-1 F100");
        var map = HeightMap.CreateForToolPath(path, toolDiameter: 6, margin: 5, maxCellsPerSide: 100);
        SimulationScene scene = SimulationScene.ForMill(map, path, _ => ToolProfile.Flat(6));

        SceneSnapshot solid = scene.Snapshot();
        scene.TransparentMaterial = true;
        SceneSnapshot transparent = scene.Snapshot();

        Assert.Equal(1f, solid.OpacityOf(SceneRole.Material));
        Assert.Equal(SimulationScene.TransparentOpacity, transparent.OpacityOf(SceneRole.Material));
        Assert.Equal(SimulationScene.TransparentOpacity, transparent.OpacityOf(SceneRole.Section));
        Assert.Equal(1f, transparent.OpacityOf(SceneRole.Tool));
        Assert.Equal(1f, transparent.OpacityOf(SceneRole.Holder));
        Assert.Equal(1f, transparent.OpacityOf(SceneRole.Fixture));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FundoEmDegradeEscurecendoParaABase(bool claro)
    {
        uint top = SceneBackground.Top(claro);
        uint bottom = SceneBackground.Bottom(claro);

        double Brilho(uint c) => ((c >> 16) & 0xFF) * 0.3 + ((c >> 8) & 0xFF) * 0.59 + (c & 0xFF) * 0.11;

        Assert.NotEqual(top, bottom);
        Assert.True(Brilho(top) > Brilho(bottom)); // luz vem de cima nos dois temas
        Assert.True(Brilho(SceneBackground.Top(true)) > Brilho(SceneBackground.Top(false)));
    }
}
