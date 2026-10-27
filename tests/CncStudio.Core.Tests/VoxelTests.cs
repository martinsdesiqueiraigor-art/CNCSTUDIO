using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;
using CncStudio.Core.Rendering;
using CncStudio.Core.Scene;
using CncStudio.Core.Simulation;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

public class VoxelTests
{
    private static readonly RotaryPartFrame Frame = new(AY: 0, AZ: -25, AReversed: false, BX: 0, BZ: -25, BReversed: false);

    private static VoxelStock Box(double voxel = 0.25) =>
        new(Bounds3.Empty.Include(new Point3(0, 0, -10)).Include(new Point3(20, 20, 0)), voxel);

    [Fact]
    public void BrutoInteiroTemOVolumeDoBloco()
    {
        Assert.Equal(20 * 20 * 10, Box().Volume, 3);
    }

    [Fact]
    public void FuroVerticalTiraUmCilindro()
    {
        VoxelStock stock = Box();
        Assert.True(stock.Carve(new Point3(10, 10, -4), new Point3(0, 0, 1), ToolProfile.Flat(6), 25));
        Assert.InRange(4000 - stock.Volume, 113 * 0.95, 113 * 1.05);
    }

    [Fact]
    public void FerramentaDeLadoTiraUmCilindroHorizontal()
    {
        VoxelStock stock = Box();
        stock.Carve(new Point3(0, 10, -5), new Point3(1, 0, 0), ToolProfile.Flat(4), 25);
        Assert.InRange(4000 - stock.Volume, Math.PI * 4 * 20 * 0.95, Math.PI * 4 * 20 * 1.05);
    }

    [Fact]
    public void FerramentaEsfericaDeixaFundoRedondo()
    {
        VoxelStock flat = Box(), ball = Box();
        flat.Carve(new Point3(10, 10, -4), new Point3(0, 0, 1), ToolProfile.Flat(6), 25);
        ball.Carve(new Point3(10, 10, -4), new Point3(0, 0, 1), new ToolProfile(ToolTipShape.Ball, 3), 25);
        Assert.True(ball.Volume > flat.Volume + 20); // a esférica tira menos (a ponta é uma meia esfera)
    }

    [Fact]
    public void ForaDoBrutoNaoTiraNada()
    {
        VoxelStock stock = Box();
        Assert.False(stock.Carve(new Point3(100, 100, 100), new Point3(0, 0, 1), ToolProfile.Flat(6), 25));
        Assert.Equal(4000, stock.Volume, 3);
    }

    [Fact]
    public void RestaurarVoltaAoMaterialGuardado()
    {
        VoxelStock stock = Box();
        ulong[] before = stock.TakeSnapshot();
        stock.Carve(new Point3(10, 10, -4), new Point3(0, 0, 1), ToolProfile.Flat(6), 25);
        stock.Restore(before);
        Assert.Equal(4000, stock.Volume, 3);
    }

    private const string Rotated = "G21 G90 G54\nG00 A90.\nG00 X0 Y0 Z5\nG01 Z-3 F100\nG01 X20\nG00 Z10\nG00 A0\nG00 X0 Y0\nG01 Z-2 F100\nG01 X20\nG00 Z10";

    private static (VoxelSimulator Sim, ToolPath Path) Simulate(string text)
    {
        ToolPath path = MachineExecutor.Execute(GCodeParser.Parse(text, Dialect.Fanuc), new MachineSettings { PartFrame = Frame });
        Assert.True(SimulationFactory.NeedsVoxels(path));
        VoxelStock stock = SimulationFactory.VoxelStockFor(null, path, _ => ToolProfile.Flat(6));
        return (new VoxelSimulator(stock, path, _ => ToolProfile.Flat(6)), path);
    }

    [Fact]
    public void CorteComPecaGiradaRetiraMaterialNaLateral()
    {
        (VoxelSimulator sim, ToolPath path) = Simulate(Rotated);
        double before = sim.Stock.Volume;
        sim.RunToEnd();
        Assert.True(sim.Stock.Volume < before - 20);
        // O corte com A90 fica na lateral da peça (Y ~ 22 a 25), não no topo: o bruto automático envolve esse lado
        Assert.True(sim.Stock.Bounds.Max.Y >= 25);
        Assert.True(path.Moves.Count > 0);
    }

