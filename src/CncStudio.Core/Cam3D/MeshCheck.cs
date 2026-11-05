using CncStudio.Core.Geometry;

namespace CncStudio.Core.Cam3D;

/// <summary>
/// Conferência da malha em números, para a tela dizer o que está aberto em vez de só "malha aberta".
/// Vértices iguais até 1e-6 mm são o mesmo ponto; triângulos colapsados (<see cref="TriangleMesh.IsCollapsed"/>) não contam.
/// </summary>
/// <param name="BorderEdges">Arestas usadas por um só triângulo (furo na superfície).</param>
/// <param name="BorderLength">Comprimento somado dessas arestas, em mm.</param>
/// <param name="NonManifoldEdges">Arestas usadas por mais de dois triângulos (ambíguas).</param>
/// <param name="Pieces">Pedaços ligados por arestas de exatamente dois triângulos (mesma regra de <see cref="MeshOrientation"/>).</param>
/// <param name="Collapsed">Triângulos colapsados encontrados (ignorados).</param>
public sealed record MeshCheck(int BorderEdges, double BorderLength, int NonManifoldEdges, int Pieces, int Collapsed)
{
    /// <summary>Fechada: sem borda, sem aresta ambígua e com pelo menos um triângulo de verdade.</summary>
    public bool IsClosed => BorderEdges == 0 && NonManifoldEdges == 0 && Pieces > 0;

    public static MeshCheck Of(TriangleMesh mesh)
    {
        var ids = new Dictionary<(long, long, long), int>();
        var points = new List<Point3>();
        int Id(Point3 p)
        {
            (long, long, long) key = TriangleMesh.Key(p);
            if (!ids.TryGetValue(key, out int id))
            {
                id = points.Count;
                ids[key] = id;
                points.Add(p);
            }

            return id;
        }

        var edges = new Dictionary<(int, int), List<int>>();
        var used = new List<int>();
        int collapsed = 0;
        for (int t = 0; t < mesh.TriangleCount; t++)
        {
            if (mesh.IsCollapsed(t))
            {
                collapsed++;
                continue;
            }

            (Point3 a, Point3 b, Point3 c) = mesh.Triangle(t);
            int[] v = [Id(a), Id(b), Id(c)];
            used.Add(t);
            for (int k = 0; k < 3; k++)
            {
                int p = v[k], q = v[(k + 1) % 3];
                (int, int) key = p < q ? (p, q) : (q, p);
                if (!edges.TryGetValue(key, out List<int>? list))
                {
                    list = [];
                    edges[key] = list;
                }

                list.Add(t);
            }
        }

        int border = 0, nonManifold = 0;
        double length = 0;
        var parent = new Dictionary<int, int>();
        foreach (int t in used)
        {
            parent[t] = t;
        }

        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }

            return x;
        }

        foreach (((int p, int q), List<int> list) in edges)
        {
            if (list.Count == 1)
            {
                border++;
                Point3 d = points[p] - points[q];
                length += Math.Sqrt((d.X * d.X) + (d.Y * d.Y) + (d.Z * d.Z));
            }
            else if (list.Count == 2)
            {
                parent[Find(list[0])] = Find(list[1]);
            }
            else
            {
                nonManifold++;
            }
        }

        int pieces = used.Select(Find).Distinct().Count();
        return new MeshCheck(border, length, nonManifold, pieces, collapsed);
    }
}
