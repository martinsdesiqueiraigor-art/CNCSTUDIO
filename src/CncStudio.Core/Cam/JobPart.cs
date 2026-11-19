using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;

namespace CncStudio.Core.Cam;

/// <summary>
/// Peça do Trabalho fresa em STEP (M3D-1): caminho, resumo (caixa e volume da peça já girada) e os giros escolhidos. Nunca a malha:
/// o STEP só é lido para o 3D, sob demanda. As coordenadas da peça girada valem como as do desenho; o zero do programa continua
/// sendo o do cartão Zero.
/// </summary>
public sealed record JobPartStep(string Path, StepStockSummary Summary)
{
    /// <summary>Giros aplicados ao modelo do arquivo, na ordem (vazio = como veio no arquivo).</summary>
    public IReadOnlyList<ModelTurn> Turns { get; init; } = [];

    /// <summary>Topo da peça levado a Z 0 depois dos giros (como o DXF, que fica em Z 0).</summary>
    public bool TopAtZero { get; init; } = true;

    /// <summary>Arquivo que sumiu ou mudou de lugar (<see cref="CamJobPart.VerifyFile"/>). Só da tela: não vai para o arquivo.</summary>
    public string? FileProblem { get; init; }

    /// <summary>Mesma peça (caminho, resumo, giros e topo; o problema do arquivo não conta).</summary>
    public bool SameAs(JobPartStep? other) =>
        other is not null && other.Path == Path && other.Summary == Summary && other.TopAtZero == TopAtZero && other.Turns.SequenceEqual(Turns);
}

/// <summary>Regras da peça STEP do Trabalho (M3D-1) e das operações que nascem de uma face dela (M3D-2).</summary>
public static class CamJobPart
{
    /// <summary>Rótulo das operações criadas a partir do 3D (vai como comentário no G-code: sem acento; o usuário pode trocar).</summary>
    public const string PocketLabel = "BOLSAO 3D";

    public const string DrillLabel = "FUROS 3D";

    /// <summary>Campo do fundo na tela (o mesmo do painel da operação).</summary>
    public const string BottomField = "Z fundo";

    /// <summary>Aviso fixo das operações que vêm do 3D: o contorno e o fundo são lidos da malha.</summary>
    public const string MeshContourNote = "Contorno e Z fundo lidos da malha do 3D: arcos viram trechos retos (G1 curtos). Confira as medidas no desenho.";

    /// <summary>Modelo do arquivo na posição da peça: giros na ordem e, se escolhido, topo em Z 0.</summary>
    public static TriangleMesh Place(TriangleMesh raw, IReadOnlyList<ModelTurn> turns, bool topAtZero)
    {
        TriangleMesh mesh = raw;
        foreach (ModelTurn t in turns)
        {
            mesh = mesh.Turned(t);
        }

        return topAtZero ? mesh.PlacedTopAtZero() : mesh;
    }

    /// <summary>Peça nova (ou girada de novo) a partir do modelo lido do arquivo: o resumo é o da peça já na posição.</summary>
    public static JobPartStep Import(string path, TriangleMesh raw, IReadOnlyList<ModelTurn> turns, bool topAtZero) =>
        new(path, StepStockSummary.From(Place(raw, turns, topAtZero))) { Turns = [.. turns], TopAtZero = topAtZero };

    /// <summary>Confere se o arquivo da peça ainda está no lugar (nunca lê o STEP). Sumiu: marca o problema.</summary>
    public static void VerifyFile(CamJob job, Func<string, bool> exists)
    {
        if (job.PartStep is { } p)
        {
            job.PartStep = p with { FileProblem = exists(p.Path) ? null : "o arquivo não está mais em " + p.Path };
        }
    }

    /// <summary>Nome do arquivo da peça (sem a pasta); null sem peça STEP.</summary>
    public static string? FileName(CamJob job) => job.PartStep?.Path is { } path ? CamJobStock.FileNameOf(path) : null;

    /// <summary>
    /// Motivo de não poder trocar a fonte da peça (DXF ↔ STEP): uma fonte só, porque DXF e STEP não têm referência comum garantida.
    /// Com operações em contornos da outra fonte, pede um trabalho novo. Null = pode.
    /// </summary>
    public static string? CannotSwitch(CamJob job, bool toStep)
    {
        bool hasOps = job.Operations.Count > 0 && job.Contours.Count > 0;
        if (!hasOps)
        {
            return null;
        }

        if (toStep && job.PartStep is null)
        {
            return "O trabalho já tem operações nos contornos do DXF. Abra o STEP num trabalho novo para não misturar DXF e STEP.";
        }

        return !toStep && job.PartStep is not null
            ? "O trabalho já tem operações feitas na peça STEP. Abra o DXF num trabalho novo para não misturar DXF e STEP."
            : null;
    }

    /// <summary>Arquivo precisa da versão 4: peça STEP ou furação com lista de contornos.</summary>
    public static bool NeedsVersion4(CamJob job) => job.PartStep is not null || job.Operations.Any(o => o.ContourIndices is { Count: > 0 });

