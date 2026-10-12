namespace CncStudio.Core.Geometry;

/// <summary>Caixa alinhada aos eixos (envelope) de um conjunto de pontos.</summary>
public readonly record struct Bounds3(Point3 Min, Point3 Max)
{
    /// <summary>Caixa vazia: qualquer ponto incluído passa a ser o envelope.</summary>
    public static readonly Bounds3 Empty = new(
        new Point3(double.PositiveInfinity, double.PositiveInfinity, double.PositiveInfinity),
        new Point3(double.NegativeInfinity, double.NegativeInfinity, double.NegativeInfinity));

    public bool IsEmpty => Min.X > Max.X;

    public double SizeX => IsEmpty ? 0 : Max.X - Min.X;
    public double SizeY => IsEmpty ? 0 : Max.Y - Min.Y;
    public double SizeZ => IsEmpty ? 0 : Max.Z - Min.Z;

    /// <summary>Retorna uma nova caixa que contém também o ponto informado.</summary>
    public Bounds3 Include(Point3 p) => new(
        new Point3(Math.Min(Min.X, p.X), Math.Min(Min.Y, p.Y), Math.Min(Min.Z, p.Z)),
        new Point3(Math.Max(Max.X, p.X), Math.Max(Max.Y, p.Y), Math.Max(Max.Z, p.Z)));
}
