using CncStudio.Core.Rendering;
using CncStudio.Core.Scene;
using CncStudio.Core.Setup;
using CncStudio.Core.Simulation;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>Fresa de topo com canais helicoidais só visuais (CODE-13, etapa 3).</summary>
public class FlutedEndMillTests
{
    private static (SolidMesh Cutter, SolidMesh Holder) Mill(double diameter = 10) =>
        SceneMeshes.MillTool(ToolProfile.Flat(diameter), new SetupTool { Kind = ToolKind.FlatEndMill, Diameter = diameter }, ScenePalette.Realistic);

    private static List<(double X, double Y, double Z)> Vertices(SolidMesh mesh) =>
        [.. Enumerable.Range(0, mesh.VertexCount).Select(i => ((double)mesh.Positions[i * 3], (double)mesh.Positions[i * 3 + 1], (double)mesh.Positions[i * 3 + 2]))];

    [Fact]
    public void TipStaysAtTheOriginAndDiameterIsNominal()
    {
        var v = Vertices(Mill(10).Cutter);
        Assert.Equal(0, v.Min(p => p.Z), 6);
        Assert.Equal(5, v.Max(p => Math.Sqrt(p.X * p.X + p.Y * p.Y)), 2);
        Assert.All(v, p => Assert.True(double.IsFinite(p.X + p.Y + p.Z)));
    }

    [Fact]
    public void TipSectionHasFourFlutesWithAValleyAndFourLands()
    {
        var v = Vertices(Mill(10).Cutter).Where(p => Math.Abs(p.Z) < 1e-6 && (p.X != 0 || p.Y != 0)).ToList();
        // anel da ponta: raio varia entre o fundo do canal e o raio cheio
        double min = v.Min(p => Math.Sqrt(p.X * p.X + p.Y * p.Y)), max = v.Max(p => Math.Sqrt(p.X * p.X + p.Y * p.Y));
        Assert.InRange(min, 2.5, 3.5);
        Assert.Equal(5, max, 3);

        // 4 trechos de raio cheio (margens) separados por canais
        var radii = v.OrderBy(p => Math.Atan2(p.Y, p.X)).Select(p => Math.Sqrt(p.X * p.X + p.Y * p.Y) > 4.9).ToList();
        int lands = 0;
        for (int i = 0; i < radii.Count; i++)
        {
            if (radii[i] && !radii[(i + radii.Count - 1) % radii.Count]) { lands++; }
        }

        Assert.Equal(SceneMeshes.MillFlutes, lands);
    }

    [Fact]
    public void FlutesAreHelical_TheSectionRotatesAlongZ()
    {
        var v = Vertices(Mill(10).Cutter);
        double AngleOfDeepestValley(double z)
        {
            var ring = v.Where(p => Math.Abs(p.Z - z) < 1e-3).ToList();
            var deepest = ring.OrderBy(p => p.X * p.X + p.Y * p.Y).First();
            return Math.Atan2(deepest.Y, deepest.X);
        }

        double z0 = v.Where(p => p.Z > 0.5).Min(p => p.Z); // primeiro anel acima da ponta
        double z1 = v.Where(p => p.Z < 20 && p.Z > 10).Select(p => p.Z).Distinct().OrderBy(z => z).First();
        Assert.NotEqual(AngleOfDeepestValley(z0), AngleOfDeepestValley(z1), 2);
    }

    [Fact]
    public void MeshStaysLightForPhones()
    {
        var (cutter, holder) = Mill(10);
        Assert.InRange(cutter.TriangleCount, 100, 8000);
        Assert.InRange(cutter.TriangleCount + holder.TriangleCount, 100, 30000);
    }

    [Fact]
    public void BallEndMillStillUsesTheRevolvedProfile()
    {
        var (cutter, _) = SceneMeshes.MillTool(new ToolProfile(ToolTipShape.Ball, 4), new SetupTool { Kind = ToolKind.BallEndMill, Diameter = 8 }, ScenePalette.Realistic);
        Assert.True(cutter.TriangleCount < 2000);
        Assert.Equal(0, Vertices(cutter).Min(p => p.Z), 6);
    }
}
