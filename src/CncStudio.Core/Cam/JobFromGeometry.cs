using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;

namespace CncStudio.Core.Cam;

/// <summary>
/// STEP-OPS S0: operação nascida da seleção no 3D, com a geometria guardada (<see cref="GeoSource"/>). Criar e atualizar passam pelo
/// mesmo caminho: o contorno é lido da malha de novo e substitui os contornos que eram da operação. T, Ø, rotação, avanços e alturas
/// já digitados ficam; o Z do fundo só vem da face na criação (depois só o usuário muda).
/// </summary>
public static class CamJobFromGeometry
{
    /// <summary>Texto da operação que perdeu a geometria (STEP trocado): fica Incompleta até reselecionar.</summary>
    public const string ReselectField = "Geometria";

    /// <summary>
    /// Cria a(s) operação(ões) da seleção: bolsão (um por face de fundo) ou furação (uma só, todos os furos). Devolve os índices ou o
    /// motivo. Nada é alterado se houver erro.
    /// </summary>
    public static (IReadOnlyList<int> Indices, string? Error) Add(
        CamJob job, JobOpKind kind, MeshFeatures features, IReadOnlyList<MeshFace> faces, IReadOnlyList<MeshEdge> edges, double modelTopZ)
    {
        switch (kind)
        {
            case JobOpKind.Pocket:
                if (faces.Count == 0)
                {
                    return ([], "Escolha a face do fundo do bolsão no 3D.");
                }

                // valida tudo antes de mexer no trabalho
                foreach (MeshFace f in faces)
                {
                    if (PocketError(features, f, modelTopZ) is { } why)
                    {
                        return ([], why);
                    }
                }

                var made = new List<int>();
                foreach (MeshFace f in faces)
                {
                    made.Add(AddPocket(job, features, f, modelTopZ));
                }

                return (made, null);
            case JobOpKind.Drilling:
                (int index, string? error) = AddDrilling(job, faces);
                return (index < 0 ? [] : [index], error);
            default:
                return ([], CamPalette.SoonMessage);
        }
    }

    /// <summary>Motivo de a face não servir de fundo de bolsão (null = serve).</summary>
    private static string? PocketError(MeshFeatures features, MeshFace face, double modelTopZ)
    {
        if (face.Kind != FaceKind.Plane || face.Normal.Z < 0.99)
        {
            return "Escolha uma face plana horizontal voltada para cima (o fundo do bolsão).";
        }

        if (face.Center.Z >= modelTopZ - FaceMenuRules.TopTolerance)
        {
            return "Esta face está no topo da peça: não há o que cavar.";
        }

        return features.BoundaryLoops(face).Count == 0 ? "Não achei o contorno desta face (malha aberta?)." : null;
    }

    /// <summary>Laço de maior área (o contorno de fora); os outros são as ilhas e os furos dentro da face.</summary>
    public static (IReadOnlyList<Point3> Outer, IReadOnlyList<IReadOnlyList<Point3>> Inner) SplitLoops(IReadOnlyList<IReadOnlyList<Point3>> loops)
    {
        int outer = 0;
        double best = -1;
        for (int i = 0; i < loops.Count; i++)
        {
            double a = Math.Abs(Area(loops[i]));
            if (a > best)
            {
                best = a;
                outer = i;
            }
        }

        return (loops[outer], [.. loops.Where((_, i) => i != outer)]);
    }

    private static double Area(IReadOnlyList<Point3> ring)
    {
        double s = 0;
        for (int i = 0; i < ring.Count; i++)
        {
            Point3 a = ring[i], b = ring[(i + 1) % ring.Count];
            s += (a.X * b.Y) - (b.X * a.Y);
        }

        return s / 2;
    }

    private static int AddPocket(CamJob job, MeshFeatures features, MeshFace face, double modelTopZ)
    {
        var geo = new GeoSource([JobGeometry.Of(face)], LoopFilter.All, []);
        (List<Contour> contours, bool islands) = PocketContours(features, face, LoopFilter.All);
        List<int> owned = AppendOwned(job, [], contours);
        CamOperationSpec spec = ToolBankPick.EmptyTool(CamJobInfo.NewOperation(JobOpKind.Pocket, ContourSide.Outside, owned[0]));
        spec = spec with
        {
            Label = CamJobPart.PocketLabel,
            Islands = islands,
            Geometry = geo with { Owned = owned },
            Levels = CamJobPart.AboveTop(spec.Levels, modelTopZ) with { BottomZ = Math.Round(face.Center.Z, 3) },
        };
        job.Operations.Add(spec);
        return job.Operations.Count - 1;
    }

    private static (List<Contour> Contours, bool Islands) PocketContours(MeshFeatures features, MeshFace face, LoopFilter filter)
    {
        (IReadOnlyList<Point3> outer, IReadOnlyList<IReadOnlyList<Point3>> inner) = SplitLoops(features.BoundaryLoops(face));
        var contours = new List<Contour> { CamJobPart.Polygon(outer) };
        bool islands = filter == LoopFilter.All && inner.Count > 0;
        if (islands)
        {
            contours.AddRange(inner.Select(CamJobPart.Polygon));
        }

        return (contours, islands);
    }

