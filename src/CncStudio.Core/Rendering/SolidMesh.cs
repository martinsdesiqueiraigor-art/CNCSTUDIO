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
        MillSolidRenderer.EmitQuads(map, mesh.AddQuad);
        return mesh;
    }

    /// <summary>Acrescenta o quadrilátero a, b, c, d (em ordem) como dois triângulos.</summary>
    internal void AddQuad(Point3 a, Point3 b, Point3 c, Point3 d, Point3 normal, uint rgb)
    {
        EnsureRoom();
        Point3 n = ViewCamera.Normalize(normal);
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

    private void EnsureRoom()
    {
        if ((VertexCount + 4) * 3 <= _positions.Length)
        {
            return;
        }

        int quads = _positions.Length / 12 * 2;
        Array.Resize(ref _positions, quads * 12);
        Array.Resize(ref _normals, quads * 12);
        Array.Resize(ref _colors, quads * 12);
        Array.Resize(ref _indices, quads * 6);
    }
}
