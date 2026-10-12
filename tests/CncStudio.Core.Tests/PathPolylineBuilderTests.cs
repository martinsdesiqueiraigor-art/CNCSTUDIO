using CncStudio.Core.Execution;
using CncStudio.Core.Dialects;
using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

public class PathPolylineBuilderTests
{
    private static ToolPath BuildFanuc(params string[] lines) =>
        MachineExecutor.Execute(GCodeParser.Parse(string.Join("\n", lines), Dialect.Fanuc));

    [Fact]
    public void JuntaMovimentosEncadeadosDoMesmoTipo()
    {
        ToolPath path = BuildFanuc(
            "G00 X10 Y10",
            "G00 Z1",
            "G01 Z-1 F100",
            "X20",
            "Y20",
            "G00 Z5");

        List<PathPolyline> figures = PathPolylineBuilder.Build(path.Moves, 0.01);

        Assert.Equal(3, figures.Count);
        Assert.True(figures[0].IsRapid);
        Assert.Equal(3, figures[0].Points.Length); // origem → (10,10,0) → (10,10,1)
        Assert.False(figures[1].IsRapid);
        Assert.Equal(4, figures[1].Points.Length);
        Assert.Equal(new Point3(20, 20, -1), figures[1].Points[^1]);
        Assert.True(figures[2].IsRapid);
    }

    [Fact]
    public void ArcoEntraInterpoladoNaFigura()
    {
        ToolPath path = BuildFanuc(
            "G00 X10 Y0",
            "G03 I-10 F100");

        List<PathPolyline> figures = PathPolylineBuilder.Build(path.Moves, 0.01);

        PathPolyline circle = figures[1];
        Assert.True(circle.Points.Length > 20);
        Assert.Equal(circle.Points[0], circle.Points[^1]);
    }

    [Fact]
    public void TrajetoriaVaziaNaoGeraFiguras()
    {
        Assert.Empty(PathPolylineBuilder.Build([], 0.01));
    }
}

public class PathColorsAndPassesTests
{
    private static ToolPath Fanuc(string program) =>
        MachineExecutor.Execute(GCodeParser.Parse(program, Dialect.Fanuc));

    [Fact]
    public void FigurasSeparamG01G02EG03()
    {
        ToolPath path = Fanuc("""
            G00 X10 Y0
            G01 X20 F100
            G02 X30 Y0 R5
            G03 X40 Y0 R5
            G01 X50
            """);

        List<PathPolyline> figures = PathPolylineBuilder.Build(path.Moves, 0.01);

        Assert.Equal(
            [MotionMode.Rapid, MotionMode.Linear, MotionMode.ClockwiseArc, MotionMode.CounterClockwiseArc, MotionMode.Linear],
            figures.Select(f => f.Motion));
        Assert.Equal(2, figures[2].FirstMove);
    }

    [Fact]
    public void FiguraSabeOndeCadaMovimentoComeca()
    {
        ToolPath path = Fanuc("G01 X10 F100\nX20\nY10\nX0");
        PathPolyline figure = Assert.Single(PathPolylineBuilder.Build(path.Moves, 0.01));

        Assert.Equal([0, 1, 2, 3], figure.MoveStarts);
        Assert.Equal(3, figure.LastMove);
        Assert.Equal(3, figure.PointCountThrough(1)); // origem, X10, X20
        Assert.Equal(5, figure.PointCountThrough(3));
        Assert.Equal(0, figure.PointCountThrough(-1));
    }

    [Fact]
    public void PasseComecaNoRapidoDepoisDeUmCorte()
    {
        ToolPath path = Fanuc("""
            G00 X10 Y10
            G00 Z1
            G01 Z-1 F100
            G00 Z1
            X20
            G01 Z-1
            X30
            """);

        int[] starts = PathPasses.Starts(path.Moves);
        Assert.Equal([0, 3], starts);
        Assert.Equal(0, PathPasses.PassOf(starts, 2));
        Assert.Equal(1, PathPasses.PassOf(starts, 3));
        Assert.Equal(1, PathPasses.PassOf(starts, 6));
        Assert.Equal(3, PathPasses.EndOf(starts, 0, path.Moves.Count));
        Assert.Equal(7, PathPasses.EndOf(starts, 1, path.Moves.Count));
    }
}
