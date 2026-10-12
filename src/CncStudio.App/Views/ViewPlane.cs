using Avalonia;
using CncStudio.Core.Geometry;

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
}

public static class ViewPlaneExtensions
{
    /// <summary>Projeta um ponto 3D no plano (u = horizontal, v = vertical).</summary>
    public static Point Project(this ViewPlane plane, Point3 p) => plane switch
    {
        ViewPlane.XY => new Point(p.X, p.Y),
        ViewPlane.XZ => new Point(p.X, p.Z),
        _ => new Point(p.Y, p.Z),
    };

    /// <summary>Nomes dos eixos horizontal e vertical.</summary>
    public static (string Horizontal, string Vertical) AxisNames(this ViewPlane plane) => plane switch
    {
        ViewPlane.XY => ("X", "Y"),
        ViewPlane.XZ => ("X", "Z"),
        _ => ("Y", "Z"),
    };

    /// <summary>Retângulo da caixa 3D projetada no plano.</summary>
    public static Rect ProjectBounds(this ViewPlane plane, Bounds3 bounds)
    {
        Point min = plane.Project(bounds.Min);
        Point max = plane.Project(bounds.Max);
        return new Rect(min, max);
    }
}
