using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;
using CncStudio.Core.Scene;
using CncStudio.Core.Setup;
using CncStudio.Core.Simulation;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

public class SetupAxisTests
{
    private static SetupStock BarX() => new() { Kind = SetupStockKind.Round, Axis = SetupAxis.X, X = 0, Y = 0, Z = -25, Height = 60, Diameter = 50 };

    [Fact]
    public void CaixaDoCilindroDeitadoSegueOEixo()
    {
        Bounds3 x = BarX().Extent;
        Assert.Equal((0, -25, -50), (x.Min.X, x.Min.Y, x.Min.Z));
        Assert.Equal((60, 25, 0), (x.Max.X, x.Max.Y, x.Max.Z));

        var y = new SetupStock { Kind = SetupStockKind.Round, Axis = SetupAxis.Y, X = 5, Y = 10, Z = -25, Height = 40, Diameter = 20 };
        Assert.Equal((-5, 10, -35), (y.Extent.Min.X, y.Extent.Min.Y, y.Extent.Min.Z));
        Assert.Equal((15, 50, -15), (y.Extent.Max.X, y.Extent.Max.Y, y.Extent.Max.Z));
    }

    [Fact]
    public void EmPeContinuaComoAntes()
    {
        var round = new SetupStock { Kind = SetupStockKind.Round, X = 10, Y = 20, Z = -15, Height = 15, Diameter = 40 };
        Assert.False(round.IsLying);
        Assert.Equal((-10, 0, -15), (round.Extent.Min.X, round.Extent.Min.Y, round.Extent.Min.Z));
        Assert.Equal((30, 40, 0), (round.Extent.Max.X, round.Extent.Max.Y, round.Extent.Max.Z));
    }

    [Fact]
    public void ContemRespeitaOFuroDoTubo()
    {
        var tube = new SetupStock { Kind = SetupStockKind.Tube, Axis = SetupAxis.X, X = 0, Y = 0, Z = 0, Height = 50, Diameter = 40, InnerDiameter = 20 };
        Assert.True(tube.Contains(10, 15, 0));
        Assert.False(tube.Contains(10, 5, 0));
        Assert.False(tube.Contains(10, 25, 0));
    }

    [Fact]
    public void BrutoDoSetupDaOBlocoQueEnvolveOCilindro()
    {
        Stock block = BarX().ToStock(MachineKind.Mill)!;
        Assert.Equal((0, 60, -25, 25, -50, 0), (block.MinX, block.MaxX, block.MinY, block.MaxY, block.MinZ, block.MaxZ));
    }

    [Fact]
    public void EixoSobreviveAoJsonEDefaultEZ()
    {
        var setup = new MachiningSetup { Stock = BarX() };
        MachiningSetup back = SetupJson.Parse(SetupJson.Serialize(setup)).Setup!;
        Assert.Equal(SetupAxis.X, back.Stock!.Axis);
        Assert.Equal(SetupAxis.Z, new SetupStock().Axis);
    }

    [Fact]
    public void CilindroDeitadoUsaVoxelsEFicaRedondo()
    {
        const string text = "G21 G90 G54\nG00 X-10 Y0 Z30\nG00 Z-1\nG01 X70 F200\nG00 Z30";
        var setup = new MachiningSetup { Stock = BarX() };
        ProgramAnalysis a = ProgramAnalyzer.Analyze(text, Dialect.Fanuc);
        Assert.True(SimulationFactory.UsesVoxels(a.ToolPath, setup));
        Assert.False(SimulationFactory.UsesVoxels(a.ToolPath, null));

        MaterialSimulationParts parts = SimulationFactory.Create(a, setup, text);
        Assert.NotNull(parts.Voxels);
        Assert.Null(parts.Map);
        double cylinder = Math.PI * 25 * 25 * 60;
        Assert.InRange(parts.Voxels!.Volume, cylinder * 0.97, cylinder * 1.03); // redondo, não a caixa (150 000)
        double before = parts.Voxels.Volume;
        parts.Simulation.RunToEnd();
        Assert.True(parts.Voxels.Volume < before - 50);
    }

