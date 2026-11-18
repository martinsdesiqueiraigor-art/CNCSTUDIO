using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Setup;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>WEB-02: na web, o bruto da fresa segue o comentário do programa (forma, medidas e posição).</summary>
public class Web02ProgramStockTests
{
    private static Bounds3 Box(double x0, double x1, double y0, double y1, double z0, double z1) =>
        Bounds3.Empty.Include(new Point3(x0, y0, z0)).Include(new Point3(x1, y1, z1));

    [Fact]
    public void BlocoComCorteDosDoisLadosFicaCentradoNoZero()
    {
        // corte assimétrico (bolsão só de um lado) não desloca o bruto: o zero no centro é do programa
        SetupStock s = ProgramStock.Read("(BRUTO 100 X 80 X 20)\n", MachineKind.Mill, Box(-10, 40, -30, 5, -5, 0))!;
        Assert.Equal(SetupStockKind.Prism, s.Kind);
        Assert.Equal((-50.0, -40.0, -20.0), (s.X, s.Y, s.Z));
        Assert.Equal((100.0, 80.0, 20.0), (s.Width, s.Depth, s.Height));
    }

    [Fact]
    public void CortePositivoPoeOCantoNoZeroENegativoOCantoOposto()
    {
        SetupStock pos = ProgramStock.Read("(BRUTO 100 X 80 X 20)", MachineKind.Mill, Box(5, 90, 5, 70, -5, 0))!;
        Assert.Equal((0.0, 0.0), (pos.X, pos.Y));
        SetupStock neg = ProgramStock.Read("(BRUTO 100 X 80 X 20)", MachineKind.Mill, Box(-90, -5, 5, 70, -5, 0))!;
        Assert.Equal((-100.0, 0.0), (neg.X, neg.Y));
    }

    [Fact]
    public void CorteTodoAcimaDeZ0PoeZ0NaBase()
    {
        SetupStock s = ProgramStock.Read("(BRUTO 100 X 80 X 20)", MachineKind.Mill, Box(-40, 40, -30, 30, 12, 20))!;
        Assert.Equal(0, s.Z);
        Assert.Equal(20, s.Top);
    }

    [Theory]
    [InlineData("ZERO G54: CENTRO DA PECA, Z0 NO TOPO", -50, -40, -20)]
    [InlineData("ZERO G55: CANTO INFERIOR ESQUERDO DA PECA, Z0 NA BASE", 0, 0, 0)]
    [InlineData("ZERO G54: CANTO SUPERIOR DIREITO DA PECA, Z0 NO TOPO", -100, -80, -20)]
    [InlineData("ZERO G54: MEIO DA ARESTA ESQUERDA DA PECA, Z0 NA BASE", 0, -40, 0)]
    public void LinhaDoZeroDoCamManda(string zero, double x, double y, double z)
    {
        // o corte diria "canto no zero e topo": a linha do zero vence
        string program = $"O1\n({zero})\n(BRUTO 100 X 80 X 20)\nG00 X0 Y0\n";
        SetupStock s = ProgramStock.Read(program, MachineKind.Mill, Box(5, 90, 5, 70, -5, 0))!;
        Assert.Equal((x, y, z), (s.X, s.Y, s.Z));
    }

    [Theory]
    [InlineData("(BRUTO D50 X 30)")]
    [InlineData("(BRUTO REDONDO Ø50 X 30)")]
    [InlineData("(STOCK DIAM 50 x 30)")]
    public void RedondoNaFresaViraCilindroEmPe(string comment)
    {
        SetupStock s = ProgramStock.Read(comment, MachineKind.Mill, Box(-20, 20, -20, 20, -10, 0))!;
        Assert.Equal(SetupStockKind.Round, s.Kind);
        Assert.Equal(SetupAxis.Z, s.Axis);
        Assert.Equal((50.0, 30.0), (s.Diameter, s.Height));
        Assert.Equal((0.0, 0.0, -30.0), (s.X, s.Y, s.Z));
    }

    [Fact]
    public void RedondoComCortePositivoTemOCentroNoRaio()
    {
        SetupStock s = ProgramStock.Read("(BRUTO D50 X 30)", MachineKind.Mill, Box(5, 45, 5, 45, -10, 0))!;
        Assert.Equal((25.0, 25.0), (s.X, s.Y));
    }

