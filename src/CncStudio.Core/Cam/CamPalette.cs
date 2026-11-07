namespace CncStudio.Core.Cam;

/// <summary>
/// Botão da faixa do Trabalho fresa. Três tipos: operação (<see cref="Kind"/> preenchido), ação de arquivo (<see cref="Action"/>
/// preenchida, ex.: "abrir-dxf") ou "em breve" (<see cref="Soon"/>: aparece apagado, ainda não existe). <see cref="Kind"/> null sem
/// ação nem "em breve" = operação que só existe fora do Trabalho (grupo 3D oculto).
/// </summary>
public sealed record CamPaletteItem(string Id, string Name, string Icon, JobOpKind? Kind, ContourSide Side, string Hint)
{
    /// <summary>Ação de arquivo do botão ("abrir-dxf", "abrir-trabalho", "salvar-trabalho"); null = não é ação.</summary>
    public string? Action { get; init; }

    /// <summary>Ainda não existe: o botão aparece apagado com o selo "em breve" e não faz nada.</summary>
    public bool Soon { get; init; }

    /// <summary>Botão "em breve" (sem operação nem ação).</summary>
    public static CamPaletteItem Later(string id, string name, string icon, string hint) =>
        new(id, name, icon, null, ContourSide.Outside, hint) { Soon = true };

    /// <summary>Botão de ação de arquivo.</summary>
    public static CamPaletteItem File(string id, string name, string icon, string action, string hint) =>
        new(id, name, icon, null, ContourSide.Outside, hint) { Action = action };
}

/// <summary>Grupo rotulado da faixa (Arquivo, Fixação, 2D, Furação, 4º eixo, 3D). <see cref="Visible"/> false = definido, mas fora da tela.</summary>
public sealed record CamPaletteGroup(string Id, string Title, bool Visible, IReadOnlyList<CamPaletteItem> Items);

/// <summary>
/// Faixa de operações do Trabalho fresa (UX-03a): grupos como dados, não fixos na tela. Só catálogo e conferência de
/// pré-requisito; a operação criada é a mesma de <see cref="CamJobInfo.NewOperation"/>.
/// </summary>
public static class CamPalette
{
    /// <summary>Texto do botão "em breve" quando alguém tenta usar.</summary>
    public const string SoonMessage = "Em breve: ainda não existe no Casillas CAM.";

    public static readonly IReadOnlyList<CamPaletteGroup> Groups =
    [
        new("arquivo", "Arquivo", true,
        [
            CamPaletteItem.File("abrir-dxf", "Abrir DXF", "abrir-dxf", "abrir-dxf", "Abre o desenho (DXF) da peça."),
            CamPaletteItem.File("abrir-trabalho", "Abrir", "abrir", "abrir-trabalho", "Abre um trabalho salvo (.casillas-job)."),
            CamPaletteItem.File("salvar-trabalho", "Salvar", "salvar", "salvar-trabalho", "Salva o trabalho (.casillas-job)."),
        ]),
        // hoje o trabalho tem uma fixação e um zero só (Core): os dois botões ficam "em breve"
        new("fixacao", "Fixação", true,
        [
            CamPaletteItem.Later("nova-fixacao", "Nova fixação", "nova-fixacao", "Cada fixação com zero e orientação próprios."),
            CamPaletteItem.Later("zero", "Zero", "zero", "Escolher o zero da fixação pela peça."),
        ]),
        new("2d", "2D", true,
        [
            new("faceamento", "Faceamento", "faceamento", JobOpKind.Facing, ContourSide.Outside, "Aplaina o topo na área do contorno."),
            new("contorno-fora", "Contorno fora", "contorno-fora", JobOpKind.Contour, ContourSide.Outside, "Contorno por fora: a ferramenta passa do lado de fora da linha."),
            new("contorno-dentro", "Contorno dentro", "contorno-dentro", JobOpKind.Contour, ContourSide.Inside, "Contorno por dentro: a ferramenta passa do lado de dentro da linha."),
            new("contorno-linha", "Sobre a linha", "contorno-linha", JobOpKind.Contour, ContourSide.OnLine, "Contorno sobre a linha: o centro da ferramenta segue o desenho."),
            new("bolsao", "Bolsão", "bolsao", JobOpKind.Pocket, ContourSide.Outside, "Esvazia a área dentro do contorno (com ilhas, se houver)."),
            CamPaletteItem.Later("chanfro", "Chanfro", "chanfro", "Chanfro nas arestas do contorno."),
            CamPaletteItem.Later("ranhura", "Ranhura", "ranhura", "Ranhura com a largura da ferramenta."),
        ]),
        new("furacao", "Furação", true,
        [
            new("furacao", "Furação", "furo", JobOpKind.Drilling, ContourSide.Outside, "Fura os círculos do desenho (G81, G83 ou G73)."),
            CamPaletteItem.Later("macho", "Macho", "macho", "Rosca com macho nos furos."),
            CamPaletteItem.Later("mandrilhar", "Mandrilhar", "mandrilhar", "Mandrilhamento dos furos."),
        ]),
        new("4eixo", "4º eixo", true,
        [
            new("texto-cilindro", "Texto no cilindro", "texto-cilindro", JobOpKind.WrapText, ContourSide.Outside, "Grava texto em volta do cilindro. Precisa de eixo rotativo no perfil da máquina."),
        ]),
        // 3D: definido e oculto até o Modelo 3D entrar na árvore (hoje varredura e desbaste ficam na aba Modelo 3D)
        new("3d", "3D", false,
        [
            new("varredura", "Varredura", "varredura", null, ContourSide.Outside, "Acabamento 3D com fresa esférica."),
            new("desbaste-niveis", "Desbaste por níveis", "desbaste-niveis", null, ContourSide.Outside, "Desbaste 3D em níveis de Z."),
        ]),
    ];

