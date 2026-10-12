using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;
using CncStudio.Core.Rendering;
using CncStudio.Core.Simulation;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

public class ViewCameraTests
{
    [Fact]
    public void VistasPadraoProjetamOsEixosEsperados()
    {
        var p = new Point3(3, 5, 7);
        Assert.Equal((3.0, 5.0), Round(ViewCamera.Top.Project(p)));
        Assert.Equal((3.0, 7.0), Round(ViewCamera.Front.Project(p)));
        Assert.Equal((5.0, 7.0), Round(ViewCamera.Side.Project(p)));
    }

    [Fact]
    public void CamerasSaoOrtonormaisEOObservadorFicaNaFrente()
    {
        foreach (ViewCamera camera in new[] { ViewCamera.Top, ViewCamera.Front, ViewCamera.Side, ViewCamera.Iso, ViewCamera.Lathe })
        {
            Assert.Equal(0, ViewCamera.Dot(camera.Right, camera.Up), 9);
            Assert.Equal(0, ViewCamera.Dot(camera.Right, camera.ToViewer), 9);
            Assert.Equal(1, ViewCamera.Dot(camera.Up, camera.Up), 9);
            Assert.True(camera.Depth(camera.ToViewer) < camera.Depth(Point3.Origin)); // mais perto = menor profundidade
        }

        // Isométrica: o canto X+/Y−/Z+ é o mais próximo do observador
        Assert.True(ViewCamera.Iso.Depth(new Point3(1, -1, 1)) < ViewCamera.Iso.Depth(new Point3(-1, 1, -1)));
    }

    private static (double, double) Round((double U, double V) p) => (Math.Round(p.U, 9), Math.Round(p.V, 9));
}

public class Raster3DTests
{
    [Fact]
    public void TrianguloMaisPertoCobreOMaisLonge()
    {
        var raster = new Raster3D(ViewCamera.Top, 0, 10, 10, 100, 100);
        // Longe (Z = 0) em vermelho, perto (Z = 5) em verde, desenhados nessa ordem e ao contrário
        raster.Triangle(new Point3(0, 0, 5), new Point3(10, 0, 5), new Point3(0, 10, 5), 0xFF00FF00);
        raster.Triangle(new Point3(0, 0, 0), new Point3(10, 0, 0), new Point3(0, 10, 0), 0xFFFF0000);

        Assert.Equal(0xFF00FF00, raster.Pixels[90 * 100 + 10]); // perto de (1, 1)
        Assert.Equal(0u, raster.Pixels[5 * 100 + 95]);           // fora do triângulo: transparente
    }

    [Fact]
    public void LuzDeixaAFaceDeFrenteMaisClaraQueADeLado()
    {
        var raster = new Raster3D(ViewCamera.Iso, 0, 10, 1, 10, 10);
        uint facing = raster.Shade(0x808080, new Point3(0, 0, 1));
        uint grazing = raster.Shade(0x808080, new Point3(-1, 1, 0) );
        Assert.True((facing & 0xFF) > (grazing & 0xFF));
    }
}

public class SolidRenderingTests
{
    private static ToolPath Fanuc(string program, MachineSettings? settings = null) =>
        MachineExecutor.Execute(GCodeParser.Parse(program, Dialect.Fanuc), settings);

    [Fact]
    public void BlocoUsinadoTemBegeNoTopoEAzulNoBolsao()
    {
        var map = HeightMap.CreateForStock(Stock.Block(0, 40, 0, 40, -10, 0), 80);
        ToolPath path = Fanuc("G00 X20 Y20 Z5\nG01 Z-5 F100\nG00 Z5");
        new MaterialSimulator(map, path.Moves, 10).RunToEnd();

        SolidImage image = MillSolidRenderer.Render(map, ViewCamera.Top, 200);
        Assert.Equal(200, image.Width);
        uint Pixel(double x, double y) => image.Pixels[(int)((image.MaxV - y) * image.PixelsPerMm) * image.Width + (int)((x - image.MinU) * image.PixelsPerMm)];

        uint corner = Pixel(2, 38) & 0xFFFFFF;
        uint pocket = Pixel(20, 20) & 0xFFFFFF;
        Assert.True((corner >> 16 & 0xFF) > (corner & 0xFF));  // bege: mais vermelho que azul
        Assert.True((pocket & 0xFF) > (pocket >> 16 & 0xFF));  // azul
    }

    [Fact]
    public void TodasAsVistasDaFresaDesenhamAlgo()
    {
        var map = HeightMap.CreateForStock(Stock.Block(0, 30, 0, 20, -5, 0), 60);
        foreach (ViewCamera camera in new[] { ViewCamera.Top, ViewCamera.Front, ViewCamera.Side, ViewCamera.Iso })
        {
            SolidImage image = MillSolidRenderer.Render(map, camera, 120);
            Assert.True(image.Pixels.Count(p => p != 0) > image.Pixels.Length / 4, camera.Name);
        }
    }

