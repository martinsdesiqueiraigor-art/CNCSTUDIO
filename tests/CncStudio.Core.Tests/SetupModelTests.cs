using CncStudio.Core.Setup;

namespace CncStudio.Core.Tests;

public class SetupModelTests
{
    private static MachiningSetup Sample() => new()
    {
        ProjectKey = "pecas/flange.nc",
        Stock = new SetupStock { Kind = SetupStockKind.Tube, X = 10, Y = -5, Z = -30, Height = 30, Diameter = 80, InnerDiameter = 30 },
        Fixtures = [new SetupFixture { Kind = SetupFixtureKind.ThreeJawChuck, Name = "Placa", Width = 160, Depth = 160, Rotation = 15 }],
        Tools =
        [
            new SetupTool { Kind = ToolKind.BullNoseEndMill, Number = 3, Diameter = 12, CornerRadius = 1 },
            new SetupTool { Kind = ToolKind.Drill, Number = 7, Diameter = 6.8, TipAngle = 135 },
        ],
    };

    [Fact]
    public void GravaELeSemPerderNada()
    {
        MachiningSetup original = Sample();
        string json = SetupJson.Serialize(original);
        SetupReadResult read = SetupJson.Parse(json);

        Assert.Null(read.Warning);
        Assert.False(read.Migrated);
        MachiningSetup copy = Assert.IsType<MachiningSetup>(read.Setup);
        Assert.Equal(json, SetupJson.Serialize(copy));
        Assert.Equal(SetupStockKind.Tube, copy.Stock!.Kind);
        Assert.Equal(30, copy.Stock.InnerDiameter);
        Assert.Equal(15, copy.Fixtures[0].Rotation);
        Assert.Equal(ToolKind.BullNoseEndMill, copy.ToolOf(3)!.Kind);
        Assert.Equal(135, copy.ToolOf(7)!.TipAngle);
    }

    [Fact]
    public void TipoDeFerramentaUsaNomeEstavelNoJson()
    {
        string json = SetupJson.Serialize(Sample());
        Assert.Contains("\"Kind\":\"fresa-toroidal\"", json);
        Assert.Contains("\"Kind\":\"broca\"", json);
        Assert.Contains("\"SchemaVersion\":2", json);
    }

    [Theory]
    [InlineData("Top")]
    [InlineData("IsRound")]
    [InlineData("Footprint")]
    [InlineData("Radius")]
    public void PropriedadesCalculadasNaoVaoParaOJson(string nome)
    {
        Assert.DoesNotContain("\"" + nome + "\"", SetupJson.Serialize(Sample()));
    }

    [Fact]
    public void BrutoNaoInformadoFicaNulo()
    {
        var setup = MachiningSetup.CreateDefault("a.nc");
        Assert.Null(setup.Stock);
        Assert.True(setup.IsValid());
        Assert.Null(SetupJson.Parse(SetupJson.Serialize(setup)).Setup!.Stock);
    }

    [Fact]
    public void CopiaEIndependente()
    {
        MachiningSetup original = Sample();
        MachiningSetup copy = original.Clone();
        copy.Stock!.Diameter = 99;
        copy.Tools[0].Kind = ToolKind.Tap;
        copy.Fixtures.Clear();

        Assert.Equal(80, original.Stock!.Diameter);
        Assert.Equal(ToolKind.BullNoseEndMill, original.Tools[0].Kind);
        Assert.Single(original.Fixtures);
    }

    public static TheoryData<string, Action<MachiningSetup>> Invalidos => new()
    {
        { "altura negativa", s => s.Stock!.Height = -1 },
        { "posição infinita", s => s.Stock!.X = double.PositiveInfinity },
        { "medida absurda", s => s.Stock!.Diameter = 1e9 },
        { "furo maior que o tubo", s => s.Stock!.InnerDiameter = 80 },
        { "prisma sem largura", s => { s.Stock!.Kind = SetupStockKind.Prism; s.Stock.Width = 0; } },
        { "fixação sem altura", s => s.Fixtures[0].Height = 0 },
        { "raio de canto maior que o raio", s => s.Tools[0].CornerRadius = 6.5 },
        { "ângulo de ponta de 180°", s => s.Tools[1].TipAngle = 180 },
        { "ferramenta T0", s => s.Tools[0].Number = 0 },
        { "ferramenta repetida", s => s.Tools[1].Number = 3 },
        { "ferramentas demais", s => s.Tools.AddRange(Enumerable.Range(100, 19).Select(n => new SetupTool { Number = n })) },
        { "versão errada", s => s.SchemaVersion = 1 },
    };

