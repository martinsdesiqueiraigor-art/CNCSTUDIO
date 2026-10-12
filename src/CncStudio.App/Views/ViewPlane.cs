using Avalonia;
using CncStudio.Core.Geometry;
using CncStudio.Core.Simulation;

namespace CncStudio.App.Views;

/// <summary>Plano de projeção das vistas 2D.</summary>
public enum ViewPlane
{
    /// <summary>Vista superior: X para a direita, Y para cima.</summary>
    XY,

    /// <summary>Vista frontal: X para a direita, Z para cima.</summary>
    XZ,

    /// <summary>Vista lateral: Y para a direita, Z para cima.</summary>
    YZ,

    /// <summary>Isométrica: mesma projeção da simulação (canto X+ / Y−, de cima).</summary>
    Iso,

    /// <summary>Torno: Z para a direita, X (raio) para cima, como no painel do CNC.</summary>
    Lathe,
}

public static class ViewPlaneExtensions
{
    /// <summary>Projeta um ponto 3D no plano (u = horizontal, v = vertical).</summary>
    public static Point Project(this ViewPlane plane, Point3 p) => plane switch
    {
        ViewPlane.XY => new Point(p.X, p.Y),
        ViewPlane.XZ => new Point(p.X, p.Z),
        ViewPlane.YZ => new Point(p.Y, p.Z),
        ViewPlane.Lathe => new Point(p.Z, p.X),
        _ => new Point(IsoProjection.U(p.X, p.Y), IsoProjection.V(p.X, p.Y, p.Z)),
    };

    /// <summary>
    /// Direção de cada eixo da máquina na vista (u, v), para desenhar o indicador de eixos.
    /// Nas vistas 2D são dois eixos; na isométrica, os três.
    /// </summary>
    public static (string Name, double U, double V)[] AxisDirections(this ViewPlane plane) => plane switch
    {
        ViewPlane.XY => [("X", 1, 0), ("Y", 0, 1)],
        ViewPlane.XZ => [("X", 1, 0), ("Z", 0, 1)],
        ViewPlane.YZ => [("Y", 1, 0), ("Z", 0, 1)],
        ViewPlane.Lathe => [("Z", 1, 0), ("X", 0, 1)],
        _ =>
        [
            ("X", IsoProjection.Cos30, -IsoProjection.Sin30),
            ("Y", IsoProjection.Cos30, IsoProjection.Sin30),
            ("Z", 0, 1),
        ],
    };

    /// <summary>Nome curto da vista para legendas.</summary>
    public static string Caption(this ViewPlane plane) => plane switch
    {
        ViewPlane.XY => "Vista XY",
        ViewPlane.XZ => "Vista XZ",
        ViewPlane.YZ => "Vista YZ",
        ViewPlane.Lathe => "Torno (ZX)",
        _ => "Isométrica",
    };

    /// <summary>Retângulo da caixa 3D projetada no plano.</summary>
    public static Rect ProjectBounds(this ViewPlane plane, Bounds3 bounds)
    {
        // Projeta os 8 cantos da caixa (necessário na isométrica)
        double minU = double.MaxValue, minV = double.MaxValue;
        double maxU = double.MinValue, maxV = double.MinValue;
        foreach (double x in new[] { bounds.Min.X, bounds.Max.X })
        foreach (double y in new[] { bounds.Min.Y, bounds.Max.Y })
        foreach (double z in new[] { bounds.Min.Z, bounds.Max.Z })
        {
            Point p = plane.Project(new Point3(x, y, z));
            minU = Math.Min(minU, p.X);
            maxU = Math.Max(maxU, p.X);
            minV = Math.Min(minV, p.Y);
            maxV = Math.Max(maxV, p.Y);
        }

        return new Rect(minU, minV, maxU - minU, maxV - minV);
    }
}
