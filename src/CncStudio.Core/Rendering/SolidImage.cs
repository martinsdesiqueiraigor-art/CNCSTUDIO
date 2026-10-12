namespace CncStudio.Core.Rendering;

/// <summary>
/// Imagem do sólido pronta para a tela (BGRA pré-multiplicado, 0 = transparente) e onde ela
/// fica no plano da vista: o pixel (0, 0) é o canto (MinU, MaxV) e cada mm tem PixelsPerMm pixels.
/// </summary>
public sealed class SolidImage(int width, int height, double pixelsPerMm, double minU, double maxV, uint[] pixels)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public double PixelsPerMm { get; } = pixelsPerMm;
    public double MinU { get; } = minU;
    public double MaxV { get; } = maxV;
    public uint[] Pixels { get; } = pixels;

    public double WidthMm => Width / PixelsPerMm;
    public double HeightMm => Height / PixelsPerMm;
}
