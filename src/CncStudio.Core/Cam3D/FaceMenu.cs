using System.Globalization;
using CncStudio.Core.Cam;

namespace CncStudio.Core.Cam3D;

/// <summary>
/// Linha do menu da face: o botão do catálogo (<see cref="CamPalette"/>) e, se não serve, o porquê.
/// <paramref name="Related"/> = operação do tipo da seleção (ex.: Faceamento numa face plana para cima): entra no menu curto
/// mesmo apagada, com o motivo.
/// </summary>
public sealed record FaceMenuItem(CamPaletteItem Item, string? Unavailable, bool Related = false)
{
    public bool Fits => Unavailable is null;
}

/// <summary>
/// Menu que abre ao clicar numa face ou aresta na aba Modelo 3D (UX-03b). <see cref="Header"/> = o que foi clicado
/// (ex.: "Face plana · Z = 30,0 mm", cota lida da malha). <see cref="Fits"/> = só o que serve (pode ser vazia);
/// <see cref="Short"/> = menu curto: o que serve e o que seria do tipo mas está apagado (com o motivo);
/// <see cref="All"/> = catálogo inteiro, com o motivo de cada item que não serve.
/// </summary>
public sealed record FaceMenu(string Header, IReadOnlyList<FaceMenuItem> All)
{
    public IReadOnlyList<FaceMenuItem> Fits => [.. All.Where(i => i.Fits)];

    public IReadOnlyList<FaceMenuItem> Short => [.. All.Where(i => i.Fits || i.Related)];
}

/// <summary>
/// Regra "o que serve a cada seleção" do Modelo 3D. Só leitura: não cria trajetória e não muda geometria. Segue as mesmas
/// condições de <see cref="FaceDrilling"/> e <see cref="FacePocket"/> (furo vertical; fundo plano para cima, abaixo do topo,
/// com um contorno só). Faceamento, contornos e bolsão com ilhas saem no Trabalho fresa (STEP-OPS), não desta aba; texto no cilindro não sai do 3D; varredura e desbaste usam o modelo inteiro.
/// </summary>
/// <summary>
/// CAM-FACE-02: linha do menu da face no Trabalho fresa. <paramref name="Adaptive"/> = Bolsão convertido em desbaste adaptativo.
/// Se serve ou não (e o porquê) é do <see cref="CamJobFromGeometry.Unavailable"/>, na hora do clique.
/// </summary>
public sealed record JobFaceItem(string Id, string Label, JobOpKind Kind, ContourSide Side, bool Adaptive = false);

public static class FaceMenuRules
{
    /// <summary>
    /// CAM-FACE-02: menu da face no Trabalho fresa (como os CAMs de mercado). Fundo plano: Faceamento, Bolsão e Desbaste adaptativo.
    /// Furo: Furação e Contorno por dentro. Pino e parede reta: Contorno por fora e por dentro (o lado errado fica apagado com o motivo).
    /// Parede inclinada: Chanfro e Contorno pela base (em degraus). A aba Modelo 3D continua com <see cref="For(MeshFeatures, MeshFace, double)"/>.
    /// </summary>
    public static (string Header, IReadOnlyList<JobFaceItem> Items) ForJob(MeshFeatures features, MeshFace face, double modelTopZ)
    {
        if (face.Kind == FaceKind.Plane && face.Normal.Z >= 0.99)
        {
            return (Header(face, modelTopZ),
            [
                new("faceamento", "Faceamento", JobOpKind.Facing, ContourSide.Outside),
                new("bolsao", "Bolsão", JobOpKind.Pocket, ContourSide.Outside),
                new(CamPalette.AdaptiveId, "Desbaste adaptativo", JobOpKind.Pocket, ContourSide.Outside, Adaptive: true),
            ]);
        }

        (WallContour? wall, _) = WallSection.Of(features, face);
        string z = $"Z {F(face.Box.Max.Z)} → {F(face.Box.Min.Z)}";
        if (wall is { Inclined: true } w)
        {
            string side = w.Side == ContourSide.Outside ? "por fora" : "por dentro";
            return ($"Parede inclinada · {z} · ≈{F(w.ChamferTipAngle / 2)}° da vertical",
            [
                new("chanfro", "Chanfro", JobOpKind.Chamfer, w.Side),
                new(w.Side == ContourSide.Outside ? "contorno-fora" : "contorno-dentro", $"Contorno pela base ({side}, em degraus)", JobOpKind.Contour, w.Side),
            ]);
        }

        string header = face.Kind switch
        {
            FaceKind.Boss => $"Parede de pino Ø {F(face.Diameter)} mm · {z}",
            FaceKind.Hole => $"Parede de furo Ø {F(face.Diameter)} mm · {z}",
            _ when wall is not null => $"Parede reta · {z}",
            _ => Header(face, modelTopZ),
        };
        var items = new List<JobFaceItem>();
        if (face.Kind == FaceKind.Hole)
        {
            items.Add(new("furacao", "Furação", JobOpKind.Drilling, ContourSide.Inside));
        }

        items.Add(new("contorno-fora", "Contorno por fora", JobOpKind.Contour, ContourSide.Outside));
        items.Add(new("contorno-dentro", "Contorno por dentro", JobOpKind.Contour, ContourSide.Inside));
        return (header, items);
    }

