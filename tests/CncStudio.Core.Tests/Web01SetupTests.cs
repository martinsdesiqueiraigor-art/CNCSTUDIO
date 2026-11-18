using CncStudio.Core.Execution;
using CncStudio.Core.Rendering;
using CncStudio.Core.Trajectory;
using CncStudio.Core.Geometry;
using CncStudio.Core.Scene;
using CncStudio.Core.Setup;
using CncStudio.Core.Simulation;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>WEB-01: bruto do setup × comentário do programa (S1) e ferramenta de torno sem Ø (S3).</summary>
public class Web01SetupTests
{
    private const string Lathe = "O1\n(BRUTO D50 X 70 - ZERO NA FACE)\nG00 X52. Z2.\nG01 X40. F0.2\nM30\n";

    private static MachiningSetup WithStock(SetupStock stock) => new() { Stock = stock };

    [Fact]
    public void Bruto_do_setup_igual_ao_comentario_nao_avisa()
    {
        var same = WithStock(new SetupStock { Kind = SetupStockKind.Round, Diameter = 50, Height = 70, Z = -70 });
        Assert.Null(SetupBinding.StockMismatch(same, MachineKind.Lathe, Lathe, Bounds3.Empty));
        Assert.Null(SetupBinding.StockMismatch(null, MachineKind.Lathe, Lathe, Bounds3.Empty));
        Assert.Null(SetupBinding.StockMismatch(same, MachineKind.Lathe, "O1\nG00 X10.\nM30\n", Bounds3.Empty));
    }

    [Fact]
    public void Bruto_do_setup_diferente_do_comentario_avisa_com_as_medidas()
    {
        var other = WithStock(new SetupStock { Kind = SetupStockKind.Round, Diameter = 80, Height = 70, Z = -70 });
        string? warning = SetupBinding.StockMismatch(other, MachineKind.Lathe, Lathe, Bounds3.Empty);
        Assert.NotNull(warning);
        Assert.Contains("Ø80 × 70", warning);
        Assert.Contains("Ø50 × 70", warning);

        // face em outro Z também conta
        var shifted = WithStock(new SetupStock { Kind = SetupStockKind.Round, Diameter = 50, Height = 70, Z = -68 });
        Assert.NotNull(SetupBinding.StockMismatch(shifted, MachineKind.Lathe, Lathe, Bounds3.Empty));
    }

    [Fact]
    public void Fresa_compara_a_caixa_do_bloco()
    {
        const string mill = "O1\n(BRUTO 100 X 80 X 20)\nG00 X0 Y0\nM30\n";
        Stock? comment = SetupBinding.FromProgramComments(mill, MachineKind.Mill, Bounds3.Empty);
        Assert.NotNull(comment);
        var same = WithStock(SetupBinding.FromStock(comment!));
        Assert.Null(SetupBinding.StockMismatch(same, MachineKind.Mill, mill, Bounds3.Empty));
        same.Stock!.Width += 5;
        Assert.NotNull(SetupBinding.StockMismatch(same, MachineKind.Mill, mill, Bounds3.Empty));
    }

    [Fact]
    public void Ferramenta_de_torno_nao_vira_diametro_e_broca_vira()
    {
        var setup = new MachiningSetup();
        setup.Tools.Add(new SetupTool { Number = 1, Kind = ToolKind.TurnExternal, Diameter = 10 });
        setup.Tools.Add(new SetupTool { Number = 2, Kind = ToolKind.Drill, Diameter = 8 });
        MachineSettings settings = setup.ApplyTo(MachineSettings.Default with { ToolDiameterOf = _ => 0 });
        Assert.Equal(0, settings.ToolDiameterOf!(1));
        Assert.Equal(8, settings.ToolDiameterOf(2));
    }

    [Fact]
    public void Raio_da_ponta_digitado_vale_na_remocao()
    {
        Assert.Equal(LatheToolShape.NoseRadius, LatheToolShape.CutOf(new SetupTool { Kind = ToolKind.TurnExternal }).HalfWidth);
        Assert.Equal(0.8, LatheToolShape.CutOf(new SetupTool { Kind = ToolKind.TurnExternal, CornerRadius = 0.8 }).HalfWidth);
        Assert.Equal(1.5, LatheToolShape.CutOf(new SetupTool { Kind = ToolKind.PartExternal, InsertWidth = 3, CornerRadius = 0.2 }).HalfWidth);
    }
}

/// <summary>WEB-01 S5 e S6: placa só quando há placa; barra quadrada, retangular e sextavada no torno.</summary>
public class Web01LatheBarTests
{
    private static ToolPath Path() => MachineExecutor.Execute(Parsing.GCodeParser.Parse("G18 G21 G99\nT0101\nG00 X60. Z2.\nG01 X40. F0.2\nZ-20.\nG00 X100. Z100.\nM30\n", Dialects.Dialect.Fanuc));

