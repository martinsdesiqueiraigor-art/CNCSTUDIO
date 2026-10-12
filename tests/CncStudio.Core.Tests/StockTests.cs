using System.Globalization;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;
using CncStudio.Core.Simulation;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

public class StockTests
{
    private static readonly CultureInfo PtBr = new("pt-BR");

    [Fact]
    public void BlocoEBarraTemDimensoesEDescricao()
    {
        Stock block = Stock.Block(0, 100, 0, 80, -20, 0);
        Assert.True(block.IsValid);
        Assert.StartsWith("100 × 80 × 20 mm", block.Describe(PtBr));

        Stock bar = Stock.Bar(50, 80, 1);
        Assert.Equal(25, bar.MaxX);
        Assert.Equal(-79, bar.MinZ);
        Assert.Equal("Ø50 × 80 mm (face em Z 1)", bar.Describe(PtBr));
    }

    [Theory]
    [InlineData("BRUTO 100 X 80 X 20", 0, 100, 0, 80)]
    [InlineData("STOCK 120x60x25.5", 0, 120, 0, 60)]
    public void LeBlocoDoComentarioComCantoNoZero(string comment, double minX, double maxX, double minY, double maxY)
    {
        Bounds3 cutting = Bounds3.Empty.Include(new Point3(5, 5, -3)).Include(new Point3(50, 40, 0));
        Stock? stock = Stock.FromComment(comment, MachineKind.Mill, cutting);

        Assert.NotNull(stock);
        Assert.Equal(minX, stock.MinX, 6);
        Assert.Equal(maxX, stock.MaxX, 6);
        Assert.Equal(minY, stock.MinY, 6);
        Assert.Equal(maxY, stock.MaxY, 6);
        Assert.Equal(0, stock.MaxZ, 6);
    }

    [Fact]
    public void BlocoCentradoQuandoOCorteTemCoordenadasNegativas()
    {
        Bounds3 cutting = Bounds3.Empty.Include(new Point3(-40, -30, -5)).Include(new Point3(40, 30, 0));
        Stock stock = Stock.FromComment("(BRUTO 100 X 80 X 20)", MachineKind.Mill, cutting)!;
        Assert.Equal(-50, stock.MinX, 6);
        Assert.Equal(40, stock.MaxY, 6);
        Assert.Equal(-20, stock.MinZ, 6);
    }

    [Fact]
    public void LeBarraDoTornoEIgnoraComentarioQualquer()
    {
        Stock bar = Stock.FromComment("BRUTO D50 X 70 - ZERO NA FACE", MachineKind.Lathe, Bounds3.Empty)!;
        Assert.Equal(50, bar.Diameter, 6);
        Assert.Equal(70, bar.Length, 6);
        Assert.Null(Stock.FromComment("DESBASTE EXTERNO", MachineKind.Lathe, Bounds3.Empty));
        Assert.Null(Stock.FromComment("MATERIAL 1045", MachineKind.Mill, Bounds3.Empty));
    }

    [Fact]
    public void AlturaDoMapaVemDoBruto()
    {
        HeightMap map = HeightMap.CreateForStock(Stock.Block(-10, 90, 0, 50, -15, 2));
        Assert.Equal(-10, map.OriginX, 6);
        Assert.Equal(2, map.TopZ, 6);
        Assert.Equal(-15, map.BottomZ, 6);
        Assert.True(map.Columns <= 400 && map.Rows <= 400);
    }

    [Fact]
    public void G00QueRemoveMaterialEhColisao()
    {
        ToolPath path = MachineExecutor.Execute(GCodeParser.Parse("""
            G00 X-20 Y25 Z5
            G00 Z-2
            G00 X50
            G01 X80 F100
            G00 Z10
            """, Dialect.Fanuc));
        var simulator = new MaterialSimulator(HeightMap.CreateForStock(Stock.Block(0, 100, 0, 50, -20, 0)), path.Moves, 10);
        simulator.RunToEnd();

        // O G00 X50 com Z-2 atravessa o bruto; o G01 corta normalmente (não é colisão)
        int collision = Assert.Single(simulator.RapidCollisions);
        Assert.Equal(3, path.Moves[collision].LineNumber);

        simulator.RunToMove(0);
        Assert.Empty(simulator.RapidCollisions);
    }
}
