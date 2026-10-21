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
    public sealed record HolderPart(IReadOnlyList<(double X, double Z)> Polygon, double Y0, double Y1);

    /// <summary>Seção do cabo de um porta-ferramenta de torneamento (25 × 25 mm, padrão de mercado, ex.: PCLNR 2525).</summary>
    public const double ShankSize = 25;

    /// <summary>Diâmetro da barra de mandrilar (mm).</summary>
    public const double BoringBarSize = 16;

    private static HolderPart Rect(double x0, double x1, double z0, double z1, double y0, double y1) =>
        new([(x0, z0), (x1, z0), (x1, z1), (x0, z1)], y0, y1);

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
    /// o bedame corta a largura inteira do inserto a partir do canto da ponta, para o lado de +Z.
    /// </summary>
    public static CutSpec CutOf(SetupTool? tool) => KindOf(tool) switch
    {
        ToolKind.PartExternal or ToolKind.PartInternal => new CutSpec(Math.Max(0.1, tool!.InsertWidth) / 2, Math.Max(0.1, tool.InsertWidth) / 2),
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
            [Rect(3, 75, 2.5, 2.5 + ShankSize, -thickness / 2 - ShankSize, -thickness / 2)],
            thickness, true);
    }

    // Inserto triangular de 60° (TNMG/16ER) com a ponta cortada em 0,4 mm; cabo de 20 mm centrado no inserto
    private static LatheToolShape Thread()
    {
        const double length = 9.5, halfBase = 5.5, thickness = 3.5, shank = 20;
        return new LatheToolShape(
            [(0, -0.2), (length, -halfBase), (length, halfBase), (0, 0.2)],
            [Rect(length - 1, length + 65, -shank / 2, shank / 2, -thickness / 2 - shank, -thickness / 2)],
            thickness, false);
    }

    // Bedame: lâmina fina de largura "width" com o canto da ponta na origem (ocupa Z de 0 a width),
    // 26 mm de altura (como as lâminas de canal/corte) presa a um bloco de 25 × 25 mm
    private static LatheToolShape Parting(double width)
    {
        const double blade = 14, bladeHeight = 26;
        return new LatheToolShape(
            [(0, 0), (blade, 0), (blade, width), (0, width)],
            [
                Rect(blade - 1, blade + 32, -0.3, width + 0.3, -bladeHeight / 2, bladeHeight / 2),                 // lâmina
                Rect(blade + 31, blade + 85, width / 2 - ShankSize / 2, width / 2 + ShankSize / 2, -ShankSize / 2, ShankSize / 2), // bloco
            ],
            Math.Max(2, Math.Min(width, 6)), false);
    }

    /// <summary>Espelho em X; nas ferramentas internas o corpo é uma barra que corre ao longo de Z, para fora do furo.</summary>
    private LatheToolShape Mirrored()
    {
        static IReadOnlyList<(double X, double Z)> Flip(IReadOnlyList<(double X, double Z)> p) =>
            [.. p.Select(v => (-v.X, v.Z)).Reverse()];

        double reach = Insert.Max(p => p.X);
        double start = Insert.Max(p => p.Z);
        double top = Holders.Min(h => h.Y1); // a barra fica logo abaixo do inserto
        // Bedame interno e rosca interna: a barra fica ao lado do inserto; no torneamento, atrás dele
        HolderPart bar = Rect(-(reach + BoringBarSize * 0.6), -Math.Max(1.5, reach * 0.35), start - 0.5, start + 90, top - BoringBarSize, top);
        return this with { Insert = Flip(Insert), Holders = [bar] };
    }
}
