using CncStudio.Core.Geometry;
using CncStudio.Core.Simulation;

namespace CncStudio.Core.Rendering;

/// <summary>
/// Malha de triângulos pronta para a GPU: posições, normais e cores por vértice (floats) e
/// índices. Cada quadrilátero tem 4 vértices próprios (normal reta, sem suavizar arestas).
/// </summary>
public sealed class SolidMesh
{
    private float[] _positions;
    private float[] _normals;
    private float[] _colors;
    private int[] _indices;
    private List<float>? _explicitEdges;
    private float[]? _featureEdges;

    /// <summary>Malha vazia para ser preenchida pelos construtores do Core.</summary>
    internal static SolidMesh Create(int quadCapacity = 16) => new(quadCapacity);

    private SolidMesh(int quadCapacity)
    {
        quadCapacity = Math.Max(16, quadCapacity);
        _positions = new float[quadCapacity * 12];
        _normals = new float[quadCapacity * 12];
        _colors = new float[quadCapacity * 12];
        _indices = new int[quadCapacity * 6];
    }

    public int VertexCount { get; private set; }
    public int IndexCount { get; private set; }
    public int TriangleCount => IndexCount / 3;

    /// <summary>x, y, z de cada vértice (3 floats por vértice; só os primeiros VertexCount×3 valem).</summary>
    public ReadOnlySpan<float> Positions => _positions.AsSpan(0, VertexCount * 3);

    /// <summary>Normal unitária de cada vértice.</summary>
    public ReadOnlySpan<float> Normals => _normals.AsSpan(0, VertexCount * 3);

    /// <summary>Cor de cada vértice (r, g, b de 0 a 1).</summary>
    public ReadOnlySpan<float> Colors => _colors.AsSpan(0, VertexCount * 3);

    /// <summary>Três índices por triângulo.</summary>
    public ReadOnlySpan<int> Indices => _indices.AsSpan(0, IndexCount);

    /// <summary>Malha do sólido da fresa: a mesma superfície que o rasterizador por CPU desenha.</summary>
    public static SolidMesh FromHeightMap(HeightMap map)
    {
        var mesh = new SolidMesh(map.Columns * 4 + map.Rows * 4 + map.Rows * 8);
        MillSolidRenderer.EmitQuads(map, (a, b, c, d, n, rgb) => mesh.AddQuad(a, b, c, d, n, rgb));
        return mesh;
    }

    /// <summary>
    /// Acrescenta o quadrilátero a, b, c, d (em ordem) como dois triângulos. A ordem dos vértices é
    /// acertada pela normal informada: vistos de fora, os triângulos ficam no sentido anti-horário
    /// (o padrão das placas de vídeo para descartar o lado de trás).
    /// </summary>
    /// <param name="shade">
    /// Normal usada só na luz (opcional): paredes da grade em degraus ficam lisas como a peça real.
    /// A ordem dos vértices continua pela <paramref name="normal"/>.
    /// </param>
    internal void AddQuad(Point3 a, Point3 b, Point3 c, Point3 d, Point3 normal, uint rgb, Point3? shade = null)
    {
        // Normal geométrica pelas diagonais (funciona também com um canto repetido, no eixo de revolução)
        Point3 geometric = ViewCamera.Cross(c - a, d - b);
        if (ViewCamera.Dot(geometric, normal) < 0)
        {
            (b, d) = (d, b);
        }

        EnsureRoom();
        Point3 n = ViewCamera.Normalize(shade ?? normal);
        float r = ((rgb >> 16) & 0xFF) / 255f, g = ((rgb >> 8) & 0xFF) / 255f, bl = (rgb & 0xFF) / 255f;
        int v = VertexCount;
        Put(v, a, n, r, g, bl);
        Put(v + 1, b, n, r, g, bl);
        Put(v + 2, c, n, r, g, bl);
        Put(v + 3, d, n, r, g, bl);

        int i = IndexCount;
        _indices[i] = v;
        _indices[i + 1] = v + 1;
        _indices[i + 2] = v + 2;
        _indices[i + 3] = v;
        _indices[i + 4] = v + 2;
        _indices[i + 5] = v + 3;
        VertexCount += 4;
        IndexCount += 6;
    }

