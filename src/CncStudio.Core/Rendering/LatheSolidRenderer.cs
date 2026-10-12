using CncStudio.Core.Geometry;
using CncStudio.Core.Simulation;

namespace CncStudio.Core.Rendering;

/// <summary>
/// Peça do torno em 3D: o perfil de revolução da <see cref="TurningStock"/> girado em volta de Z,
/// com um quarto removido (meia seção) para mostrar o perfil e o furo por dentro.
/// Barra bruta em bege, superfícies usinadas em cinza-azulado, corte em laranja-claro.
/// </summary>
public static class LatheSolidRenderer
{
    private const uint StockColor = 0xD9BC8C;
    private const uint MachinedColor = 0xB9C6D3;
    private const uint SectionColor = 0xE8A866;
    private const int Segments = 60;              // divisões da volta completa

    /// <summary>
    /// Renderiza com no máximo "maxPixels" no lado maior. Com "quarterSection", tira o quarto entre
    /// +X (cima) e +Y (observador) e mostra as faces cortadas.
    /// </summary>
    public static SolidImage Render(TurningStock stock, ViewCamera camera, bool quarterSection = true, int maxPixels = 1200)
    {
        double r = stock.Radius;
        var box = Bounds3.Empty.Include(new Point3(-r, -r, stock.MinZ)).Include(new Point3(r, r, stock.MaxZ));
        (double minU, double minV, double maxU, double maxV) = camera.ProjectBounds(ViewCamera.Corners(box));
        double widthMm = Math.Max(maxU - minU, 1e-3);
        double heightMm = Math.Max(maxV - minV, 1e-3);
        double ppm = Math.Min(maxPixels / widthMm, maxPixels / heightMm);
        int width = Math.Max(1, (int)Math.Ceiling(widthMm * ppm));
        int height = Math.Max(1, (int)Math.Ceiling(heightMm * ppm));

        var raster = new Raster3D(camera, minU, maxV, ppm, width, height);
        Draw(stock, raster, quarterSection);
        return new SolidImage(width, height, ppm, minU, maxV, raster.Pixels);
    }

    internal static void Draw(TurningStock stock, Raster3D raster, bool quarterSection)
    {
        // Ângulos desenhados: volta inteira, ou de 90° a 360° (sem o quarto de cima/frente)
        double start = quarterSection ? Math.PI / 2 : 0;
        int segments = quarterSection ? Segments * 3 / 4 : Segments;
        double step = (2 * Math.PI - start) / segments;

        ReadOnlySpan<float> outer = stock.Outer;
        ReadOnlySpan<float> inner = stock.Inner;
        int n = stock.Slices;
        double length = stock.SliceLength;

        // Trechos de fatias com o mesmo perfil viram um cilindro só
        int first = 0;
        for (int i = 1; i <= n; i++)
        {
            if (i < n && outer[i] == outer[first] && inner[i] == inner[first])
            {
                continue;
            }

            double z0 = stock.MinZ + first * length;
            double z1 = stock.MinZ + i * length;
            float ro = outer[first], ri = inner[first];
            if (ro > ri)
            {
                uint color = ro < stock.InitialOuter - 1e-4 ? MachinedColor : StockColor;
                Cylinder(raster, z0, z1, ro, start, step, segments, color, outward: true);
                if (ri > 0)
                {
                    Cylinder(raster, z0, z1, ri, start, step, segments, MachinedColor, outward: false);
                }

                if (quarterSection)
                {
                    // Faces cortadas: plano θ = 0 (X+, Y = 0) e θ = 90° (Y+, X = 0)
                    raster.Quad(new Point3(ri, 0, z0), new Point3(ro, 0, z0), new Point3(ro, 0, z1), new Point3(ri, 0, z1),
                        new Point3(0, 1, 0), SectionColor);
                    raster.Quad(new Point3(0, ri, z0), new Point3(0, ro, z0), new Point3(0, ro, z1), new Point3(0, ri, z1),
                        new Point3(1, 0, 0), SectionColor);
                }
            }

            first = i;
        }

        // Faces planas (anéis) onde o perfil muda e nas pontas
        for (int i = 0; i <= n; i++)
        {
            float leftOuter = i > 0 ? outer[i - 1] : 0, leftInner = i > 0 ? inner[i - 1] : 0;
            float rightOuter = i < n ? outer[i] : 0, rightInner = i < n ? inner[i] : 0;
            if (leftOuter <= leftInner) { leftOuter = 0; leftInner = 0; }
            if (rightOuter <= rightInner) { rightOuter = 0; rightInner = 0; }
            double z = stock.MinZ + i * length;

            // Anel externo: entre os dois raios externos; anel do furo: entre os dois furos
            Ring(raster, z, Math.Min(leftOuter, rightOuter), Math.Max(leftOuter, rightOuter), start, step, segments, leftOuter > rightOuter ? 1 : -1, stock);
            Ring(raster, z, Math.Min(leftInner, rightInner), Math.Max(leftInner, rightInner), start, step, segments, leftInner < rightInner ? 1 : -1, stock);
        }
    }

    private static void Cylinder(Raster3D raster, double z0, double z1, double radius, double start, double step, int segments, uint color, bool outward)
    {
        for (int k = 0; k < segments; k++)
        {
            double a0 = start + k * step, a1 = a0 + step, mid = a0 + step / 2;
            var p0 = new Point3(radius * Math.Cos(a0), radius * Math.Sin(a0), z0);
            var p1 = new Point3(radius * Math.Cos(a1), radius * Math.Sin(a1), z0);
            var p2 = new Point3(radius * Math.Cos(a1), radius * Math.Sin(a1), z1);
            var p3 = new Point3(radius * Math.Cos(a0), radius * Math.Sin(a0), z1);
            double sign = outward ? 1 : -1;
            raster.Quad(p0, p1, p2, p3, new Point3(sign * Math.Cos(mid), sign * Math.Sin(mid), 0), color);
        }
    }

    private static void Ring(Raster3D raster, double z, double r0, double r1, double start, double step, int segments, int direction, TurningStock stock)
    {
        if (r1 - r0 < 1e-6)
        {
            return;
        }

        uint color = r1 < stock.InitialOuter - 1e-4 || (z > stock.MinZ + 1e-6 && z < stock.MaxZ - 1e-6) ? MachinedColor : StockColor;
        var normal = new Point3(0, 0, direction);
        for (int k = 0; k < segments; k++)
        {
            double a0 = start + k * step, a1 = a0 + step;
            raster.Quad(
                new Point3(r0 * Math.Cos(a0), r0 * Math.Sin(a0), z), new Point3(r1 * Math.Cos(a0), r1 * Math.Sin(a0), z),
                new Point3(r1 * Math.Cos(a1), r1 * Math.Sin(a1), z), new Point3(r0 * Math.Cos(a1), r0 * Math.Sin(a1), z),
                normal, color);
        }
    }
}