    [Fact]
    public void TornoComMeiaSecaoMostraOCorte()
    {
        var stock = new TurningStock(-40, 0, 15);
        SolidImage full = LatheSolidRenderer.Render(stock, ViewCamera.Lathe, quarterSection: false, maxPixels: 200);
        SolidImage cut = LatheSolidRenderer.Render(stock, ViewCamera.Lathe, quarterSection: true, maxPixels: 200);

        static int Orange(SolidImage i) => i.Pixels.Count(p => ((p >> 16) & 0xFF) > 150 && (p & 0xFF) < 140 && ((p >> 8) & 0xFF) > 90);
        Assert.True(Orange(cut) > Orange(full) + 100); // faces cortadas (laranja) só aparecem com a meia seção
    }
}

public class TurningSimulationTests
{
    private static readonly MachineSettings Lathe = new() { Machine = MachineKind.Lathe };

    private static ToolPath Fanuc(string program) =>
        MachineExecutor.Execute(GCodeParser.Parse(program, Dialect.Fanuc), Lathe);

    [Fact]
    public void TorneamentoExternoBaixaORaio()
    {
        var stock = new TurningStock(-50, 0, 25);
        ToolPath path = Fanuc("G97 S1000\nG00 X52 Z2\nG00 X40\nG01 Z-20 F0.2\nG00 X52");
        var simulator = new TurningSimulator(stock, path.Moves, _ => 10);
        simulator.RunToEnd();

        int Slice(double z) => (int)((z - stock.MinZ) / stock.SliceLength);
        Assert.Equal(20, stock.Outer[Slice(-10)], 3);   // Ø40
        Assert.Equal(25, stock.Outer[Slice(-30)], 3);   // além do corte: intacto
        Assert.Empty(simulator.RapidCollisions);
    }

    [Fact]
    public void FuroNoEixoEMandrilamentoAbremOFuro()
    {
        var stock = new TurningStock(-50, 0, 25);
        ToolPath path = Fanuc("""
            G97 S800
            G00 X0 Z2
            G01 Z-30 F0.1
            G00 Z2
            G00 X24
            G01 Z-15
            G00 X20
            G00 Z2
            """);
        var simulator = new TurningSimulator(stock, path.Moves, t => 10);
        simulator.RunToEnd();

        int Slice(double z) => (int)((z - stock.MinZ) / stock.SliceLength);
        Assert.Equal(12, stock.Inner[Slice(-10)], 3);  // mandrilado a Ø24
        Assert.Equal(5, stock.Inner[Slice(-25)], 3);   // só o furo da broca Ø10
        Assert.Equal(0, stock.Inner[Slice(-40)], 3);   // maciço
        Assert.Equal(25, stock.Outer[Slice(-10)], 3);
    }

    [Fact]
    public void G00DentroDaBarraEhColisaoMasOPrimeiroMovimentoNao()
    {
        var stock = new TurningStock(-50, 0, 25);
        ToolPath path = Fanuc("G00 X30 Z-10\nG00 X20\nG00 X60");   // 1º G00 sai do X0 Z0 (ponto fictício)
        var simulator = new TurningSimulator(stock, path.Moves, _ => 10);
        simulator.RunToEnd();
        Assert.Equal([1], simulator.RapidCollisions);
    }

    [Fact]
    public void AvancarPorLinhaExecutaOCicloInteiro()
    {
        var stock = new TurningStock(-50, 0, 26);
        ToolPath path = Fanuc("""
            G97 S1000
            G00 X52 Z2
            G71 U2 R0.5
            G71 P10 Q20 U0 W0 F0.2
            N10 G00 X20
            G01 Z-20
            X40
            N20 X52
            G00 X100 Z100
            """);
        var simulator = new TurningSimulator(stock, path.Moves, _ => 10);

        simulator.AdvanceLine(); // linha 2 (aproximação)
        Assert.Equal(4, simulator.CurrentLineNumber);
        simulator.AdvanceLine(); // linha 4: o G71 inteiro
        Assert.Equal(9, simulator.CurrentLineNumber);
        int Slice(double z) => (int)((z - stock.MinZ) / stock.SliceLength);
        Assert.Equal(10, stock.Outer[Slice(-10)], 3);
    }

    [Fact]
    public void BrutoAutomaticoTemFaceNoZeroERaioDoCorte()
    {
        ToolPath path = Fanuc("G97 S1000\nG00 X52 Z2\nG01 X50 Z0 F0.2\nZ-40\nG00 X60");
        TurningStock stock = TurningStock.CreateForToolPath(path);
        Assert.Equal(0, stock.MaxZ, 1);
        Assert.Equal(26, stock.Radius, 3);
        Assert.True(stock.MinZ < -40);
    }
}