    private static (int Index, string? Error) AddDrilling(CamJob job, IReadOnlyList<MeshFace> holes)
    {
        if (holes.Count == 0)
        {
            return (-1, "Escolha furos verticais (Ctrl soma).");
        }

        if (holes.Any(h => h.Kind != FaceKind.Hole || h.Axis is not { } a || Math.Abs(a.Z) < 0.99))
        {
            return (-1, "Só furos verticais saem em furação a partir do 3D.");
        }

        List<int> owned = AppendOwned(job, [], holes.Select(CircleOf).ToList());
        double top = holes.Max(h => h.Box.Max.Z);
        CamOperationSpec spec = ToolBankPick.EmptyTool(CamJobInfo.NewOperation(JobOpKind.Drilling, ContourSide.Outside, -1));
        spec = spec with
        {
            Label = CamJobPart.DrillLabel,
            ContourIndices = owned,
            Levels = CamJobPart.AboveTop(spec.Levels, top),
            Geometry = new GeoSource([.. holes.Select(JobGeometry.Of)], LoopFilter.All, owned),
        };
        double low = holes.Min(h => h.Box.Min.Z), high = holes.Max(h => h.Box.Min.Z);
        spec = high - low <= 0.01
            ? spec with { Levels = spec.Levels with { BottomZ = Math.Round(low, 3) } }
            : CamJobInfo.SetMissing(spec, CamJobPart.BottomField, true);
        job.Operations.Add(spec);
        return (job.Operations.Count - 1, null);
    }

    private static Contour CircleOf(MeshFace h) => CamJobPart.Circle(new Point2(h.Center.X, h.Center.Y), h.Diameter / 2);

    /// <summary>Acrescenta contornos ao trabalho e devolve os índices (os da operação).</summary>
    private static List<int> AppendOwned(CamJob job, IReadOnlyList<int> old, IReadOnlyList<Contour> contours, int ownerOp = -1)
    {
        var owned = new List<int>();
        for (int i = 0; i < contours.Count; i++)
        {
            if (i < old.Count && old[i] >= 0 && old[i] < job.Contours.Count)
            {
                job.Contours[old[i]] = contours[i];
                owned.Add(old[i]);
            }
            else
            {
                job.Contours.Add(contours[i]);
                owned.Add(job.Contours.Count - 1);
            }
        }

        // sobras no fim da lista de contornos saem; no meio ficam (outras operações usam os índices depois delas)
        for (int i = old.Count - 1; i >= contours.Count; i--)
        {
            if (old[i] == job.Contours.Count - 1 && !job.Operations.Where((_, k) => k != ownerOp).Any(o => UsesContour(o, old[i])))
            {
                job.Contours.RemoveAt(old[i]);
            }
        }

        return owned;
    }

    private static bool UsesContour(CamOperationSpec o, int index) =>
        o.ContourIndex == index || (o.ContourIndices?.Contains(index) ?? false) || (o.Geometry?.Owned.Contains(index) ?? false);

    /// <summary>
    /// Troca a geometria da operação (Trocar seleção, Adicionar, Remover, filtro de laços): lê o contorno da malha de novo e substitui
    /// os contornos dela. Mantém T, avanços e alturas. Devolve o motivo se não der; nada muda nesse caso.
    /// </summary>
    public static string? Update(CamJob job, int index, MeshFeatures features, IReadOnlyList<MeshFace> faces, IReadOnlyList<MeshEdge> edges, LoopFilter loops, double modelTopZ)
    {
        CamOperationSpec spec = job.Operations[index];
        GeoSource old = spec.Geometry ?? new GeoSource([], LoopFilter.All, []);
        switch (spec.Kind)
        {
            case JobOpKind.Pocket:
                if (faces.Count != 1)
                {
                    return "O bolsão usa uma face (o fundo). Para mais fundos, crie outra operação.";
                }

                if (PocketError(features, faces[0], modelTopZ) is { } why)
                {
                    return why;
                }

                (List<Contour> contours, bool islands) = PocketContours(features, faces[0], loops);
                List<int> owned = AppendOwned(job, old.Owned, contours, index);
                job.Operations[index] = spec with
                {
                    ContourIndex = owned[0],
                    Islands = islands,
                    Geometry = new GeoSource([JobGeometry.Of(faces[0])], loops, owned),
                };
                return null;
            case JobOpKind.Drilling:
                if (faces.Count == 0 || faces.Any(h => h.Kind != FaceKind.Hole || h.Axis is not { } a || Math.Abs(a.Z) < 0.99))
                {
                    return faces.Count == 0 ? "Escolha furos verticais (Ctrl soma)." : "Só furos verticais saem em furação a partir do 3D.";
                }

                List<int> ownedHoles = AppendOwned(job, old.Owned, faces.Select(CircleOf).ToList(), index);
                job.Operations[index] = spec with
                {
                    ContourIndices = ownedHoles,
                    Geometry = new GeoSource([.. faces.Select(JobGeometry.Of)], loops, ownedHoles),
                };
                return null;
            default:
                return CamPalette.SoonMessage;
        }
    }

    /// <summary>Z do fundo que a seleção mostra (referência para "Pegar do modelo"). Null se não há uma resposta única.</summary>
    public static double? ModelBottomZ(JobOpKind kind, IReadOnlyList<MeshFace> faces) => kind switch
    {
        JobOpKind.Pocket when faces.Count == 1 => Math.Round(faces[0].Center.Z, 3),
        JobOpKind.Drilling when faces.Count > 0 && faces.Max(f => f.Box.Min.Z) - faces.Min(f => f.Box.Min.Z) <= 0.01 => Math.Round(faces.Min(f => f.Box.Min.Z), 3),
        _ => null,
    };
}
