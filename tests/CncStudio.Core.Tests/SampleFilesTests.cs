using CncStudio.Core.Dialects;
using CncStudio.Core.Parsing;
using CncStudio.Core.Simulation;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

/// <summary>Garante que os arquivos de exemplo da pasta samples continuam válidos.</summary>
public class SampleFilesTests
{
    private static string SamplePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "samples", fileName);

    [Theory]
    [InlineData("EXEMPLO.nc", Dialect.Fanuc)]
    [InlineData("EXEMPLO.mpf", Dialect.Siemens)]
    public void ExemploProcessaSemDiagnosticosESimulaDentroDoBruto(string fileName, Dialect expectedDialect)
    {
        string path = SamplePath(fileName);
        Dialect dialect = DialectDetector.FromFileName(path);
        Assert.Equal(expectedDialect, dialect);

        ParseResult parse = GCodeParser.Parse(File.ReadAllText(path), dialect);
        ToolPath toolPath = TrajectoryBuilder.Build(parse);

        Assert.Empty(parse.Diagnostics);
        Assert.Empty(toolPath.Diagnostics);
        Assert.True(parse.Blocks[^1].EndsProgram);

        // Peça de 100 x 60 mm com zero no canto: o corte fica perto dessa área
        Assert.InRange(toolPath.CuttingBounds.Min.X, -10, 0);
        Assert.InRange(toolPath.CuttingBounds.Max.X, 100, 110);
        Assert.Equal(-6, toolPath.CuttingBounds.Min.Z, 9);

        HeightMap map = HeightMap.CreateForToolPath(toolPath, toolDiameter: 10);
        var simulator = new MaterialSimulator(map, toolPath.Segments, 10);
        simulator.RunToEnd();

        Assert.Equal(-3f, map.GetHeightAt(40, 30));  // fundo do bolsão retangular
        Assert.Equal(-6f, map.GetHeightAt(80, 30));  // fundo do bolsão circular
        Assert.Equal(-6f, map.GetHeightAt(8, 8));    // furo
        Assert.Equal(-0.5f, map.GetHeightAt(20, 4)); // faceamento
    }

    [Fact]
    public void FanucESiemensGeramAMesmaTrajetoria()
    {
        ToolPath fanuc = Build("EXEMPLO.nc");
        ToolPath siemens = Build("EXEMPLO.mpf");

        Assert.Equal(fanuc.Segments.Count, siemens.Segments.Count);
        for (int k = 0; k < fanuc.Segments.Count; k++)
        {
            Assert.Equal(fanuc.Segments[k].End.X, siemens.Segments[k].End.X, 9);
            Assert.Equal(fanuc.Segments[k].End.Y, siemens.Segments[k].End.Y, 9);
            Assert.Equal(fanuc.Segments[k].End.Z, siemens.Segments[k].End.Z, 9);
        }
    }

    private static ToolPath Build(string fileName)
    {
        string path = SamplePath(fileName);
        return TrajectoryBuilder.Build(GCodeParser.Parse(File.ReadAllText(path), DialectDetector.FromFileName(path)));
    }
}