    /// <summary>Folga (mm) para dizer que uma face plana está no topo do modelo.</summary>
    public const double TopTolerance = 0.01;

    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Menu de uma região lida da malha; conta os contornos da região em <paramref name="features"/>.</summary>
    public static FaceMenu For(MeshFeatures features, MeshFace face, double modelTopZ) =>
        For(face, face.Kind == FaceKind.Plane ? features.BoundaryLoops(face).Count : 0, modelTopZ);

    /// <summary>Menu de uma região. <paramref name="boundaryLoops"/>: contornos da região (1 = sem ilhas nem furos dentro).</summary>
    public static FaceMenu For(MeshFace face, int boundaryLoops, double modelTopZ)
    {
        string header = Header(face, modelTopZ);
        return new FaceMenu(header, [.. Catalog().Select(item => new FaceMenuItem(item, Why(item.Id, face, boundaryLoops, modelTopZ), IsRelated(item.Id, face)))]);
    }

    /// <summary>
    /// Operações do tipo da seleção, que entram no menu curto mesmo apagadas: numa face plana para cima (topo ou fundo),
    /// Faceamento e Bolsão. Furo inclinado, pino, curva e aresta não têm (menu curto = só o que serve).
    /// </summary>
    private static bool IsRelated(string id, MeshFace f) =>
        f.Kind == FaceKind.Plane && f.Normal.Z >= 0.99 && id is "faceamento" or "bolsao";

    /// <summary>Menu de uma aresta: nada do catálogo sai de uma aresta hoje (o menu mostra só o cabeçalho e "Ver todas").</summary>
    public static FaceMenu For(MeshEdge edge) =>
        new(Header(edge), [.. Catalog().Select(item => new FaceMenuItem(item, Why(item.Id, edge)))]);

    /// <summary>
    /// Catálogo de operações na ordem da faixa, com o grupo 3D (oculto na faixa do Trabalho, mas é desta aba). Arquivo e Fixação
    /// não são operações e ficam fora (UX-04c).
    /// </summary>
    private static IEnumerable<CamPaletteItem> Catalog() =>
        CamPalette.Groups.Where(g => g.Id is not ("arquivo" or "fixacao")).SelectMany(g => g.Items).Where(i => i.Action is null);

    private static string F(double x) => x.ToString("0.0", Pt);

    private static bool IsTop(MeshFace f, double topZ) => f.Normal.Z >= 0.99 && f.Center.Z >= topZ - TopTolerance;

    private static bool IsVerticalHole(MeshFace f) => f.Kind == FaceKind.Hole && f.Axis is { } a && Math.Abs(a.Z) >= 0.99;

