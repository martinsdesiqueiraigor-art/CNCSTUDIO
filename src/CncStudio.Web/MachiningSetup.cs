using System.Text.Json;

namespace CncStudio.Web;

/// <summary>Setup físico da fresa (bruto, zero, fixações e ferramentas), independente da interpretação do G-code.</summary>
public sealed class MachiningSetup
{
    public int SchemaVersion { get; set; } = 1;
    public string ProjectKey { get; set; } = "";
    public SetupStock Stock { get; set; } = new();
    public SetupOrigin G54 { get; set; } = new();
    public List<SetupFixture> Fixtures { get; set; } = [];
    public List<SetupToolGeometry> Tools { get; set; } = [];

    public static MachiningSetup CreateDefault(string projectKey) => new() { ProjectKey = projectKey };

    public static MachiningSetup? Parse(string json)
    {
        try
        {
            var result = JsonSerializer.Deserialize(json, SetupJsonContext.Default.MachiningSetup);
            return result is not null && result.IsValid() ? result : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Cópia independente (alterações nela não afetam o original).</summary>
    public MachiningSetup Clone() =>
        JsonSerializer.Deserialize(ToJson(), SetupJsonContext.Default.MachiningSetup)!;

    public string ToJson() => JsonSerializer.Serialize(this, SetupJsonContext.Default.MachiningSetup);

    public bool IsValid() =>
        SchemaVersion == 1 && ProjectKey.Length <= 512 &&
        Stock.IsValid() && G54.IsValid() &&
        Fixtures.Count <= 40 && Fixtures.All(x => x.IsValid()) &&
        Tools.Count <= 20 && Tools.All(x => x.IsValid()) &&
        Tools.Select(x => x.Number).Distinct().Count() == Tools.Count;
}

public enum SetupStockKind { Prism, Round, Tube }

public sealed class SetupStock
{
    public SetupStockKind Kind { get; set; } = SetupStockKind.Prism;
    public double Diameter { get; set; } = 80;
    public double InnerDiameter { get; set; } = 40;
    public double Width { get; set; } = 100;
    public double Depth { get; set; } = 80;
    public double Height { get; set; } = 20;
    public double X { get; set; } = 0;
    public double Y { get; set; } = 0;
    public double Z { get; set; } = -20;
    public string Material { get; set; } = "Aco";
    public bool IsValid() => Enum.IsDefined(Kind) &&
        SetupNumbers.Positive(Width) && SetupNumbers.Positive(Depth) &&
        SetupNumbers.Positive(Height) && SetupNumbers.Positive(Diameter) &&
        SetupNumbers.Finite(InnerDiameter, X, Y, Z) && InnerDiameter >= 0 &&
        (Kind != SetupStockKind.Tube || InnerDiameter < Diameter) && Material.Length <= 100;
}

public sealed class SetupOrigin
{
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public bool IsValid() => SetupNumbers.Finite(X, Y, Z);
}

public enum SetupFixtureKind { Vise, Clamp, ThreeJawChuck }

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
    public double Rotation { get; set; }
    public bool IsValid() => Enum.IsDefined(Kind) && Name.Length <= 100 &&
        SetupNumbers.Finite(X, Y, Z) && SetupNumbers.Positive(Width) &&
        SetupNumbers.Positive(Depth) && SetupNumbers.Positive(Height) &&
        SetupNumbers.Positive(JawOpening) && SetupNumbers.Positive(JawThickness) &&
        SetupNumbers.Finite(Rotation);
}

public sealed class SetupToolGeometry
{
    public string Kind { get; set; } = "Fresa";
    public int Number { get; set; } = 1;
    public double Diameter { get; set; } = 10;
    public double CuttingLength { get; set; } = 25;
    public double HolderDiameter { get; set; } = 35;
    public double HolderLength { get; set; } = 45;
    public bool IsValid() => (Kind is "Fresa" or "Broca" or "Macho" or "Fresa esferica" or "Fresa de facear") && Number is >= 1 and <= 9999 &&
        SetupNumbers.Positive(Diameter) && SetupNumbers.Positive(CuttingLength) &&
        SetupNumbers.Positive(HolderDiameter) && SetupNumbers.Positive(HolderLength);
}

internal static class SetupNumbers
{
    public static bool Finite(params double[] values) => values.All(double.IsFinite);
    public static bool Positive(double value) => double.IsFinite(value) && value > 0 && value <= 100000;
}
