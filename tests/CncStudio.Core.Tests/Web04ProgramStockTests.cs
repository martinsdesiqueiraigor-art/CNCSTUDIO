using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Setup;
using CncStudio.Core.Simulation;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>WEB-04: a mesma escrita do comentário BRUTO no torno e na fresa (redondo, tubo, prisma e sextavado).</summary>
public class Web04ProgramStockTests
{
    private static Bounds3 Box(double x0, double x1, double y0, double y1, double z0, double z1) =>
        Bounds3.Empty.Include(new Point3(x0, y0, z0)).Include(new Point3(x1, y1, z1));

    [Theory]
    [InlineData("(BRUTO D100 X 60)", SetupStockKind.Round, 100, 0, 60)]
    [InlineData("(BRUTO D100 X D70 X 100)", SetupStockKind.Tube, 100, 70, 100)]
    [InlineData("(Bruto d100 x d70 x 100)", SetupStockKind.Tube, 100, 70, 100)]
    public void TornoRedondoETubo(string comment, SetupStockKind kind, double d, double inner, double length)
    {
        SetupStock s = ProgramStock.Read(comment, MachineKind.Lathe, Bounds3.Empty)!;
        Assert.Equal(kind, s.Kind);
        Assert.Equal((d, length, 0.0), (s.Diameter, s.Height, s.Top));
        if (kind == SetupStockKind.Tube)
        {
            Assert.Equal(inner, s.InnerDiameter);
        }
    }

    [Fact]
    public void TornoPrismaViraBarraRetangularComOUltimoNumeroDeComprimento()
    {
        SetupStock s = ProgramStock.Read("(BRUTO 40 X 30 X 100)", MachineKind.Lathe, Bounds3.Empty)!;
        Assert.Equal(SetupStockKind.Prism, s.Kind);
        Assert.Equal((40.0, 30.0, 100.0, 0.0), (s.Width, s.Depth, s.Height, s.Top));
        Stock bar = s.ToStock(MachineKind.Lathe)!;
        Assert.Equal(LatheBarShape.Rectangle, bar.Section!.Shape);
        Assert.Equal(100, bar.Length, 6);
    }

    [Theory]
    [InlineData("(BRUTO S60 X 100)")]
    [InlineData("(BRUTO SEXT60 X 100)")]
    [InlineData("(BRUTO SEXTAVADO 60 x 100)")]
    public void TornoSextavado(string comment)
    {
        SetupStock s = ProgramStock.Read(comment, MachineKind.Lathe, Bounds3.Empty)!;
        Assert.Equal(SetupStockKind.Hexagon, s.Kind);
        Assert.Equal((60.0, 100.0, 0.0), (s.Width, s.Height, s.Top));
        Assert.Equal(LatheBarShape.Hexagon, s.ToStock(MachineKind.Lathe)!.Section!.Shape);
    }

    [Fact]
    public void TornoTuboComecaFurado()
    {
        Stock bar = ProgramStock.Read("(BRUTO D100 X D70 X 100)", MachineKind.Lathe, Bounds3.Empty)!.ToStock(MachineKind.Lathe)!;
        Assert.Equal(70, bar.BoreDiameter);
        TurningStock turning = TurningStock.CreateForStock(bar);
        Assert.Equal(35f, turning.InitialInner, 3);
        Assert.Equal(50f, turning.InitialOuter, 3);
    }

    [Fact]
    public void FresaSextavadoEmPeCentradoNoZero()
    {
        SetupStock s = ProgramStock.Read("(BRUTO S60 X 100)", MachineKind.Mill, Box(-20, 20, -20, 20, -10, 0))!;
        Assert.Equal(SetupStockKind.Hexagon, s.Kind);
        Assert.Equal((0.0, 0.0, -100.0), (s.X, s.Y, s.Z));
        Assert.Null(s.Validate());
    }

    [Fact]
    public void FresaSextavadoComCortePositivoTemOCentroNoMeio()
    {
        SetupStock s = ProgramStock.Read("(BRUTO S60 X 20)", MachineKind.Mill, Box(5, 60, 5, 55, -5, 0))!;
        Assert.Equal(60 / Math.Sqrt(3), s.X, 6);
        Assert.Equal(30, s.Y, 6);
    }

    [Fact]
    public void FresaSextavadoSoTemMaterialDentroDaSecao()
    {
        var setup = new MachiningSetup { Stock = ProgramStock.Read("(BRUTO S60 X 20)", MachineKind.Mill, Box(-20, 20, -20, 20, -5, 0)) };
        string program = "(BRUTO S60 X 20)\nG90 G54\nT1 M6\nG00 X0 Y0 Z5\nG01 Z-1 F100\nG00 Z50\nM30\n";
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(program, Dialect.Fanuc, setup.ApplyTo(MachineSettings.Default));
        HeightMap map = SimulationFactory.Create(analysis, setup, program).Map!;
        Assert.True(Material(map, 0, 0));
        Assert.True(Material(map, 33, 0));   // perto do canto em X (raio do canto 34,6)
        Assert.False(Material(map, 0, 32));  // além da face em Y (30)
        Assert.False(Material(map, 30, 20)); // fora da face inclinada
    }

    private static bool Material(HeightMap map, double x, double y)
    {
        int c = (int)Math.Floor((x - map.OriginX) / map.CellSize), r = (int)Math.Floor((y - map.OriginY) / map.CellSize);
        return c >= 0 && r >= 0 && c < map.Columns && r < map.Rows && !map.IsEmptyCell(c, r);
    }

    [Fact]
    public void SemSAntesDoNumeroNaoViraSextavado()
    {
        Assert.Equal(SetupStockKind.Prism, ProgramStock.Read("(BRUTO ACO SAE 100 X 80 X 20)", MachineKind.Mill, Bounds3.Empty)!.Kind);
        Assert.Equal(SetupStockKind.Round, ProgramStock.Read("(BRUTO ACO SAE D50 X 70)", MachineKind.Lathe, Bounds3.Empty)!.Kind);
    }
}