    /// <summary>Cabeçalho do menu: tipo da região e a cota que a malha mostra.</summary>
    public static string Header(MeshFace f, double modelTopZ) => f.Kind switch
    {
        FaceKind.Plane when IsTop(f, modelTopZ) => $"Face plana de topo · Z = {F(f.Center.Z)} mm",
        FaceKind.Plane when f.Normal.Z >= 0.99 => $"Face plana · Z = {F(f.Center.Z)} mm",
        FaceKind.Plane when f.Normal.Z <= -0.99 => $"Face plana para baixo · Z = {F(f.Center.Z)} mm",
        FaceKind.Plane when Math.Abs(f.Normal.Z) <= 0.01 => "Face plana vertical",
        FaceKind.Plane => "Face plana inclinada",
        FaceKind.Hole when IsVerticalHole(f) => $"Furo Ø {F(f.Diameter)} mm · fundo na malha Z = {F(f.Box.Min.Z)} mm",
        FaceKind.Hole => $"Furo inclinado Ø {F(f.Diameter)} mm",
        FaceKind.Boss => $"Pino Ø {F(f.Diameter)} mm",
        FaceKind.ConcaveCurve => $"Raio interno Ø {F(f.Diameter)} mm",
        FaceKind.ConvexCurve => $"Arredondamento Ø {F(f.Diameter)} mm",
        _ => "Superfície livre",
    };

    public static string Header(MeshEdge e) => e.Kind switch
    {
        EdgeKind.Line => $"Aresta reta · {F(e.Length)} mm",
        EdgeKind.Circle => $"Aresta circular Ø {F(e.Diameter)} mm",
        EdgeKind.Arc => $"Arco Ø {F(e.Diameter)} mm",
        _ => $"Aresta curva · {F(e.Length)} mm",
    };

    /// <summary>Motivo comum aos itens que não dependem da seleção (null = depende, ver Bolsão e Furação).</summary>
    private static string? Fixed(string id) => id switch
    {
        "faceamento" => "Faceamento pela face sai no Trabalho fresa: abra o STEP lá e clique na face.",
        "contorno-fora" or "contorno-dentro" or "contorno-linha" => "Contorno pela aresta ou face sai no Trabalho fresa: abra o STEP lá e escolha as arestas.",
        "texto-cilindro" => "Texto no cilindro é do Trabalho fresa (4º eixo), não sai do 3D.",
        "varredura" => "Não depende da face: usa o modelo inteiro (botão “Gerar varredura” no painel).",
        "desbaste-niveis" => "Não depende da face: usa o modelo inteiro (botão “Gerar desbaste” no painel).",
        "bolsao" or "furacao" => null,
        _ => "Ainda não sai do 3D.",
    };

    private static string? Why(string id, MeshFace f, int loops, double topZ)
    {
        if (Fixed(id) is { } fixedWhy)
        {
            return fixedWhy;
        }

        if (id == "furacao")
        {
            return f.Kind switch
            {
                FaceKind.Hole when IsVerticalHole(f) => null,
                FaceKind.Hole => "Furo inclinado: sem furação automática (3 eixos).",
                FaceKind.Boss => "Pino é material, não furo.",
                _ => "Furação pede um furo (parede cilíndrica vertical): clique na parede do furo.",
            };
        }

        // Bolsão: as mesmas condições de FacePocket.Build
        return f.Kind switch
        {
            FaceKind.Plane when IsTop(f, topZ) => "Esta face é o topo do modelo: o bolsão precisa de um fundo abaixo do topo.",
            FaceKind.Plane when f.Normal.Z >= 0.99 && loops > 1 => "Bolsão com ilhas sai no Trabalho fresa: abra o STEP lá e clique no fundo do rebaixo.",
            FaceKind.Plane when f.Normal.Z >= 0.99 && loops == 0 => "Não achei o contorno desta região (malha aberta?).",
            FaceKind.Plane when f.Normal.Z >= 0.99 => null,
            FaceKind.Plane when f.Normal.Z <= -0.99 => "Face voltada para baixo: a fresa só chega por cima (3 eixos).",
            FaceKind.Plane => "Bolsão pede fundo plano horizontal voltado para cima; esta face não é horizontal.",
            FaceKind.Hole => "Isto é um furo: use Furação.",
            FaceKind.Boss => "Pino não é fundo de bolsão.",
            _ => "Bolsão a partir do 3D pede fundo plano; esta região é curva.",
        };
    }

    private static string? Why(string id, MeshEdge e) => Fixed(id) ?? (id == "furacao"
        ? e.Kind == EdgeKind.Circle ? "Aresta circular: clique na parede do furo para furar." : "Furação pede um furo, não uma aresta."
        : "Bolsão pede uma face plana (o fundo), não uma aresta.");
}
