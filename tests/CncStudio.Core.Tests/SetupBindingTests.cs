using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Setup;
using CncStudio.Core.Simulation;

namespace CncStudio.Core.Tests;

public class SetupBindingTests
{
    private const string ComBruto = "O1000\n(BRUTO 100 X 80 X 20)\nG00 X0 Y0\n";
    private const string SemBruto = "O1000\nG00 X0 Y0\n";

    private static readonly Bounds3 Corte = Bounds3.Empty.Include(new Point3(5, 5, -3)).Include(new Point3(95, 75, 0));

    private static MachiningSetup ComPrisma() => new()
    {
        Stock = new SetupStock { Kind = SetupStockKind.Prism, X = -60, Y = -50, Z = -30, Width = 120, Depth = 100, Height = 30 },
    };

    // ───────── Precedência do bruto: setup → comentário → automático ─────────

    [Fact]
    public void BrutoDoSetupVemPrimeiro()
    {
        EffectiveStockResult result = SetupBinding.EffectiveStock(ComPrisma(), MachineKind.Mill, ComBruto, Corte);
        Assert.Equal(StockSource.Setup, result.Source);
        Assert.Equal(Stock.Block(-60, 60, -50, 50, -30, 0), result.Stock);
    }

    [Fact]
    public void SemBrutoNoSetupValeOComentario()
    {
        EffectiveStockResult result = SetupBinding.EffectiveStock(MachiningSetup.CreateDefault("a"), MachineKind.Mill, ComBruto, Corte);
        Assert.Equal(StockSource.Comment, result.Source);
        Assert.Equal(Stock.Block(0, 100, 0, 80, -20, 0), result.Stock);
    }

    [Fact]
    public void SemSetupNemComentarioEAutomatico()
    {
        EffectiveStockResult result = SetupBinding.EffectiveStock(null, MachineKind.Mill, SemBruto, Corte);
        Assert.Equal(StockSource.Automatic, result.Source);
        Assert.Null(result.Stock);
    }

    [Fact]
    public void PrismaNoTornoEIgnoradoEValeOComentario()
    {
        const string torno = "(BRUTO D50 X 70)\nG00 X52 Z2\n";
        EffectiveStockResult result = SetupBinding.EffectiveStock(ComPrisma(), MachineKind.Lathe, torno, Bounds3.Empty);
        Assert.Equal(StockSource.Comment, result.Source);
        Assert.Equal(Stock.Bar(50, 70), result.Stock);
    }

    // ───────── Convenções de posição ─────────

    [Fact]
    public void BrutoRedondoNaFresaEOBlocoQueEnvolvePeloCentro()
    {
        var round = new SetupStock { Kind = SetupStockKind.Round, X = 10, Y = 20, Z = -15, Height = 15, Diameter = 40 };
        Assert.Equal(Stock.Block(-10, 30, 0, 40, -15, 0), round.ToStock(MachineKind.Mill));
    }

    [Fact]
    public void BrutoRedondoNoTornoEBarraComFaceNoTopo()
    {
        var round = new SetupStock { Kind = SetupStockKind.Round, Z = -79, Height = 80, Diameter = 50 };
        Stock bar = round.ToStock(MachineKind.Lathe)!;
        Assert.Equal(Stock.Bar(50, 80, 1), bar);
        Assert.Equal(1, bar.MaxZ);
    }

    [Fact]
    public void TuboNoTornoUsaODiametroExterno()
    {
        var tube = new SetupStock { Kind = SetupStockKind.Tube, Z = -40, Height = 40, Diameter = 60, InnerDiameter = 20 };
        Assert.Equal(60, tube.ToStock(MachineKind.Lathe)!.Diameter);
    }

    [Fact]
    public void BrutoInvalidoNaoViraStock()
    {
        var bad = new SetupStock { Height = -1 };
        Assert.Null(bad.ToStock(MachineKind.Mill));
    }

    [Theory]
    [InlineData(MachineKind.Mill)]
    [InlineData(MachineKind.Lathe)]
    public void ConversaoIdaEVoltaPreservaOBruto(MachineKind machine)
    {
        Stock original = machine == MachineKind.Lathe ? Stock.Bar(42, 65, 0.5) : Stock.Block(-5, 95, -2, 78, -25, 1);
        SetupStock setup = SetupBinding.FromStock(original);
        Assert.Null(setup.Validate());
        Stock back = setup.ToStock(machine)!;

        Assert.Equal(original.Machine, back.Machine);
        Assert.Equal(original.MinX, back.MinX, 9);
        Assert.Equal(original.MaxX, back.MaxX, 9);
        Assert.Equal(original.MinY, back.MinY, 9);
        Assert.Equal(original.MaxY, back.MaxY, 9);
        Assert.Equal(original.MinZ, back.MinZ, 9);
        Assert.Equal(original.MaxZ, back.MaxZ, 9);
    }

    [Fact]
    public void SugestaoUsaComentarioOuTrajetoria()
    {
        Assert.Equal(Stock.Block(0, 100, 0, 80, -20, 0), SetupBinding.SuggestStock(MachineKind.Mill, ComBruto, Corte));
        Assert.Equal(Stock.Block(3, 97, 3, 77, -5, 0), SetupBinding.SuggestStock(MachineKind.Mill, SemBruto, Corte));
    }

    // ───────── Diâmetro das ferramentas: setup → tabela → comentário ─────────

    // Contorno com G41 ao longo de +X: a compensação à esquerda desloca a trajetória em +Y pelo raio
    private const string Contorno = "T1 M06 (FRESA D20)\nG00 X-20 Y-20\nG41 G01 X0 Y0 F100\nG01 X50 Y0\nG40 G01 X70 Y-20\n";

    private static double DeslocamentoEmY(MachiningSetup? setup, Func<int, double>? tabela)
    {
        MachineSettings settings = setup.ApplyTo(new MachineSettings { ToolDiameterOf = tabela });
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(Contorno, Dialect.Fanuc, settings);
        Assert.Equal(0, analysis.ErrorCount);
        return analysis.ToolPath.Bounds.Max.Y;
    }

    [Fact]
    public void FerramentaDoSetupVenceTabelaEComentario()
    {
        var setup = new MachiningSetup { Tools = [new SetupTool { Number = 1, Diameter = 12 }] };
        Assert.Equal(6, DeslocamentoEmY(setup, _ => 8), 6);
    }

    [Fact]
    public void SemFerramentaNoSetupValeATabela()
    {
        var setup = new MachiningSetup { Tools = [new SetupTool { Number = 2, Diameter = 12 }] };
        Assert.Equal(4, DeslocamentoEmY(setup, t => t == 1 ? 8 : 0), 6);
    }

    [Fact]
    public void SemSetupNemTabelaValeOComentario()
    {
        Assert.Equal(10, DeslocamentoEmY(MachiningSetup.CreateDefault("a"), null), 6);
    }

    [Fact]
    public void SetupSemFerramentasNaoMudaAConfiguracao()
    {
        var settings = new MachineSettings { RapidRate = 12_345 };
        Assert.Same(settings, MachiningSetup.CreateDefault("a").ApplyTo(settings));
        Assert.Same(settings, ((MachiningSetup?)null).ApplyTo(settings));
    }
}
