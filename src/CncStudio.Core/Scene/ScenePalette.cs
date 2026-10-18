using CncStudio.Core.Rendering;

namespace CncStudio.Core.Scene;

/// <summary>
/// Cores da cena (RGB 0xRRGGBB). <see cref="Realistic"/> é o metal cinza padrão;
/// <see cref="Technical"/> pinta o corte com a cor de cada ferramenta ("modo técnico").
/// </summary>
public sealed class ScenePalette
{
    /// <summary>Cores das ferramentas no modo técnico, em ordem de T1, T2, … (repete depois da última).</summary>
    public static readonly IReadOnlyList<uint> ToolColors =
    [
        0x3F7CC0, // azul
        0xD9822B, // laranja
        0x4FA463, // verde
        0xA85FC4, // roxo
        0xC9B03A, // amarelo
        0x3AAFB0, // turquesa
        0xC2547A, // vinho
        0x7F8C3A, // oliva
    ];

    private ScenePalette(bool byTool)
    {
        ColorByTool = byTool;
    }

    /// <summary>
    /// Material fosco e uniforme (padrão): o bruto e o corte quase da mesma cor, com só uma
    /// diferença suave de profundidade; quem destaca as formas é a luz e as arestas.
    /// </summary>
    public static ScenePalette Realistic { get; } = new(byTool: false)
    {
        Stock = 0xB9BEC4,
        MachinedShallow = 0xCDD1D5,
        MachinedDeep = 0xAEB4BA,
        Section = 0xE0C9A6,
        Fixture = 0x5E6B78,
        FixtureJaw = 0x7C8996,
        Tool = 0xC9D3DC,
        Holder = 0x4A5561,
        Nut = 0x8A949E,
        Coating = 0xD2A93A,
    };

    /// <summary>Modo técnico: bruto bege e corte na cor da ferramenta (mais escuro quanto mais fundo).</summary>
    public static ScenePalette Technical { get; } = new(byTool: true)
    {
        Stock = MillSolidRenderer.StockColor,
        MachinedShallow = 0xA9CDEB,
        MachinedDeep = 0x3F7CC0,
        Section = 0xE8A866,
        Fixture = 0x647789,
        FixtureJaw = 0x8798A6,
        Tool = 0xC7E8FF,
        Holder = 0x526879,
        Nut = 0x7F95A6,
        Coating = 0xD2A93A,
    };

    /// <summary>True no modo técnico.</summary>
    public bool ColorByTool { get; }

    public uint Stock { get; private init; }
    public uint MachinedShallow { get; private init; }
    public uint MachinedDeep { get; private init; }
    public uint Section { get; private init; }
    public uint Fixture { get; private init; }
    public uint FixtureJaw { get; private init; }
    public uint Tool { get; private init; }
    public uint Holder { get; private init; }

    /// <summary>Porca da pinça (ER) do porta-ferramenta.</summary>
    public uint Nut { get; private init; }

    /// <summary>Cor da cobertura da fresa e da broca (tipo TiN, dourado).</summary>
    public uint Coating { get; private init; }

    /// <summary>Cor das arestas marcadas por cima das superfícies (traço fino, cinza escuro: sobre a peça clara dá contraste sem pesar).</summary>
    public uint Edge { get; private init; } = 0x59626C;

    public uint FeedPath { get; private init; } = 0x58A6FF;
    public uint RapidPath { get; private init; } = 0xE3B341;
    public uint RapidInMaterial { get; private init; } = 0xF85149;

    /// <summary>Movimento em execução (traço curto sobre a peça).</summary>
    public uint CurrentPath { get; private init; } = 0x00A878;

    /// <summary>Cor fixa de cada papel (o material usinado usa <see cref="Machined"/>).</summary>
    public uint Of(SceneRole role) => role switch
    {
        SceneRole.Material => Stock,
        SceneRole.Section => Section,
        SceneRole.Fixture => Fixture,
        SceneRole.FixtureJaw => FixtureJaw,
        SceneRole.Tool => Tool,
        SceneRole.Holder => Holder,
        SceneRole.FeedPath => FeedPath,
        SceneRole.RapidPath => RapidPath,
        SceneRole.CurrentPath => CurrentPath,
        _ => RapidInMaterial,
    };

    /// <summary>
    /// Cor de uma superfície usinada: "depth" vai de 0 (no topo do bruto) a 1 (no fundo).
    /// No modo técnico, a cor base é a da ferramenta que cortou (tool = número T; 0 = sem número).
    /// </summary>
    public uint Machined(double depth, int tool)
    {
        double t = Math.Sqrt(Math.Clamp(depth, 0, 1));
        if (ColorByTool && tool > 0)
        {
            uint baseColor = ToolColors[(tool - 1) % ToolColors.Count];
            return MillSolidRenderer.Lerp(MillSolidRenderer.Lerp(baseColor, 0xFFFFFF, 0.35), baseColor, t);
        }

        return MillSolidRenderer.Lerp(MachinedShallow, MachinedDeep, t);
    }
}
