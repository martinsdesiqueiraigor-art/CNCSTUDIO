using System.Text.Json.Serialization;

namespace CncStudio.Core.Setup;

/// <summary>
/// Setup de usinagem de um programa: bruto, fixações e ferramentas.
/// É a fonte única desses dados para a simulação, os relatórios e as interfaces (web e desktop).
///
/// Coordenadas: tudo em mm, nas coordenadas do programa (o zero de peça do programa é a origem).
/// Os zeros de peça da máquina (G54–G59) continuam em <see cref="Execution.MachineSettings.WorkOffsets"/>.
/// </summary>
public sealed class MachiningSetup
{
    /// <summary>Versão do formato salvo por esta versão do Casillas Code.</summary>
    public const int CurrentSchemaVersion = 2;

    public const int MaxFixtures = 40;
    public const int MaxTools = 20;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>Programa a que o setup pertence (caminho na biblioteca ou nome do arquivo).</summary>
    public string ProjectKey { get; set; } = "";

    /// <summary>Bruto informado. Null = não informado: vale o comentário (BRUTO ...) ou o automático.</summary>
    public SetupStock? Stock { get; set; }

    public List<SetupFixture> Fixtures { get; set; } = [];

    public List<SetupTool> Tools { get; set; } = [];

    public static MachiningSetup CreateDefault(string projectKey) => new() { ProjectKey = projectKey ?? "" };

    /// <summary>Ferramenta cadastrada com o número T (null se não houver).</summary>
    public SetupTool? ToolOf(int number) => Tools.FirstOrDefault(t => t.Number == number);

    /// <summary>Cópia independente: alterações nela não afetam o original.</summary>
    public MachiningSetup Clone() => SetupJson.Parse(SetupJson.Serialize(this)).Setup!;

    public bool IsValid() => Validate() is null;

    /// <summary>Null quando válido; senão, o primeiro problema encontrado (em português).</summary>
    public string? Validate()
    {
        if (SchemaVersion != CurrentSchemaVersion)
        {
            return "Versão do setup não suportada.";
        }

        if ((ProjectKey ?? "").Length > 512)
        {
            return "Nome do programa longo demais.";
        }

        if (Stock is not null && Stock.Validate() is { } stockError)
        {
            return stockError;
        }

        if (Fixtures is null || Fixtures.Count > MaxFixtures)
        {
            return $"No máximo {MaxFixtures} fixações.";
        }

        foreach (SetupFixture fixture in Fixtures)
        {
            if ((fixture is null ? "Fixação vazia." : fixture.Validate()) is { } fixtureError)
            {
                return fixtureError;
            }
        }

        if (Tools is null || Tools.Count > MaxTools)
        {
            return $"No máximo {MaxTools} ferramentas.";
        }

        foreach (SetupTool tool in Tools)
        {
            if ((tool is null ? "Ferramenta vazia." : tool.Validate()) is { } toolError)
            {
                return toolError;
            }
        }

        if (Tools.Select(t => t.Number).Distinct().Count() != Tools.Count)
        {
            return "Número de ferramenta repetido.";
        }

        return null;
    }
}

public enum SetupStockKind
{
    /// <summary>Bloco retangular.</summary>
    Prism = 0,

    /// <summary>Cilindro maciço.</summary>
    Round = 1,

    /// <summary>Cilindro com furo central.</summary>
    Tube = 2,
}

