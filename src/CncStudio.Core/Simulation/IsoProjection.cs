using CncStudio.Core.Geometry;

namespace CncStudio.Core.Simulation;

/// <summary>
/// Projeção isométrica usada na vista 3D: observador na frente, à direita e acima
/// (peça vista do canto X+ / Y−). Resultado em mm: u para a direita, v para cima.
/// Profundidade (quem está mais perto do observador) = x − y.
/// </summary>
public static class IsoProjection
{
    public static readonly double Cos30 = Math.Cos(Math.PI / 6);
    public const double Sin30 = 0.5;

    public static double U(double x, double y) => (x + y) * Cos30;

    public static double V(double x, double y, double z) => (y - x) * Sin30 + z;

    public static (double U, double V) Project(Point3 p) => (U(p.X, p.Y), V(p.X, p.Y, p.Z));
}