    /// <summary>
    /// Polígono convexo "points" (em ordem) como um leque de triângulos, todos com a mesma normal. A ordem
    /// dos vértices é acertada pela normal informada, como em <see cref="AddQuad"/>.
    /// </summary>
    internal void AddPolygon(ReadOnlySpan<Point3> points, Point3 normal, uint rgb)
    {
        int n = points.Length;
        if (n < 3)
        {
            return;
        }

        EnsureCapacity(n, (n - 2) * 3);
        bool reverse = false;
        for (int k = 1; k + 1 < n; k++)
        {
            Point3 g = ViewCamera.Cross(points[k] - points[0], points[k + 1] - points[0]);
            double len = Math.Abs(g.X) + Math.Abs(g.Y) + Math.Abs(g.Z);
            if (len > 1e-12)
            {
                reverse = ViewCamera.Dot(g, normal) < 0;
                break;
            }
        }

        Point3 nn = ViewCamera.Normalize(normal);
        float r = ((rgb >> 16) & 0xFF) / 255f, g2 = ((rgb >> 8) & 0xFF) / 255f, bl = (rgb & 0xFF) / 255f;
        int v = VertexCount;
        for (int k = 0; k < n; k++)
        {
            Put(v + k, points[reverse ? n - 1 - k : k], nn, r, g2, bl);
        }

        int i = IndexCount;
        for (int k = 1; k + 1 < n; k++)
        {
            _indices[i++] = v;
            _indices[i++] = v + k;
            _indices[i++] = v + k + 1;
        }

        VertexCount += n;
        IndexCount += (n - 2) * 3;
    }

    /// <summary>
    /// Parede vertical entre dois pontos do plano XY, de zLow a zHigh. "normal" (para fora) decide o
    /// lado das faces; "shadeA" e "shadeB" são as normais de luz em cada ponta (parede curva sem facetas).
    /// </summary>
    internal void AddWall(double ax, double ay, double bx, double by, double zLow, double zHigh, Point3 normal, Point3 shadeA, Point3 shadeB, uint rgb)
    {
        Point3 a0 = new(ax, ay, zLow), b0 = new(bx, by, zLow), b1 = new(bx, by, zHigh), a1 = new(ax, ay, zHigh);
        Point3 geometric = ViewCamera.Cross(b1 - a0, a1 - b0);
        Point3 na = ViewCamera.Normalize(shadeA), nb = ViewCamera.Normalize(shadeB);
        if (ViewCamera.Dot(na, normal) <= 0)
        {
            na = ViewCamera.Normalize(normal);
        }

        if (ViewCamera.Dot(nb, normal) <= 0)
        {
            nb = ViewCamera.Normalize(normal);
        }

        EnsureCapacity(4, 6);
        float r = ((rgb >> 16) & 0xFF) / 255f, g = ((rgb >> 8) & 0xFF) / 255f, bl = (rgb & 0xFF) / 255f;
        int v = VertexCount;
        if (ViewCamera.Dot(geometric, normal) >= 0)
        {
            Put(v, a0, na, r, g, bl);
            Put(v + 1, b0, nb, r, g, bl);
            Put(v + 2, b1, nb, r, g, bl);
            Put(v + 3, a1, na, r, g, bl);
        }
        else
        {
            Put(v, a0, na, r, g, bl);
            Put(v + 1, a1, na, r, g, bl);
            Put(v + 2, b1, nb, r, g, bl);
            Put(v + 3, b0, nb, r, g, bl);
        }

        int i = IndexCount;
        _indices[i] = v;
        _indices[i + 1] = v + 1;
        _indices[i + 2] = v + 2;
        _indices[i + 3] = v;
        _indices[i + 4] = v + 2;
        _indices[i + 5] = v + 3;
        VertexCount += 4;
        IndexCount += 6;
    }

    /// <summary>Acrescenta os triângulos de outra malha (deslocados por "offset").</summary>
    internal void Append(SolidMesh other, Point3 offset = default)
    {
        int vertices = other.VertexCount, indices = other.IndexCount;
        while ((VertexCount + vertices) * 3 > _positions.Length || IndexCount + indices > _indices.Length)
        {
            Grow();
        }

        ReadOnlySpan<float> p = other.Positions;
        for (int i = 0; i < p.Length; i += 3)
        {
            int k = VertexCount * 3 + i;
            _positions[k] = (float)(p[i] + offset.X);
            _positions[k + 1] = (float)(p[i + 1] + offset.Y);
            _positions[k + 2] = (float)(p[i + 2] + offset.Z);
        }

        other.Normals.CopyTo(_normals.AsSpan(VertexCount * 3));
        other.Colors.CopyTo(_colors.AsSpan(VertexCount * 3));
        ReadOnlySpan<int> idx = other.Indices;
        for (int i = 0; i < idx.Length; i++)
        {
            _indices[IndexCount + i] = idx[i] + VertexCount;
        }

        VertexCount += vertices;
        IndexCount += indices;
    }

