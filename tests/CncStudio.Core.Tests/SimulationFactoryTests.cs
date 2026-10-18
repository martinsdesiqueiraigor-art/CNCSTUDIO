using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Setup;
using CncStudio.Core.Simulation;

namespace CncStudio.Core.Tests;

/// <summary>Remoção de material igual no desktop e na web (Fase 1, S3).</summary>
public class SimulationFactoryTests
{
    private static string Exemplo(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", name));

    private static MachiningSetup SetupComBrutoEFerramenta() => new()
    {
        Stock = new SetupStock { Kind = SetupStockKind.Prism, X = 0, Y = 0, Z = -20, Width = 100, Depth = 60, Height = 20 },
        Tools = [new SetupTool { Number = 1, Kind = ToolKind.BullNoseEndMill, Diameter = 10, CornerRadius = 1 }],
    };

    /// <summary>
    /// A web usa <see cref="SimulationFactory.Create"/>; o desktop monta as mesmas peças
    /// (MillProfiles + MillStock) a partir do bruto efetivo. O material final tem de ser idêntico.
    /// </summary>
    [Fact]
    public void WebEDesktopChegamAoMesmoMaterialEAosMesmosRapidosNoMaterial()
    {
        string text = Exemplo("EXEMPLO.nc");
        MachiningSetup setup = SetupComBrutoEFerramenta();
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(text, Dialect.Fanuc, setup.ApplyTo(MachineSettings.Default));

        // Web: tudo pela fábrica, em fatias
        MaterialSimulationParts web = SimulationFactory.Create(analysis, setup, text, SimulationFactory.PhoneCells);
        var driver = new SimulationDriver(web.Simulation) { Target = analysis.ToolPath.Moves.Count };
        int slices = 0;
        while (!driver.AtTarget)
        {
            driver.Run(TimeSpan.FromMilliseconds(2));
            slices++;
        }

        Assert.True(slices > 1);

        // Desktop: as peças da fábrica montadas pela casca, execução direta
        Stock? stock = SetupBinding.EffectiveStock(setup, analysis.ToolPath.Machine, text, analysis.ToolPath.CuttingBounds).Stock;
        Func<int, ToolProfile> profiles = SimulationFactory.MillProfiles(setup, analysis.ToolDiameterOf);
        HeightMap desktopMap = SimulationFactory.MillStock(stock, analysis.ToolPath, profiles, SimulationFactory.PhoneCells);
        var desktop = new MaterialSimulator(desktopMap, analysis.ToolPath.Moves, profiles);
        desktop.RunToEnd();

        Assert.NotNull(web.Map);
        Assert.Equal(desktopMap.Columns, web.Map!.Columns);
        Assert.Equal(desktopMap.Heights.ToArray(), web.Map.Heights.ToArray());
        Assert.Equal(desktop.RapidCollisions, web.Simulation.RapidCollisions);
        Assert.Equal(ToolTipShape.BullNose, web.Profiles(1).Shape);
        Assert.Contains(web.Map.Heights.ToArray(), h => h < 0);
    }

    [Fact]
    public void TornoTambemPelaFabrica()
    {
        string text = Exemplo(Path.Combine("torno", "EXEMPLO_TORNO.nc"));
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(text, Dialect.Fanuc);
        MaterialSimulationParts parts = SimulationFactory.Create(analysis, null, text);
        Assert.NotNull(parts.Bar);
        Assert.Null(parts.Map);
        Assert.IsType<TurningSimulator>(parts.Simulation);

        // Em fatias chega ao mesmo perfil que de uma vez
        var driver = new SimulationDriver(parts.Simulation) { Target = analysis.ToolPath.Moves.Count };
        while (!driver.AtTarget)
        {
            driver.Run(TimeSpan.FromMilliseconds(1));
        }

        TurningStock direct = SimulationFactory.LatheStock(parts.Stock, analysis.ToolPath);
        new TurningSimulator(direct, analysis.ToolPath.Moves, analysis.ToolDiameterOf).RunToEnd();
        Assert.Equal(direct.Outer.ToArray(), parts.Bar!.Outer.ToArray());
        Assert.Equal(direct.Inner.ToArray(), parts.Bar.Inner.ToArray());
    }

    [Fact]
    public void DiametroForaDoSetupSegueARegraDaCompensacao()
    {
        const string text = """
            O0001
            T1 M6 (FRESA D6)
            G00 X0 Y0 Z5
            T2 M6 (FRESA D16)
            G00 X0 Y0 Z5
            T3 M6
            G00 X0 Y0 Z5
            M30
            """;

        // Só comentário: T1 = 6, T2 = 16, T3 = padrão (10)
        ProgramAnalysis plain = ProgramAnalyzer.Analyze(text, Dialect.Fanuc);
        Assert.Equal(6, plain.ToolDiameterOf(1));
        Assert.Equal(16, plain.ToolDiameterOf(2));
        Assert.Equal(ProgramAnalysis.DefaultToolDiameter, plain.ToolDiameterOf(3));

        // Tabela vence o comentário; padrão da máquina vale para quem não tem nada
        var settings = MachineSettings.Default with { ToolDiameterOf = t => t == 2 ? 12 : 0, FallbackToolDiameter = 8 };
        ProgramAnalysis withTable = ProgramAnalyzer.Analyze(text, Dialect.Fanuc, settings);
        Assert.Equal(6, withTable.ToolDiameterOf(1));
        Assert.Equal(12, withTable.ToolDiameterOf(2));
        Assert.Equal(8, withTable.ToolDiameterOf(3));

        // Setup vence tudo e dá a forma da ponta
        var setup = new MachiningSetup { Tools = [new SetupTool { Number = 2, Kind = ToolKind.BallEndMill, Diameter = 20 }] };
        ProgramAnalysis withSetup = ProgramAnalyzer.Analyze(text, Dialect.Fanuc, setup.ApplyTo(settings));
        Func<int, ToolProfile> profiles = SimulationFactory.MillProfiles(setup, withSetup.ToolDiameterOf);
        Assert.Equal(new ToolProfile(ToolTipShape.Ball, 10), profiles(2));
        Assert.Equal(ToolProfile.Flat(6), profiles(1));
        Assert.Equal(ToolProfile.Flat(8), profiles(3));
    }

    [Fact]
    public void VoltarRecomecaDoBrutoEChegaAoMesmoMaterial()
    {
        string text = Exemplo("EXEMPLO.nc");
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(text, Dialect.Fanuc);
        MaterialSimulationParts parts = SimulationFactory.Create(analysis, null, text, 120);
        IMaterialSimulation sim = parts.Simulation;
        int middle = analysis.ToolPath.Moves.Count / 2;

        while (!sim.AdvanceToward(middle, 3))
        {
        }

        float[] atMiddle = parts.Map!.Heights.ToArray();
        sim.RunToEnd();
        Assert.NotEqual(atMiddle, parts.Map.Heights.ToArray());

        while (!sim.AdvanceToward(middle, 50))
        {
        }

        Assert.Equal(middle, sim.CurrentMoveIndex);
        Assert.Equal(atMiddle, parts.Map.Heights.ToArray());
    }

    [Fact]
    public void ResolucaoMenorNoCelular()
    {
        Assert.Equal(SimulationFactory.PhoneCells, SimulationFactory.CellsFor(412, 700));
        Assert.Equal(SimulationFactory.PhoneCells, SimulationFactory.CellsFor(900, 400));
        Assert.Equal(SimulationFactory.DesktopCells, SimulationFactory.CellsFor(1280, 700));
    }

    [Fact]
    public void OrcamentoZeroAndaUmPedacoPorVez()
    {
        string text = Exemplo("EXEMPLO.nc");
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(text, Dialect.Fanuc);
        MaterialSimulationParts parts = SimulationFactory.Create(analysis, null, text, 100);
        var driver = new SimulationDriver(parts.Simulation) { Target = analysis.ToolPath.Moves.Count };
        Assert.True(driver.Run(TimeSpan.Zero));
        Assert.False(driver.AtTarget);
        Assert.InRange(parts.Simulation.Travelled, 1e-9, 2 + 1e-9);
    }
}
