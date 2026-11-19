using CncStudio.Core.Cam;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Scene;
using CncStudio.Core.Setup;
using CncStudio.Core.Simulation;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>DESK-01: a placa e o contraponto que vêm do CAM aparecem na cena do Code, no lugar certo.</summary>
public class Desk01TailstockSceneTests
{
    [Fact]
    public void Tailstock_from_cam_sits_on_the_bar_axis_past_the_face()
    {
        const string program = "O1234\nG54 G18 G21 G40 G97 S1000 M3\nT0101\nG0 X32 Z2\nG1 X28 F0.2\nG1 Z-40\nG0 X32\nG0 Z2\nM30\n";
        var setup = new MachiningSetup
        {
            Stock = new SetupStock { Kind = SetupStockKind.Round, Diameter = 30, Height = 125, Z = -125 },
        };
        setup.Fixtures.Add(new SetupFixture
        {
            Kind = SetupFixtureKind.ThreeJawChuck, JawOpening = 30, GripLength = 20, Tailstock = true, Width = 160, Height = 48,
        });
        MachiningSetup sim = SetupBinding.ProgramFirst(setup, MachineKind.Lathe, program, Bounds3.Empty);
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(program, Dialect.Fanuc, sim.ApplyTo(MachineSettings.Default));
        SimulationScene scene = SimulationScene.ForLathe(SimulationFactory.Create(analysis, sim, program).Bar!, analysis.ToolPath, sim);
        scene.Refresh();

        Bounds3 bar = scene.Snapshot().Objects.Single(o => o.Id == "material/0").Mesh.Bounds;
        Bounds3 tail = scene.Snapshot().Objects.Single(o => o.Id == "fixacao/contraponto").Mesh.Bounds;
        // eixo da barra na cena = X; o contraponto começa na face (maior X da barra) e segue para +X, centrado em Y e Z
        Assert.Equal(bar.Max.X, tail.Min.X, 3);
        Assert.True(tail.Max.X > tail.Min.X + 20);
        Assert.Equal(0, (tail.Min.Z + tail.Max.Z) / 2, 3);
        Assert.Equal(0, (tail.Min.Y + tail.Max.Y) / 2, 3);
    }
}
