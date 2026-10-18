using CncStudio.Core.Geometry;
using CncStudio.Core.Rendering;
using CncStudio.Core.Trajectory;

namespace CncStudio.App.Graphics;

/// <summary>Perfil da peça do torno (cópia, para desenhar no plano ZX sem tocar na simulação).</summary>
public sealed record TurningProfile(double MinZ, double SliceLength, float[] Outer, float[] Inner, float InitialOuter);

/// <summary>
/// "Foto" da simulação para a tela, calculada em segundo plano: sólido já renderizado,
/// perfil do torno, posição da ferramenta e o que mostrar no painel de coordenadas.
/// </summary>
public sealed record GraphicsFrame
{
    public required int Generation { get; init; }
    public required GraphicsViewKind View { get; init; }

    /// <summary>Sólido renderizado na vista (null = sem sólido nesta vista/opção).</summary>
    public SolidImage? Image { get; init; }

    /// <summary>Torno: perfil para o plano ZX.</summary>
    public TurningProfile? Profile { get; init; }

    /// <summary>Caixa do bruto (para enquadrar e para o contorno).</summary>
    public required Bounds3 StockBounds { get; init; }

    public required Point3 ToolPosition { get; init; }
    public required double ToolDiameter { get; init; }
    public required int Tool { get; init; }
    public required int MoveIndex { get; init; }
    public required int MoveCount { get; init; }
    public Move? CurrentMove { get; init; }
    public required int LineNumber { get; init; }
    public required bool IsFinished { get; init; }
    public required double Travelled { get; init; }
    public string? Message { get; init; }
    public int[] CollisionLines { get; init; } = [];
    public int CollisionCount { get; init; }

    /// <summary>Vista 3D pela placa de vídeo: foto da cena (null nas outras vistas).</summary>
    public CncStudio.Core.Scene.SceneSnapshot? Scene { get; init; }
}