/// <summary>
/// Bruto. Posição em coordenadas do programa:
/// <list type="bullet">
/// <item>Prisma: (X, Y) é o canto de menor X e menor Y; tamanho Width × Depth.</item>
/// <item>Redondo e tubo: (X, Y) é o centro; Diameter (e InnerDiameter no tubo).</item>
/// <item>Z é o fundo do bruto; o topo fica em Z + Height (no torno, Height é o comprimento e o topo é a face).</item>
/// </list>
/// </summary>
public sealed class SetupStock
{
    public SetupStockKind Kind { get; set; } = SetupStockKind.Prism;
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; } = -20;
    public double Width { get; set; } = 100;
    public double Depth { get; set; } = 80;
    public double Height { get; set; } = 20;
    public double Diameter { get; set; } = 80;
    public double InnerDiameter { get; set; } = 40;
    public string Material { get; set; } = "Aço";

    [JsonIgnore]
    public double Top => Z + Height;

    [JsonIgnore]
    public bool IsRound => Kind is SetupStockKind.Round or SetupStockKind.Tube;

    /// <summary>Retângulo ocupado no plano XY (menor X, menor Y, maior X, maior Y).</summary>
    [JsonIgnore]
    public (double MinX, double MinY, double MaxX, double MaxY) Footprint => IsRound
        ? (X - Diameter / 2, Y - Diameter / 2, X + Diameter / 2, Y + Diameter / 2)
        : (X, Y, X + Width, Y + Depth);

    public string? Validate()
    {
        if (!Enum.IsDefined(Kind))
        {
            return "Tipo de bruto desconhecido.";
        }

        if (!SetupNumbers.Finite(X, Y, Z) || !SetupNumbers.Positive(Height))
        {
            return "Posição ou altura do bruto inválida.";
        }

        if (Kind == SetupStockKind.Prism && !(SetupNumbers.Positive(Width) && SetupNumbers.Positive(Depth)))
        {
            return "Largura e profundidade do bruto precisam ser positivas.";
        }

        if (IsRound && !SetupNumbers.Positive(Diameter))
        {
            return "O diâmetro do bruto precisa ser positivo.";
        }

        if (Kind == SetupStockKind.Tube && !(SetupNumbers.Positive(InnerDiameter) && InnerDiameter < Diameter))
        {
            return "O diâmetro interno do tubo precisa ser positivo e menor que o externo.";
        }

        if ((Material ?? "").Length > 100)
        {
            return "Nome do material longo demais.";
        }

        return null;
    }
}

public enum SetupFixtureKind
{
    Vise = 0,
    Clamp = 1,
    ThreeJawChuck = 2,
}

/// <summary>
/// Fixação (representação simplificada). (X, Y, Z) é o canto de menor X, Y e Z da base;
/// Width × Depth × Height é a base. Na placa de 3 castanhas, Width é o diâmetro do corpo.
/// </summary>
public sealed class SetupFixture
{
    public SetupFixtureKind Kind { get; set; } = SetupFixtureKind.Vise;
    public string Name { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; } = -30;
    public double Width { get; set; } = 150;
    public double Depth { get; set; } = 110;
    public double Height { get; set; } = 30;
    public double JawOpening { get; set; } = 60;
    public double JawThickness { get; set; } = 12;

    /// <summary>Rotação em torno de Z, em graus.</summary>
    public double Rotation { get; set; }

    public string? Validate()
    {
        if (!Enum.IsDefined(Kind))
        {
            return "Tipo de fixação desconhecido.";
        }

        if ((Name ?? "").Length > 100)
        {
            return "Nome da fixação longo demais.";
        }

        if (!SetupNumbers.Finite(X, Y, Z, Rotation))
        {
            return "Posição da fixação inválida.";
        }

        if (!(SetupNumbers.Positive(Width) && SetupNumbers.Positive(Depth) && SetupNumbers.Positive(Height)
              && SetupNumbers.Positive(JawOpening) && SetupNumbers.Positive(JawThickness)))
        {
            return "As medidas da fixação precisam ser positivas.";
        }

        return null;
    }
}

/// <summary>Tipo de ferramenta. O nome no JSON é estável e não depende do idioma da interface.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ToolKind>))]
public enum ToolKind
{
    [JsonStringEnumMemberName("fresa")]
    FlatEndMill,

    [JsonStringEnumMemberName("fresa-esferica")]
    BallEndMill,

    [JsonStringEnumMemberName("fresa-toroidal")]
    BullNoseEndMill,

    [JsonStringEnumMemberName("broca")]
    Drill,

    [JsonStringEnumMemberName("macho")]
    Tap,

    [JsonStringEnumMemberName("fresa-facear")]
    FaceMill,

    // ── Torno ──

