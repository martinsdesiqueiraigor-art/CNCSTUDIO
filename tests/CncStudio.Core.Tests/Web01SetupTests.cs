using CncStudio.Core.Execution;
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