    /// <summary>
    /// Cópia com todos os vértices passados por uma rotação (ou rotação + translação) "map";
    /// as normais passam pela parte de rotação (map(n) − map(0)).
    /// </summary>
    internal SolidMesh Transformed(Func<Point3, Point3> map)
    {
        var copy = new SolidMesh(Math.Max(16, VertexCount / 4));
        while (VertexCount * 3 > copy._positions.Length || IndexCount > copy._indices.Length)
        {
            copy.Grow();
        }

        Point3 origin = map(Point3.Origin);
        for (int v = 0; v < VertexCount; v++)
        {
            int k = v * 3;
            Point3 p = map(new Point3(_positions[k], _positions[k + 1], _positions[k + 2]));
            Point3 n = map(new Point3(_normals[k], _normals[k + 1], _normals[k + 2])) - origin;
            copy._positions[k] = (float)p.X;
            copy._positions[k + 1] = (float)p.Y;
            copy._positions[k + 2] = (float)p.Z;
            copy._normals[k] = (float)n.X;
            copy._normals[k + 1] = (float)n.Y;
            copy._normals[k + 2] = (float)n.Z;
        }

        Colors.CopyTo(copy._colors);
        Indices.CopyTo(copy._indices);
        copy.VertexCount = VertexCount;
        copy.IndexCount = IndexCount;
        return copy;
    }

    /// <summary>
    /// Segmentos das arestas que a tela marca por cima da malha (6 floats por segmento). Quem monta a
    /// malha pode informar as arestas com <see cref="AddEdge"/> (o material da fresa faz isso); sem isso,
    /// saem as arestas vivas, onde as faces vizinhas formam um ângulo grande.
    /// </summary>
    public ReadOnlySpan<float> Edges => _explicitEdges is { } e ? System.Runtime.InteropServices.CollectionsMarshal.AsSpan(e) : FeatureEdges(30);

    /// <summary>Quem informa as arestas à mão (mesmo que nenhuma) chama isto: desliga o cálculo automático de <see cref="Edges"/>.</summary>
    internal void UseExplicitEdges() => _explicitEdges ??= [];

    /// <summary>Acrescenta uma aresta a marcar (desliga o cálculo automático de <see cref="Edges"/>).</summary>
    internal void AddEdge(double ax, double ay, double az, double bx, double by, double bz)
    {
        _explicitEdges ??= [];
        _explicitEdges.Add((float)ax);
        _explicitEdges.Add((float)ay);
        _explicitEdges.Add((float)az);
        _explicitEdges.Add((float)bx);
        _explicitEdges.Add((float)by);
        _explicitEdges.Add((float)bz);
    }

