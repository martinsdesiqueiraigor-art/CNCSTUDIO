using CncStudio.Core.Geometry;
using CncStudio.Core.Rendering;

namespace CncStudio.Core.Cam3D;

/// <summary>
/// Resultado de orientar uma malha: a malha nova, quantos pedaços separados ela tem, quantos triângulos foram
/// virados, arestas com mais de dois triângulos (ambíguas, não ligam pedaços) e conflitos (pedaço que não
/// admite orientação única, como uma faixa de Möbius).
/// </summary>
public sealed record MeshOrientationResult(TriangleMesh Mesh, int Components, int FlippedTriangles, int NonManifoldEdges, int Conflicts);

/// <summary>
/// Deixa os triângulos de uma malha com a mesma orientação e voltados para fora. Triangulação de CAD (STEP)
/// costuma vir com faces reversas (paredes de furo) e o volume sai errado; o STL do usuário também pode ter
/// triângulos soltos virados. Vértices iguais até 1e-6 mm são tratados como o mesmo ponto.
/// Limite: cada pedaço desligado fica com volume positivo; uma cavidade interna fechada, sem nenhuma aresta
/// em comum com a casca de fora, não é reconhecida como cavidade (peças de usinagem raramente têm).
/// </summary>
public static class MeshOrientation
{
    /// <summary>Volume com sinal (positivo se os triângulos apontam para fora), em mm³.</summary>
    public static double SignedVolume(TriangleMesh mesh)
    {
        double sum = 0;
        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            (Point3 a, Point3 b, Point3 c) = mesh.Triangle(i);
            sum += ViewCamera.Dot(a, ViewCamera.Cross(b, c)) / 6.0;
        }

        return sum;
    }

    public static MeshOrientationResult MakeConsistent(TriangleMesh mesh)
    {
        int n = mesh.TriangleCount;
        if (n == 0)
        {
            return new MeshOrientationResult(mesh, 0, 0, 0, 0);
        }

        // Vértices iguais viram o mesmo número
        var ids = new Dictionary<(long, long, long), int>();
        int Id(Point3 p)
        {
            var key = ((long)Math.Round(p.X * 1e6), (long)Math.Round(p.Y * 1e6), (long)Math.Round(p.Z * 1e6));
            if (!ids.TryGetValue(key, out int id))
            {
                id = ids.Count;
                ids[key] = id;
            }

            return id;
        }

        var tri = new int[n][];
        for (int t = 0; t < n; t++)
        {
            (Point3 a, Point3 b, Point3 c) = mesh.Triangle(t);
            tri[t] = [Id(a), Id(b), Id(c)];
        }

        // Aresta (sem sentido) → triângulos que a usam e o sentido em cada um (+1 se vai do menor id ao maior)
        var edges = new Dictionary<(int, int), List<(int Tri, int Dir)>>();
        for (int t = 0; t < n; t++)
        {
            for (int k = 0; k < 3; k++)
            {
                int u = tri[t][k], v = tri[t][(k + 1) % 3];
                if (u == v)
                {
                    continue; // triângulo degenerado
                }

                (int, int) key = u < v ? (u, v) : (v, u);
                if (!edges.TryGetValue(key, out List<(int, int)>? list))
                {
                    list = [];
                    edges[key] = list;
                }

                list.Add((t, u < v ? 1 : -1));
            }
        }

        int nonManifold = edges.Values.Count(l => l.Count > 2);
        var neighbors = new List<(int Other, int DirHere, int DirOther)>[n];
        foreach (List<(int Tri, int Dir)> list in edges.Values.Where(l => l.Count == 2))
        {
            (int t0, int d0) = list[0];
            (int t1, int d1) = list[1];
            if (t0 == t1)
            {
                continue;
            }

            (neighbors[t0] ??= []).Add((t1, d0, d1));
            (neighbors[t1] ??= []).Add((t0, d1, d0));
        }

        var flip = new bool[n];
        var seen = new bool[n];
        int components = 0, conflicts = 0, flipped = 0;
        var queue = new Queue<int>();
        for (int start = 0; start < n; start++)
        {
            if (seen[start])
            {
                continue;
            }

            components++;
            var members = new List<int>();
            seen[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int t = queue.Dequeue();
                members.Add(t);
                if (neighbors[t] is null)
                {
                    continue;
                }

                foreach ((int other, int dirHere, int dirOther) in neighbors[t])
                {
                    int here = flip[t] ? -dirHere : dirHere;
                    if (!seen[other])
                    {
                        seen[other] = true;
                        flip[other] = dirOther == here; // mesmo sentido na aresta comum: precisa virar
                        queue.Enqueue(other);
                    }
                    else
                    {
                        int there = flip[other] ? -dirOther : dirOther;
                        if (there == here)
                        {
                            conflicts++;
                        }
                    }
                }
            }

            // O pedaço inteiro tem de apontar para fora: volume positivo
            double volume = 0;
            foreach (int t in members)
            {
                (Point3 a, Point3 b, Point3 c) = mesh.Triangle(t);
                if (flip[t])
                {
                    (b, c) = (c, b);
                }

                volume += ViewCamera.Dot(a, ViewCamera.Cross(b, c)) / 6.0;
            }

            if (volume < 0)
            {
                foreach (int t in members)
                {
                    flip[t] = !flip[t];
                }
            }
        }

        var result = new List<(Point3, Point3, Point3)>(n);
        for (int t = 0; t < n; t++)
        {
            (Point3 a, Point3 b, Point3 c) = mesh.Triangle(t);
            if (flip[t])
            {
                (b, c) = (c, b);
                flipped++;
            }

            result.Add((a, b, c));
        }

        // flipped conta contra a malha original: já é o que o chamador quer saber
        return new MeshOrientationResult(new TriangleMesh(result), components, flipped, nonManifold, conflicts);
    }
}
