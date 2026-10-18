using CncStudio.Core.Geometry;
using CncStudio.Core.Simulation;

namespace CncStudio.Core.Rendering;

/// <summary>
/// Desenha o bruto da fresa (HeightMap) como sólido iluminado, em qualquer vista fixa:
/// topos das células (trechos de mesma altura juntos), paredes entre alturas diferentes e as
/// laterais do bloco. Cor: bruto intocado em bege; superfícies usinadas em azul, mais escuro
/// quanto mais fundo.
/// </summary>
public static class MillSolidRenderer
{
    public const uint StockColor = 0xD9BC8C;     // bege (como o CIMCO)
    private const uint MachinedTop = 0xA9CDEB;  // azul claro (raso)
    private const uint MachinedDeep = 0x3F7CC0; // azul (fundo)

    /// <summary>
    /// Renderiza com no máximo "maxPixels" no lado maior da imagem. "extra" amplia a área
    /// (ex.: para caber a trajetória acima do bruto).
    /// </summary>
    public static SolidImage Render(HeightMap map, ViewCamera camera, int maxPixels = 1200)
    {
        var box = Bounds3.Empty
            .Include(new Point3(map.OriginX, map.OriginY, map.BottomZ))
            .Include(new Point3(map.OriginX + map.Width, map.OriginY + map.Height, map.TopZ));
        (double minU, double minV, double maxU, double maxV) = camera.ProjectBounds(ViewCamera.Corners(box));

        double widthMm = Math.Max(maxU - minU, 1e-3);
        double heightMm = Math.Max(maxV - minV, 1e-3);
        double ppm = Math.Min(maxPixels / widthMm, maxPixels / heightMm);
        int width = Math.Max(1, (int)Math.Ceiling(widthMm * ppm));
        int height = Math.Max(1, (int)Math.Ceiling(heightMm * ppm));

        var raster = new Raster3D(camera, minU, maxV, ppm, width, height);
        Draw(map, raster);
        return new SolidImage(width, height, ppm, minU, maxV, raster.Pixels);
    }

    /// <summary>Desenha o sólido no rasterizador (separado para os testes).</summary>
    internal static void Draw(HeightMap map, Raster3D raster) => EmitQuads(map, raster.Quad);

    /// <summary>
    /// Superfície do sólido como quadriláteros (a, b, c, d em ordem, normal, cor RGB): topos
    /// agrupados por trechos de mesma altura, paredes entre células e laterais do bloco.
    /// A mesma geometria serve ao rasterizador por CPU e às malhas para GPU (<see cref="SolidMesh"/>).
    /// </summary>
    public static void EmitQuads(HeightMap map, Action<Point3, Point3, Point3, Point3, Point3, uint> quad)
    {
        int columns = map.Columns, rows = map.Rows;
        double c = map.CellSize;
        double ox = map.OriginX, oy = map.OriginY;
        double top = map.TopZ, bottom = map.BottomZ;
        ReadOnlySpan<float> h = map.Heights;

        uint ColorAt(float height) => height >= top - 1e-4 ? StockColor : Machined(top, bottom, height);

        // Topos: trechos seguidos de mesma altura numa linha viram um quadrilátero só
        var up = new Point3(0, 0, 1);
        for (int row = 0; row < rows; row++)
        {
            double y0 = oy + row * c, y1 = y0 + c;
            int start = 0;
            for (int column = 1; column <= columns; column++)
            {
                if (column < columns && h[row * columns + column] == h[row * columns + start])
                {
                    continue;
                }

                float z = h[row * columns + start];
                double x0 = ox + start * c, x1 = ox + column * c;
                if (map.HasEmptyCells && z <= bottom)
                {
                    start = column;
                    continue;   // sem material (fora do bruto redondo ou furo do tubo)
                }

                quad(new Point3(x0, y0, z), new Point3(x1, y0, z), new Point3(x1, y1, z), new Point3(x0, y1, z), up, ColorAt(z));
                start = column;
            }
        }

        // Paredes internas entre células vizinhas de alturas diferentes
        for (int row = 0; row < rows; row++)
        {
            double y0 = oy + row * c, y1 = y0 + c;
            for (int column = 0; column < columns; column++)
            {
                float a = h[row * columns + column];
                double x1 = ox + (column + 1) * c;

                if (column + 1 < columns)
                {
                    float b = h[row * columns + column + 1];
                    if (a != b)
                    {
                        float low = Math.Min(a, b), high = Math.Max(a, b);
                        var normal = new Point3(a > b ? 1 : -1, 0, 0);
                        quad(new Point3(x1, y0, low), new Point3(x1, y1, low), new Point3(x1, y1, high), new Point3(x1, y0, high),
                            normal, Machined(top, bottom, low));
                    }
                }

                if (row + 1 < rows)
                {
                    float b = h[(row + 1) * columns + column];
                    if (a != b)
                    {
                        double x0 = ox + column * c;
                        float low = Math.Min(a, b), high = Math.Max(a, b);
                        var normal = new Point3(0, a > b ? 1 : -1, 0);
                        quad(new Point3(x0, y1, low), new Point3(x1, y1, low), new Point3(x1, y1, high), new Point3(x0, y1, high),
                            normal, Machined(top, bottom, low));
                    }
                }
            }
        }

        // Laterais do bloco (do fundo até a altura de cada célula da borda)
        double xMax = ox + columns * c, yMax = oy + rows * c;
        for (int column = 0; column < columns; column++)
        {
            double x0 = ox + column * c, x1 = x0 + c;
            float front = h[column], back = h[(rows - 1) * columns + column];
            quad(new Point3(x0, oy, bottom), new Point3(x1, oy, bottom), new Point3(x1, oy, front), new Point3(x0, oy, front),
                new Point3(0, -1, 0), StockColor);
            quad(new Point3(x0, yMax, bottom), new Point3(x1, yMax, bottom), new Point3(x1, yMax, back), new Point3(x0, yMax, back),
                new Point3(0, 1, 0), StockColor);
        }

        for (int row = 0; row < rows; row++)
        {
            double y0 = oy + row * c, y1 = y0 + c;
            float left = h[row * columns], right = h[row * columns + columns - 1];
            quad(new Point3(ox, y0, bottom), new Point3(ox, y1, bottom), new Point3(ox, y1, left), new Point3(ox, y0, left),
                new Point3(-1, 0, 0), StockColor);
            quad(new Point3(xMax, y0, bottom), new Point3(xMax, y1, bottom), new Point3(xMax, y1, right), new Point3(xMax, y0, right),
                new Point3(1, 0, 0), StockColor);
        }
    }

    /// <summary>Azul do usinado: claro perto do topo, escuro no fundo.</summary>
    internal static uint Machined(double top, double bottom, double height)
    {
        double t = Math.Clamp((top - height) / Math.Max(top - bottom, 1e-6), 0, 1);
        return Lerp(MachinedTop, MachinedDeep, Math.Sqrt(t));
    }

    internal static uint Lerp(uint a, uint b, double t)
    {
        uint Mix(int shift) => (uint)Math.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return (Mix(16) << 16) | (Mix(8) << 8) | Mix(0);
    }
}