    /// <summary>
    /// Arestas vivas: lados de triângulo com um só triângulo (borda aberta) ou entre faces cuja normal
    /// difere mais que "minAngleDegrees". Arestas de facetas de uma superfície curva ficam de fora.
    /// </summary>
    public float[] FeatureEdges(double minAngleDegrees)
    {
        if (_featureEdges is { } cached)
        {
            return cached;
        }

        double cosLimit = Math.Cos(minAngleDegrees * Math.PI / 180);
        float[] p = _positions;
        int[] idx = _indices;
        int indexCount = IndexCount;
        var open = new Dictionary<(long, long, long, long, long, long), (int A, int B, double Nx, double Ny, double Nz)>();
        var edges = new List<float>();

        static long Q(float v) => (long)Math.Round(v * 200.0);

        void Visit(int a, int b, double nx, double ny, double nz)
        {
            int ka = a * 3, kb = b * 3;
            var pa = (Q(p[ka]), Q(p[ka + 1]), Q(p[ka + 2]));
            var pb = (Q(p[kb]), Q(p[kb + 1]), Q(p[kb + 2]));
            // Chave sem direção: o mesmo lado visto de dois triângulos vizinhos
            var key = pa.Item1 < pb.Item1 || (pa.Item1 == pb.Item1 && (pa.Item2 < pb.Item2 || (pa.Item2 == pb.Item2 && pa.Item3 <= pb.Item3)))
                ? (pa.Item1, pa.Item2, pa.Item3, pb.Item1, pb.Item2, pb.Item3)
                : (pb.Item1, pb.Item2, pb.Item3, pa.Item1, pa.Item2, pa.Item3);
            if (open.Remove(key, out var other))
            {
                if (other.Nx * nx + other.Ny * ny + other.Nz * nz < cosLimit)
                {
                    Emit(a, b);
                }
            }
            else
            {
                open[key] = (a, b, nx, ny, nz);
            }
        }

        void Emit(int a, int b)
        {
            int ka = a * 3, kb = b * 3;
            edges.Add(p[ka]);
            edges.Add(p[ka + 1]);
            edges.Add(p[ka + 2]);
            edges.Add(p[kb]);
            edges.Add(p[kb + 1]);
            edges.Add(p[kb + 2]);
        }

        for (int i = 0; i + 2 < indexCount; i += 3)
        {
            int a = idx[i], b = idx[i + 1], c = idx[i + 2];
            int ka = a * 3, kb = b * 3, kc = c * 3;
            double ux = p[kb] - p[ka], uy = p[kb + 1] - p[ka + 1], uz = p[kb + 2] - p[ka + 2];
            double vx = p[kc] - p[ka], vy = p[kc + 1] - p[ka + 1], vz = p[kc + 2] - p[ka + 2];
            double gx = uy * vz - uz * vy, gy = uz * vx - ux * vz, gz = ux * vy - uy * vx;
            double len = Math.Sqrt(gx * gx + gy * gy + gz * gz);
            if (len < 1e-9)
            {
                continue;   // triângulo sem área (canto repetido no eixo de revolução)
            }

            gx /= len;
            gy /= len;
            gz /= len;
            Visit(a, b, gx, gy, gz);
            Visit(b, c, gx, gy, gz);
            Visit(c, a, gx, gy, gz);
        }

        // Lados que ficaram sem par: borda aberta da malha
        foreach ((int a, int b, _, _, _) in open.Values)
        {
            Emit(a, b);
        }

        _featureEdges = [.. edges];
        return _featureEdges;
    }

    /// <summary>Menor caixa que contém todos os vértices.</summary>
    public Bounds3 Bounds
    {
        get
        {
            Bounds3 box = Bounds3.Empty;
            ReadOnlySpan<float> p = Positions;
            for (int i = 0; i < p.Length; i += 3)
            {
                box = box.Include(new Point3(p[i], p[i + 1], p[i + 2]));
            }

            return box;
        }
    }

    /// <summary>
    /// Volume fechado pela malha (teorema da divergência). Só faz sentido para malha fechada com os
    /// triângulos virados para fora; os testes usam isso para conferir as duas coisas.
    /// </summary>
    public double EnclosedVolume()
    {
        ReadOnlySpan<float> p = Positions;
        ReadOnlySpan<int> idx = Indices;
        double sum = 0;
        for (int i = 0; i < idx.Length; i += 3)
        {
            int a = idx[i] * 3, b = idx[i + 1] * 3, c = idx[i + 2] * 3;
            double ax = p[a], ay = p[a + 1], az = p[a + 2];
            double bx = p[b], by = p[b + 1], bz = p[b + 2];
            double cx = p[c], cy = p[c + 1], cz = p[c + 2];
            sum += ax * (by * cz - bz * cy) - ay * (bx * cz - bz * cx) + az * (bx * cy - by * cx);
        }

        return sum / 6;
    }

    private void Put(int vertex, Point3 p, Point3 n, float r, float g, float b)
    {
        int k = vertex * 3;
        _positions[k] = (float)p.X;
        _positions[k + 1] = (float)p.Y;
        _positions[k + 2] = (float)p.Z;
        _normals[k] = (float)n.X;
        _normals[k + 1] = (float)n.Y;
        _normals[k + 2] = (float)n.Z;
        _colors[k] = r;
        _colors[k + 1] = g;
        _colors[k + 2] = b;
    }

    private void EnsureRoom() => EnsureCapacity(4, 6);

    private void EnsureCapacity(int vertices, int indices)
    {
        while ((VertexCount + vertices) * 3 > _positions.Length || IndexCount + indices > _indices.Length)
        {
            Grow();
        }
    }

    private void Grow()
    {
        int quads = _positions.Length / 12 * 2;
        Array.Resize(ref _positions, quads * 12);
        Array.Resize(ref _normals, quads * 12);
        Array.Resize(ref _colors, quads * 12);
        Array.Resize(ref _indices, quads * 6);
    }
}
