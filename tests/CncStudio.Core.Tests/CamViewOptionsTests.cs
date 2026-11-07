using CncStudio.Core.Cam;
using CncStudio.Core.Dialects;
using CncStudio.Core.Scene;
using CncStudio.Core.Trajectory;
using Xunit;

namespace CncStudio.Core.Tests;

public class CamViewOptionsTests
{
    [Fact]
    public void Padroes_sao_os_aprovados()
    {
        CamViewOptions o = CamViewOptions.Default;
        Assert.Equal(StockDisplay.Transparent, o.Stock);
        Assert.Equal(PartDisplay.Solid, o.Part);
        Assert.True(o.Edges);
        Assert.True(o.Path);
        Assert.True(o.Rapids);
        Assert.True(o.Axes);
    }

    [Fact]
    public void Texto_vai_e_volta()
    {
        var o = new CamViewOptions { Stock = StockDisplay.Hidden, Part = PartDisplay.Wireframe, Edges = false, Path = false, Rapids = false, Axes = false };
        Assert.Equal(o, CamViewOptions.Parse(o.ToText()));
        Assert.Equal(CamViewOptions.Default, CamViewOptions.Parse(CamViewOptions.Default.ToText()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("lixo")]
    [InlineData("bruto=roxo;eixos=talvez;=1;peca")]
    public void Texto_invalido_fica_no_padrao(string? text) => Assert.Equal(CamViewOptions.Default, CamViewOptions.Parse(text));

    [Fact]
    public void Texto_parcial_muda_so_o_que_veio()
    {
        CamViewOptions o = CamViewOptions.Parse("bruto=solido; eixos=0");
        Assert.Equal(CamViewOptions.Default with { Stock = StockDisplay.Solid, Axes = false }, o);
    }

    [Fact]
    public void Peca_solida_sem_altura_vira_aramado()
    {
        Assert.Equal(PartDisplay.Wireframe, CamViewOptions.Default.EffectivePart(hasSolidPart: false));
        Assert.Equal(PartDisplay.Solid, CamViewOptions.Default.EffectivePart(hasSolidPart: true));
        Assert.Equal(PartDisplay.Hidden, (CamViewOptions.Default with { Part = PartDisplay.Hidden }).EffectivePart(false));
    }

    [Fact]
    public void Rapidos_dependem_da_trajetoria()
    {
        Assert.True(CamViewOptions.Default.ShowsRapids);
        Assert.False((CamViewOptions.Default with { Path = false }).ShowsRapids);
    }

    private static readonly JobStock Stock = new(0, 0, 100, 60, 20);
    private static readonly Contour[] Square = [Contour.Rectangle(10, 10, 40, 30)];

    private static ToolPath Path() => ProgramAnalyzer.Analyze("G90 G54 G0 X0 Y0 Z5\nG1 Z-2 F100\nG1 X20\nG0 Z5\nM30", Dialect.Fanuc).ToolPath;

    [Fact]
    public void Cena_padrao_tem_bruto_transparente_inteiro_contorno_trajetoria_e_eixos()
    {
        SceneSnapshot scene = CamJobScene.Build(Stock, Square, Path(), CamViewOptions.Default);
        SceneObjectState stock = Assert.Single(scene.Objects);
        Assert.Equal(SceneRole.Material, stock.Role);
        Assert.Equal(SimulationScene.TransparentOpacity, scene.MaterialOpacity);
        Assert.Equal(12, stock.Mesh.TriangleCount); // caixa inteira: nada removido
        Assert.True(stock.Mesh.Edges.Length > 0);
        Assert.Contains(scene.Lines, l => l.Role == SceneRole.PartOutline && l.Positions.Length > 0);
        Assert.Contains(scene.Lines, l => l.Role == SceneRole.FeedPath);
        Assert.Contains(scene.Lines, l => l.Role == SceneRole.RapidPath);
        Assert.Equal(3, scene.Lines.Count(l => l.Role == SceneRole.Axis));
        // o topo do bruto em Z 0, o fundo na espessura
        Assert.Equal(-20, scene.Bounds.Min.Z, 6);
        Assert.Equal(100, scene.Bounds.Max.X, 6);
    }

    [Fact]
    public void Contorno_fica_em_z0()
    {
        SceneLinesState outline = CamJobScene.Build(Stock, Square, null, CamViewOptions.Default).Lines.Single(l => l.Role == SceneRole.PartOutline);
        for (int i = 2; i < outline.Positions.Length; i += 3)
        {
            Assert.Equal(0f, outline.Positions[i]);
        }
    }

    [Fact]
    public void Eixos_saem_do_zero_com_as_cores_certas()
    {
        SceneLinesState[] axes = [.. CamJobScene.Build(Stock, Square, null, CamViewOptions.Default).Lines.Where(l => l.Role == SceneRole.Axis)];
        Assert.Equal([CamJobScene.AxisX, CamJobScene.AxisY, CamJobScene.AxisZ], axes.Select(a => a.Color));
        foreach (SceneLinesState a in axes)
        {
            Assert.Equal([0f, 0f, 0f], a.Positions[..3]);
        }

        Assert.True(axes[0].Positions[3] > 0 && axes[0].Positions[4] == 0 && axes[0].Positions[5] == 0);
        Assert.True(axes[2].Positions[5] > 0);
    }

    [Fact]
    public void Chips_desligados_tiram_cada_parte()
    {
        var off = new CamViewOptions { Stock = StockDisplay.Hidden, Part = PartDisplay.Hidden, Path = false, Axes = false };
        SceneSnapshot scene = CamJobScene.Build(Stock, Square, Path(), off);
        Assert.Empty(scene.Objects);
        Assert.Empty(scene.Lines);
        Assert.False(scene.Bounds.IsEmpty); // o enquadramento continua igual
    }

    [Fact]
    public void Bruto_solido_sem_arestas_e_sem_rapidos()
    {
        var o = CamViewOptions.Default with { Stock = StockDisplay.Solid, Edges = false, Rapids = false };
        SceneSnapshot scene = CamJobScene.Build(Stock, Square, Path(), o);
        Assert.Equal(1f, scene.MaterialOpacity);
        Assert.Equal(0, Assert.Single(scene.Objects).Mesh.Edges.Length);
        Assert.DoesNotContain(scene.Lines, l => l.Role == SceneRole.RapidPath);
        Assert.Contains(scene.Lines, l => l.Role == SceneRole.FeedPath);
    }

    [Fact]
    public void Trajetoria_nao_muda_com_os_chips()
    {
        ToolPath path = Path();
        float[] a = CamJobScene.Build(Stock, Square, path, CamViewOptions.Default).Lines.Single(l => l.Role == SceneRole.FeedPath).Positions;
        float[] b = CamJobScene.Build(Stock, Square, path, CamViewOptions.Default with { Stock = StockDisplay.Solid, Axes = false }).Lines.Single(l => l.Role == SceneRole.FeedPath).Positions;
        Assert.Equal(a, b);
    }

    [Fact]
    public void Sem_bruto_nem_desenho_a_cena_nao_quebra()
    {
        SceneSnapshot scene = CamJobScene.Build(null, [], null, CamViewOptions.Default);
        Assert.Empty(scene.Objects);
        Assert.False(scene.Bounds.IsEmpty);
    }
}
