using CncStudio.Core.Cam;
using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;
using CncStudio.Core.Scene;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>CAM-ILHAS-02: resumo, lista e cores dos laços do bolsão automático; peça transparente.</summary>
public class PocketLoopReportTests
{
    private static MeshFace Top(MeshFeatures f) =>
        f.Faces.Where(x => x.Kind == FaceKind.Plane && x.Normal.Z > 0.99 && Math.Abs(x.Center.Z - 5) < 0.5).OrderByDescending(x => x.Area).First();

    [Fact]
    public void Flange_borda_aberta_cubo_ilha_furo_ignorado()
    {
        MeshFeatures f = MeshFeatures.Analyze(LoopSideTests.Flange());
        IReadOnlyList<LoopItem> items = PocketLoopReport.Describe(f, [Top(f)]);
        Assert.Equal([LoopRole.OpenBorder, LoopRole.Island, LoopRole.Ignored], items.Select(i => i.Role));
        Assert.Equal(["Borda de fora 60 × 60", "Ilha 20 × 20", "Furo 4 × 4"], items.Select(i => i.Name));
        Assert.Equal("1 ilha · 1 furo ignorado · borda aberta", PocketLoopReport.Summary(items));
        IReadOnlyList<LoopGroup> groups = PocketLoopReport.Group(items);
        Assert.Equal(["desce · aberta", "sobe · ilha", "desce · ignorado"], groups.Select(g => g.Effect));
    }

    private static IReadOnlyList<Point3> Circle(double cx, double cy, double r, int n = 32) =>
        [.. Enumerable.Range(0, n).Select(k => new Point3(cx + (r * Math.Cos(2 * Math.PI * k / n)), cy + (r * Math.Sin(2 * Math.PI * k / n)), 0))];

    [Fact]
    public void Circulo_ganha_diametro_e_iguais_se_juntam()
    {
        Assert.Equal("Ø10", PocketLoopReport.Size(Circle(3, 4, 5)));
        Assert.Equal("Ø40,5", PocketLoopReport.Size(Circle(0, 0, 20.25)));
        LoopItem[] items =
        [
            new(LoopRole.ClosedBorder, "Borda de fora Ø100", Circle(0, 0, 50)),
            new(LoopRole.Ignored, "Furo Ø10", Circle(30, 0, 5)),
            new(LoopRole.Ignored, "Furo Ø10", Circle(-30, 0, 5)),
            new(LoopRole.Ignored, "Furo Ø10", Circle(0, 30, 5)),
        ];
        IReadOnlyList<LoopGroup> groups = PocketLoopReport.Group(items);
        Assert.Equal(2, groups.Count);
        Assert.Equal("3 furos Ø10", groups[1].Name);
        Assert.Equal("descem · ignorados", groups[1].Effect);
        Assert.Equal("0 ilhas · 3 furos ignorados · bolsão fechado", PocketLoopReport.Summary(items));
    }

    [Fact]
    public void Cores_iguais_as_da_imagem()
    {
        Assert.Equal(0x3DBE6Au, PocketLoopReport.ColorOf(LoopRole.Island));
        Assert.Equal(0x8A939Eu, PocketLoopReport.ColorOf(LoopRole.Ignored));
        Assert.Equal(0xF08A24u, PocketLoopReport.ColorOf(LoopRole.OpenBorder));
        Assert.Equal(0xE05A5Au, PocketLoopReport.ColorOf(LoopRole.Mixed));
    }

    [Fact]
    public void Peca_transparente_vai_e_volta_e_sem_solido_vira_aramado()
    {
        var o = CamViewOptions.Default with { Part = PartDisplay.Transparent };
        Assert.Contains("peca=transparente", o.ToText());
        Assert.Equal(o, CamViewOptions.Parse(o.ToText()));
        Assert.Equal(PartDisplay.Wireframe, o.EffectivePart(hasSolidPart: false));
        Assert.Equal(PartDisplay.Transparent, o.EffectivePart(hasSolidPart: true));
    }

    [Fact]
    public void Opacidade_da_peca_so_vale_para_o_papel_peca()
    {
        var snap = new SceneSnapshot([], [], new Bounds3(Point3.Origin, Point3.Origin), ScenePalette.Realistic) { PartOpacity = 0.3f };
        Assert.Equal(0.3f, snap.OpacityOf(SceneRole.Part));
        Assert.Equal(1f, snap.OpacityOf(SceneRole.Fixture));
        Assert.Equal(1f, snap.OpacityOf(SceneRole.Material));
        Assert.True(SceneSnapshot.RotatesWithTable(SceneRole.Part));
    }
}
