using CncStudio.Core.Dialects;
using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;
using CncStudio.Core.Simulation;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

public class SimulationTests
{
    /// <summary>Bruto de 50 x 50 mm com células de 0,5 mm, topo Z0 e fundo Z-10.</summary>
    private static HeightMap CreateMap() => new(0, 0, 100, 100, 0.5, 0, -10);

    private static ToolPath BuildFanuc(params string[] lines) =>
        TrajectoryBuilder.Build(GCodeParser.Parse(string.Join("\n", lines), Dialect.Fanuc));

    [Fact]
    public void BrutoNovoTemTudoNoTopo()
    {
        HeightMap map = CreateMap();

        Assert.Equal(10000, map.Heights.Length);
        Assert.Equal(0f, map[0, 0]);
        Assert.Equal(0f, map[99, 99]);
    }

    [Fact]
    public void DiscoRebaixaSomenteDentroDoRaio()
    {
        HeightMap map = CreateMap();

        map.CutDisc(25, 25, radius: 5, z: -2);

        Assert.Equal(-2f, map.GetHeightAt(25, 25));
        Assert.Equal(-2f, map.GetHeightAt(29.5, 25));
        Assert.Equal(-2f, map.GetHeightAt(25, 20.6));
        Assert.Equal(0f, map.GetHeightAt(30.6, 25));
        Assert.Equal(0f, map.GetHeightAt(29, 29)); // fora do círculo (diagonal)
    }

    [Fact]
    public void DiscoNaoSobeMaterialJaRemovido()
    {
        HeightMap map = CreateMap();

        map.CutDisc(25, 25, 5, -3);
        map.CutDisc(25, 25, 5, -1);

        Assert.Equal(-3f, map.GetHeightAt(25, 25));
    }

    [Fact]
    public void DiscoNaoPassaDoFundoDoBruto()
    {
        HeightMap map = CreateMap();

        map.CutDisc(25, 25, 5, -50);

        Assert.Equal(-10f, map.GetHeightAt(25, 25));
    }

    [Fact]
    public void DiscoAcimaDoBrutoNaoMudaVersao()
    {
        HeightMap map = CreateMap();
        int version = map.Version;

        map.CutDisc(25, 25, 5, 1);

        Assert.Equal(version, map.Version);
    }

    [Fact]
    public void DiscoParcialmenteForaDaGradeNaoQuebra()
    {
        HeightMap map = CreateMap();

        map.CutDisc(0, 0, 5, -1);

        Assert.Equal(-1f, map[0, 0]);
    }

    [Fact]
    public void CorteRetoDeixaUmCanalDaLarguraDaFerramenta()
    {
        ToolPath path = BuildFanuc(
            "G00 X10 Y25 Z5",
            "G01 Z-2 F100",
            "X40");
        var simulator = new MaterialSimulator(CreateMap(), path.Segments, toolDiameter: 10);

        simulator.RunToEnd();
        HeightMap map = simulator.Map;

        Assert.True(simulator.IsFinished);
        Assert.Equal(-2f, map.GetHeightAt(25, 25));
        Assert.Equal(-2f, map.GetHeightAt(25, 29.4));
        Assert.Equal(0f, map.GetHeightAt(25, 30.6));
        Assert.Equal(0f, map.GetHeightAt(4.4, 25)); // antes do início do corte
        Assert.Equal(-2f, map.GetHeightAt(44.4, 25)); // ponta arredondada no fim
    }

    [Fact]
    public void RapidoAcimaDoBrutoNaoRemoveMaterial()
    {
        ToolPath path = BuildFanuc(
            "G00 Z5",
            "G00 X40 Y40");
        var simulator = new MaterialSimulator(CreateMap(), path.Segments, 10);

        simulator.RunToEnd();

        Assert.All(simulator.Map.Heights.ToArray(), h => Assert.Equal(0f, h));
    }

    [Fact]
    public void AvancoParcialParaNoMeioDoSegmento()
    {
        ToolPath path = BuildFanuc(
            "G00 X10 Y25 Z-1",
            "G01 X40 F100");
        var simulator = new MaterialSimulator(CreateMap(), path.Segments, 4);
        double rapidLength = path.Segments[0].Length;

        simulator.Advance(rapidLength + 10);

        Assert.False(simulator.IsFinished);
        Assert.Equal(20, simulator.ToolPosition.X, 9);
        Assert.Equal(25, simulator.ToolPosition.Y, 9);
        Assert.Equal(-1, simulator.ToolPosition.Z, 9);
        Assert.Equal(-1f, simulator.Map.GetHeightAt(19, 25));
        Assert.Equal(0f, simulator.Map.GetHeightAt(24, 25)); // ainda não chegou lá
        Assert.Equal(2, simulator.CurrentLineNumber);
    }

    [Fact]
    public void PassoAPassoAvancaUmaLinhaInteira()
    {
        ToolPath path = BuildFanuc(
            "G00 X10 Y0",
            "G03 X0 Y10 I-10 J0 F100",
            "G01 X-10");
        var simulator = new MaterialSimulator(CreateMap(), path.Segments, 4);

        simulator.AdvanceToNextLine(); // linha 1
        simulator.AdvanceToNextLine(); // linha 2 (arco com vários segmentos)

        Assert.Equal(new Point3(0, 10, 0), simulator.ToolPosition);
        Assert.Equal(3, simulator.CurrentLineNumber);
    }

    [Fact]
    public void ResetVoltaAoInicio()
    {
        ToolPath path = BuildFanuc("G01 X10 Y10 Z-1 F100");
        var simulator = new MaterialSimulator(CreateMap(), path.Segments, 4);

        simulator.RunToEnd();
        simulator.Reset();

        Assert.False(simulator.IsFinished);
        Assert.Equal(Point3.Origin, simulator.ToolPosition);
        Assert.Equal(0f, simulator.Map.GetHeightAt(10, 10));
    }

    [Fact]
    public void BrutoAutomaticoCobreOCorteComFolga()
    {
        ToolPath path = BuildFanuc(
            "G00 X0 Y0 Z5",
            "G01 Z-3 F100",
            "X100 Y60");

        HeightMap map = HeightMap.CreateForToolPath(path, toolDiameter: 10, margin: 5, maxCellsPerSide: 400);

        Assert.Equal(-10, map.OriginX, 9);
        Assert.Equal(-10, map.OriginY, 9);
        Assert.True(map.Width >= 120 - 1e-9);
        Assert.True(map.Height >= 80 - 1e-9);
        Assert.InRange(map.Columns, 400, 401); // arredondamento de ponto flutuante
        Assert.Equal(0, map.TopZ, 9);
        Assert.Equal(-8, map.BottomZ, 9);
    }
}