    [Fact]
    public void Secao_sextavada_e_quadrada_tem_cantos_e_faces_certos()
    {
        var hex = new LatheBarSection(LatheBarShape.Hexagon, 46, 46);
        Assert.Equal(46 / Math.Sqrt(3), hex.Circumradius, 9);
        Assert.Equal(hex.Circumradius, hex.RadiusAt(0), 9);
        Assert.Equal(23, hex.RadiusAt(Math.PI / 6), 9);
        Assert.Equal(hex.Circumradius, hex.RadiusAt(Math.PI / 3), 9);
        var sq = new LatheBarSection(LatheBarShape.Rectangle, 40, 40);
        Assert.Equal(20, sq.RadiusAt(0), 9);
        Assert.Equal(20 * Math.Sqrt(2), sq.RadiusAt(Math.PI / 4), 9);
        Assert.Equal("sextavado 46", hex.Describe());
        Assert.Equal("quadrado 40", sq.Describe());
        Assert.Equal("40 × 30", new LatheBarSection(LatheBarShape.Rectangle, 40, 30).Describe());
    }

    [Fact]
    public void Setup_sextavado_e_prisma_viram_barra_pelo_canto_com_a_secao()
    {
        var hex = new SetupStock { Kind = SetupStockKind.Hexagon, Width = 46, Height = 70, Z = -70 };
        Assert.Null(hex.Validate());
        Stock bar = hex.ToStock(MachineKind.Lathe)!;
        Assert.Equal(2 * 46 / Math.Sqrt(3), bar.Diameter, 9);
        Assert.Equal(LatheBarShape.Hexagon, bar.Section!.Shape);
        Assert.Equal(0, bar.MaxZ, 9);
        Assert.Equal(70, bar.Length, 9);

        var prism = new SetupStock { Kind = SetupStockKind.Prism, Width = 40, Depth = 30, Height = 70, Z = -70 };
        Stock box = prism.ToStock(MachineKind.Lathe)!;
        Assert.Equal(50, box.Diameter, 9);
        Assert.Equal(LatheBarShape.Rectangle, box.Section!.Shape);

        Assert.NotNull(new SetupStock { Kind = SetupStockKind.Hexagon, Width = 0, Height = 10 }.Validate());
        Assert.Contains("sextavado 46 × 70", SetupBinding.Describe(bar));
    }

    [Fact]
    public void Sextavado_difere_do_comentario_redondo()
    {
        var setup = new MachiningSetup { Stock = new SetupStock { Kind = SetupStockKind.Hexagon, Width = 46, Height = 70, Z = -70 } };
        Assert.Contains("sextavado 46", SetupBinding.StockMismatch(setup, MachineKind.Lathe, "(BRUTO D50 X 70)\nG00 X0\n", Bounds3.Empty));
    }

    [Fact]
    public void Barra_sextavada_inteira_tem_o_volume_do_sextavado()
    {
        var stock = new TurningStock(-70, 0, 46 / Math.Sqrt(3)) { Section = new LatheBarSection(LatheBarShape.Hexagon, 46, 46) };
        double volume = Math.Abs(SceneMeshes.Turning(stock, false, ScenePalette.Realistic).EnclosedVolume());
        double expected = Math.Sqrt(3) / 2 * 46 * 46 * 70;
        Assert.Equal(expected, volume, expected * 0.002);

        // torneada a Ø40 nos 20 mm da ponta: ali vira cilindro (abaixo das faces, 46/2 = 23)
        stock.Cut(-10, 20, 10, 0);
        double turned = Math.Abs(SceneMeshes.Turning(stock, false, ScenePalette.Realistic).EnclosedVolume());
        Assert.True(turned < volume);
    }

    [Fact]
    public void Placa_so_aparece_sem_setup_ou_com_placa_no_setup()
    {
        var stock = new TurningStock(-70, 0, 25);
        bool HasChuck(MachiningSetup? s) => SimulationScene.ForLathe(stock, Path(), s).Objects.Any(o => o.Id.StartsWith("fixacao/placa"));
        Assert.True(HasChuck(null));
        Assert.True(HasChuck(new MachiningSetup()));
        var semFixacao = new MachiningSetup { Stock = new SetupStock { Kind = SetupStockKind.Round, Diameter = 50, Height = 70, Z = -70 } };
        Assert.False(HasChuck(semFixacao));
        semFixacao.Fixtures.Add(new SetupFixture { Kind = SetupFixtureKind.ThreeJawChuck });
        Assert.True(HasChuck(semFixacao));
    }
}