    [Fact]
    public void VoltarEAvancarDaOMesmoMaterial()
    {
        (VoxelSimulator sim, ToolPath path) = Simulate(Rotated);
        sim.RunToEnd();
        double full = sim.Stock.Volume;
        long fullCount = sim.Stock.SolidCount;

        sim.RunToMove(4);
        Assert.True(sim.Stock.Volume > full);
        sim.RunToEnd();
        Assert.Equal(fullCount, sim.Stock.SolidCount);

        // Em fatias de distância dá o mesmo que de uma vez
        sim.Reset();
        while (!sim.IsFinished)
        {
            sim.Advance(1.3);
        }

        Assert.Equal(fullCount, sim.Stock.SolidCount);
        Assert.True(path.Moves.Count > 4);
    }

    [Fact]
    public void GiroContinuoComCorteTiraMaterialEmVolta()
    {
        // Ferramenta parada no raio, mesa girando 90° com avanço: risca um arco
        const string text = "G21 G90 G54\nG00 A0\nG00 X0 Y0 Z0\nG01 Z-3 F100\nG01 A90. F100\nG00 Z10";
        (VoxelSimulator sim, _) = Simulate(text);
        double before = sim.Stock.Volume;
        sim.RunToEnd();
        Assert.True(sim.Stock.Volume < before - 10);
    }

    [Fact]
    public void MalhaDoBrutoInteiroFechaComOVolumeDoBloco()
    {
        var mesh = new VoxelMesh(Box(0.5));
        SolidMesh merged = mesh.Merged();
        Assert.Equal(4000, merged.EnclosedVolume(), 1);
    }

    [Fact]
    public void MalhaAposCorteAcompanhaOVolumeDosVoxels()
    {
        VoxelStock stock = Box(0.5);
        var mesh = new VoxelMesh(stock);
        mesh.Update();
        stock.Carve(new Point3(10, 10, -4), new Point3(0, 0, 1), ToolProfile.Flat(6), 25);
        IReadOnlyList<int> changed = mesh.Update();
        Assert.InRange(changed.Count, 1, mesh.TileCount - 1); // só os blocos tocados são refeitos
        Assert.Equal(stock.Volume, mesh.Merged().EnclosedVolume(), 1);
    }

    [Fact]
    public void CenaDeVoxelsTemUmObjetoPorBloco()
    {
        (VoxelSimulator sim, ToolPath path) = Simulate(Rotated);
        var scene = SimulationScene.ForVoxels(sim.Stock, path, _ => ToolProfile.Flat(6));
        Assert.Equal(sim.Stock.TileCount, scene.Objects.Count(o => o.Role == SceneRole.Material));
        sim.RunToEnd();
        Assert.NotEmpty(scene.Refresh());
    }

    [Fact]
    public void LuzDoCilindroEhSuaveEFacesPlanasContinuamPlanas()
    {
        var round = new Setup.SetupStock { Kind = Setup.SetupStockKind.Round, Axis = Setup.SetupAxis.X, X = 0, Y = 0, Z = 0, Height = 20, Diameter = 20 };
        var stock = new VoxelStock(round.Extent, 0.5, round.Contains);
        ReadOnlySpan<float> n = new VoxelMesh(stock).Merged().Normals;
        int oblique = 0;
        for (int i = 0; i < n.Length; i += 3)
        {
            // normal da lateral num ângulo qualquer (nem em X, nem em Y, nem em Z)
            if (Math.Abs(n[i + 1]) > 0.3 && Math.Abs(n[i + 2]) > 0.3)
            {
                oblique++;
            }
        }

        Assert.True(oblique > 100);

        // Caixa: as faces planas ficam com normal de eixo, sem arredondar o meio
        ReadOnlySpan<float> flat = new VoxelMesh(Box(0.5)).Merged().Normals;
        int axisAligned = 0;
        for (int i = 0; i < flat.Length; i += 3)
        {
            if (Math.Abs(flat[i]) > 0.99 || Math.Abs(flat[i + 1]) > 0.99 || Math.Abs(flat[i + 2]) > 0.99)
            {
                axisAligned++;
            }
        }

        Assert.True(axisAligned > (flat.Length / 3) * 0.95);
    }
}