    /// <summary>
    /// Bolsão a partir de uma face plana para cima (M3D-2): a borda da face vira contorno do trabalho e nasce a operação, com T, Ø,
    /// rotação e avanços vazios e o Z do fundo vazio (o Z da malha é só referência). Z do topo = topo da peça. Devolve o índice da
    /// operação ou o motivo.
    /// </summary>
    public static (int Index, string? Error) AddPocket(CamJob job, MeshFeatures features, MeshFace face, double modelTopZ)
    {
        if (face.Kind != FaceKind.Plane || face.Normal.Z < 0.99)
        {
            return (-1, "Escolha uma face plana horizontal voltada para cima (o fundo do bolsão).");
        }

        if (face.Center.Z >= modelTopZ - FaceMenuRules.TopTolerance)
        {
            return (-1, "Esta face está no topo da peça: não há o que cavar.");
        }

        IReadOnlyList<IReadOnlyList<Point3>> loops = features.BoundaryLoops(face);
        if (loops.Count != 1)
        {
            return (-1, loops.Count == 0 ? "Não achei o contorno desta face (malha aberta?)." : "Esta face tem ilhas ou furos dentro. Bolsão com ilhas ainda não sai do 3D.");
        }

        job.Contours.Add(Polygon(loops[0]));
        int contour = job.Contours.Count - 1;
        CamOperationSpec spec = ToolBankPick.EmptyTool(CamJobInfo.NewOperation(JobOpKind.Pocket, ContourSide.Outside, contour));
        // DESK-01 (ordem do Igor, 18/11): o fundo vem do modelo (Z da face), preenchido para conferir
        spec = spec with { Label = PocketLabel, Levels = AboveTop(spec.Levels, modelTopZ) with { BottomZ = Math.Round(face.Center.Z, 3) } };
        job.Operations.Add(spec);
        return (job.Operations.Count - 1, null);
    }

    /// <summary>
    /// Furação a partir de furos verticais escolhidos (M3D-2): cada furo vira um círculo do trabalho, e uma operação só fura todos
    /// (lista de contornos). T, Ø, rotação, avanços e o Z do fundo nascem vazios; Z do topo = topo dos furos.
    /// </summary>
    public static (int Index, string? Error) AddDrilling(CamJob job, IReadOnlyList<MeshFace> holes)
    {
        if (holes.Count == 0)
        {
            return (-1, "Escolha furos verticais (Ctrl soma).");
        }

        if (holes.Any(h => h.Kind != FaceKind.Hole || h.Axis is not { } a || Math.Abs(a.Z) < 0.99))
        {
            return (-1, "Só furos verticais saem em furação a partir do 3D.");
        }

        var indices = new List<int>();
        foreach (MeshFace h in holes)
        {
            job.Contours.Add(Circle(new Point2(h.Center.X, h.Center.Y), h.Diameter / 2));
            indices.Add(job.Contours.Count - 1);
        }

        double top = holes.Max(h => h.Box.Max.Z);
        CamOperationSpec spec = ToolBankPick.EmptyTool(CamJobInfo.NewOperation(JobOpKind.Drilling, ContourSide.Outside, -1));
        spec = spec with { Label = DrillLabel, ContourIndices = indices, Levels = AboveTop(spec.Levels, top) };
        // DESK-01 (ordem do Igor, 18/11): o fundo vem do modelo quando todos os furos têm o mesmo fundo; fundos diferentes ficam vazios
        double low = holes.Min(h => h.Box.Min.Z), high = holes.Max(h => h.Box.Min.Z);
        spec = high - low <= 0.01
            ? spec with { Levels = spec.Levels with { BottomZ = Math.Round(low, 3) } }
            : CamJobInfo.SetMissing(spec, BottomField, true);
        job.Operations.Add(spec);
        return (job.Operations.Count - 1, null);
    }

    /// <summary>
    /// Alturas da operação nova medidas a partir do topo lido (as mesmas folgas da operação nova da faixa, que contam a partir de Z 0):
    /// Z seguro e de aproximação ficam acima do topo; o fundo fica vazio (marcado à parte).
    /// </summary>
    private static CamLevels AboveTop(CamLevels l, double top) =>
        l with { TopZ = top, SafeZ = top + (l.SafeZ - l.TopZ), ClearanceZ = top + (l.ClearanceZ - l.TopZ), BottomZ = 0 };

    /// <summary>Contorno fechado de trechos retos pelos pontos do laço (em XY).</summary>
    public static Contour Polygon(IReadOnlyList<Point3> ring)
    {
        var segments = new List<Segment2>();
        for (int i = 0; i < ring.Count; i++)
        {
            Point3 a = ring[i], b = ring[(i + 1) % ring.Count];
            if (Math.Abs(a.X - b.X) > 1e-9 || Math.Abs(a.Y - b.Y) > 1e-9)
            {
                segments.Add(Segment2.Line(new Point2(a.X, a.Y), new Point2(b.X, b.Y)));
            }
        }

        return new Contour(segments);
    }

    /// <summary>Círculo inteiro (um arco que começa e termina no mesmo ponto), o mesmo formato do círculo do DXF.</summary>
    public static Contour Circle(Point2 center, double radius)
    {
        var start = new Point2(center.X + radius, center.Y);
        return new Contour([Segment2.Arc(start, start, center, true)]);
    }
}
