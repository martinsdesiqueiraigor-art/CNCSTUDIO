namespace CncStudio.Core.Simulation;

/// <summary>Imagem isométrica do material, com a posição dela em coordenadas da projeção (mm).</summary>
public sealed class IsoImage
{
    public IsoImage(int width, int height, double pixelsPerMm, double minU, double maxV)
    {
        Width = width;
        Height = height;
        PixelsPerMm = pixelsPerMm;
        MinU = minU;
        MaxV = maxV;
        Pixels = new uint[width * height];
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>Pixels BGRA (0xAARRGGBB), linha 0 em cima. Transparente onde não há material.</summary>
    public uint[] Pixels { get; }

    public double PixelsPerMm { get; }

    /// <summary>u do lado esquerdo da imagem.</summary>
    public double MinU { get; }

    /// <summary>v do lado de cima da imagem.</summary>
    public double MaxV { get; }

    public double WidthMm => Width / PixelsPerMm;
    public double HeightMm => Height / PixelsPerMm;
}

/// <summary>
/// Converte o <see cref="HeightMap"/> em imagens e perfis para as vistas da simulação.
/// Código puro (sem UI) para poder rodar fora da thread de interface e ser testado.
/// </summary>
public static class HeightMapRasterizer
{
    // Direção da luz (de cima, à esquerda e ao fundo), já normalizada
    private static readonly (double X, double Y, double Z) Light = Normalize(-0.5, 0.6, 0.8);

    /// <summary>Cor do material ainda no topo do bruto (alumínio).</summary>
    public const uint StockTopColor = 0xFFB8C2CC;

    /// <summary>
    /// Vista superior: 1 pixel por célula. Cor pela profundidade + sombreamento de relevo.
    /// "pixels" precisa ter Columns × Rows posições; a linha 0 da imagem é o maior Y.
    /// </summary>
    public static void RenderTop(HeightMap map, uint[] pixels)
    {
        if (pixels.Length < map.Columns * map.Rows)
        {
            throw new ArgumentException("Buffer menor que a grade.", nameof(pixels));
        }

        for (int row = 0; row < map.Rows; row++)
        {
            int imageRow = map.Rows - 1 - row;
            for (int column = 0; column < map.Columns; column++)
            {
                pixels[imageRow * map.Columns + column] = ShadedColor(map, column, row, 1.0);
            }
        }
    }

    /// <summary>
    /// Vista isométrica por "pintor": cada célula vira um losango (face de cima) com
    /// paredes até a altura dos vizinhos da frente, desenhadas de trás para frente
    /// em diagonais de mesma profundidade (x − y).
    /// </summary>
    public static IsoImage RenderIso(HeightMap map, double pixelsPerMm)
    {
        double c = map.CellSize;
        double x0 = map.OriginX;
        double y0 = map.OriginY;
        double x1 = x0 + map.Width;
        double y1 = y0 + map.Height;

        double minU = IsoProjection.U(x0, y0);
        double maxU = IsoProjection.U(x1, y1);
        double maxV = IsoProjection.V(x0, y1, map.TopZ);   // canto do fundo à esquerda, no topo
        double minV = IsoProjection.V(x1, y0, map.BottomZ); // canto da frente à direita, no fundo

        int width = Math.Max(1, (int)Math.Ceiling((maxU - minU) * pixelsPerMm) + 2);
        int height = Math.Max(1, (int)Math.Ceiling((maxV - minV) * pixelsPerMm) + 2);
        var image = new IsoImage(width, height, pixelsPerMm, minU, maxV);
        uint[] pixels = image.Pixels;

        double halfWidthPx = c * IsoProjection.Cos30 * pixelsPerMm;  // meia largura do losango
        double halfHeightPx = c * IsoProjection.Sin30 * pixelsPerMm; // meia altura do losango

        int columns = map.Columns;
        int rows = map.Rows;

        // Diagonais de profundidade crescente: d = column − row (mais ao fundo primeiro)
        for (int d = -(rows - 1); d <= columns - 1; d++)
        {
            int rowStart = Math.Max(0, -d);
            int rowEnd = Math.Min(rows - 1, columns - 1 - d);
            for (int row = rowStart; row <= rowEnd; row++)
            {
                int column = row + d;
                float h = map[column, row];

                double x = map.CellCenterX(column);
                double y = map.CellCenterY(row);
                double centerPx = (IsoProjection.U(x, y) - minU) * pixelsPerMm;
                double centerPy = (maxV - IsoProjection.V(x, y, h)) * pixelsPerMm;

                // Altura das paredes: até os vizinhos da frente (−Y à esquerda, +X à direita)
                double leftLow = row > 0 ? map[column, row - 1] : map.BottomZ;
                double rightLow = column < columns - 1 ? map[column + 1, row] : map.BottomZ;
                double leftWallPx = Math.Max(0, (h - leftLow) * pixelsPerMm);
                double rightWallPx = Math.Max(0, (h - rightLow) * pixelsPerMm);

                uint top = ShadedColor(map, column, row, 1.0);
                uint leftWall = Darken(top, 0.70);
                uint rightWall = Darken(top, 0.50);

                int pxStart = (int)Math.Floor(centerPx - halfWidthPx);
                int pxEnd = (int)Math.Ceiling(centerPx + halfWidthPx);
                for (int px = Math.Max(0, pxStart); px <= Math.Min(width - 1, pxEnd); px++)
                {
                    double dx = Math.Abs(px + 0.5 - centerPx);
                    if (dx > halfWidthPx + 0.5)
                    {
                        continue;
                    }

                    double half = halfHeightPx * Math.Max(0, 1 - dx / (halfWidthPx + 0.5)) + 0.5;
                    int topY = (int)Math.Floor(centerPy - half);
                    int bottomY = (int)Math.Ceiling(centerPy + half);
                    bool leftSide = px + 0.5 < centerPx;
                    int wallEnd = bottomY + (int)Math.Ceiling(leftSide ? leftWallPx : rightWallPx);
                    uint wallColor = leftSide ? leftWall : rightWall;

                    for (int py = Math.Max(0, topY); py <= Math.Min(height - 1, wallEnd); py++)
                    {
                        pixels[py * width + px] = py <= bottomY ? top : wallColor;
                    }
                }
            }
        }

        return image;
    }

