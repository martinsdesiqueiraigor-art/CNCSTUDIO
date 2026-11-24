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
        CamJob job, JobOpKind kind, MeshFeatures features, IReadOnlyList<MeshFace> faces, IReadOnlyList<MeshEdge> edges, double modelTopZ, ContourSide side = ContourSide.Outside, LoopFilter loops = LoopFilter.Outer)
    {
        switch (kind)
        {
            case JobOpKind.Contour:
                {
                    List<ContourPiece> pieces = ContourPieces(features, faces, edges, side, loops, out string? why);
                    if (why is not null)
                    {
                        return ([], why);
                    }

                    return ([.. pieces.Select(p => AddContour(job, p, side))], null);
                }

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
            case JobOpKind.Facing:
                if (faces.Count == 0)
                {
                    return ([], "Escolha no 3D a face a aplainar.");
                }

                foreach (MeshFace f in faces)
                {
                    if (FacingError(features, f) is { } why)
                    {
                        return ([], why);
                    }
                }

                return ([.. faces.Select(f => AddFacing(job, features, f, modelTopZ))], null);
            case JobOpKind.Drilling:
                (int index, string? error) = AddDrilling(job, faces);
                return (index < 0 ? [] : [index], error);
            case JobOpKind.Chamfer:
                {
                    // CAM-FACE-02: chanfro pela parede inclinada (largura e ângulo da ponta lidos; profundidade e entrada vazias)
                    if (ChamferWall(features, faces) is { Why: { } chamferWhy })
                    {
                        return ([], chamferWhy);
                    }

                    WallContour wall = ChamferWall(features, faces).Wall!;
                    List<int> owned = AppendOwned(job, [], [wall.Contour]);
                    CamOperationSpec spec = ToolBankPick.EmptyTool(CamJobInfo.NewOperation(JobOpKind.Chamfer, wall.Side, owned[0]));
                    spec = spec with
                    {
                        Label = "Chanfro da parede",
                        Geometry = new GeoSource([JobGeometry.Of(faces[0])], LoopFilter.Outer, owned),
                        Levels = CamJobPart.AboveTop(spec.Levels, wall.TopZ) with { BottomZ = wall.BottomZ },
                        Chamfer = spec.Chamfer! with { Width = wall.ChamferWidth, TipAngle = wall.ChamferTipAngle },
                    };
                    spec = CamJobInfo.SetFieldState(spec, CamJobInfo.ChamferWidthField, false, false);
                    spec = CamJobInfo.SetFieldState(spec, CamJobInfo.TipAngleField, false, false);
                    job.Operations.Add(spec);
                    return ([job.Operations.Count - 1], null);
                }

            default:
                return ([], CamPalette.SoonMessage);
        }
    }

    /// <summary>CAM-FACE-02: a parede inclinada única da seleção que vira chanfro (ou o motivo de não virar).</summary>
    private static (WallContour? Wall, string? Why) ChamferWall(MeshFeatures features, IReadOnlyList<MeshFace> faces)
    {
        if (faces.Count != 1)
        {
            return (null, "Escolha no 3D uma face inclinada (a do chanfro).");
        }

        (WallContour? wall, string? why) = WallSection.Of(features, faces[0]);
        if (wall is null)
        {
            return (null, why);
        }

        return wall.Inclined && wall.ChamferTipAngle is > 0 and < 180
            ? (wall, null)
            : (null, "Chanfro pede uma face inclinada; esta parede é reta (use Contorno).");
    }

    /// <summary>
    /// Por que a operação não pode sair da seleção atual (a dica do botão da faixa): null = serve. Operação que ainda não sai do 3D
    /// diz isso.
    /// </summary>
    public static string? Unavailable(JobOpKind kind, MeshFeatures features, IReadOnlyList<MeshFace> faces, IReadOnlyList<MeshEdge> edges, double modelTopZ, ContourSide side = ContourSide.Outside, LoopFilter loops = LoopFilter.Outer)
    {
        switch (kind)
        {
            case JobOpKind.Contour:
                if (faces.Count + edges.Count == 0)
                {
                    return "Selecione no 3D a aresta ou a face do contorno (Ctrl soma).";
                }

                ContourPieces(features, faces, edges, side, loops, out string? contourWhy);
                return contourWhy;
            case JobOpKind.Pocket:
                if (faces.Count == 0)
                {
                    return "Selecione no 3D a face do fundo do bolsão.";
                }

                return faces.Select(f => PocketError(features, f, modelTopZ)).FirstOrDefault(w => w is not null);
            case JobOpKind.Facing:
                return faces.Count == 0 ? "Selecione no 3D a face a aplainar." : faces.Select(f => FacingError(features, f)).FirstOrDefault(w => w is not null);
            case JobOpKind.Chamfer:
                return ChamferWall(features, faces).Why;
            case JobOpKind.Drilling:
                if (faces.Count == 0)
                {
                    return "Selecione no 3D os furos (Ctrl soma).";
                }

                return faces.Any(h => h.Kind != FaceKind.Hole || h.Axis is not { } a || Math.Abs(a.Z) < 0.99) ? "Só furos verticais saem em furação a partir do 3D." : null;
            default:
                return CamPalette.SoonMessage;
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

    /// <summary>Um contorno lido da seleção, com a impressão do que o originou (face ou aresta).</summary>
    private sealed record ContourPiece(Contour Contour, IReadOnlyList<GeoRef> Refs, LoopFilter Loops)
    {
        /// <summary>CAM-FACE-02: veio de uma parede clicada (alturas lidas da malha).</summary>
        public WallContour? Wall { get; init; }
    }

    /// <summary>
    /// Contornos da seleção: aresta = uma peça; face plana para cima = o laço de fora (filtro Externo/Todos) e/ou os de dentro (Internos/Todos).
    /// Aresta aberta só serve "sobre a linha". Devolve o motivo em <paramref name="why"/> (e nada) se algo não servir.
    /// </summary>
    private static List<ContourPiece> ContourPieces(MeshFeatures features, IReadOnlyList<MeshFace> faces, IReadOnlyList<MeshEdge> edges, ContourSide side, LoopFilter loops, out string? why)
    {
        var pieces = new List<ContourPiece>();
        why = null;
        foreach (MeshEdge e in edges)
        {
            if (e.Points.Count < 2 || e.Points.Max(p => p.Z) - e.Points.Min(p => p.Z) > 0.01)
            {
                why = "A aresta não é plana na horizontal: só arestas num plano Z fixo viram contorno.";
                return [];
            }
        }

        // arestas retas vizinhas (Ctrl soma) se juntam numa cadeia só; círculo inteiro é um contorno por si
        foreach (List<MeshEdge> chain in Chains(edges))
        {
            (Contour contour, bool closed) = ChainContour(chain);
            if (!closed && side != ContourSide.OnLine)
            {
                why = "Aresta aberta: só “Sobre a linha”. Para dentro ou fora, selecione todas as arestas do laço (ou a face).";
                return [];
            }

            pieces.Add(new ContourPiece(contour, [.. chain.Select(JobGeometry.Of)], LoopFilter.All));
        }

        foreach (MeshFace f in faces)
        {
            if (!(f.Kind == FaceKind.Plane && Math.Abs(f.Normal.Z) >= 0.99))
            {
                // CAM-FACE-02: parede (pino, furo, parede reta ou inclinada) = a seção horizontal dela, com o lado do material
                (WallContour? wall, string? wallWhy) = WallSection.Of(features, f);
                if (wall is null)
                {
                    why = wallWhy;
                    return [];
                }

                if (side != ContourSide.OnLine && side != wall.Side)
                {
                    why = wall.Side == ContourSide.Outside
                        ? "O material fica dentro desta parede (pino ou ressalto): o contorno é por fora."
                        : "O material fica fora desta parede (furo ou rebaixo): o contorno é por dentro.";
                    return [];
                }

                pieces.Add(new ContourPiece(wall.Contour, [JobGeometry.Of(f)], LoopFilter.Outer) { Wall = wall });
                continue;
            }

            if (f.Normal.Z < 0.99)
            {
                why = "Escolha uma face plana horizontal voltada para cima (ou as arestas dela).";
                return [];
            }

            IReadOnlyList<IReadOnlyList<Point3>> all = features.BoundaryLoops(f);
            if (all.Count == 0)
            {
                why = "Não achei o contorno desta face (malha aberta?).";
                return [];
            }

            (IReadOnlyList<Point3> outer, IReadOnlyList<IReadOnlyList<Point3>> inner) = SplitLoops(all);
            GeoRef r = JobGeometry.Of(f);
            if (loops != LoopFilter.Inner)
            {
                pieces.Add(new ContourPiece(CamJobPart.Polygon(outer), [r], LoopFilter.Outer));
            }

            if (loops != LoopFilter.Outer)
            {
                pieces.AddRange(inner.Select(l => new ContourPiece(CamJobPart.Polygon(l), [r], LoopFilter.Inner)));
            }
        }

        if (pieces.Count == 0)
        {
            why = "Esta face não tem laço de dentro (furos ou ilhas) para o filtro escolhido.";
        }

        return pieces;
    }

    private const double JoinTolerance = 0.001;

    private static bool Same(Point3 a, Point3 b) => Math.Abs(a.X - b.X) < JoinTolerance && Math.Abs(a.Y - b.Y) < JoinTolerance && Math.Abs(a.Z - b.Z) < JoinTolerance;

    /// <summary>Agrupa arestas que se encontram nas pontas (na ordem em que foram escolhidas); fechadas sozinhas ficam sozinhas.</summary>
    private static List<List<MeshEdge>> Chains(IReadOnlyList<MeshEdge> edges)
    {
        var chains = new List<List<MeshEdge>>();
        var rest = new List<MeshEdge>();
        foreach (MeshEdge e in edges)
        {
            if (e.Closed)
            {
                chains.Add([e]);
            }
            else
            {
                rest.Add(e);
            }
        }

        while (rest.Count > 0)
        {
            var chain = new List<MeshEdge> { rest[0] };
            rest.RemoveAt(0);
            bool grew = true;
            while (grew)
            {
                grew = false;
                for (int i = 0; i < rest.Count; i++)
                {
                    (Point3 head, Point3 tail) = (Ends(chain[0]).Start, Ends(chain[^1]).End);
                    (Point3 a, Point3 b) = (rest[i].Points[0], rest[i].Points[^1]);
                    if (Same(tail, a) || Same(tail, b))
                    {
                        chain.Add(rest[i]);
                    }
                    else if (Same(head, a) || Same(head, b))
                    {
                        chain.Insert(0, rest[i]);
                    }
                    else
                    {
                        continue;
                    }

                    rest.RemoveAt(i);
                    grew = true;
                    break;
                }
            }

            chains.Add(chain);
        }

        return chains;
    }

    /// <summary>Pontas da aresta já na ordem de caminhada da cadeia (a ordem real é resolvida em <see cref="ChainContour"/>).</summary>
    private static (Point3 Start, Point3 End) Ends(MeshEdge e) => (e.Points[0], e.Points[^1]);

    /// <summary>Contorno da cadeia, caminhando de ponta a ponta; devolve se fecha.</summary>
    private static (Contour Contour, bool Closed) ChainContour(List<MeshEdge> chain)
    {
        if (chain.Count == 1 && chain[0].Closed)
        {
            MeshEdge c = chain[0];
            return (c.Kind == EdgeKind.Circle && c.Diameter > 0
                ? CamJobPart.Circle(new Point2(c.Center.X, c.Center.Y), c.Diameter / 2)
                : CamJobPart.Polygon(c.Points), true);
        }

        var pts = new List<Point3>();
        foreach (MeshEdge e in chain)
        {
            IReadOnlyList<Point3> ep = e.Points;
            if (pts.Count == 0)
            {
                // a primeira aresta começa na ponta que não encosta na segunda
                bool startsAtNext = chain.Count > 1 && (Same(ep[0], chain[1].Points[0]) || Same(ep[0], chain[1].Points[^1]));
                pts.AddRange(startsAtNext ? ep.Reverse() : ep);
                continue;
            }

            pts.AddRange(Same(pts[^1], ep[0]) ? ep.Skip(1) : ep.Reverse().Skip(1));
        }

        bool closed = pts.Count > 2 && Same(pts[0], pts[^1]);
        if (closed)
        {
            pts.RemoveAt(pts.Count - 1);
            return (CamJobPart.Polygon(pts), true);
        }

        var segs = new List<Segment2>();
        for (int i = 0; i + 1 < pts.Count; i++)
        {
            if (Math.Abs(pts[i].X - pts[i + 1].X) > 1e-9 || Math.Abs(pts[i].Y - pts[i + 1].Y) > 1e-9)
            {
                segs.Add(Segment2.Line(new Point2(pts[i].X, pts[i].Y), new Point2(pts[i + 1].X, pts[i + 1].Y)));
            }
        }

        return (new Contour(segs), false);
    }

    private static int AddContour(CamJob job, ContourPiece piece, ContourSide side)
    {
        List<int> owned = AppendOwned(job, [], [piece.Contour]);
        CamOperationSpec spec = ToolBankPick.EmptyTool(CamJobInfo.NewOperation(JobOpKind.Contour, side, owned[0]));
        spec = spec with
        {
            Label = "Contorno do 3D",
            Geometry = new GeoSource(piece.Refs, piece.Loops, owned),
        };
        spec = piece.Wall is { } wall
            ? spec with
            {
                // CAM-FACE-02: alturas da parede clicada (como o fundo do bolsão do 3D); a inclinada sai em degraus pela base
                Label = wall.Inclined ? "Contorno pela base" : "Contorno da parede",
                Levels = CamJobPart.AboveTop(spec.Levels, wall.TopZ) with { BottomZ = wall.BottomZ },
            }
            : CamJobInfo.SetMissing(spec, CamJobPart.BottomField, true);
        job.Operations.Add(spec);
        return job.Operations.Count - 1;
    }

    /// <summary>Motivo de a face não servir ao faceamento (null = serve): plana, horizontal, voltada para cima, com contorno.</summary>
    private static string? FacingError(MeshFeatures features, MeshFace face)
    {
        if (face.Kind != FaceKind.Plane || face.Normal.Z < 0.99)
        {
            return "Escolha uma face plana horizontal voltada para cima.";
        }

        return features.BoundaryLoops(face).Count == 0 ? "Não achei o contorno desta face (malha aberta?)." : null;
    }

    private static int AddFacing(CamJob job, MeshFeatures features, MeshFace face, double modelTopZ)
    {
        Contour outer = CamJobPart.Polygon(SplitLoops(features.BoundaryLoops(face)).Outer);
        List<int> owned = AppendOwned(job, [], [outer]);
        CamOperationSpec spec = ToolBankPick.EmptyTool(CamJobInfo.NewOperation(JobOpKind.Facing, ContourSide.Outside, owned[0]));
        spec = spec with
        {
            Label = "Faceamento do 3D",
            Geometry = new GeoSource([JobGeometry.Of(face)], LoopFilter.Outer, owned),
            Levels = CamJobPart.AboveTop(spec.Levels, modelTopZ) with { BottomZ = Math.Round(face.Center.Z, 3) },
        };
        job.Operations.Add(spec);
        return job.Operations.Count - 1;
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
        // CAM-ILHAS-01: o bolsão do 3D nasce no filtro automático (como os CAMs de mercado: ilha só onde a parede sobe)
        (List<Contour> contours, bool islands, PocketBorder border) = PocketContours(features, face, LoopFilter.Auto);
        var geo = new GeoSource([JobGeometry.Of(face)], LoopFilter.Auto, []) { Border = border };
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

    /// <summary>
    /// Contornos do bolsão: o de fora primeiro e, depois, as ilhas. Todos = todo laço de dentro vira ilha; Só o de fora = nenhuma;
    /// Automático = só os laços de dentro cuja parede sobe (furo e escareado ficam de fora) e a borda de fora diz se o bolsão é aberto.
    /// </summary>
    private static (List<Contour> Contours, bool Islands, PocketBorder Border) PocketContours(MeshFeatures features, MeshFace face, LoopFilter filter)
    {
        if (filter == LoopFilter.Auto)
        {
            IReadOnlyList<SidedLoop> sided = features.ClassifiedLoops(face);
            var list = new List<Contour> { CamJobPart.Polygon(sided[0].Points) };
            list.AddRange(sided.Skip(1).Where(l => l.Side == LoopSide.Up).Select(l => CamJobPart.Polygon(l.Points)));
            PocketBorder border = sided[0].Side switch { LoopSide.Up => PocketBorder.Closed, LoopSide.Down => PocketBorder.Open, _ => PocketBorder.Mixed };
            return (list, list.Count > 1, border);
        }

        (IReadOnlyList<Point3> outer, IReadOnlyList<IReadOnlyList<Point3>> inner) = SplitLoops(features.BoundaryLoops(face));
        var contours = new List<Contour> { CamJobPart.Polygon(outer) };
        bool islands = filter == LoopFilter.All && inner.Count > 0;
        if (islands)
        {
            contours.AddRange(inner.Select(CamJobPart.Polygon));
        }

        return (contours, islands, PocketBorder.Closed);
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
        string? error = UpdateCore(job, index, features, faces, edges, loops, modelTopZ);
        if (error is null)
        {
            job.Operations[index] = CamJobInfo.SetMissing(job.Operations[index], ReselectField, false); // geometria nova: não há mais o que reselecionar
        }

        return error;
    }

    private static string? UpdateCore(CamJob job, int index, MeshFeatures features, IReadOnlyList<MeshFace> faces, IReadOnlyList<MeshEdge> edges, LoopFilter loops, double modelTopZ)
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

                (List<Contour> contours, bool islands, PocketBorder border) = PocketContours(features, faces[0], loops);
                List<int> owned = AppendOwned(job, old.Owned, contours, index);
                job.Operations[index] = spec with
                {
                    ContourIndex = owned[0],
                    Islands = islands,
                    Geometry = new GeoSource([JobGeometry.Of(faces[0])], loops, owned) { Border = border },
                };
                return null;
            case JobOpKind.Contour:
                {
                    if (spec.ContourParams is not { } cp)
                    {
                        return "Operação sem parâmetros de contorno.";
                    }

                    if (faces.Count > 1 || (faces.Count == 1 && edges.Count > 0) || faces.Count + edges.Count == 0)
                    {
                        return "O contorno usa uma face ou arestas de um mesmo laço. Para outro laço, crie outra operação.";
                    }

                    List<ContourPiece> pieces = ContourPieces(features, faces, edges, cp.Side, loops is LoopFilter.All or LoopFilter.Auto ? LoopFilter.Outer : loops, out string? cwhy);
                    if (cwhy is not null)
                    {
                        return cwhy;
                    }

                    if (pieces.Count != 1)
                    {
                        return "Esta face tem mais de um laço de dentro: crie uma operação para cada um.";
                    }

                    List<int> ownedContour = AppendOwned(job, old.Owned, [pieces[0].Contour], index);
                    job.Operations[index] = spec with
                    {
                        ContourIndex = ownedContour[0],
                        Geometry = new GeoSource(pieces[0].Refs, pieces[0].Loops, ownedContour),
                    };
                    return null;
                }
            case JobOpKind.Facing:
                if (faces.Count != 1)
                {
                    return "O faceamento usa uma face. Para outra face, crie outra operação.";
                }

                if (FacingError(features, faces[0]) is { } fwhy)
                {
                    return fwhy;
                }

                List<int> ownedFace = AppendOwned(job, old.Owned, [CamJobPart.Polygon(SplitLoops(features.BoundaryLoops(faces[0])).Outer)], index);
                job.Operations[index] = spec with
                {
                    ContourIndex = ownedFace[0],
                    Geometry = new GeoSource([JobGeometry.Of(faces[0])], LoopFilter.Outer, ownedFace),
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
            case JobOpKind.Chamfer:
                {
                    // CAM-FACE-02: trocar a face inclinada do chanfro (só a aresta muda; largura e ângulo digitados ficam)
                    (WallContour? wall, string? cwhy) = ChamferWall(features, faces);
                    if (wall is null || edges.Count > 0)
                    {
                        return cwhy ?? "O chanfro usa uma face inclinada.";
                    }

                    List<int> ownedChamfer = AppendOwned(job, old.Owned, [wall.Contour], index);
                    job.Operations[index] = spec with
                    {
                        ContourIndex = ownedChamfer[0],
                        Geometry = new GeoSource([JobGeometry.Of(faces[0])], LoopFilter.Outer, ownedChamfer),
                    };
                    return null;
                }

            default:
                return CamPalette.SoonMessage;
        }
    }

    /// <summary>
    /// Confere as operações do 3D com o STEP lido (depois de ler a peça ou de ela mudar): a operação cuja face ou aresta não é achada fica
    /// Incompleta ("Falta Geometria": não gera até Reselecionar; salvar continua livre); a que volta a ser achada sai desse estado.
    /// O contorno guardado não muda. Devolve quantas operações estão a reselecionar.
    /// </summary>
    public static int Reconcile(CamJob job, MeshFeatures features)
    {
        int pending = 0;
        for (int i = 0; i < job.Operations.Count; i++)
        {
            if (job.Operations[i].Geometry is not { } geo)
            {
                continue;
            }

            bool lost = JobGeometry.Resolve(features, geo).Lost.Count > 0;
            job.Operations[i] = CamJobInfo.SetMissing(job.Operations[i], ReselectField, lost);
            pending += lost ? 1 : 0;
        }

        return pending;
    }

    /// <summary>Z do fundo que a seleção mostra (referência para "Pegar do modelo"). Null se não há uma resposta única.</summary>
    public static double? ModelBottomZ(JobOpKind kind, IReadOnlyList<MeshFace> faces) => kind switch
    {
        JobOpKind.Pocket or JobOpKind.Facing when faces.Count == 1 => Math.Round(faces[0].Center.Z, 3),
        JobOpKind.Drilling when faces.Count > 0 && faces.Max(f => f.Box.Min.Z) - faces.Min(f => f.Box.Min.Z) <= 0.01 => Math.Round(faces.Min(f => f.Box.Min.Z), 3),
        _ => null,
    };
}
