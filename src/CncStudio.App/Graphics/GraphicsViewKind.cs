using Avalonia;
using CncStudio.Core.Geometry;
using CncStudio.Core.Rendering;

namespace CncStudio.App.Graphics;

/// <summary>Vistas da área gráfica (fixas, sem girar com o mouse).</summary>
public enum GraphicsViewKind
{
    /// <summary>Fresa: 3D livre pela placa de vídeo (gira com o mouse).</summary>
    Orbit3D,

    Iso,
    Top,
    Front,
    Side,

    /// <summary>Torno: peça 3D por revolução (meia seção).</summary>
    Lathe3D,

    /// <summary>Torno: plano ZX (Z para a direita, X/raio para cima), como no painel do CNC.</summary>
    LatheZX,
}

public static class GraphicsViews
{
    public static bool IsLathe(this GraphicsViewKind kind) => kind is GraphicsViewKind.Lathe3D or GraphicsViewKind.LatheZX;

    /// <summary>Câmera 3D da vista (null no plano ZX do torno, que é 2D).</summary>
    public static ViewCamera? Camera(this GraphicsViewKind kind) => kind switch
    {
        GraphicsViewKind.Iso or GraphicsViewKind.Orbit3D => ViewCamera.Iso,
        GraphicsViewKind.Top => ViewCamera.Top,
        GraphicsViewKind.Front => ViewCamera.Front,
        GraphicsViewKind.Side => ViewCamera.Side,
        GraphicsViewKind.Lathe3D => ViewCamera.Lathe,
        _ => null,
    };

    /// <summary>Ponto 3D → plano da vista (mm; u para a direita, v para cima).</summary>
    public static Point Project(this GraphicsViewKind kind, Point3 p)
    {
        if (kind.Camera() is { } camera)
        {
            (double u, double v) = camera.Project(p);
            return new Point(u, v);
        }

        return new Point(p.Z, p.X);
    }

    public static (double U, double V) ProjectTuple(this GraphicsViewKind kind, Point3 p)
    {
        Point q = kind.Project(p);
        return (q.X, q.Y);
    }

    /// <summary>Retângulo da caixa projetada.</summary>
    public static Rect ProjectBounds(this GraphicsViewKind kind, Bounds3 bounds)
    {
        double minU = double.MaxValue, minV = double.MaxValue, maxU = double.MinValue, maxV = double.MinValue;
        foreach (Point3 corner in ViewCamera.Corners(bounds))
        {
            Point p = kind.Project(corner);
            minU = Math.Min(minU, p.X); maxU = Math.Max(maxU, p.X);
            minV = Math.Min(minV, p.Y); maxV = Math.Max(maxV, p.Y);
        }

        return new Rect(minU, minV, maxU - minU, maxV - minV);
    }

    /// <summary>Eixos para o indicador no canto (direção na tela de cada eixo visível).</summary>
    public static (string Name, double U, double V)[] Axes(this GraphicsViewKind kind)
    {
        var axes = new List<(string, double, double)>();
        foreach ((string name, Point3 axis) in new[] { ("X", new Point3(1, 0, 0)), ("Y", new Point3(0, 1, 0)), ("Z", new Point3(0, 0, 1)) })
        {
            if (kind.IsLathe() && name == "Y")
            {
                continue;
            }

            Point p = kind.Project(axis);
            if (Math.Abs(p.X) + Math.Abs(p.Y) > 0.05)
            {
                axes.Add((name, p.X, p.Y));
            }
        }

        return axes.ToArray();
    }

    public static string Caption(this GraphicsViewKind kind, bool halfSection = true) => kind switch
    {
        GraphicsViewKind.Orbit3D => "3D",
        GraphicsViewKind.Iso => "Isométrica",
        GraphicsViewKind.Top => "Topo (XY)",
        GraphicsViewKind.Front => "Frente (XZ)",
        GraphicsViewKind.Side => "Lado (YZ)",
        GraphicsViewKind.Lathe3D => halfSection ? "Torno 3D (meia seção)" : "Torno 3D",
        _ => "Torno ZX",
    };
}
