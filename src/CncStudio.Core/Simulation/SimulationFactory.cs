using CncStudio.Core.Geometry;
using CncStudio.Core.Scene;
using CncStudio.Core.Execution;
using CncStudio.Core.Setup;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Simulation;

/// <summary>
/// Monta a simulação de remoção de material do mesmo jeito no desktop e na web: bruto efetivo
/// (setup → comentário → automático), perfil de cada ferramenta (setup → diâmetro efetivo da
/// análise) e resolução da grade conforme o aparelho. Uma regra só para as duas cascas.
/// </summary>
public static class SimulationFactory
{
    /// <summary>Células no lado maior da grade da fresa no PC.</summary>
    public const int DesktopCells = 400;

    /// <summary>No celular: grade menor para manter a tela fluida (um quarto das células).</summary>
    public const int PhoneCells = 200;

    /// <summary>Área de desenho com o lado menor abaixo disto (em unidades da tela) conta como celular.</summary>
    public const double PhoneMaxSide = 600;

    /// <summary>Resolução da grade para uma área de desenho de largura × altura (unidades da tela).</summary>
    public static int CellsFor(double width, double height) =>
        Math.Min(width, height) < PhoneMaxSide ? PhoneCells : DesktopCells;

    /// <summary>Perfil de corte de cada ferramenta: o do setup; senão, topo reto com o diâmetro dado.</summary>
    public static Func<int, ToolProfile> MillProfiles(MachiningSetup? setup, Func<int, double> diameterOf) =>
        tool => SetupBinding.ProfileOf(setup, tool, diameterOf(tool));

    /// <summary>Material da fresa: o bruto dado (bloco) ou um que envolve o corte com folga da maior ferramenta.</summary>
    public static HeightMap MillStock(Stock? stock, ToolPath path, Func<int, ToolProfile> profileOf, int maxCellsPerSide = DesktopCells)
    {
        if (stock is { Machine: MachineKind.Mill })
        {
            return HeightMap.CreateForStock(stock, maxCellsPerSide);
        }

        double largest = path.Tools.Count == 0 ? profileOf(0).Diameter : path.Tools.Max(tool => profileOf(tool).Diameter);
        return HeightMap.CreateForToolPath(path, largest, maxCellsPerSide: maxCellsPerSide);
    }

    /// <summary>Bruto redondo ou tubo na fresa: só o círculo (menos o furo do tubo) tem material.</summary>
    public static void ApplyRoundShape(HeightMap map, SetupStock stock)
    {
        double outer = stock.Diameter / 2, inner = stock.Kind == SetupStockKind.Tube ? stock.InnerDiameter / 2 : 0;
        map.SetMaterialShape((x, y) =>
        {
            double d = Math.Sqrt((x - stock.X) * (x - stock.X) + (y - stock.Y) * (y - stock.Y));
            return d <= outer && d >= inner;
        });
    }

    /// <summary>Fresa com eixo rotativo: movimentos com a peça girada (A/B ≠ 0) não cortam o mapa de alturas (3 eixos).</summary>
    public static Func<Move, bool> RotatedMoveSkipper(ToolPath path) =>
        move => move.ModalId >= 0 && move.ModalId < path.Modals.Count && path.Modals[move.ModalId].Rotary.IsRotated;

    /// <summary>Fresa com eixo rotativo e o programa gira a peça (A/B ≠ 0 em algum ponto): o material é em voxels.</summary>
    public static bool NeedsVoxels(ToolPath path) =>
        path.Machine == MachineKind.Mill && path.PartFrame is not null && path.Modals.Any(m => m.Rotary.IsRotated);

    /// <summary>Material em voxels: programa que gira a peça, ou bruto cilíndrico deitado do setup (a grade de alturas só vê de cima).</summary>
    public static bool UsesVoxels(ToolPath path, MachiningSetup? setup) =>
        path.Machine == MachineKind.Mill && (NeedsVoxels(path) || (setup?.Stock is { IsLying: true } lying && lying.Validate() is null));

    /// <summary>Voxels no lado maior e no total: PC e celular (mesma regra de <see cref="CellsFor"/>).</summary>
    public static (int PerSide, long Total) VoxelLimits(int maxCellsPerSide) =>
        maxCellsPerSide >= DesktopCells ? (256, 12_000_000) : (96, 1_000_000);

