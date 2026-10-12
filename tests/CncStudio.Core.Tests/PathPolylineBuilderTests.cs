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
