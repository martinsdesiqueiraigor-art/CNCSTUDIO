using CncStudio.Core.Geometry;

namespace CncStudio.Core.Rendering;

/// <summary>
/// Rasterizador por software: triângulos com cor sólida, teste de profundidade (z-buffer) e
/// iluminação simples (ambiente + difusa + um brilho especular). Sem GPU e sem bibliotecas:
/// roda em segundo plano e devolve só os pixels.
/// </summary>
public sealed class Raster3D
{
    private readonly ViewCamera _camera;
    private readonly double _minU;
    private readonly double _maxV;
    private readonly double _ppm;
    private readonly float[] _depth;
    private readonly Point3 _light;
    private readonly Point3 _half;

    public Raster3D(ViewCamera camera, double minU, double maxV, double pixelsPerMm, int width, int height)
    {
        _camera = camera;
        _minU = minU;
        _maxV = maxV;
        _ppm = pixelsPerMm;
        Width = width;
        Height = height;
        Pixels = new uint[width * height];
        _depth = new float[width * height];
        Array.Fill(_depth, float.MaxValue);

        // Luz vinda de cima, da esquerda e de trás do observador
        _light = ViewCamera.Normalize(Add(Add(Scale(camera.ToViewer, 0.8), Scale(camera.Up, 0.55)), Scale(camera.Right, -0.35)));
        _half = ViewCamera.Normalize(Add(_light, camera.ToViewer));
    }

    public int Width { get; }
    public int Height { get; }
    public uint[] Pixels { get; }

    /// <summary>Quantos triângulos foram desenhados (diagnóstico/testes).</summary>
    public int TriangleCount { get; private set; }

    /// <summary>Quadrilátero plano (a, b, c, d em ordem) com a cor iluminada pela normal.</summary>
    public void Quad(Point3 a, Point3 b, Point3 c, Point3 d, Point3 normal, uint rgb)
    {
        uint color = Shade(rgb, normal);
        Triangle(a, b, c, color);
        Triangle(a, c, d, color);
    }

    /// <summary>Cor final (ARGB opaco) de uma superfície com essa normal.</summary>
    public uint Shade(uint rgb, Point3 normal)
    {
        Point3 n = ViewCamera.Normalize(normal);
        if (ViewCamera.Dot(n, _camera.ToViewer) < 0)
        {
            n = Scale(n, -1); // superfície vista por trás: ilumina o lado visível
        }

        double diffuse = Math.Max(0, ViewCamera.Dot(n, _light));
        double specular = Math.Pow(Math.Max(0, ViewCamera.Dot(n, _half)), 28) * 0.22;
        double intensity = 0.34 + 0.66 * diffuse;

        int r = Channel((rgb >> 16) & 0xFF, intensity, specular);
        int g = Channel((rgb >> 8) & 0xFF, intensity, specular);
        int b = Channel(rgb & 0xFF, intensity, specular);
        return 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | (uint)b;
    }

    /// <summary>Triângulo com cor sólida e z-buffer.</summary>
    public void Triangle(Point3 a, Point3 b, Point3 c, uint color)
    {
        (double ax, double ay, double az) = ToPixel(a);
        (double bx, double by, double bz) = ToPixel(b);
        (double cx, double cy, double cz) = ToPixel(c);

        double area = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
        if (Math.Abs(area) < 1e-9)
        {
            return; // triângulo de canto (degenerado na tela)
        }

        int minX = Math.Max(0, (int)Math.Floor(Math.Min(ax, Math.Min(bx, cx))));
        int maxX = Math.Min(Width - 1, (int)Math.Ceiling(Math.Max(ax, Math.Max(bx, cx))));
        int minY = Math.Max(0, (int)Math.Floor(Math.Min(ay, Math.Min(by, cy))));
        int maxY = Math.Min(Height - 1, (int)Math.Ceiling(Math.Max(ay, Math.Max(by, cy))));
        if (minX > maxX || minY > maxY)
        {
            return;
        }

        TriangleCount++;
        double inverseArea = 1 / area;
        for (int py = minY; py <= maxY; py++)
        {
            double sy = py + 0.5;
            int row = py * Width;
            for (int px = minX; px <= maxX; px++)
            {
                double sx = px + 0.5;
                double w0 = ((bx - sx) * (cy - sy) - (by - sy) * (cx - sx)) * inverseArea;
                double w1 = ((cx - sx) * (ay - sy) - (cy - sy) * (ax - sx)) * inverseArea;
                double w2 = 1 - w0 - w1;
                const double edge = -1e-6; // inclui as bordas: sem frestas entre triângulos vizinhos
                if (w0 < edge || w1 < edge || w2 < edge)
                {
                    continue;
                }

                float z = (float)(w0 * az + w1 * bz + w2 * cz);
                int index = row + px;
                if (z < _depth[index])
                {
                    _depth[index] = z;
                    Pixels[index] = color;
                }
            }
        }
    }

    /// <summary>Profundidade guardada no pixel (float.MaxValue = vazio).</summary>
    public float DepthAt(int x, int y) => _depth[y * Width + x];

    private (double X, double Y, double Z) ToPixel(Point3 p)
    {
        (double u, double v) = _camera.Project(p);
        return ((u - _minU) * _ppm, (_maxV - v) * _ppm, _camera.Depth(p));
    }

    private static int Channel(uint value, double intensity, double specular) =>
        (int)Math.Clamp(value * intensity + 255 * specular, 0, 255);

    private static Point3 Add(Point3 a, Point3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    private static Point3 Scale(Point3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
}