    /// <summary>
    /// Bruto em voxels, na peça parada: o do setup (bloco, ou cilindro/tubo no eixo escolhido), o bruto dado ou uma
    /// caixa que envolve os cortes com folga da maior ferramenta. O voxel sai de <see cref="VoxelStock.ChooseVoxelSize"/>.
    /// </summary>
    public static VoxelStock VoxelStockFor(Stock? stock, ToolPath path, Func<int, ToolProfile> profileOf,
        MachiningSetup? setup = null, int maxCellsPerSide = DesktopCells)
    {
        Bounds3 box;
        Func<double, double, double, bool>? shape = null;
        if (setup?.Stock is { IsRound: true } round && round.Validate() is null)
        {
            box = round.Extent;
            shape = round.Contains;
        }
        else if (stock is { Machine: MachineKind.Mill, IsValid: true })
        {
            box = stock.Bounds;
        }
        else
        {
            double largest = path.Tools.Count == 0 ? profileOf(0).Diameter : path.Tools.Max(tool => profileOf(tool).Diameter);
            Bounds3 cut = path.CuttingBounds.IsEmpty ? path.Bounds : path.CuttingBounds;
            double margin = (largest / 2) + 1;
            box = cut.IsEmpty
                ? Bounds3.Empty.Include(new Point3(-10, -10, -10)).Include(new Point3(10, 10, 0))
                : Bounds3.Empty.Include(cut.Min - new Point3(margin, margin, margin)).Include(cut.Max + new Point3(margin, margin, margin));
        }

        (int perSide, long total) = VoxelLimits(maxCellsPerSide);
        return new VoxelStock(box, VoxelStock.ChooseVoxelSize(box, perSide, total), shape);
    }

    /// <summary>Barra do torno: a do bruto dado ou uma automática pela trajetória.</summary>
    public static TurningStock LatheStock(Stock? stock, ToolPath path) =>
        stock is { Machine: MachineKind.Lathe } bar ? TurningStock.CreateForStock(bar) : TurningStock.CreateForToolPath(path);

    /// <summary>
    /// Simulação completa para o programa analisado. "Map" ou "Bar" vem preenchido conforme a
    /// máquina (fresa ou torno); a simulação começa no bruto inteiro.
    /// </summary>
    public static MaterialSimulationParts Create(ProgramAnalysis analysis, MachiningSetup? setup, string programText, int maxCellsPerSide = DesktopCells)
    {
        ToolPath path = analysis.ToolPath;
        Stock? stock = SetupBinding.EffectiveStock(setup, path.Machine, programText, path.CuttingBounds).Stock;
        if (path.Machine == MachineKind.Lathe)
        {
            TurningStock bar = LatheStock(stock, path);
            return new MaterialSimulationParts(new TurningSimulator(bar, path.Moves, analysis.ToolDiameterOf, tool => LatheToolShape.CutOf(setup?.ToolOf(tool))), null, bar, stock);
        }

        Func<int, ToolProfile> profiles = MillProfiles(setup, analysis.ToolDiameterOf);
        if (UsesVoxels(path, setup))
        {
            VoxelStock voxels = VoxelStockFor(stock, path, profiles, setup, maxCellsPerSide);
            return new MaterialSimulationParts(new VoxelSimulator(voxels, path, profiles), null, null, stock) { Profiles = profiles, Voxels = voxels };
        }

        HeightMap map = MillStock(stock, path, profiles, maxCellsPerSide);
        if (setup?.Stock is { IsRound: true } round && stock is not null && round.ToStock(MachineKind.Mill) is not null
            && SetupBinding.EffectiveStock(setup, path.Machine, programText, path.CuttingBounds).Source == StockSource.Setup)
        {
            ApplyRoundShape(map, round);
        }

        return new MaterialSimulationParts(new MaterialSimulator(map, path.Moves, profiles, RotatedMoveSkipper(path)), map, null, stock) { Profiles = profiles };
    }
}

/// <summary>O que <see cref="SimulationFactory.Create"/> monta.</summary>
/// <param name="Simulation">Simulação (fresa ou torno).</param>
/// <param name="Map">Material da fresa (null no torno).</param>
/// <param name="Bar">Barra do torno (null na fresa).</param>
/// <param name="Stock">Bruto efetivo usado (null = automático pela trajetória).</param>
public sealed record MaterialSimulationParts(IMaterialSimulation Simulation, HeightMap? Map, TurningStock? Bar, Stock? Stock)
{
    /// <summary>Perfil de cada ferramenta da fresa (para desenhar a ferramenta igual ao que corta).</summary>
    /// <summary>Material em voxels (fresa com peça girada ou bruto cilíndrico deitado); quando preenchido, <c>Map</c> é null.</summary>
    public VoxelStock? Voxels { get; init; }

    public Func<int, ToolProfile> Profiles { get; init; } = tool => ToolProfile.Flat(ProgramAnalysis.DefaultToolDiameter);
}
