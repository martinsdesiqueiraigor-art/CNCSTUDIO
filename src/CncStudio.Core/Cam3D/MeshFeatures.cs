using CncStudio.Core.Geometry;
using CncStudio.Core.Rendering;

namespace CncStudio.Core.Cam3D;

/// <summary>Tipo de uma região de superfície detectada na malha.</summary>
public enum FaceKind
{
    /// <summary>Região plana.</summary>
    Plane,

    /// <summary>Cilindro côncavo e (quase) completo: um furo.</summary>
    Hole,

    /// <summary>Cilindro convexo e (quase) completo: um pino ou boss.</summary>
    Boss,

    /// <summary>Pedaço de cilindro côncavo (raio interno, rebaixo).</summary>
    ConcaveCurve,

    /// <summary>Pedaço de cilindro convexo (arredondamento, filete).</summary>
    ConvexCurve,

    /// <summary>Superfície livre (cone, esfera, forma qualquer).</summary>
    Free,
}

/// <summary>Tipo de uma aresta detectada na malha.</summary>
public enum EdgeKind
{
    Line,
    Circle,
    Arc,
    Curve,
}

/// <summary>
/// Região de superfície: triângulos vizinhos sem dobra forte entre eles. Não é a face do CAD (o STEP chega
/// aqui já como malha), é a região que a malha mostra.
/// </summary>
public sealed record MeshFace(
    int Id,
    FaceKind Kind,
    IReadOnlyList<int> Triangles,
    double Area,
    Point3 Normal,
    Point3 Center,
    Bounds3 Box,
    double Diameter = 0,
    Point3? Axis = null,
    double Depth = 0);

/// <summary>Aresta viva (dobra forte ou borda aberta), uma cadeia de segmentos.</summary>
public sealed record MeshEdge(
    int Id,
    EdgeKind Kind,
    IReadOnlyList<Point3> Points,
    bool Closed,
    double Length,
    Point3 Center,
    double Diameter = 0,
    Point3? Normal = null);

/// <summary>
/// Detecta, na malha, as regiões de superfície ("faces") e as arestas vivas, e escolhe uma delas por um raio
/// (clique na tela 3D). Tudo em mm, sem pacotes. O resultado é uma leitura da malha: com malha grossa um
/// furo pode virar vários pedaços, e a normal da malha precisa apontar para fora do material.
/// </summary>
public sealed class MeshFeatures
{
    private readonly TriangleMesh _mesh;
    private readonly int[] _faceOfTriangle;
    private readonly List<(Point3 A, Point3 B, int Edge)> _segments = [];

    private MeshFeatures(TriangleMesh mesh, int[] faceOfTriangle, List<MeshFace> faces, List<MeshEdge> edges)
    {
        _mesh = mesh;
        _faceOfTriangle = faceOfTriangle;
        Faces = faces;
        Edges = edges;
        foreach (MeshEdge e in edges)
        {
            for (int i = 0; i + 1 < e.Points.Count; i++)
            {
                _segments.Add((e.Points[i], e.Points[i + 1], e.Id));
            }
        }
    }

    public IReadOnlyList<MeshFace> Faces { get; }

    public IReadOnlyList<MeshEdge> Edges { get; }

    /// <summary>Região de cada triângulo (−1 se o triângulo é degenerado).</summary>
    public int FaceOf(int triangle) => _faceOfTriangle[triangle];

    /// <summary>Lê a malha. <paramref name="creaseDegrees"/> é a dobra a partir da qual há aresta viva.</summary>
    public static MeshFeatures Analyze(TriangleMesh mesh, double creaseDegrees = 30)
    {
        int count = mesh.TriangleCount;
        double cosCrease = Math.Cos(creaseDegrees * Math.PI / 180);

        // Vértices soldados (o STL repete o vértice em cada triângulo)
        var ids = new Dictionary<(long, long, long), int>();
        var points = new List<Point3>();
        var tri = new int[count][];
        var normal = new Point3[count];
        var area = new double[count];
        for (int i = 0; i < count; i++)
        {
            (Point3 a, Point3 b, Point3 c) = mesh.Triangle(i);
            tri[i] = [Weld(ids, points, a), Weld(ids, points, b), Weld(ids, points, c)];
            Point3 n = ViewCamera.Cross(b - a, c - a);
            double len = Math.Sqrt((n.X * n.X) + (n.Y * n.Y) + (n.Z * n.Z));
            area[i] = len / 2;
            normal[i] = len < 1e-12 ? Point3.Origin : new Point3(n.X / len, n.Y / len, n.Z / len);
        }

        // Triângulos de cada aresta da malha
        var edgeTris = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < count; i++)
        {
            if (area[i] < 1e-12)
            {
                continue;
            }

            for (int k = 0; k < 3; k++)
            {
                int p = tri[i][k], q = tri[i][(k + 1) % 3];
                if (p == q)
                {
                    continue;
                }

                (int, int) key = p < q ? (p, q) : (q, p);
                if (!edgeTris.TryGetValue(key, out List<int>? list))
                {
                    edgeTris[key] = list = [];
                }

                list.Add(i);
            }
        }