    [Theory]
    [MemberData(nameof(Invalidos))]
    public void ValidacaoRecusa(string caso, Action<MachiningSetup> estraga)
    {
        MachiningSetup setup = Sample();
        Assert.True(setup.IsValid());
        estraga(setup);
        Assert.False(setup.IsValid(), caso);
        Assert.False(string.IsNullOrWhiteSpace(setup.Validate()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TextoVazioNaoTemSetupNemAviso(string? json)
    {
        SetupReadResult read = SetupJson.Parse(json);
        Assert.Null(read.Setup);
        Assert.Null(read.Warning);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[1,2]")]
    [InlineData("\"texto\"")]
    [InlineData("{\"SchemaVersion\":2,\"Stock\":{\"Height\":\"alto\"}}")]
    [InlineData("{\"SchemaVersion\":2,\"Tools\":[{\"Kind\":\"serra\",\"Number\":1}]}")]
    [InlineData("{\"SchemaVersion\":2,\"Stock\":{\"Height\":-5}}")]
    [InlineData("{\"SchemaVersion\":0}")]
    [InlineData("{\"Stock\":null}")]
    public void JsonInvalidoVoltaParaOPadraoSemExcecao(string json)
    {
        SetupReadResult read = SetupJson.Parse(json);
        Assert.Null(read.Setup);
        Assert.Equal(SetupJson.InvalidWarning, read.Warning);

        (MachiningSetup setup, string? warning) = SetupJson.LoadOrDefault(json, "x.nc");
        Assert.Equal("x.nc", setup.ProjectKey);
        Assert.Null(setup.Stock);
        Assert.Equal(SetupJson.InvalidWarning, warning);
    }

    [Fact]
    public void VersaoFuturaNaoECarregada()
    {
        SetupReadResult read = SetupJson.Parse("{\"SchemaVersion\":3,\"Stock\":null}");
        Assert.Null(read.Setup);
        Assert.Equal(SetupJson.FutureWarning, read.Warning);
    }

    [Fact]
    public void LoadOrDefaultUsaOProgramaAtual()
    {
        string json = SetupJson.Serialize(Sample());
        (MachiningSetup setup, string? warning) = SetupJson.LoadOrDefault(json, "renomeado.nc");
        Assert.Null(warning);
        Assert.Equal("renomeado.nc", setup.ProjectKey);
        Assert.NotNull(setup.Stock);
    }

    // Formato 1 como o protótipo web gravava (JSON indentado, G54 e bruto sempre presentes)
    private const string V1Redondo = """
        {
          "SchemaVersion": 1,
          "ProjectKey": "PECA.nc",
          "Stock": { "Kind": 1, "Diameter": 50, "InnerDiameter": 40, "Width": 50, "Depth": 50, "Height": 25,
                     "X": -25, "Y": -25, "Z": -25, "Material": "Aco" },
          "G54": { "X": 0, "Y": 0, "Z": 0 },
          "Fixtures": [ { "Kind": 2, "Name": "", "X": -80, "Y": -80, "Z": -55, "Width": 160, "Depth": 160,
                          "Height": 30, "JawOpening": 60, "JawThickness": 12, "Rotation": 0 } ],
          "Tools": [
            { "Kind": "Fresa", "Number": 1, "Diameter": 10, "CuttingLength": 25, "HolderDiameter": 35, "HolderLength": 45 },
            { "Kind": "Fresa esferica", "Number": 2, "Diameter": 6, "CuttingLength": 20, "HolderDiameter": 30, "HolderLength": 40 },
            { "Kind": "Broca", "Number": 3, "Diameter": 8.5, "CuttingLength": 40, "HolderDiameter": 30, "HolderLength": 50 },
            { "Kind": "Macho", "Number": 4, "Diameter": 10, "CuttingLength": 20, "HolderDiameter": 30, "HolderLength": 50 },
            { "Kind": "Fresa de facear", "Number": 5, "Diameter": 50, "CuttingLength": 10, "HolderDiameter": 40, "HolderLength": 50 }
          ]
        }
        """;

    [Fact]
    public void MigraFormato1ParaFormato2()
    {
        SetupReadResult read = SetupJson.Parse(V1Redondo);

        Assert.True(read.Migrated);
        Assert.Null(read.Warning);
        MachiningSetup setup = read.Setup!;
        Assert.Equal(MachiningSetup.CurrentSchemaVersion, setup.SchemaVersion);
        Assert.Equal("PECA.nc", setup.ProjectKey);

        // Bruto redondo: o canto (-25, -25) do formato 1 vira o centro (0, 0)
        Assert.Equal(SetupStockKind.Round, setup.Stock!.Kind);
        Assert.Equal(0, setup.Stock.X, 9);
        Assert.Equal(0, setup.Stock.Y, 9);
        Assert.Equal(-25, setup.Stock.Z);
        Assert.Equal(0, setup.Stock.Top);
        Assert.Equal("Aço", setup.Stock.Material);

        Assert.Equal(SetupFixtureKind.ThreeJawChuck, Assert.Single(setup.Fixtures).Kind);
        Assert.Equal(
            [ToolKind.FlatEndMill, ToolKind.BallEndMill, ToolKind.Drill, ToolKind.Tap, ToolKind.FaceMill],
            setup.Tools.Select(t => t.Kind));
        Assert.Equal(SetupTool.DefaultDrillTipAngle, setup.ToolOf(3)!.TipAngle);
        Assert.Equal(0, setup.ToolOf(1)!.CornerRadius);

        // O G54 do formato 1 era decorativo e não existe no formato 2
        Assert.DoesNotContain("G54", SetupJson.Serialize(setup));
    }

    [Fact]
    public void MigraPrismaSemMexerNaPosicao()
    {
        const string v1 = """{"SchemaVersion":1,"ProjectKey":"a","Stock":{"Kind":0,"Width":100,"Depth":80,"Height":20,"X":-50,"Y":-40,"Z":-20},"G54":{"X":5,"Y":5,"Z":0},"Fixtures":[],"Tools":[]}""";
        SetupStock stock = SetupJson.Parse(v1).Setup!.Stock!;
        Assert.Equal((-50, -40, 50, 40), stock.Footprint);
    }

    [Fact]
    public void Formato1InvalidoNaoEMigrado()
    {
        const string v1 = """{"SchemaVersion":1,"ProjectKey":"a","Stock":{"Kind":0,"Width":-1,"Depth":80,"Height":20},"Fixtures":[],"Tools":[]}""";
        Assert.Equal(SetupJson.InvalidWarning, SetupJson.Parse(v1).Warning);
    }

    [Fact]
    public void ContornoDoBrutoRedondoEPeloCentro()
    {
        var stock = new SetupStock { Kind = SetupStockKind.Round, X = 10, Y = 20, Diameter = 40 };
        Assert.Equal((-10, 0, 30, 40), stock.Footprint);
    }
}
