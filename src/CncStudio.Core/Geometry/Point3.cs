namespace CncStudio.Core.Geometry;

/// <summary>Ponto 3D em milímetros, com precisão double.</summary>
public readonly record struct Point3(double X, double Y, double Z)
{
    public static readonly Point3 Origin = new(0, 0, 0);

    /// <summary>Distância euclidiana 3D.</summary>
    public double DistanceTo(Point3 other)
    {
        double dx = other.X - X;
        double dy = other.Y - Y;
        double dz = other.Z - Z;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>Interpolação linear: t = 0 retorna "a", t = 1 retorna "b".</summary>
    public static Point3 Lerp(Point3 a, Point3 b, double t) =>
        new(a.X + (b.X - a.X) * t,
            a.Y + (b.Y - a.Y) * t,
            a.Z + (b.Z - a.Z) * t);
}