    /// <summary>Botões que criam operação (sem ação de arquivo nem "em breve").</summary>
    public static IEnumerable<CamPaletteItem> OperationItems(CamPaletteGroup g) => g.Items.Where(i => i.Kind is not null);

    /// <summary>Grupos que aparecem na tela.</summary>
    public static IEnumerable<CamPaletteGroup> VisibleGroups => Groups.Where(g => g.Visible);

    /// <summary>Botão da faixa que cria esta operação (para ícone e nome curto na árvore); null se nenhum.</summary>
    public static CamPaletteItem? ItemFor(CamOperationSpec s) =>
        Groups.SelectMany(g => g.Items).FirstOrDefault(i => i.Kind == s.Kind && (s.Kind != JobOpKind.Contour || i.Side == (s.ContourParams?.Side ?? ContourSide.Outside)));

    /// <summary>
    /// Contorno onde a operação nova entra: o selecionado na árvore, senão o da operação selecionada, senão o primeiro; -1 sem desenho.
    /// </summary>
    public static int TargetContour(CamJob job, int? selectedContour, int? selectedOpContour)
    {
        int c = selectedContour ?? selectedOpContour ?? -1;
        return c >= 0 && c < job.Contours.Count ? c : job.Contours.Count > 0 ? 0 : -1;
    }

    /// <summary>
    /// Por que o botão não serve à seleção atual (null = serve). Mesmas condições em que a operação daria erro ao gerar:
    /// contorno e bolsão pedem contorno fechado; faceamento pede um contorno; furação pede círculo no desenho.
    /// <paramref name="openChains"/> = cadeias abertas que o DXF trouxe e ficaram fora da Peça: a dica diz isso, não "abra um DXF".
    /// </summary>
    public static string? Unavailable(CamPaletteItem item, CamJob job, int targetContour, int openChains = 0)
    {
        if (item.Soon)
        {
            return SoonMessage;
        }

        if (item.Action is not null)
        {
            return null;
        }

        if (item.Kind is not { } kind)
        {
            return "Ainda não entra no Trabalho: use Mais › Modelo 3D.";
        }

        switch (kind)
        {
            case JobOpKind.Drilling:
                return CamOperations.HolesFromCircles(job.Contours).Count == 0 ? "Nenhum círculo no desenho para furar." : null;
            case JobOpKind.WrapText:
                return null;
        }

        if (targetContour < 0 || targetContour >= job.Contours.Count)
        {
            return openChains > 0
                ? $"Contorno aberto: o DXF só tem {openChains} cadeia(s) aberta(s) (as pontas não se encontram). Feche a cadeia no CAD e abra o DXF de novo."
                : "Abra um DXF, um trabalho com desenho ou desenhe uma forma (item Peça) primeiro.";
        }

        return kind is JobOpKind.Contour or JobOpKind.Pocket && !job.Contours[targetContour].IsClosed
            ? $"Contorno {targetContour + 1} aberto: as pontas não se encontram. Feche a cadeia no DXF."
            : null;
    }

    /// <summary>Operação nova do botão (a mesma de <see cref="CamJobInfo.NewOperation"/>); null para item sem operação no Trabalho.</summary>
    public static CamOperationSpec? NewOperation(CamPaletteItem item, int targetContour, CamTool? tool = null) =>
        item.Kind is { } kind ? CamJobInfo.NewOperation(kind, item.Side, kind is JobOpKind.Drilling or JobOpKind.WrapText ? -1 : targetContour, tool) : null;
}
