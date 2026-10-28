using CncStudio.Core.Geometry;
using CncStudio.Core.Rendering;
using CncStudio.Core.Scene;

namespace CncStudio.Core.Cam3D;

/// <summary>
/// Modelo 3D da peça como triângulos (vem de um STL). Base do CAM 3D: aqui só leitura, medidas e
/// desenho; o percurso 3D é outra etapa. Unidade: mm (o STL não diz a unidade; ver <see cref="Scaled"/>).
/// </summary>
public sealed class TriangleMesh
{
    private readonly Point3[] _vertices; // 3 por triângulo

    public TriangleMesh(IEnumerable<(Point3 A, Point3 B, Point3 C)> triangles)
    {
        var list = new List<Point3>();
        foreach ((Point3 a, Point3 b, Point3 c) in triangles)
        {
            list.Add(a);
            list.Add(b);
            list.Add(c);
        }

        _vertices = [.. list];
        Bounds = Bounds3.Empty;
        foreach (Point3 p in _vertices)
        {
            Bounds = Bounds.Include(p);
        }
    }

    public int TriangleCount => _vertices.Length / 3;

    /// <summary>Caixa que envolve o modelo (vazia se não há triângulos).</summary>
    public Bounds3 Bounds { get; }

    public (Point3 A, Point3 B, Point3 C) Triangle(int index) =>
        (_vertices[index * 3], _vertices[(index * 3) + 1], _vertices[(index * 3) + 2]);

    /// <summary>Tamanho X, Y, Z em mm.</summary>
    public Point3 Size => TriangleCount == 0 ? Point3.Origin : Bounds.Max - Bounds.Min;

    /// <summary>Área total da superfície, em mm².</summary>
    public double SurfaceArea
    {
        get
        {
            double sum = 0;
            for (int i = 0; i < TriangleCount; i++)
            {
                (Point3 a, Point3 b, Point3 c) = Triangle(i);
                Point3 n = ViewCamera.Cross(b - a, c - a);
                sum += 0.5 * Math.Sqrt((n.X * n.X) + (n.Y * n.Y) + (n.Z * n.Z));
            }

            return sum;
        }
    }

    /// <summary>
    /// Volume em mm³ (válido se a malha é fechada e com os triângulos orientados para fora; senão é só uma estimativa).
    /// </summary>
    public double Volume
    {
        get
        {
            double sum = 0;
            for (int i = 0; i < TriangleCount; i++)
            {
                (Point3 a, Point3 b, Point3 c) = Triangle(i);
                sum += ViewCamera.Dot(a, ViewCamera.Cross(b, c)) / 6.0;
            }

            return Math.Abs(sum);
        }
    }

    /// <summary>
    /// A malha é fechada? Cada aresta aparece em exatamente dois triângulos (vértices iguais até 1e-6 mm).
    /// Malha aberta ainda serve para ver, mas o volume e o CAM 3D ficam incertos.
    /// </summary>
    public bool IsClosed
    {
        get
        {
            if (TriangleCount == 0)
            {
                return false;
            }

            var edges = new Dictionary<(long, long, long, long, long, long), int>();
            for (int i = 0; i < TriangleCount; i++)
            {
                (Point3 a, Point3 b, Point3 c) = Triangle(i);
                foreach ((Point3 p, Point3 q) in new[] { (a, b), (b, c), (c, a) })
                {
                    (long, long, long) kp = Key(p), kq = Key(q);
                    var key = Compare(kp, kq) <= 0
                        ? (kp.Item1, kp.Item2, kp.Item3, kq.Item1, kq.Item2, kq.Item3)
                        : (kq.Item1, kq.Item2, kq.Item3, kp.Item1, kp.Item2, kp.Item3);
                    edges[key] = edges.GetValueOrDefault(key) + 1;
                }
            }

            return edges.Values.All(count => count == 2);
        }
    }

    /// <summary>Cópia com todas as medidas multiplicadas (ex.: 25,4 se o STL estava em polegadas; 1000 se em metros).</summary>
    public TriangleMesh Scaled(double factor) => Map(p => new Point3(p.X * factor, p.Y * factor, p.Z * factor));

    /// <summary>Cópia deslocada.</summary>
    public TriangleMesh Translated(double dx, double dy, double dz) => Map(p => new Point3(p.X + dx, p.Y + dy, p.Z + dz));

    /// <summary>Cópia com o canto mínimo na origem em X e Y e o topo em Z = 0 (convenção do CAM: Z0 no topo da peça).</summary>
    public TriangleMesh PlacedTopAtZero() =>
        TriangleCount == 0 ? this : Translated(-Bounds.Min.X, -Bounds.Min.Y, -Bounds.Max.Z);

    private TriangleMesh Map(Func<Point3, Point3> f)
    {
        var triangles = new List<(Point3, Point3, Point3)>(TriangleCount);
        for (int i = 0; i < TriangleCount; i++)
        {
            (Point3 a, Point3 b, Point3 c) = Triangle(i);
            triangles.Add((f(a), f(b), f(c)));
        }

        return new TriangleMesh(triangles);
    }

    private static (long, long, long) Key(Point3 p) =>
        ((long)Math.Round(p.X * 1e6), (long)Math.Round(p.Y * 1e6), (long)Math.Round(p.Z * 1e6));

    private static int Compare((long, long, long) a, (long, long, long) b) =>
        a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1) : a.Item2 != b.Item2 ? a.Item2.CompareTo(b.Item2) : a.Item3.CompareTo(b.Item3);

    /// <summary>Malha só com os triângulos de índice dado (ou, com <paramref name="invert"/>, com todos os outros).</summary>
    public TriangleMesh Subset(IEnumerable<int> indices, bool invert = false)
    {
        var chosen = new HashSet<int>(indices);
        var list = new List<(Point3, Point3, Point3)>();
        for (int i = 0; i < TriangleCount; i++)
        {
            if (chosen.Contains(i) != invert)
            {
                list.Add(Triangle(i));
            }
        }

        return new TriangleMesh(list);
    }

    /// <summary>Malha para a placa de vídeo (cada triângulo com a normal dele, cor do material).</summary>
    public SolidMesh ToSolidMesh(uint rgb = 0xB8C4D0)
    {
        SolidMesh mesh = SolidMesh.Create(Math.Max(16, TriangleCount));
        Span<Point3> tri = stackalloc Point3[3];
        for (int i = 0; i < TriangleCount; i++)
        {
            (Point3 a, Point3 b, Point3 c) = Triangle(i);
            Point3 n = ViewCamera.Cross(b - a, c - a);
            if (Math.Abs(n.X) + Math.Abs(n.Y) + Math.Abs(n.Z) < 1e-12)
            {
                continue; // triângulo degenerado
            }

            tri[0] = a;
            tri[1] = b;
            tri[2] = c;
            mesh.AddPolygon(tri, n, rgb);
        }

        return mesh;
    }

    /// <summary>Foto de cena só com o modelo, para a mesma tela 3D da simulação.</summary>
    public SceneSnapshot ToSnapshot(ScenePalette? palette = null)
    {
        var objects = new List<SceneObjectState>
        {
            new("modelo/0", SceneRole.Material, ToSolidMesh(), 1, Point3.Origin, true),
        };
        return new SceneSnapshot(objects, [], Bounds, palette ?? ScenePalette.Realistic);
    }
}
