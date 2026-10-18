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
            return new MaterialSimulationParts(new TurningSimulator(bar, path.Moves, analysis.ToolDiameterOf), null, bar, stock);
        }

        Func<int, ToolProfile> profiles = MillProfiles(setup, analysis.ToolDiameterOf);
        HeightMap map = MillStock(stock, path, profiles, maxCellsPerSide);
        if (setup?.Stock is { IsRound: true } round && stock is not null && round.ToStock(MachineKind.Mill) is not null
            && SetupBinding.EffectiveStock(setup, path.Machine, programText, path.CuttingBounds).Source == StockSource.Setup)
        {
            ApplyRoundShape(map, round);
        }

        return new MaterialSimulationParts(new MaterialSimulator(map, path.Moves, profiles), map, null, stock) { Profiles = profiles };
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
    public Func<int, ToolProfile> Profiles { get; init; } = tool => ToolProfile.Flat(ProgramAnalysis.DefaultToolDiameter);
}
