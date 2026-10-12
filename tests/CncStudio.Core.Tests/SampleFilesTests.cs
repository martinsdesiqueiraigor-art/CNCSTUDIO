using CncStudio.Core.Execution;
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
        ToolPath toolPath = MachineExecutor.Execute(parse);

        Assert.Empty(parse.Diagnostics);
        Assert.Empty(toolPath.Diagnostics);
        Assert.Equal(30.0, parse.Blocks[^1].Find('M')!.Value.Value); // termina com M30

        // Peça de 100 x 60 mm com zero no canto: o corte fica perto dessa área
        Assert.InRange(toolPath.CuttingBounds.Min.X, -10, 0);
        Assert.InRange(toolPath.CuttingBounds.Max.X, 100, 110);
        Assert.Equal(-6, toolPath.CuttingBounds.Min.Z, 9);

        HeightMap map = HeightMap.CreateForToolPath(toolPath, toolDiameter: 10);
        var simulator = new MaterialSimulator(map, toolPath.Moves, 10);
        simulator.RunToEnd();

        Assert.Equal(-3f, map.GetHeightAt(40, 30));  // fundo do bolsão retangular
        Assert.Equal(-6f, map.GetHeightAt(80, 30));  // fundo do bolsão circular
        Assert.Equal(-6f, map.GetHeightAt(8, 8));    // furo
        Assert.Equal(-0.5f, map.GetHeightAt(20, 4)); // faceamento
    }

    [Theory]
    [InlineData("FURACAO.nc")]
    [InlineData("FURACAO.mpf")]
    public void ExemplosDeFuracaoFuramNasPosicoesCertas(string fileName)
    {
        string path = SamplePath(fileName);
        ToolPath toolPath = MachineExecutor.Execute(GCodeParser.Parse(File.ReadAllText(path), DialectDetector.FromFileName(path)));

        Assert.Empty(toolPath.Diagnostics);
        Assert.Equal(new[] { 2, 3 }, toolPath.Tools);

        // Profundidade máxima em alguns furos
        double Bottom(double x, double y) => toolPath.Moves
            .Where(m => Math.Abs(m.End.X - x) < 1e-6 && Math.Abs(m.End.Y - y) < 1e-6)
            .Min(m => m.End.Z);

        Assert.Equal(-18, Bottom(15, 15), 6);
        Assert.Equal(-18, Bottom(85, 45), 6);
    }

    [Fact]
    public void FanucESiemensGeramAMesmaTrajetoria()
    {
        ToolPath fanuc = Build("EXEMPLO.nc");
        ToolPath siemens = Build("EXEMPLO.mpf");

        Assert.Equal(fanuc.Moves.Count, siemens.Moves.Count);
        for (int k = 0; k < fanuc.Moves.Count; k++)
        {
            Assert.Equal(fanuc.Moves[k].Motion, siemens.Moves[k].Motion);
            Assert.Equal(fanuc.Moves[k].End.X, siemens.Moves[k].End.X, 9);
            Assert.Equal(fanuc.Moves[k].End.Y, siemens.Moves[k].End.Y, 9);
            Assert.Equal(fanuc.Moves[k].End.Z, siemens.Moves[k].End.Z, 9);
            Assert.Equal(fanuc.Moves[k].Sweep, siemens.Moves[k].Sweep, 9);
        }
    }

    private static ToolPath Build(string fileName)
    {
        string path = SamplePath(fileName);
        return MachineExecutor.Execute(GCodeParser.Parse(File.ReadAllText(path), DialectDetector.FromFileName(path)));
    }
}
