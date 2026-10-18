using CncStudio.Core.Geometry;

namespace CncStudio.Core.Rendering;

/// <summary>
/// Câmera ortográfica fixa. Projeta um ponto 3D em (u, v) em mm (u para a direita, v para cima)
/// e dá a profundidade (maior = mais longe do observador).
///
/// Definida pela direção em que fica o observador ("toViewer") e pelo "para cima" do mundo.
/// O mesmo objeto serve para o sólido, para a trajetória e para a ferramenta: tudo bate.
/// </summary>
public sealed class ViewCamera
{
    private ViewCamera(string name, Point3 toViewer, Point3 worldUp)
    {
        Name = name;
        ToViewer = Normalize(toViewer);
        Right = Normalize(Cross(worldUp, ToViewer));
        Up = Cross(ToViewer, Right);
    }

    public string Name { get; }

    /// <summary>Direção (unitária) do objeto para o observador.</summary>
    public Point3 ToViewer { get; }

    /// <summary>Direção (unitária) do "para a direita" na tela, em coordenadas do mundo.</summary>
    public Point3 Right { get; }

    /// <summary>Direção (unitária) do "para cima" na tela, em coordenadas do mundo.</summary>
    public Point3 Up { get; }

    /// <summary>
    /// Câmera em órbita (fresa, Z para cima): azimute em graus a partir de −Y (frente) girando para +X,
    /// elevação em graus acima do plano XY (90 = de cima).
    /// </summary>
    public static ViewCamera Orbit(double azimuthDegrees, double elevationDegrees)
    {
        double az = azimuthDegrees * Math.PI / 180;
        double el = Math.Clamp(elevationDegrees, -89.5, 89.5) * Math.PI / 180;
        var toViewer = new Point3(Math.Sin(az) * Math.Cos(el), -Math.Cos(az) * Math.Cos(el), Math.Sin(el));
        return new ViewCamera("Órbita", toViewer, new Point3(0, 0, 1));
    }

    // ── Vistas da fresa ──

    /// <summary>De cima: X para a direita, Y para cima.</summary>
    public static ViewCamera Top { get; } = new("Topo", new Point3(0, 0, 1), new Point3(0, 1, 0));

    /// <summary>De frente (observador em Y−): X para a direita, Z para cima.</summary>
    public static ViewCamera Front { get; } = new("Frente", new Point3(0, -1, 0), new Point3(0, 0, 1));

    /// <summary>Do lado direito (observador em X+): Y para a direita, Z para cima.</summary>
    public static ViewCamera Side { get; } = new("Lado", new Point3(1, 0, 0), new Point3(0, 0, 1));

    /// <summary>Isométrica: observador no canto X+ / Y−, acima.</summary>
    public static ViewCamera Iso { get; } = new("Isométrica", new Point3(1, -1, 1), new Point3(0, 0, 1));

    // ── Torno (X = raio para cima, Z para a direita, Y para o observador) ──

    /// <summary>3D do torno: de frente, um pouco de cima e do lado da face (Z+).</summary>
    public static ViewCamera Lathe { get; } = new("Torno 3D", new Point3(0.55, 1, 0.45), new Point3(1, 0, 0));

    /// <summary>Projeção na tela (mm).</summary>
    public (double U, double V) Project(Point3 p) => (Dot(p, Right), Dot(p, Up));

    /// <summary>Profundidade: maior = mais longe do observador.</summary>
    public double Depth(Point3 p) => -Dot(p, ToViewer);

    /// <summary>Retângulo (minU, minV, maxU, maxV) que contém os pontos projetados.</summary>
    public (double MinU, double MinV, double MaxU, double MaxV) ProjectBounds(IEnumerable<Point3> points)
    {
        double minU = double.MaxValue, minV = double.MaxValue, maxU = double.MinValue, maxV = double.MinValue;
        foreach (Point3 p in points)
        {
            (double u, double v) = Project(p);
            minU = Math.Min(minU, u); maxU = Math.Max(maxU, u);
            minV = Math.Min(minV, v); maxV = Math.Max(maxV, v);
        }

        return (minU, minV, maxU, maxV);
    }

    /// <summary>Os 8 cantos de uma caixa.</summary>
    public static IEnumerable<Point3> Corners(Bounds3 b)
    {
        foreach (double x in new[] { b.Min.X, b.Max.X })
        foreach (double y in new[] { b.Min.Y, b.Max.Y })
        foreach (double z in new[] { b.Min.Z, b.Max.Z })
        {
            yield return new Point3(x, y, z);
        }
    }

    internal static double Dot(Point3 a, Point3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    internal static Point3 Cross(Point3 a, Point3 b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    internal static Point3 Normalize(Point3 p)
    {
        double length = Math.Sqrt(Dot(p, p));
        return length < 1e-12 ? p : new Point3(p.X / length, p.Y / length, p.Z / length);
    }
}
