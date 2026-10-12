using CncStudio.Core.Geometry;
using CncStudio.Core.Simulation;

namespace CncStudio.App.Views;

/// <summary>Vistas da simulação.</summary>
public enum SimulationViewMode
{
    Top,
    Front,
    Side,
    Iso,
}

/// <summary>
/// "Foto" da simulação pronta para desenhar: imagens/perfis calculados numa thread
/// de fundo a partir do HeightMap. A UI só copia para a tela — não lê o HeightMap
/// enquanto o simulador está trabalhando.
/// </summary>
public sealed class SimulationFrame
{
    public required int Generation { get; init; }
    public required SimulationViewMode Mode { get; init; }

    // Geometria do bruto (copiada para não depender do HeightMap vivo)
    public required double OriginX { get; init; }
    public required double OriginY { get; init; }
    public required double Width { get; init; }
    public required double Height { get; init; }
    public required double CellSize { get; init; }
    public required int Columns { get; init; }
    public required int Rows { get; init; }
    public required double TopZ { get; init; }
    public required double BottomZ { get; init; }

    public required Point3 ToolPosition { get; init; }
    public required double ToolDiameter { get; init; }
    public required int Tool { get; init; }
    public required int LineNumber { get; init; }
    public required bool IsFinished { get; init; }

    /// <summary>Vista superior: Columns × Rows pixels.</summary>
    public uint[]? TopPixels { get; init; }

    /// <summary>Vista isométrica.</summary>
    public IsoImage? Iso { get; init; }

    /// <summary>Vistas frontal/lateral: maior altura em cada posição e corte na posição da ferramenta.</summary>
    public float[]? Silhouette { get; init; }
    public float[]? Section { get; init; }

    /// <summary>Comprimento da ferramenta desenhado acima da ponta (mm).</summary>
    public double ToolDrawLength => Math.Max(20, ToolDiameter * 3);

    /// <summary>Captura o estado atual. Deve rodar na mesma thread que mexe no simulador.</summary>
    public static SimulationFrame Capture(MaterialSimulator simulator, SimulationViewMode mode, int generation)
    {
        HeightMap map = simulator.Map;
        uint[]? top = null;
        IsoImage? iso = null;
        float[]? silhouette = null;
        float[]? section = null;
        Point3 tool = simulator.ToolPosition;

        switch (mode)
        {
            case SimulationViewMode.Top:
                top = new uint[map.Columns * map.Rows];
                HeightMapRasterizer.RenderTop(map, top);
                break;

            case SimulationViewMode.Iso:
                // ~1,6 pixel por célula na horizontal, limitado para não gerar imagens enormes
                double pixelsPerMm = Math.Clamp(1.6 / map.CellSize, 0.5, 20);
                iso = HeightMapRasterizer.RenderIso(map, pixelsPerMm);
                break;

            case SimulationViewMode.Front:
                silhouette = new float[map.Columns];
                section = new float[map.Columns];
                HeightMapRasterizer.ComputeFrontProfile(map, tool.Y, silhouette, section);
                break;

            case SimulationViewMode.Side:
                silhouette = new float[map.Rows];
                section = new float[map.Rows];
                HeightMapRasterizer.ComputeSideProfile(map, tool.X, silhouette, section);
                break;
        }

        return new SimulationFrame
        {
            Generation = generation,
            Mode = mode,
            OriginX = map.OriginX,
            OriginY = map.OriginY,
            Width = map.Width,
            Height = map.Height,
            CellSize = map.CellSize,
            Columns = map.Columns,
            Rows = map.Rows,
            TopZ = map.TopZ,
            BottomZ = map.BottomZ,
            ToolPosition = tool,
            ToolDiameter = simulator.CurrentToolDiameter,
            Tool = simulator.CurrentTool,
            LineNumber = simulator.CurrentLineNumber,
            IsFinished = simulator.IsFinished,
            TopPixels = top,
            Iso = iso,
            Silhouette = silhouette,
            Section = section,
        };
    }
}