        // Regiões: espalha por arestas sem dobra forte
        var faceOf = new int[count];
        Array.Fill(faceOf, -1);
        var faces = new List<MeshFace>();
        for (int seed = 0; seed < count; seed++)
        {
            if (faceOf[seed] != -1 || area[seed] < 1e-12)
            {
                continue;
            }

            var members = new List<int>();
            var stack = new Stack<int>();
            stack.Push(seed);
            faceOf[seed] = faces.Count;
            while (stack.Count > 0)
            {
                int t = stack.Pop();
                members.Add(t);
                for (int k = 0; k < 3; k++)
                {
                    int p = tri[t][k], q = tri[t][(k + 1) % 3];
                    if (p == q || !edgeTris.TryGetValue(p < q ? (p, q) : (q, p), out List<int>? list) || list.Count != 2)
                    {
                        continue;
                    }

                    int other = list[0] == t ? list[1] : list[0];
                    if (faceOf[other] != -1 || Dot(normal[t], normal[other]) < cosCrease)
                    {
                        continue;
                    }

                    faceOf[other] = faces.Count;
                    stack.Push(other);
                }
            }

            faces.Add(BuildFace(faces.Count, members, tri, points, normal, area));
        }

        List<MeshEdge> edges = BuildEdges(edgeTris, points, normal, cosCrease);
        return new MeshFeatures(mesh, faceOf, faces, edges);
    }

    /// <summary>Primeira região atingida pelo raio (origem, direção) e a distância até ela.</summary>
    public (MeshFace? Face, double Distance) PickFace(Point3 origin, Point3 direction)
    {
        double best = double.MaxValue;
        int bestTri = -1;
        for (int i = 0; i < _mesh.TriangleCount; i++)
        {
            if (_faceOfTriangle[i] < 0)
            {
                continue;
            }

            (Point3 a, Point3 b, Point3 c) = _mesh.Triangle(i);
            double t = RayTriangle(origin, direction, a, b, c);
            if (t > 0 && t < best)
            {
                best = t;
                bestTri = i;
            }
        }

        return bestTri < 0 ? (null, 0) : (Faces[_faceOfTriangle[bestTri]], best);
    }

    /// <summary>
    /// Aresta mais perto do raio, dentro da tolerância angular (radianos vistos do olho).
    /// A distância devolvida é ao longo do raio.
    /// </summary>
    public (MeshEdge? Edge, double Distance) PickEdge(Point3 origin, Point3 direction, double angularTolerance)
    {
        double bestScore = double.MaxValue, bestT = 0;
        int bestEdge = -1;
        foreach ((Point3 a, Point3 b, int edge) in _segments)
        {
            (double t, double distance) = RaySegment(origin, direction, a, b);
            if (t <= 0 || distance > t * angularTolerance)
            {
                continue;
            }

            // dentro da tolerância vale a mais perto do olho (a de trás fica escondida pela da frente)
            if (t < bestScore)
            {
                bestScore = t;
                bestT = t;
                bestEdge = edge;
            }
        }

        return bestEdge < 0 ? (null, 0) : (Edges[bestEdge], bestT);
    }

    /// <summary>Clique: a aresta ganha da região quando está na frente (ou colada) a ela.</summary>
    public (MeshFace? Face, MeshEdge? Edge) Pick(Point3 origin, Point3 direction, double angularTolerance)
    {
        (MeshFace? face, double faceT) = PickFace(origin, direction);
        (MeshEdge? edge, double edgeT) = PickEdge(origin, direction, angularTolerance);
        if (edge is not null && (face is null || edgeT <= faceT + (faceT * angularTolerance * 4) + 1e-6))
        {
            return (null, edge);
        }

        return (face, null);
    }

    // ---------------------------------------------------------------- regiões

    private static MeshFace BuildFace(int id, List<int> members, int[][] tri, List<Point3> points, Point3[] normal, double[] area)
    {
        double total = 0;
        double nx = 0, ny = 0, nz = 0, cx = 0, cy = 0, cz = 0;
        var box = Bounds3.Empty;
        var verts = new HashSet<int>();
        foreach (int t in members)
        {
            total += area[t];
            nx += normal[t].X * area[t];
            ny += normal[t].Y * area[t];
            nz += normal[t].Z * area[t];
            foreach (int vi2 in tri[t])
            {
                verts.Add(vi2);
                box = box.Include(points[vi2]);
                cx += points[vi2].X * area[t] / 3;
                cy += points[vi2].Y * area[t] / 3;
                cz += points[vi2].Z * area[t] / 3;
            }
        }

        Point3 center = new(cx / total, cy / total, cz / total);
        double nl = Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
        Point3 avg = nl < 1e-9 ? new Point3(0, 0, 1) : new Point3(nx / nl, ny / nl, nz / nl);

        // Plana: todas as normais quase iguais
        double cosPlane = Math.Cos(3 * Math.PI / 180);
        if (members.All(t => Dot(normal[t], avg) >= cosPlane))
        {
            return new MeshFace(id, FaceKind.Plane, members, total, avg, center, box);
        }

        // Cilindro: eixo = direção em que as normais quase não têm componente (menor autovalor)
        var m = new double[3, 3];
        foreach (int t in members)
        {
            double[] n = [normal[t].X, normal[t].Y, normal[t].Z];
            for (int r = 0; r < 3; r++)
            {
                for (int c = 0; c < 3; c++)
                {
                    m[r, c] += area[t] * n[r] * n[c];
                }
            }
        }

        Point3 axis = SmallestEigenvector(m);
        double off = members.Sum(t => area[t] * Dot(normal[t], axis) * Dot(normal[t], axis)) / total;
        if (off > 0.01)
        {
            return new MeshFace(id, FaceKind.Free, members, total, avg, center, box);
        }

        (Point3 u, Point3 v) = Basis(axis);
        Point3 o = center;
        var uv = verts.Select(i => ((points[i] - o).Dot(u), (points[i] - o).Dot(v), (points[i] - o).Dot(axis))).ToList();
        if (!FitCircle(uv.Select(p => (p.Item1, p.Item2)).ToList(), out double ccx, out double ccy, out double radius) || radius <= 0)
        {
            return new MeshFace(id, FaceKind.Free, members, total, avg, center, box);
        }

        double rms = Math.Sqrt(uv.Average(p => Math.Pow(Math.Sqrt(((p.Item1 - ccx) * (p.Item1 - ccx)) + ((p.Item2 - ccy) * (p.Item2 - ccy))) - radius, 2)));
        if (rms > 0.02 * radius)
        {
            return new MeshFace(id, FaceKind.Free, members, total, avg, center, box);
        }

        // Côncavo (furo) ou convexo (pino): a normal aponta para o eixo ou para longe dele
        double side = 0;
        foreach (int t in members)
        {
            Point3 g = Point3.Origin;
            foreach (int vi in tri[t])
            {
                g = new Point3(g.X + points[vi].X / 3, g.Y + points[vi].Y / 3, g.Z + points[vi].Z / 3);
            }

            Point3 d = g - o;
            double radialU = d.Dot(u) - ccx, radialV = d.Dot(v) - ccy;
            side += area[t] * ((normal[t].Dot(u) * radialU) + (normal[t].Dot(v) * radialV));
        }

        bool concave = side < 0;
        var angles = uv.Select(p => Math.Atan2(p.Item2 - ccy, p.Item1 - ccx) * 180 / Math.PI).OrderBy(a => a).ToList();
        double maxGap = 360 - (angles[^1] - angles[0]);
        for (int i = 1; i < angles.Count; i++)
        {
            maxGap = Math.Max(maxGap, angles[i] - angles[i - 1]);
        }

        double span = 360 - maxGap;
        double tMin = uv.Min(p => p.Item3), tMax = uv.Max(p => p.Item3);
        Point3 axisCenter = o + Scale(u, ccx) + Scale(v, ccy) + Scale(axis, (tMin + tMax) / 2);
        FaceKind kind = span >= 300
            ? (concave ? FaceKind.Hole : FaceKind.Boss)
            : (concave ? FaceKind.ConcaveCurve : FaceKind.ConvexCurve);
        return new MeshFace(id, kind, members, total, avg, axisCenter, box, radius * 2, axis, tMax - tMin);
    }

    // ---------------------------------------------------------------- arestas

    private static List<MeshEdge> BuildEdges(Dictionary<(int, int), List<int>> edgeTris, List<Point3> points, Point3[] normal, double cosCrease)
    {
        var feature = new List<(int A, int B)>();
        foreach (((int p, int q), List<int> list) in edgeTris)
        {
            if (list.Count != 2 || Dot(normal[list[0]], normal[list[1]]) < cosCrease)
            {
                feature.Add((p, q));
            }
        }

        var byVertex = new Dictionary<int, List<int>>();
        for (int i = 0; i < feature.Count; i++)
        {
            foreach (int v in new[] { feature[i].A, feature[i].B })
            {
                if (!byVertex.TryGetValue(v, out List<int>? l))
                {
                    byVertex[v] = l = [];
                }

                l.Add(i);
            }
        }

        var used = new bool[feature.Count];
        var edges = new List<MeshEdge>();

        void Walk(int startVertex, int firstEdge)
        {
            var chain = new List<int> { startVertex };
            int vertex = startVertex, edge = firstEdge;
            bool closed = false;
            while (true)
            {
                used[edge] = true;
                int next = feature[edge].A == vertex ? feature[edge].B : feature[edge].A;
                chain.Add(next);
                vertex = next;
                if (vertex == startVertex)
                {
                    closed = true;
                    break;
                }

                List<int> around = byVertex[vertex];
                if (around.Count != 2)
                {
                    break;
                }

                int following = around[0] == edge ? around[1] : around[0];
                if (used[following])
                {
                    break;
                }

                edge = following;
            }

            edges.Add(MakeEdge(edges.Count, chain.Select(i => points[i]).ToList(), closed));
        }

        // primeiro as pontas (cadeias abertas), depois os laços
        foreach ((int v, List<int> list) in byVertex.OrderBy(k => k.Key))
        {
            if (list.Count == 2)
            {
                continue;
            }

            foreach (int e in list)
            {
                if (!used[e])
                {
                    Walk(v, e);
                }
            }
        }

        for (int e = 0; e < feature.Count; e++)
        {
            if (!used[e])
            {
                Walk(feature[e].A, e);
            }
        }

        return edges;
    }

    private static MeshEdge MakeEdge(int id, List<Point3> pts, bool closed)
    {
        double length = 0;
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            length += pts[i].DistanceTo(pts[i + 1]);
        }

        Point3 mid = Point3.Lerp(pts[0], pts[^1], 0.5);
        double dev = 0;
        Point3 d0 = pts[^1] - pts[0];
        double l0 = Math.Sqrt(d0.Dot(d0));
        if (!closed && l0 > 1e-9)
        {
            foreach (Point3 p in pts)
            {
                Point3 c = ViewCamera.Cross(p - pts[0], d0);
                dev = Math.Max(dev, Math.Sqrt(c.Dot(c)) / l0);
            }

            if (dev <= 1e-4 + (1e-6 * length))
            {
                return new MeshEdge(id, EdgeKind.Line, pts, false, length, mid);
            }
        }

        // círculo / arco: plano pelos pontos + ajuste de círculo
        if (pts.Count >= (closed ? 8 : 5))
        {
            var ring = closed ? pts.Take(pts.Count - 1).ToList() : pts;
            Point3 g = new(ring.Average(p => p.X), ring.Average(p => p.Y), ring.Average(p => p.Z));
            Point3 n = Point3.Origin;
            for (int i = 0; i < ring.Count; i++)
            {
                Point3 a = ring[i] - g, b = ring[(i + 1) % ring.Count] - g;
                Point3 c = ViewCamera.Cross(a, b);
                n = new Point3(n.X + c.X, n.Y + c.Y, n.Z + c.Z);
            }

            double nl = Math.Sqrt(n.Dot(n));
            if (nl > 1e-9)
            {
                n = new Point3(n.X / nl, n.Y / nl, n.Z / nl);
                (Point3 u, Point3 v) = Basis(n);
                var flat = ring.Select(p => ((p - g).Dot(u), (p - g).Dot(v))).ToList();
                double planeDev = ring.Max(p => Math.Abs((p - g).Dot(n)));
                if (FitCircle(flat, out double cx, out double cy, out double r) && r > 0 && planeDev < 0.01 * r + 1e-6
                    && flat.All(p => Math.Abs(Math.Sqrt(((p.Item1 - cx) * (p.Item1 - cx)) + ((p.Item2 - cy) * (p.Item2 - cy))) - r) < 0.01 * r + 1e-6))
                {
                    Point3 center = g + Scale(u, cx) + Scale(v, cy);
                    return new MeshEdge(id, closed ? EdgeKind.Circle : EdgeKind.Arc, pts, closed, length, center, r * 2, n);
                }
            }
        }

        return new MeshEdge(id, EdgeKind.Curve, pts, closed, length, mid);
    }

    // ---------------------------------------------------------------- matemática

    private static int Weld(Dictionary<(long, long, long), int> ids, List<Point3> points, Point3 p)
    {
        (long, long, long) key = ((long)Math.Round(p.X * 1e4), (long)Math.Round(p.Y * 1e4), (long)Math.Round(p.Z * 1e4));
        if (!ids.TryGetValue(key, out int id))
        {
            id = points.Count;
            ids[key] = id;
            points.Add(p);
        }

        return id;
    }

    private static double Dot(Point3 a, Point3 b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private static Point3 Scale(Point3 a, double k) => new(a.X * k, a.Y * k, a.Z * k);

    private static (Point3 U, Point3 V) Basis(Point3 n)
    {
        Point3 helper = Math.Abs(n.Z) < 0.9 ? new Point3(0, 0, 1) : new Point3(1, 0, 0);
        Point3 u = ViewCamera.Normalize(ViewCamera.Cross(helper, n));
        Point3 v = ViewCamera.Cross(n, u);
        return (u, v);
    }

    /// <summary>Círculo por mínimos quadrados (Kasa), com os pontos centralizados antes.</summary>
    private static bool FitCircle(List<(double X, double Y)> pts, out double cx, out double cy, out double r)
    {
        cx = cy = r = 0;
        if (pts.Count < 3)
        {
            return false;
        }

        double mx = pts.Average(p => p.X), my = pts.Average(p => p.Y);
        double sxx = 0, sxy = 0, syy = 0, sx = 0, sy = 0, sxz = 0, syz = 0, sz = 0;
        foreach ((double px, double py) in pts)
        {
            double x = px - mx, y = py - my, z = (x * x) + (y * y);
            sxx += x * x;
            sxy += x * y;
            syy += y * y;
            sx += x;
            sy += y;
            sxz += x * z;
            syz += y * z;
            sz += z;
        }

        // [sxx sxy sx][D]   [-sxz]
        // [sxy syy sy][E] = [-syz]
        // [sx  sy  n ][F]   [-sz ]
        double n = pts.Count;
        double det = (sxx * ((syy * n) - (sy * sy))) - (sxy * ((sxy * n) - (sy * sx))) + (sx * ((sxy * sy) - (syy * sx)));
        if (Math.Abs(det) < 1e-18)
        {
            return false;
        }

        double d = ((-sxz * ((syy * n) - (sy * sy))) - (sxy * ((-syz * n) - (sy * -sz))) + (sx * ((-syz * sy) - (syy * -sz)))) / det;
        double e = ((sxx * ((-syz * n) - (sy * -sz))) - (-sxz * ((sxy * n) - (sy * sx))) + (sx * ((sxy * -sz) - (-syz * sx)))) / det;
        double f = ((sxx * ((syy * -sz) - (-syz * sy))) - (sxy * ((sxy * -sz) - (-syz * sx))) + (-sxz * ((sxy * sy) - (syy * sx)))) / det;
        double ccx = -d / 2, ccy = -e / 2;
        double rr = (ccx * ccx) + (ccy * ccy) - f;
        if (rr <= 0)
        {
            return false;
        }

        cx = ccx + mx;
        cy = ccy + my;
        r = Math.Sqrt(rr);
        return true;
    }

    /// <summary>Autovetor do menor autovalor de uma matriz 3×3 simétrica (Jacobi).</summary>
    private static Point3 SmallestEigenvector(double[,] a)
    {
        var v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        for (int sweep = 0; sweep < 50; sweep++)
        {
            double off = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
            if (off < 1e-14)
            {
                break;
            }

            for (int p = 0; p < 2; p++)
            {
                for (int q = p + 1; q < 3; q++)
                {
                    if (Math.Abs(a[p, q]) < 1e-18)
                    {
                        continue;
                    }

                    double theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                    double t = Math.Sign(theta == 0 ? 1 : theta) / (Math.Abs(theta) + Math.Sqrt((theta * theta) + 1));
                    double c = 1 / Math.Sqrt((t * t) + 1), s = t * c;
                    for (int k = 0; k < 3; k++)
                    {
                        double akp = a[k, p], akq = a[k, q];
                        a[k, p] = (c * akp) - (s * akq);
                        a[k, q] = (s * akp) + (c * akq);
                    }

                    for (int k = 0; k < 3; k++)
                    {
                        double apk = a[p, k], aqk = a[q, k];
                        a[p, k] = (c * apk) - (s * aqk);
                        a[q, k] = (s * apk) + (c * aqk);
                    }

                    for (int k = 0; k < 3; k++)
                    {
                        double vkp = v[k, p], vkq = v[k, q];
                        v[k, p] = (c * vkp) - (s * vkq);
                        v[k, q] = (s * vkp) + (c * vkq);
                    }
                }
            }
        }

        int min = 0;
        for (int i = 1; i < 3; i++)
        {
            if (a[i, i] < a[min, min])
            {
                min = i;
            }
        }

        return ViewCamera.Normalize(new Point3(v[0, min], v[1, min], v[2, min]));
    }

    /// <summary>Möller–Trumbore, dos dois lados; distância ao longo do raio ou −1.</summary>
    private static double RayTriangle(Point3 o, Point3 d, Point3 a, Point3 b, Point3 c)
    {
        Point3 e1 = b - a, e2 = c - a;
        Point3 p = ViewCamera.Cross(d, e2);
        double det = Dot(e1, p);
        if (Math.Abs(det) < 1e-12)
        {
            return -1;
        }

        double inv = 1 / det;
        Point3 s = o - a;
        double u = Dot(s, p) * inv;
        if (u < 0 || u > 1)
        {
            return -1;
        }

        Point3 q = ViewCamera.Cross(s, e1);
        double v = Dot(d, q) * inv;
        if (v < 0 || u + v > 1)
        {
            return -1;
        }

        double t = Dot(e2, q) * inv;
        return t > 1e-9 ? t : -1;
    }

    /// <summary>Ponto do raio mais perto do segmento: distância ao longo do raio e distância entre os dois.</summary>
    private static (double T, double Distance) RaySegment(Point3 o, Point3 d, Point3 a, Point3 b)
    {
        Point3 e = b - a, w = o - a;
        double dd = Dot(d, d), de = Dot(d, e), ee = Dot(e, e), dw = Dot(d, w), ew = Dot(e, w);
        double den = (dd * ee) - (de * de);
        double s = ee < 1e-18 ? 0 : (den < 1e-12 * dd * ee ? 0 : ((dd * ew) - (de * dw)) / den);
        s = Math.Clamp(s, 0, 1);
        double t = (de * s - dw) / dd;
        if (t <= 0)
        {
            return (-1, double.MaxValue);
        }

        Point3 onRay = new(o.X + (d.X * t), o.Y + (d.Y * t), o.Z + (d.Z * t));
        Point3 onSeg = new(a.X + (e.X * s), a.Y + (e.Y * s), a.Z + (e.Z * s));
        return (t, onRay.DistanceTo(onSeg));
    }
}

internal static class Point3Math
{
    public static double Dot(this Point3 a, Point3 b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);
}