    /// <summary>
    /// Perfis da vista frontal (eixo horizontal = X): para cada coluna, a maior altura em
    /// toda a peça (silhueta) e a altura no corte em Y = sectionY (onde está a ferramenta).
    /// </summary>
    public static void ComputeFrontProfile(HeightMap map, double sectionY, float[] silhouette, float[] section)
    {
        int sectionRow = Math.Clamp((int)Math.Floor((sectionY - map.OriginY) / map.CellSize), 0, map.Rows - 1);
        for (int column = 0; column < map.Columns; column++)
        {
            float max = float.MinValue;
            for (int row = 0; row < map.Rows; row++)
            {
                max = Math.Max(max, map[column, row]);
            }

            silhouette[column] = max;
            section[column] = map[column, sectionRow];
        }
    }

    /// <summary>Perfis da vista lateral (eixo horizontal = Y), com corte em X = sectionX.</summary>
    public static void ComputeSideProfile(HeightMap map, double sectionX, float[] silhouette, float[] section)
    {
        int sectionColumn = Math.Clamp((int)Math.Floor((sectionX - map.OriginX) / map.CellSize), 0, map.Columns - 1);
        for (int row = 0; row < map.Rows; row++)
        {
            float max = float.MinValue;
            for (int column = 0; column < map.Columns; column++)
            {
                max = Math.Max(max, map[column, row]);
            }

            silhouette[row] = max;
            section[row] = map[sectionColumn, row];
        }
    }

    /// <summary>Cor pela profundidade (sem sombreamento). Topo = alumínio; mais fundo = azul → roxo.</summary>
    public static uint DepthColor(HeightMap map, float h)
    {
        double range = map.TopZ - map.BottomZ;
        double depth = (map.TopZ - h) / range;
        if (depth <= 1e-4)
        {
            return StockTopColor;
        }

        // Gradiente: ciano claro (raso) → azul (meio) → roxo (fundo do bruto)
        (double r, double g, double b) = depth < 0.5
            ? Lerp((120, 215, 235), (45, 95, 210), depth / 0.5)
            : Lerp((45, 95, 210), (135, 60, 175), (depth - 0.5) / 0.5);
        return Pack(r, g, b);
    }

    private static uint ShadedColor(HeightMap map, int column, int row, double factor)
    {
        float h = map[column, row];

        // Normal pela diferença entre vizinhos (bordas usam a própria célula)
        float left = map[Math.Max(0, column - 1), row];
        float right = map[Math.Min(map.Columns - 1, column + 1), row];
        float down = map[column, Math.Max(0, row - 1)];
        float up = map[column, Math.Min(map.Rows - 1, row + 1)];
        double dzdx = (right - left) / (2 * map.CellSize);
        double dzdy = (up - down) / (2 * map.CellSize);
        (double nx, double ny, double nz) = Normalize(-dzdx, -dzdy, 1);

        double lambert = Math.Max(0, nx * Light.X + ny * Light.Y + nz * Light.Z);
        double shade = (0.45 + 0.55 * lambert) * factor;
        return Darken(DepthColor(map, h), shade);
    }

    private static uint Darken(uint color, double factor)
    {
        double r = ((color >> 16) & 0xFF) * factor;
        double g = ((color >> 8) & 0xFF) * factor;
        double b = (color & 0xFF) * factor;
        return Pack(r, g, b);
    }

    private static uint Pack(double r, double g, double b)
    {
        static uint Channel(double v) => (uint)Math.Clamp((int)Math.Round(v), 0, 255);
        return 0xFF000000 | (Channel(r) << 16) | (Channel(g) << 8) | Channel(b);
    }

    private static (double, double, double) Lerp((double R, double G, double B) a, (double R, double G, double B) b, double t) =>
        (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

    private static (double X, double Y, double Z) Normalize(double x, double y, double z)
    {
        double length = Math.Sqrt(x * x + y * y + z * z);
        return (x / length, y / length, z / length);
    }
}