    [Fact]
    public void CelularUsaVoxelsMaisGrossos()
    {
        const string text = "G21 G90 G54\nG00 X-10 Y0 Z30\nG01 X70 F200";
        var setup = new MachiningSetup { Stock = BarX() };
        ProgramAnalysis a = ProgramAnalyzer.Analyze(text, Dialect.Fanuc);
        double pc = SimulationFactory.Create(a, setup, text, SimulationFactory.DesktopCells).Voxels!.VoxelSize;
        double phone = SimulationFactory.Create(a, setup, text, SimulationFactory.PhoneCells).Voxels!.VoxelSize;
        Assert.True(phone > pc);
    }
}

public class SetupAxisAutoTests
{
    private static SetupStock Auto() => new() { Kind = SetupStockKind.Round, Axis = SetupAxis.Auto, X = -5, Y = 99, Z = 99, Height = 60, Diameter = 50 };

    [Fact]
    public void AutoComAAlinhaEmXNoPivo()
    {
        var frame = new RotaryPartFrame(AY: 3, AZ: -25, AReversed: false, BX: 0, BZ: 0, BReversed: false);
        SetupStock r = Auto().Resolved(frame);
        Assert.Equal(SetupAxis.X, r.Axis);
        Assert.Equal((-5, 3, -25), (r.X, r.Y, r.Z));
        Assert.Equal(-5, r.Extent.Min.X);
        Assert.Equal(55, r.Extent.Max.X);
        Assert.Equal(-50, r.Extent.Min.Z);
    }

    [Fact]
    public void AutoSoComBAlinhaEmYNoPivo()
    {
        var frame = new RotaryPartFrame(AY: 0, AZ: 0, AReversed: false, BX: 7, BZ: -30, BReversed: false) { HasA = false };
        SetupStock r = Auto().Resolved(frame);
        Assert.Equal(SetupAxis.Y, r.Axis);
        Assert.Equal((7, -30), (r.X, r.Z));
        Assert.Equal(99, r.Y); // começo ao longo do eixo continua o informado
    }

    [Fact]
    public void AutoSemMaquinaVaiParaXComOsCamposInformados()
    {
        SetupStock r = Auto().Resolved(null);
        Assert.Equal(SetupAxis.X, r.Axis);
        Assert.Equal((99, 99), (r.Y, r.Z));
    }

    [Fact]
    public void PerfilComANaMesaMarcaTemAESemB()
    {
        var machine = new Cam.MachineProfile { Axes = [new Cam.RotaryAxisConfig { Letter = "A", Carrier = Cam.RotaryCarrier.Table, PivotY = 2, PivotZ = -25 }] };
        RotaryPartFrame frame = RotaryPartFrame.From(machine)!;
        Assert.True(frame.HasA);
        Assert.False(frame.HasB);
    }

    [Fact]
    public void ProgramaComPerfilAUsaVoxelsNoCilindroDoPivo()
    {
        const string text = "G21 G90 G54\nG00 X-10 Y0 Z30\nG00 Z-1\nG01 X70 F200\nG00 Z30";
        var frame = new RotaryPartFrame(AY: 0, AZ: -25, AReversed: false, BX: 0, BZ: 0, BReversed: false);
        ToolPath path = MachineExecutor.Execute(GCodeParser.Parse(text, Dialect.Fanuc), new MachineSettings { PartFrame = frame });
        var setup = new MachiningSetup { Stock = new SetupStock { Kind = SetupStockKind.Round, Axis = SetupAxis.Auto, X = 0, Height = 60, Diameter = 50 } };
        Assert.True(SimulationFactory.UsesVoxels(path, setup));
        VoxelStock v = SimulationFactory.VoxelStockFor(null, path, _ => ToolProfile.Flat(6), setup);
        Assert.Equal(-50, v.Bounds.Min.Z, 6); // centro em Z = -25 (pivô), raio 25
        Assert.InRange(v.Bounds.Max.Z, 0, v.VoxelSize);
        Assert.Equal(-25, v.Bounds.Min.Y, 6);
    }
}
