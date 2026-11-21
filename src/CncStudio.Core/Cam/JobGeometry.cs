using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;

namespace CncStudio.Core.Cam;

/// <summary>Tipo do que a operação escolheu no 3D.</summary>
public enum GeoKind { Face, Edge }

/// <summary>
/// Quais laços de uma face entram no contorno (filtro de laços, como nos outros CAMs). <see cref="Auto"/> (CAM-ILHAS-01, só bolsão):
/// laço de dentro cuja parede sobe vira ilha, o que desce (furo, escareado) fica de fora, e o de fora que desce é borda aberta.
/// </summary>
public enum LoopFilter { All, Outer, Inner, Auto }

/// <summary>Borda de fora do bolsão no filtro <see cref="LoopFilter.Auto"/>: parede que sobe (fechada), que desce (aberta) ou as duas.</summary>
public enum PocketBorder { Closed, Open, Mixed }

/// <summary>
/// Impressão digital de uma face ou aresta escolhida no 3D (STEP-OPS S0): tipo, centro, direção (normal da face; zero na aresta sem
/// normal) e tamanho (área da face ou comprimento da aresta). Não é o id da malha, que muda a cada leitura: serve para achar a mesma
/// face de novo no STEP e acender no 3D.
/// </summary>
public sealed record GeoRef(GeoKind Kind, string Shape, Point3 Center, Point3 Direction, double Size);

/// <summary>
/// De onde a operação tirou a geometria (faces e arestas do 3D, o filtro de laços e os contornos do trabalho que são dela). Campo
/// opcional da operação; só existe em operação criada a partir do STEP. Arquivo com isso sai na versão 6.
/// </summary>
public sealed record GeoSource(IReadOnlyList<GeoRef> Refs, LoopFilter Loops, IReadOnlyList<int> Owned)
{
    /// <summary>Borda de fora (só no filtro <see cref="LoopFilter.Auto"/>; nos outros fica fechada, como antes).</summary>
    public PocketBorder Border { get; init; } = PocketBorder.Closed;
}

/// <summary>Regras da geometria guardada: criar a impressão, achar de novo, dizer o que não bateu.</summary>
public static class JobGeometry
{
    /// <summary>Tolerância do centro (mm) e do tamanho (relativo) para achar a mesma face de novo.</summary>
    public const double CenterTolerance = 0.05;

    public const double SizeTolerance = 0.02;

    public static GeoRef Of(MeshFace f) => new(GeoKind.Face, f.Kind.ToString(), Round(f.Center), Round(f.Normal), Math.Round(f.Area, 4));

    public static GeoRef Of(MeshEdge e) => new(GeoKind.Edge, e.Kind.ToString(), Round(e.Center), Round(e.Normal ?? new Point3(0, 0, 0)), Math.Round(e.Length, 4));

    private static Point3 Round(Point3 p) => new(Math.Round(p.X, 4), Math.Round(p.Y, 4), Math.Round(p.Z, 4));

    private static bool Close(double a, double b) => Math.Abs(a - b) <= Math.Max(Math.Abs(a), Math.Abs(b)) * SizeTolerance + 1e-6;

    private static bool Near(Point3 a, Point3 b) =>
        Math.Abs(a.X - b.X) <= CenterTolerance && Math.Abs(a.Y - b.Y) <= CenterTolerance && Math.Abs(a.Z - b.Z) <= CenterTolerance;

    /// <summary>Acha a face da impressão na malha lida (mesmo tipo, centro, normal e área). Null = não achou.</summary>
    public static MeshFace? Find(MeshFeatures features, GeoRef r)
    {
        if (r.Kind != GeoKind.Face)
        {
            return null;
        }

        MeshFace? best = null;
        double bestDistance = double.MaxValue;
        foreach (MeshFace f in features.Faces)
        {
            if (f.Kind.ToString() != r.Shape || !Near(f.Center, r.Center) || !Close(f.Area, r.Size) || f.Normal.Dot(r.Direction) < 0.99)
            {
                continue;
            }

            double d = Distance(f.Center, r.Center);
            if (d < bestDistance)
            {
                best = f;
                bestDistance = d;
            }
        }

        return best;
    }

    /// <summary>Acha a aresta da impressão na malha lida. Null = não achou.</summary>
    public static MeshEdge? FindEdge(MeshFeatures features, GeoRef r)
    {
        if (r.Kind != GeoKind.Edge)
        {
            return null;
        }

        MeshEdge? best = null;
        double bestDistance = double.MaxValue;
        foreach (MeshEdge e in features.Edges)
        {
            if (e.Kind.ToString() != r.Shape || !Near(e.Center, r.Center) || !Close(e.Length, r.Size))
            {
                continue;
            }

            double d = Distance(e.Center, r.Center);
            if (d < bestDistance)
            {
                best = e;
                bestDistance = d;
            }
        }

        return best;
    }

    private static double Distance(Point3 a, Point3 b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)) + ((a.Z - b.Z) * (a.Z - b.Z)));

    /// <summary>
    /// Resolve a geometria guardada contra a malha lida: faces e arestas achadas e as impressões que não bateram (STEP trocado ou
    /// refeito). Com alguma sem achar, a operação fica "Reselecionar".
    /// </summary>
    public static (IReadOnlyList<MeshFace> Faces, IReadOnlyList<MeshEdge> Edges, IReadOnlyList<GeoRef> Lost) Resolve(MeshFeatures features, GeoSource source)
    {
        var faces = new List<MeshFace>();
        var edges = new List<MeshEdge>();
        var lost = new List<GeoRef>();
        foreach (GeoRef r in source.Refs)
        {
            if (r.Kind == GeoKind.Face)
            {
                if (Find(features, r) is { } f)
                {
                    faces.Add(f);
                }
                else
                {
                    lost.Add(r);
                }
            }
            else if (FindEdge(features, r) is { } e)
            {
                edges.Add(e);
            }
            else
            {
                lost.Add(r);
            }
        }

        return (faces, edges, lost);
    }

    /// <summary>Texto curto de uma impressão para a lista da seção Geometria.</summary>
    public static string Describe(GeoRef r)
    {
        string where = $"Z {r.Center.Z.ToString("0.###", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"))}";
        return r.Kind == GeoKind.Face
            ? r.Shape switch
            {
                nameof(FaceKind.Plane) => "Face plana · " + where,
                nameof(FaceKind.Hole) => "Furo · " + where,
                nameof(FaceKind.Boss) => "Pino · " + where,
                _ => "Face curva · " + where,
            }
            : "Aresta · " + where;
    }

    /// <summary>Arquivo precisa da versão 6: alguma operação guarda a geometria do 3D.</summary>
    public static bool NeedsVersion6(CamJob job) => job.Operations.Any(o => o.Geometry is not null);

    /// <summary>Versão 8 (CAM-ILHAS-01): algum bolsão usa o filtro automático de laços. Sem ele, o arquivo sai como antes.</summary>
    public static bool NeedsVersion8(CamJob job) => job.Operations.Any(o => o.Geometry?.Loops == LoopFilter.Auto);
}
