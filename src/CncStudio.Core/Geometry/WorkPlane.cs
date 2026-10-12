namespace CncStudio.Core.Geometry;

/// <summary>
/// Plano de trabalho dos arcos. Cada plano tem dois eixos (u, v) e o eixo perpendicular (w),
/// na ordem usada pelos comandos para definir horário/anti-horário olhando do lado positivo de w:
/// G17 = XY (w = Z), G18 = ZX (w = Y), G19 = YZ (w = X).
/// </summary>
public enum WorkPlane
{
    XY,
    ZX,
    YZ,
}

public static class WorkPlaneExtensions
{
    /// <summary>Converte um ponto para coordenadas do plano (u, v, w).</summary>
    public static (double U, double V, double W) ToPlane(this WorkPlane plane, Point3 p) => plane switch
    {
        WorkPlane.XY => (p.X, p.Y, p.Z),
        WorkPlane.ZX => (p.Z, p.X, p.Y),
        _ => (p.Y, p.Z, p.X),
    };

    /// <summary>Converte de volta das coordenadas do plano para X, Y, Z.</summary>
    public static Point3 FromPlane(this WorkPlane plane, double u, double v, double w) => plane switch
    {
        WorkPlane.XY => new Point3(u, v, w),
        WorkPlane.ZX => new Point3(v, w, u),
        _ => new Point3(w, u, v),
    };

    /// <summary>Letras dos endereços de centro (I/J/K) que correspondem a u e v no plano.</summary>
    public static (char U, char V) CenterLetters(this WorkPlane plane) => plane switch
    {
        WorkPlane.XY => ('I', 'J'),
        WorkPlane.ZX => ('K', 'I'),
        _ => ('J', 'K'),
    };

    public static string GCode(this WorkPlane plane) => plane switch
    {
        WorkPlane.XY => "G17",
        WorkPlane.ZX => "G18",
        _ => "G19",
    };
}
