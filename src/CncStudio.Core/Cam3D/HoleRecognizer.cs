using CncStudio.Core.Cam;
using CncStudio.Core.Geometry;

namespace CncStudio.Core.Cam3D;

/// <summary>Grupo de furos verticais iguais da peça (mesmo diâmetro, mesmo topo e mesmo fundo): vira uma operação de Furação.</summary>
public sealed record HoleGroup(double Diameter, double TopZ, double BottomZ, bool Through, IReadOnlyList<MeshFace> Faces)
{
    /// <summary>Profundidade do furo (topo menos fundo).</summary>
    public double Depth => TopZ - BottomZ;

    /// <summary>Furo profundo (mais de 3 vezes o diâmetro): só gera uma nota; nenhum campo é mudado.</summary>
    public bool Deep => Depth > 3 * Diameter;
}

/// <summary>Furo achado na peça que não vira furação automática, com a razão em texto (CAM-FURO-01).</summary>
public sealed record HoleRejected(MeshFace Face, string Reason);

/// <summary>Resultado do reconhecimento: grupos prontos e furos de fora, na ordem do maior para o menor diâmetro.</summary>
public sealed record HoleReport(IReadOnlyList<HoleGroup> Groups, IReadOnlyList<HoleRejected> Rejected);

/// <summary>
/// CAM-FURO-01. Acha os furos verticais da malha da peça (faces <see cref="FaceKind.Hole"/>) e os agrupa. Só informa: nada é
/// suposto (profundidade vem do furo, ferramenta e ciclo ficam vazios) e nada vai para o arquivo. Furo com degrau, inclinado ou
/// de fundo cônico fica de fora com a razão.
/// </summary>
public static class HoleRecognizer
{
    private const double Tolerance = 0.01;

    /// <summary>Centro de furos "da mesma linha" (para achar degrau) e do mesmo furo já criado.</summary>
    private const double AxisTolerance = 0.05;

    public static HoleReport Recognize(MeshFeatures features, TriangleMesh part)
    {
        double partBottom = part.Bounds.Min.Z;
        var holes = features.Faces.Where(f => f.Kind == FaceKind.Hole).ToList();
        var rejected = new List<HoleRejected>();
        var ready = new List<MeshFace>();
        foreach (MeshFace h in holes)
        {
            if (h.Axis is not { } a || Math.Abs(a.Z) < 0.99)
            {
                rejected.Add(new HoleRejected(h, "Furo inclinado: só furos verticais viram furação."));
                continue;
            }

            MeshFace? other = holes.FirstOrDefault(o => o.Id != h.Id && o.Axis is { } oa && Math.Abs(oa.Z) >= 0.99
                && Math.Abs(o.Center.X - h.Center.X) < AxisTolerance && Math.Abs(o.Center.Y - h.Center.Y) < AxisTolerance
                && Math.Abs(o.Diameter - h.Diameter) > Tolerance);
            if (other is not null)
            {
                rejected.Add(new HoleRejected(h, $"Furo com degrau (Ø{h.Diameter:0.##} e Ø{other.Diameter:0.##}): fica para bolsão ou contorno."));
                continue;
            }

            bool through = h.Box.Min.Z <= partBottom + Tolerance;
            if (!through && ConeBelow(features, h))
            {
                rejected.Add(new HoleRejected(h, "Furo com fundo cônico: não vira furação nesta versão."));
                continue;
            }

            ready.Add(h);
        }

        var groups = new List<(MeshFace First, List<MeshFace> Faces)>();
        foreach (MeshFace h in ready.OrderByDescending(f => f.Diameter).ThenByDescending(f => f.Box.Max.Z).ThenBy(f => f.Id))
        {
            (MeshFace First, List<MeshFace> Faces) g = groups.FirstOrDefault(x => Same(x.First, h));
            if (g.Faces is null)
            {
                groups.Add((h, [h]));
            }
            else
            {
                g.Faces.Add(h);
            }
        }

        return new HoleReport(
            [.. groups.Select(g => new HoleGroup(g.First.Diameter, g.First.Box.Max.Z, g.First.Box.Min.Z, g.First.Box.Min.Z <= partBottom + Tolerance, g.Faces))],
            rejected);
    }

    private static bool Same(MeshFace a, MeshFace b) =>
        Math.Abs(a.Diameter - b.Diameter) <= Tolerance && Math.Abs(a.Box.Max.Z - b.Box.Max.Z) <= Tolerance && Math.Abs(a.Box.Min.Z - b.Box.Min.Z) <= Tolerance;

    /// <summary>Superfície livre (cone) logo abaixo do furo, dentro do diâmetro dele: fundo de broca.</summary>
    private static bool ConeBelow(MeshFeatures features, MeshFace h)
    {
        double radius = h.Diameter / 2;
        return features.Faces.Any(f => f.Kind == FaceKind.Free
            && Math.Abs(f.Box.Max.Z - h.Box.Min.Z) <= 0.05
            && f.Box.Min.X >= h.Center.X - radius - 0.05 && f.Box.Max.X <= h.Center.X + radius + 0.05
            && f.Box.Min.Y >= h.Center.Y - radius - 0.05 && f.Box.Max.Y <= h.Center.Y + radius + 0.05);
    }

    /// <summary>O grupo já tem Furação no trabalho: todos os furos dele estão (centro e diâmetro) nos círculos de uma operação de furação.</summary>
    public static bool IsCovered(CamJob job, HoleGroup group)
    {
        foreach (CamOperationSpec op in job.Operations)
        {
            if (op.Kind != JobOpKind.Drilling || op.ContourIndices is not { Count: > 0 } chosen)
            {
                continue;
            }

            IReadOnlyList<DrillHole> have = CamOperations.HolesFromCircles(chosen.Where(i => i >= 0 && i < job.Contours.Count).Select(i => job.Contours[i]));
            if (group.Faces.All(f => have.Any(h => Math.Abs(h.Center.X - f.Center.X) < AxisTolerance && Math.Abs(h.Center.Y - f.Center.Y) < AxisTolerance
                && Math.Abs(h.Diameter - f.Diameter) < 0.02)))
            {
                return true;
            }
        }

        return false;
    }
}
