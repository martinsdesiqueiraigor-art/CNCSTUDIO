using CncStudio.Core.Setup;

namespace CncStudio.Core.Scene;

/// <summary>
/// Desenho de uma ferramenta de torno no plano XZ, com a ponta de corte na origem.
/// X é o raio "para fora" do lado da ponta (a ferramenta externa tem o corpo em +X; a interna,
/// em −X, dentro do furo) e Z cresce para longe da face da peça. Uma só fonte para o desenho 3D
/// (<see cref="SceneMeshes.LatheTool"/>) e o 2D do desktop. Todos os polígonos são quadriláteros convexos.
/// </summary>
/// <param name="Insert">Inserto (pastilha) de corte.</param>
/// <param name="Holders">Corpo da ferramenta: barra, lâmina ou haste.</param>
/// <param name="Thickness">Espessura do inserto (eixo Y da cena), em mm.</param>
/// <param name="ChipBreaker">Desenha o quebra-cavaco (só no torneamento).</param>
public sealed record LatheToolShape(
    IReadOnlyList<(double X, double Z)> Insert,
    IReadOnlyList<LatheToolShape.HolderPart> Holders,
    double Thickness,
    bool ChipBreaker)
{
    /// <summary>
    /// Peça do corpo: polígono no plano XZ e faixa em Y relativa ao plano do inserto (o inserto vai de
    /// −Thickness/2 a +Thickness/2; o corpo comum fica por baixo dele).
    /// </summary>
    public sealed record HolderPart(IReadOnlyList<(double X, double Z)> Polygon, double Y0, double Y1, bool Round = false);

    /// <summary>Seção do cabo de um porta-ferramenta de torneamento (25 × 25 mm, padrão de mercado, ex.: PCLNR 2525).</summary>
    public const double ShankSize = 25;

    /// <summary>Diâmetro da barra de mandrilar (mm).</summary>
    public const double BoringBarSize = 16;

    private static HolderPart Rect(double x0, double x1, double z0, double z1, double y0, double y1, bool round = false) =>
        new([(x0, z0), (x1, z0), (x1, z1), (x0, z1)], y0, y1, round);

    /// <summary>Folga entre o inserto e o topo do cabo, onde fica a sede (calço) do inserto.</summary>
    public const double SeatHeight = 0.8;

    /// <summary>Largura de corte e posição dela em Z: a ponta programada fica em "Offset" do centro do corte.</summary>
    public readonly record struct CutSpec(double HalfWidth, double Offset);

    /// <summary>Raio de ponta padrão do torneamento (mm).</summary>
    public const double NoseRadius = 0.4;

    private const double TurnInscribed = 12.7;

    /// <summary>Tipo de ferramenta do torno; ferramenta sem cadastro vale como torneamento externo.</summary>
    public static ToolKind KindOf(SetupTool? tool) => tool is { IsLathe: true } ? tool.Kind : ToolKind.TurnExternal;

    /// <summary>A ferramenta trabalha dentro do furo (corpo do lado do eixo).</summary>
    public static bool IsInternal(ToolKind kind) => kind is ToolKind.TurnInternal or ToolKind.PartInternal or ToolKind.ThreadInternal;

    /// <summary>
    /// Largura e posição do corte em Z. Torneamento e rosca cortam na ponta (raio de ponta);
    /// o bedame corta a largura inteira do inserto a partir do canto da ponta do lado da face (+Z), para o lado de −Z (em direção ao mandril).
    /// </summary>
    public static CutSpec CutOf(SetupTool? tool) => KindOf(tool) switch
    {
        ToolKind.PartExternal or ToolKind.PartInternal => new CutSpec(Math.Max(0.1, tool!.InsertWidth) / 2, -Math.Max(0.1, tool.InsertWidth) / 2),
        ToolKind.ThreadExternal or ToolKind.ThreadInternal => new CutSpec(0.2, 0),
        _ => new CutSpec(NoseRadius, 0),
    };

    public static LatheToolShape Of(SetupTool? tool)
    {
        ToolKind kind = KindOf(tool);
        double width = Math.Max(0.5, tool?.InsertWidth ?? SetupTool.DefaultInsertWidth);
        LatheToolShape shape = kind switch
        {
            ToolKind.PartExternal or ToolKind.PartInternal => Parting(width),
            ToolKind.ThreadExternal or ToolKind.ThreadInternal => Thread(),
            _ => Turning(),
        };

        // Interna: espelha em X (o corpo fica do lado do eixo) e troca o corpo por uma barra que sai do furo
        return IsInternal(kind) ? shape.Mirrored() : shape;
    }

    // Inserto rômbico de 80° (CNMG 12 = 12,7 mm), ponta na origem; cabo quadrado de 25 mm atrás dele (como um PCLNR 2525)
    private static LatheToolShape Turning()
    {
        double side = TurnInscribed / Math.Sin(80 * Math.PI / 180);
        double bisector = Math.PI / 4, half = 40 * Math.PI / 180;
        (double X, double Z) a = (side * Math.Cos(bisector + half), side * Math.Sin(bisector + half));
        (double X, double Z) b = (side * Math.Cos(bisector - half), side * Math.Sin(bisector - half));
        (double X, double Z) c = (a.X + b.X, a.Z + b.Z);
        const double thickness = 4.76;
        return new LatheToolShape(
            [(0, 0), b, c, a],
            [
                // Cabeça: afina da seção do cabo até o canto do inserto (como a cabeça de um PCLNR)
                new HolderPart([(1.4, 3.2), (28, 2.5), (28, 2.5 + ShankSize), (3.4, 15.5)], -thickness / 2 - SeatHeight - ShankSize, -thickness / 2 - SeatHeight),
                Rect(27, 80, 2.5, 2.5 + ShankSize, -thickness / 2 - SeatHeight - ShankSize, -thickness / 2 - SeatHeight),
            ],
            thickness, true);
    }

    // Inserto triangular de 60° (TNMG/16ER) com a ponta cortada em 0,4 mm; cabo de 20 mm centrado no inserto
    private static LatheToolShape Thread()
    {
        const double length = 9.5, halfBase = 5.5, thickness = 3.5, shank = 20;
        return new LatheToolShape(
            [(0, -0.2), (length, -halfBase), (length, halfBase), (0, 0.2)],
            [
                new HolderPart([(1.2, -6), (20, -shank / 2), (20, shank / 2), (1.2, 6)], -thickness / 2 - SeatHeight - shank, -thickness / 2 - SeatHeight),
                Rect(19, 80, -shank / 2, shank / 2, -thickness / 2 - SeatHeight - shank, -thickness / 2 - SeatHeight),
            ],
            thickness, false);
    }

    // Bedame (como um porta-lâmina de canal tipo DGTR 25B, 25 × 25 mm): inserto de metal duro de largura "width" com o canto da
    // ponta (lado da face, +Z) na origem (ocupa Z de −width a 0), assentado no topo de uma cabeça fina (um pouco mais fina que o
    // inserto, para entrar no canal), com a garra atrás dele. Atrás da cabeça o corpo alarga a 45° só para o lado +Z (oposto
    // ao mandril) até o cabo de 25 × 25 mm, com o parafuso de fixação na frente do cabo. Medidas ilustrativas, não cotas de fabricante.
    private const double PartingInsertLength = 14, PartingHeadLength = 32, PartingHeight = 25, PartingBodyLength = 140;

    private readonly record struct PartingLayout(double Top, double Thickness, double Z0, double Z1, double Widen)
    {
        public static PartingLayout Of(double width, double insertHeight)
        {
            double thickness = Math.Max(0.4, width - 2 * Math.Min(0.2, width * 0.1)); // cabeça mais fina que o inserto (ex.: 3 → 2,6)
            double z0 = -width / 2 - thickness / 2;                                    // cabeça centrada no inserto
            return new PartingLayout(-insertHeight / 2, thickness, z0, z0 + thickness, ShankSize - thickness);
        }
    }

    private static LatheToolShape Parting(double width)
    {
        const double insertHeight = 8;
        PartingLayout l = PartingLayout.Of(width, insertHeight);
        double bottom = l.Top - PartingHeight + insertHeight; // cabeça e cabo com 25 mm de altura no total, abaixo do inserto
        double bodyStart = PartingHeadLength + l.Widen;
        return new LatheToolShape(
            [(0, -width), (PartingInsertLength, -width), (PartingInsertLength, 0), (0, 0)],
            [
                Rect(1.5, PartingHeadLength, l.Z0, l.Z1, bottom, l.Top),                                          // cabeça (sob o inserto)
                Rect(PartingInsertLength, PartingHeadLength, l.Z0, l.Z1, l.Top, insertHeight / 2 - 1),            // garra atrás do inserto
                new HolderPart([(PartingHeadLength, l.Z0), (bodyStart, l.Z0), (bodyStart, l.Z0 + ShankSize), (PartingHeadLength, l.Z1)],
                    bottom, insertHeight / 2 + 2),                                                                // alargamento a 45°
                Rect(bodyStart, PartingBodyLength, l.Z0, l.Z0 + ShankSize, bottom, insertHeight / 2 + 2),         // cabo 25 × 25
            ],
            insertHeight, false);
    }

    /// <summary>Centro (X, Z) e altura do topo do cabo do bedame, onde fica o parafuso de fixação.</summary>
    public static (double X, double Z, double Top) PartingBlockTop(double width)
    {
        PartingLayout l = PartingLayout.Of(width, 8);
        return (PartingHeadLength + l.Widen + 10, l.Z0 + ShankSize / 2, 4 + 2);
    }

    /// <summary>Espelho em X; nas ferramentas internas o corpo é uma barra que corre ao longo de Z, para fora do furo.</summary>
    private LatheToolShape Mirrored()
    {
        static IReadOnlyList<(double X, double Z)> Flip(IReadOnlyList<(double X, double Z)> p) =>
            [.. p.Select(v => (-v.X, v.Z)).Reverse()];

        double reach = Insert.Max(p => p.X);
        double center = -(reach * 0.45 + BoringBarSize / 2);
        HolderPart bar = Rect(center - BoringBarSize / 2, center + BoringBarSize / 2, 3.5, 100, -BoringBarSize / 2, BoringBarSize / 2, round: true);
        return this with { Insert = Flip(Insert), Holders = [bar] };
    }
}
