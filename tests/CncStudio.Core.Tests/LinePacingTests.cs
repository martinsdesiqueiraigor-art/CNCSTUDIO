using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Parsing;
using CncStudio.Core.Simulation;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

/// <summary>Execução linha a linha suave: a ferramenta percorre a linha em vez de saltar ao fim dela.</summary>
public class LinePacingTests
{
    private static (ToolPath Path, MaterialSimulator Simulation) Create(string program)
    {
        ToolPath path = MachineExecutor.Execute(GCodeParser.Parse(program, Dialect.Fanuc));
        var map = HeightMap.CreateForToolPath(path, toolDiameter: 6, margin: 5, maxCellsPerSide: 100);
        return (path, new MaterialSimulator(map, path.Moves, _ => ToolProfile.Flat(6)));
    }

    private const string Program = "T1 M06\nG00 X0 Y0 Z5\nG01 Z-1 F100\nG01 X40 F200\nX80\nG00 Z5";

    [Fact]
    public void LinhaInteiraComOrcamentoDeUmaLinha()
    {
        var (path, sim) = Create(Program);
        sim.AdvanceToward(2, double.PositiveInfinity); // começo da linha "G01 Z-1"
        int line = path.Moves[2].LineNumber;

        LinePacing.Advance(sim, path.Moves, 1);

        Assert.True(sim.CurrentMoveIndex > 2);
        Assert.NotEqual(line, sim.CurrentLineNumber);
    }

    [Fact]
    public void MeiaLinhaNaoTerminaALinha()
    {
        var (path, sim) = Create(Program);
        sim.AdvanceToward(3, double.PositiveInfinity); // começo de um corte em X
        int line = path.Moves[3].LineNumber;

        LinePacing.Advance(sim, path.Moves, 0.5);

        Assert.Equal(line, sim.CurrentLineNumber);
        Assert.True(sim.Travelled > 0);
        double expected = path.Moves[3].PointAt(0.5).X; // meio do movimento da linha
        Assert.InRange(sim.ToolPosition.X, expected * 0.9, expected * 1.1);
    }

    [Fact]
    public void VariasFatiasChegamAoMesmoFimQueExecutarDeUmaVez()
    {
        var (path, sim) = Create(Program);

        for (int i = 0; i < 400 && !sim.IsFinished; i++)
        {
            LinePacing.Advance(sim, path.Moves, 0.1);
        }

        Assert.True(sim.IsFinished);
        var (_, whole) = Create(Program);
        whole.RunToEnd();
        Assert.Equal(whole.Travelled, sim.Travelled, 6);
    }

    [Fact]
    public void OrcamentoGrandeExecutaTudoEParaNoFim()
    {
        var (path, sim) = Create(Program);

        LinePacing.Advance(sim, path.Moves, 1000);

        Assert.True(sim.IsFinished);
    }
}