    /// <summary>Ferramenta de torneamento externo (inserto rômbico).</summary>
    [JsonStringEnumMemberName("torno-externa")]
    TurnExternal,

    /// <summary>Barra de mandrilar (torneamento interno).</summary>
    [JsonStringEnumMemberName("torno-interna")]
    TurnInternal,

    /// <summary>Bedame externo (canal e corte).</summary>
    [JsonStringEnumMemberName("bedame")]
    PartExternal,

    /// <summary>Bedame interno (canal dentro do furo).</summary>
    [JsonStringEnumMemberName("bedame-interno")]
    PartInternal,

    /// <summary>Ferramenta de rosca externa.</summary>
    [JsonStringEnumMemberName("rosca-externa")]
    ThreadExternal,

    /// <summary>Ferramenta de rosca interna.</summary>
    [JsonStringEnumMemberName("rosca-interna")]
    ThreadInternal,
}

/// <summary>Ferramenta com geometria: corte e suporte (porta-ferramenta).</summary>
public sealed class SetupTool
{
    public const double DefaultDrillTipAngle = 118;
    public const double DefaultInsertWidth = 3;

    /// <summary>Ferramenta própria do torno (a broca serve aos dois).</summary>
    [JsonIgnore]
    public bool IsLathe => Kind is ToolKind.TurnExternal or ToolKind.TurnInternal or ToolKind.PartExternal
        or ToolKind.PartInternal or ToolKind.ThreadExternal or ToolKind.ThreadInternal;

    public ToolKind Kind { get; set; } = ToolKind.FlatEndMill;

    /// <summary>Número T (1 a 9999).</summary>
    public int Number { get; set; } = 1;

    public double Diameter { get; set; } = 10;

    /// <summary>Raio de canto da fresa toroidal (0 nas demais). No máximo metade do diâmetro.</summary>
    public double CornerRadius { get; set; }

    /// <summary>Ângulo da ponta da broca, em graus (118 é o padrão de mercado).</summary>
    public double TipAngle { get; set; } = DefaultDrillTipAngle;

    /// <summary>Largura do inserto do bedame (mm). Só vale para os bedames.</summary>
    public double InsertWidth { get; set; } = DefaultInsertWidth;

    public double CuttingLength { get; set; } = 25;
    public double HolderDiameter { get; set; } = 35;
    public double HolderLength { get; set; } = 45;

    [JsonIgnore]
    public double Radius => Diameter / 2;

    public string? Validate()
    {
        if (!Enum.IsDefined(Kind))
        {
            return "Tipo de ferramenta desconhecido.";
        }

        if (Number is < 1 or > 9999)
        {
            return "O número da ferramenta vai de 1 a 9999.";
        }

        if (!(SetupNumbers.Positive(Diameter) && SetupNumbers.Positive(CuttingLength)
              && SetupNumbers.Positive(HolderDiameter) && SetupNumbers.Positive(HolderLength)))
        {
            return $"T{Number}: as medidas da ferramenta precisam ser positivas.";
        }

        if (!double.IsFinite(CornerRadius) || CornerRadius < 0 || CornerRadius > Diameter / 2)
        {
            return $"T{Number}: o raio de canto vai de 0 até metade do diâmetro.";
        }

        if (!double.IsFinite(TipAngle) || TipAngle <= 0 || TipAngle >= 180)
        {
            return $"T{Number}: o ângulo da ponta precisa estar entre 0° e 180°.";
        }

        if (!SetupNumbers.Positive(InsertWidth) || InsertWidth > 50)
        {
            return $"T{Number}: a largura do inserto vai de 0 até 50 mm.";
        }

        return null;
    }
}

internal static class SetupNumbers
{
    /// <summary>Maior medida aceita (mm): protege contra valores absurdos digitados.</summary>
    public const double MaxMeasure = 100_000;

    public static bool Finite(params double[] values) => values.All(v => double.IsFinite(v) && Math.Abs(v) <= MaxMeasure);

    public static bool Positive(double value) => double.IsFinite(value) && value > 0 && value <= MaxMeasure;
}