    [Theory]
    [InlineData("(BRUTO D50 D30 X 30)")]
    [InlineData("(BRUTO TUBO D50 X D30 X 30)")]
    [InlineData("(BRUTO Ø50/Ø30 X 30)")]
    public void TuboNaFresa(string comment)
    {
        SetupStock s = ProgramStock.Read(comment, MachineKind.Mill, Box(-20, 20, -20, 20, -10, 0))!;
        Assert.Equal(SetupStockKind.Tube, s.Kind);
        Assert.Equal((50.0, 30.0, 30.0), (s.Diameter, s.InnerDiameter, s.Height));
        Assert.Null(s.Validate());
    }

    [Fact]
    public void SemComentarioNaoHaBruto()
    {
        Assert.Null(ProgramStock.Read("O1\n(MATERIAL 1045)\nG00 X0\n", MachineKind.Mill, Box(0, 10, 0, 10, -1, 0)));
    }

    [Fact]
    public void ProgramaMandaSobreOSetupAntigoESemMexerNoOriginal()
    {
        var setup = new MachiningSetup { Stock = new SetupStock { Kind = SetupStockKind.Prism, X = 0, Y = 0, Z = -20, Width = 90, Depth = 70, Height = 26 } };
        setup.Tools.Add(new SetupTool { Number = 1, Diameter = 10 });
        MachiningSetup sim = SetupBinding.ProgramFirst(setup, MachineKind.Mill, "(BRUTO 100 X 80 X 20)\n", Box(-10, 10, -10, 10, -5, 0));
        Assert.Equal(100, sim.Stock!.Width);
        Assert.Equal(-50, sim.Stock.X);
        Assert.Single(sim.Tools);
        Assert.Equal(90, setup.Stock.Width);
        Assert.Null(SetupBinding.OverrideNote(setup, MachineKind.Mill, "(BRUTO 100 X 80 X 20)\n", Bounds3.Empty));
    }

    [Fact]
    public void OutroBrutoDePropositoValeEAvisa()
    {
        var setup = new MachiningSetup { Stock = new SetupStock { Kind = SetupStockKind.Prism, Z = -26, Width = 90, Depth = 70, Height = 26, OverridesProgram = true } };
        MachiningSetup sim = SetupBinding.ProgramFirst(setup, MachineKind.Mill, "(BRUTO 100 X 80 X 20)\n", Bounds3.Empty);
        Assert.Equal(90, sim.Stock!.Width);
        string? note = SetupBinding.OverrideNote(setup, MachineKind.Mill, "(BRUTO 100 X 80 X 20)\n", Bounds3.Empty);
        Assert.Contains("90 × 70 × 26", note);
        Assert.Contains("Do programa", note);
    }

    [Fact]
    public void SemComentarioOSetupFicaComoEsta()
    {
        var setup = new MachiningSetup { Stock = new SetupStock { Width = 90 } };
        Assert.Equal(90, SetupBinding.ProgramFirst(setup, MachineKind.Mill, "G00 X0\n", Bounds3.Empty).Stock!.Width);
        Assert.Null(SetupBinding.ProgramFirst(null, MachineKind.Mill, "G00 X0\n", Bounds3.Empty).Stock);
    }

    [Fact]
    public void TornoSegueABarraDoComentario()
    {
        var setup = new MachiningSetup { Stock = new SetupStock { Kind = SetupStockKind.Round, Z = -80, Diameter = 60, Height = 80 } };
        MachiningSetup sim = SetupBinding.ProgramFirst(setup, MachineKind.Lathe, "(BRUTO D50 X 70)\n", Bounds3.Empty);
        Assert.Equal((50.0, 70.0, 0.0), (sim.Stock!.Diameter, sim.Stock.Height, sim.Stock.Top));
    }

    [Fact]
    public void MarcaAusenteNaoEGravadaEVoltaNaLeitura()
    {
        var plain = new MachiningSetup { Stock = new SetupStock() };
        Assert.DoesNotContain("OverridesProgram", SetupJson.Serialize(plain));
        plain.Stock.OverridesProgram = true;
        string json = SetupJson.Serialize(plain);
        Assert.Contains("\"OverridesProgram\":true", json);
        Assert.True(SetupJson.Parse(json).Setup!.Stock!.OverridesProgram);
    }
}
