using CncStudio.Core.Geometry;

namespace CncStudio.Core.Cam3D;

/// <summary>
/// "Drop cutter" para fresa esférica: dado um ponto (X, Y), acha a menor altura da ponta da ferramenta
/// em que a esfera não penetra o modelo (testa as faces, as arestas e os vértices dos triângulos).
/// Considera só a esfera (a haste não é verificada). Triângulos ficam numa grade para ser rápido.
/// </summary>
public sealed class DropCutter
{
    private readonly TriangleMesh _mesh;
    private readonly double _r;
    private readonly double _cell;
    private readonly double _minX, _minY;
    private readonly int _cols, _rows;
    private readonly List<int>?[] _grid;

    public DropCutter(TriangleMesh mesh, double ballRadius)
    {
        if (ballRadius <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ballRadius), "O raio precisa ser maior que zero.");
        }

        _mesh = mesh;
        _r = ballRadius;
        _minX = mesh.Bounds.Min.X;
        _minY = mesh.Bounds.Min.Y;
        Point3 size = mesh.Size;
        _cell = Math.Max(Math.Max(ballRadius, 0.5), Math.Max(size.X, size.Y) / 128);
        _cols = (int)(size.X / _cell) + 1;
        _rows = (int)(size.Y / _cell) + 1;
        _grid = new List<int>?[_cols * _rows];
        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            (Point3 a, Point3 b, Point3 c) = mesh.Triangle(i);
            int c0 = Col(Math.Min(a.X, Math.Min(b.X, c.X))), c1 = Col(Math.Max(a.X, Math.Max(b.X, c.X)));
            int r0 = Row(Math.Min(a.Y, Math.Min(b.Y, c.Y))), r1 = Row(Math.Max(a.Y, Math.Max(b.Y, c.Y)));
            for (int row = r0; row <= r1; row++)
            {
                for (int col = c0; col <= c1; col++)
                {
                    (_grid[(row * _cols) + col] ??= []).Add(i);
                }
            }
        }
    }

    private int Col(double x) => Math.Clamp((int)Math.Floor((x - _minX) / _cell), 0, _cols - 1);

    private int Row(double y) => Math.Clamp((int)Math.Floor((y - _minY) / _cell), 0, _rows - 1);

    /// <summary>Altura mínima da ponta (Z) em (x, y), ou null se nenhum triângulo fica ao alcance da esfera.</summary>
    public double? TipHeight(double x, double y)
    {
        int c0 = Col(x - _r), c1 = Col(x + _r), r0 = Row(y - _r), r1 = Row(y + _r);
        if (x + _r < _minX || x - _r > _minX + (_cols * _cell) || y + _r < _minY || y - _r > _minY + (_rows * _cell))
        {
            return null;
        }

        double best = double.NegativeInfinity;
        var seen = new HashSet<int>();
        for (int row = r0; row <= r1; row++)
        {
            for (int col = c0; col <= c1; col++)
            {
                if (_grid[(row * _cols) + col] is not { } list)
                {
                    continue;
                }

                foreach (int t in list)
                {
                    if (seen.Add(t))
                    {
                        best = Math.Max(best, CenterHeight(t, x, y));
                    }
                }
            }
        }

        return double.IsNegativeInfinity(best) ? null : best - _r;
    }

    /// <summary>Altura do centro da esfera que encosta no triângulo (−∞ se ele não limita nesse ponto).</summary>
    private double CenterHeight(int index, double x, double y)
    {
        (Point3 a, Point3 b, Point3 c) = _mesh.Triangle(index);
        double best = double.NegativeInfinity;

        // Vértices
        foreach (Point3 v in new[] { a, b, c })
        {
            double dx = x - v.X, dy = y - v.Y, d2 = (dx * dx) + (dy * dy);
            if (d2 <= _r * _r)
            {
                best = Math.Max(best, v.Z + Math.Sqrt((_r * _r) - d2));
            }
        }

        // Arestas
        best = Math.Max(best, EdgeHeight(a, b, x, y));
        best = Math.Max(best, EdgeHeight(b, c, x, y));
        best = Math.Max(best, EdgeHeight(c, a, x, y));

        // Face (só as que olham para cima limitam por baixo da esfera)
        Point3 n = Cross(b - a, c - a);
        double len = Math.Sqrt((n.X * n.X) + (n.Y * n.Y) + (n.Z * n.Z));
        if (len > 1e-12)
        {
            n = new Point3(n.X / len, n.Y / len, n.Z / len);
            if (n.Z < 0)
            {
                n = new Point3(-n.X, -n.Y, -n.Z); // triângulo com a normal invertida: usa o lado de cima
            }

            if (n.Z > 1e-9)
            {
                double d = (n.X * a.X) + (n.Y * a.Y) + (n.Z * a.Z);
                double cz = (d + _r - (n.X * x) - (n.Y * y)) / n.Z;
                // ponto de contato = centro − R·n; precisa cair dentro do triângulo (no plano XY)
                double px = x - (_r * n.X), py = y - (_r * n.Y);
                if (InsideXY(a, b, c, px, py))
                {
                    best = Math.Max(best, cz);
                }
            }
        }

        return best;
    }

    private double EdgeHeight(Point3 p0, Point3 p1, double x, double y)
    {
        Point3 e = p1 - p0;
        double length = Math.Sqrt((e.X * e.X) + (e.Y * e.Y) + (e.Z * e.Z));
        if (length < 1e-12)
        {
            return double.NegativeInfinity;
        }

        var u = new Point3(e.X / length, e.Y / length, e.Z / length);
        // distância² do centro (x, y, cz) à reta: |w|² − (w·u)², com w = centro − p0, w = (wx, wy, cz − p0.Z)
        double wx = x - p0.X, wy = y - p0.Y;
        // Em função de s = cz − p0.Z: |w|² = wx²+wy²+s²; w·u = wx·ux + wy·uy + s·uz
        double k = (wx * u.X) + (wy * u.Y);
        double qa = 1 - (u.Z * u.Z);
        if (qa < 1e-12)
        {
            return double.NegativeInfinity; // aresta vertical: já coberta pelos vértices
        }

        double qb = -2 * k * u.Z;
        double qc = (wx * wx) + (wy * wy) - (k * k) - (_r * _r);
        double disc = (qb * qb) - (4 * qa * qc);
        if (disc < 0)
        {
            return double.NegativeInfinity;
        }

        double s = (-qb + Math.Sqrt(disc)) / (2 * qa);
        double t = k + (s * u.Z); // posição do ponto de contato ao longo da aresta
        return t >= 0 && t <= length ? p0.Z + s : double.NegativeInfinity;
    }

    private static Point3 Cross(Point3 a, Point3 b) =>
        new((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));

    private static bool InsideXY(Point3 a, Point3 b, Point3 c, double x, double y)
    {
        double d1 = ((x - b.X) * (a.Y - b.Y)) - ((a.X - b.X) * (y - b.Y));
        double d2 = ((x - c.X) * (b.Y - c.Y)) - ((b.X - c.X) * (y - c.Y));
        double d3 = ((x - a.X) * (c.Y - a.Y)) - ((c.X - a.X) * (y - a.Y));
        bool neg = d1 < -1e-12 || d2 < -1e-12 || d3 < -1e-12;
        bool pos = d1 > 1e-12 || d2 > 1e-12 || d3 > 1e-12;
        return !(neg && pos);
    }
}
