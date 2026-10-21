using CncStudio.Core.Geometry;
using CncStudio.Core.Scene;
using CncStudio.Core.Setup;
using CncStudio.Core.Simulation;
using CncStudio.Core.Trajectory;
using Xunit;

namespace CncStudio.Core.Tests;

public class LatheToolTypesTests
{
    public static IEnumerable<object[]> LatheKinds() =>
    [
        [ToolKind.TurnExternal], [ToolKind.TurnInternal], [ToolKind.PartExternal],
        [ToolKind.PartInternal], [ToolKind.ThreadExternal], [ToolKind.ThreadInternal],
    ];

    private static SetupTool Tool(ToolKind kind, double width = 3) => new() { Kind = kind, Number = 1, InsertWidth = width };

    [Theory]
    [MemberData(nameof(LatheKinds))]
    public void ShapeIsConvexQuadsWithTipAtOrigin(ToolKind kind)
    {
        LatheToolShape shape = LatheToolShape.Of(Tool(kind));
        Assert.Equal(4, shape.Insert.Count);
        Assert.All(shape.Holders, h => Assert.Equal(4, h.Polygon.Count));
        foreach (var poly in shape.Holders.Select(h => h.Polygon).Prepend(shape.Insert))
        {
            double sign = 0;
            for (int i = 0; i < 4; i++)
            {
                var (a, b, c) = (poly[i], poly[(i + 1) % 4], poly[(i + 2) % 4]);
                double cross = (b.X - a.X) * (c.Z - b.Z) - (b.Z - a.Z) * (c.X - b.X);
                Assert.True(cross > 0, $"{kind}: polígono fora do sentido anti-horário/convexo");
                sign += cross;
            }
        }

        // A ponta de corte (canto do inserto) está na origem
        Assert.Contains(shape.Insert, p => Math.Abs(p.X) < 0.3 && Math.Abs(p.Z) < 0.3);
    }

    [Theory]
    [InlineData(ToolKind.TurnInternal)]
    [InlineData(ToolKind.PartInternal)]
    [InlineData(ToolKind.ThreadInternal)]
    public void InternalToolsHaveTheBodyTowardsTheAxis(ToolKind kind)
    {
        LatheToolShape shape = LatheToolShape.Of(Tool(kind));
        Assert.All(shape.Insert, p => Assert.True(p.X <= 1e-9));
        Assert.All(shape.Holders.SelectMany(h => h.Polygon), p => Assert.True(p.X <= 1e-9));
    }

    [Theory]
    [InlineData(ToolKind.TurnExternal)]
    [InlineData(ToolKind.PartExternal)]
    [InlineData(ToolKind.ThreadExternal)]
    public void ExternalToolsHaveTheBodyAwayFromTheAxis(ToolKind kind)
    {
        LatheToolShape shape = LatheToolShape.Of(Tool(kind));
        Assert.All(shape.Insert, p => Assert.True(p.X >= -1e-9));
    }

    [Fact]
    public void PartingBladeHasTheInsertWidth()
    {
        LatheToolShape shape = LatheToolShape.Of(Tool(ToolKind.PartExternal, 4));
        Assert.Equal(4, shape.Insert.Max(p => p.Z) - shape.Insert.Min(p => p.Z), 6);
        Assert.Equal(new LatheToolShape.CutSpec(2, 2), LatheToolShape.CutOf(Tool(ToolKind.PartExternal, 4)));
    }

    [Fact]
    public void ToolWithoutSetupCutsLikeExternalTurning()
    {
        Assert.Equal(new LatheToolShape.CutSpec(LatheToolShape.NoseRadius, 0), LatheToolShape.CutOf(null));
        Assert.Equal(new LatheToolShape.CutSpec(LatheToolShape.NoseRadius, 0), LatheToolShape.CutOf(new SetupTool { Kind = ToolKind.Drill }));
    }

    [Fact]
    public void PartingToolCutsAGrooveAsWideAsTheBlade()
    {
        var stock = new TurningStock(-60, 0, 30);
        var tool = Tool(ToolKind.PartExternal, 4);
        var simulator = new TurningSimulator(stock, [], _ => 10, _ => LatheToolShape.CutOf(tool));
        Assert.NotNull(simulator);

        // Corte direto no estoque com a mesma regra usada pelo simulador: canto em Z = -20, largura 4
        LatheToolShape.CutSpec cut = LatheToolShape.CutOf(tool);
        stock.Cut(-20 + cut.Offset, 20, cut.HalfWidth, 1); // mergulho no mesmo Z

        int below = 0;
        for (int i = 0; i < stock.Slices; i++)
        {
            double z = stock.SliceCenter(i);
            if (stock.Outer[i] < 29)
            {
                below++;
                Assert.InRange(z, -20 - stock.SliceLength, -16 + stock.SliceLength);
            }
        }

        Assert.InRange(below * stock.SliceLength, 3.5, 5);
    }

    [Theory]
    [MemberData(nameof(LatheKinds))]
    public void MeshesBuildForEveryType(ToolKind kind)
    {
        (var insert, var holder) = SceneMeshes.LatheTool(ScenePalette.Realistic, Tool(kind));
        Assert.True(insert.VertexCount > 0);
        Assert.True(holder.VertexCount > 0);
    }

    [Fact]
    public void LatheDrillMeshBuilds()
    {
        (var cutter, var holder) = SceneMeshes.LatheTool(ScenePalette.Realistic, new SetupTool { Kind = ToolKind.Drill, Diameter = 10 });
        Assert.True(cutter.VertexCount > 0);
        Assert.True(holder.VertexCount > 0);
    }

    [Fact]
    public void NewKindsAndWidthSurviveJson()
    {
        var setup = new MachiningSetup
        {
            Tools = [Tool(ToolKind.PartExternal, 2.5).with2(2), Tool(ToolKind.ThreadInternal)],
        };
        MachiningSetup copy = setup.Clone();
        Assert.Equal(ToolKind.PartExternal, copy.ToolOf(2)!.Kind);
        Assert.Equal(2.5, copy.ToolOf(2)!.InsertWidth);
        Assert.Equal(ToolKind.ThreadInternal, copy.ToolOf(1)!.Kind);
        Assert.Contains("bedame", SetupJson.Serialize(setup));
        Assert.True(copy.IsValid());
        Assert.True(copy.ToolOf(1)!.IsLathe);
    }

    [Fact]
    public void InsertWidthMustBeValid()
    {
        Assert.NotNull(Tool(ToolKind.PartExternal, 0).Validate());
        Assert.NotNull(Tool(ToolKind.PartExternal, 80).Validate());
        Assert.Null(Tool(ToolKind.PartExternal, 3).Validate());
    }
}

internal static class ToolTestExtensions
{
    public static SetupTool with2(this SetupTool tool, int number)
    {
        tool.Number = number;
        return tool;
    }
}
