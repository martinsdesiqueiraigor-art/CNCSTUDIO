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
    IReadOnlyList<IReadOnlyList<(double X, double Z)>> Holders,
    double Thickness,
    bool ChipBreaker)
{
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
        double bar = Math.Clamp(tool?.HolderDiameter ?? 20, 8, 40);
        LatheToolShape shape = kind switch
        {
            ToolKind.PartExternal or ToolKind.PartInternal => Parting(width, bar),
            ToolKind.ThreadExternal or ToolKind.ThreadInternal => Thread(bar),
            _ => Turning(bar),
        };

        // Interna: espelha em X (o corpo fica do lado do eixo) e inverte a ordem para manter o sentido
        return IsInternal(kind) ? shape.Mirrored(bar) : shape;
    }

    // Inserto rômbico de 80° (12,7 mm), ponta na origem, corpo para +X e +Z; haste por trás
    private static LatheToolShape Turning(double bar)
    {
        double side = TurnInscribed / Math.Sin(80 * Math.PI / 180);
        double bisector = Math.PI / 4, half = 40 * Math.PI / 180;
        (double X, double Z) a = (side * Math.Cos(bisector + half), side * Math.Sin(bisector + half));
        (double X, double Z) b = (side * Math.Cos(bisector - half), side * Math.Sin(bisector - half));
        (double X, double Z) c = (a.X + b.X, a.Z + b.Z);
        double cx = c.X / 2, cz = c.Z / 2;
        return new LatheToolShape(
            [(0, 0), b, c, a],
            [[(cx, cz - bar / 2), (cx + 70, cz - bar / 2), (cx + 70, cz + bar / 2), (cx, cz + bar / 2)]],
            4.76, true);
    }

    // Inserto triangular de 60° cortado na ponta (0,4 mm), com o corpo atrás
    private static LatheToolShape Thread(double bar)
    {
        const double length = 9.5, halfBase = 5.5;
        return new LatheToolShape(
            [(0, -0.2), (length, -halfBase), (length, halfBase), (0, 0.2)],
            [[(length - 0.5, -bar / 2), (length + 65, -bar / 2), (length + 65, bar / 2), (length - 0.5, bar / 2)]],
            3.5, false);
    }

    // Lâmina reta de largura "width": o canto da ponta está na origem e a lâmina ocupa Z de 0 a width
    private static LatheToolShape Parting(double width, double bar)
    {
        const double blade = 14;
        return new LatheToolShape(
            [(0, 0), (blade, 0), (blade, width), (0, width)],
            [
                [(blade - 1, -0.2), (blade + 28, -0.2), (blade + 28, width + 0.2), (blade - 1, width + 0.2)], // lâmina que sai do suporte
                [(blade + 27, -bar / 2 + width / 2), (blade + 75, -bar / 2 + width / 2), (blade + 75, bar / 2 + width / 2), (blade + 27, bar / 2 + width / 2)],
            ],
            Math.Max(2, Math.Min(width, 6)), false);
    }

    /// <summary>Espelho em X; nas ferramentas internas o corpo vira uma barra que sai do furo para +Z.</summary>
    private LatheToolShape Mirrored(double bar)
    {
        static IReadOnlyList<(double X, double Z)> Flip(IReadOnlyList<(double X, double Z)> p) =>
            [.. p.Select(v => (-v.X, v.Z)).Reverse()];

        // A barra interna corre ao longo de Z, para fora do furo, atrás do inserto
        double reach = Insert.Max(p => p.X);
        double start = Insert.Max(p => p.Z);
        double barWidth = bar * 0.6;
        List<IReadOnlyList<(double X, double Z)>> holders =
        [
            [(-(reach + barWidth), start - 0.5), (-reach * 0.55, start - 0.5), (-reach * 0.55, start + 90), (-(reach + barWidth), start + 90)],
        ];
        return this with { Insert = Flip(Insert), Holders = holders };
    }
}
