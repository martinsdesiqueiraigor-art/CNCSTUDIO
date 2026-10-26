namespace CncStudio.Core.Cam;

/// <summary>Ponto no plano XY (unidades da peça, normalmente mm).</summary>
public readonly record struct Point2(double X, double Y)
{
    public static Point2 operator +(Point2 a, Point2 b) => new(a.X + b.X, a.Y + b.Y);

    public static Point2 operator -(Point2 a, Point2 b) => new(a.X - b.X, a.Y - b.Y);

    public static Point2 operator *(Point2 a, double k) => new(a.X * k, a.Y * k);

    public double Length => Math.Sqrt((X * X) + (Y * Y));

    public double DistanceTo(Point2 other) => (this - other).Length;

    public double Dot(Point2 other) => (X * other.X) + (Y * other.Y);

    public double Cross(Point2 other) => (X * other.Y) - (Y * other.X);

    public Point2 Normalized()
    {
        double length = Length;
        return length < 1e-15 ? this : new Point2(X / length, Y / length);
    }
}

/// <summary>
/// Um trecho de um contorno: reta ou arco. No arco, <see cref="Center"/> e o sentido (<see cref="Ccw"/>, anti-horário)
/// valem; início igual ao fim num arco significa círculo completo.
/// </summary>
public readonly record struct Segment2(Point2 Start, Point2 End, Point2 Center = default, bool IsArc = false, bool Ccw = true)
{
    public static Segment2 Line(Point2 start, Point2 end) => new(start, end);

    public static Segment2 Arc(Point2 start, Point2 end, Point2 center, bool ccw) => new(start, end, center, true, ccw);

    public double Radius => IsArc ? Center.DistanceTo(Start) : 0;

    /// <summary>Ângulo girado pelo arco (rad, positivo = anti-horário). Zero para reta.</summary>
    public double Sweep
    {
        get
        {
            if (!IsArc)
            {
                return 0;
            }

            double a0 = Math.Atan2(Start.Y - Center.Y, Start.X - Center.X);
            double a1 = Math.Atan2(End.Y - Center.Y, End.X - Center.X);
            double sweep = a1 - a0;
            if (Ccw)
            {
                while (sweep <= 1e-12)
                {
                    sweep += 2 * Math.PI;
                }

                while (sweep > 2 * Math.PI + 1e-12)
                {
                    sweep -= 2 * Math.PI;
                }
            }
            else
            {
                while (sweep >= -1e-12)
                {
                    sweep -= 2 * Math.PI;
                }

                while (sweep < -2 * Math.PI - 1e-12)
                {
                    sweep += 2 * Math.PI;
                }
            }

            return sweep;
        }
    }

    public double Length => IsArc ? Radius * Math.Abs(Sweep) : Start.DistanceTo(End);

    /// <summary>Direção de percurso (vetor unitário) no ponto dado do arco ou da reta.</summary>
    public Point2 TangentAt(Point2 point)
    {
        if (!IsArc)
        {
            return (End - Start).Normalized();
        }

        Point2 radial = point - Center;
        Point2 tangent = Ccw ? new Point2(-radial.Y, radial.X) : new Point2(radial.Y, -radial.X);
        return tangent.Normalized();
    }

    public Point2 StartTangent => TangentAt(Start);

    public Point2 EndTangent => TangentAt(End);

    /// <summary>Contribuição do trecho para a área com sinal (fórmula de Green; positivo = contorno anti-horário).</summary>
    public double AreaTerm()
    {
        if (!IsArc)
        {
            return Start.Cross(End) / 2;
        }

        double r = Radius;
        double sweep = Sweep;
        double t0 = Math.Atan2(Start.Y - Center.Y, Start.X - Center.X);
        double t1 = t0 + sweep;
        double term = (r * r * sweep) + (r * ((Center.X * (Math.Sin(t1) - Math.Sin(t0))) - (Center.Y * (Math.Cos(t1) - Math.Cos(t0)))));
        return term / 2;
    }

    /// <summary>Pontos ao longo do trecho (inclui o início e o fim), com a flecha máxima dada.</summary>
    public IEnumerable<Point2> Sample(double tolerance = 0.01)
    {
        yield return Start;
        if (IsArc)
        {
            double r = Radius;
            double sweep = Sweep;
            double step = r <= tolerance ? Math.PI / 2 : 2 * Math.Acos(Math.Max(0, 1 - (tolerance / r)));
            step = Math.Clamp(step, 0.02, Math.PI / 2);
            int count = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweep) / step));
            double t0 = Math.Atan2(Start.Y - Center.Y, Start.X - Center.X);
            for (int i = 1; i < count; i++)
            {
                double t = t0 + (sweep * i / count);
                yield return new Point2(Center.X + (r * Math.Cos(t)), Center.Y + (r * Math.Sin(t)));
            }
        }

        yield return End;
    }
}
