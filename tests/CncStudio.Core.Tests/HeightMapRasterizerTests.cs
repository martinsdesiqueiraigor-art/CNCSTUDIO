using CncStudio.Core.Simulation;

namespace CncStudio.Core.Tests;

public class HeightMapRasterizerTests
{
    private static HeightMap CreateMapWithPocket()
    {
        var map = new HeightMap(0, 0, 40, 20, 1, 0, -10);
        map.CutDisc(10, 10, 4, -5); // furo à esquerda
        return map;
    }

    [Fact]
    public void CorDoTopoEhAluminioEMaisFundoEhDiferente()
    {
        HeightMap map = CreateMapWithPocket();

        Assert.Equal(HeightMapRasterizer.StockTopColor, HeightMapRasterizer.DepthColor(map, 0));
        Assert.NotEqual(HeightMapRasterizer.DepthColor(map, -2), HeightMapRasterizer.DepthColor(map, -8));
    }

    [Fact]
    public void VistaSuperiorTemUmPixelPorCelulaComYParaCima()
    {
        HeightMap map = CreateMapWithPocket();
        var pixels = new uint[map.Columns * map.Rows];

        HeightMapRasterizer.RenderTop(map, pixels);

        // Célula (10,10) cortada; linha da imagem = Rows − 1 − row
        uint pocket = pixels[(map.Rows - 1 - 10) * map.Columns + 10];
        uint stock = pixels[(map.Rows - 1 - 10) * map.Columns + 30];
        Assert.NotEqual(pocket, stock);
        Assert.All(pixels, p => Assert.Equal(0xFFu, p >> 24)); // tudo opaco
    }

    [Fact]
    public void VistaIsometricaDesenhaOBrutoComFundoTransparente()
    {
        HeightMap map = CreateMapWithPocket();

        IsoImage image = HeightMapRasterizer.RenderIso(map, pixelsPerMm: 4);

        Assert.True(image.Width > 100 && image.Height > 50);
        Assert.Equal(0u, image.Pixels[0]); // canto superior esquerdo fica fora do bloco
        Assert.Contains(image.Pixels, p => p != 0);

        // O centro da face de cima do bloco (canto sem corte) tem material
        double u = IsoProjection.U(30, 10);
        double v = IsoProjection.V(30, 10, 0);
        int px = (int)((u - image.MinU) * image.PixelsPerMm);
        int py = (int)((image.MaxV - v) * image.PixelsPerMm);
        Assert.NotEqual(0u, image.Pixels[py * image.Width + px]);
    }

    [Fact]
    public void PerfilFrontalMostraSilhuetaECorteNaPosicaoDaFerramenta()
    {
        HeightMap map = CreateMapWithPocket();
        var silhouette = new float[map.Columns];
        var section = new float[map.Columns];

        HeightMapRasterizer.ComputeFrontProfile(map, sectionY: 10, silhouette, section);

        Assert.Equal(0f, silhouette[10]); // de frente, o furo fica escondido pelo material
        Assert.Equal(-5f, section[10]);    // no corte em Y=10 ele aparece
        Assert.Equal(0f, section[30]);
    }

    [Fact]
    public void PerfilLateralUsaCorteEmX()
    {
        HeightMap map = CreateMapWithPocket();
        var silhouette = new float[map.Rows];
        var section = new float[map.Rows];

        HeightMapRasterizer.ComputeSideProfile(map, sectionX: 10, silhouette, section);

        Assert.Equal(-5f, section[10]);
        Assert.Equal(0f, section[0]);
    }
}
