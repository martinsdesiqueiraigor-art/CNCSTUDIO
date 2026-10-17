namespace CncStudio.Core.Setup;

/// <summary>
/// Formato 1 do setup (protótipo web, ramo analise-desktop-2026-10). Só para leitura e migração.
/// Diferenças para o formato 2: bruto redondo posicionado pelo canto, campo G54 decorativo,
/// tipo de ferramenta em texto livre e bruto sempre presente.
/// </summary>
internal sealed class SetupV1
{
    public int SchemaVersion { get; set; }
    public string ProjectKey { get; set; } = "";
    public StockV1? Stock { get; set; }
    public OriginV1? G54 { get; set; }
    public List<SetupFixture>? Fixtures { get; set; }
    public List<ToolV1>? Tools { get; set; }

    internal sealed class StockV1
    {
        public SetupStockKind Kind { get; set; }
        public double Diameter { get; set; } = 80;
        public double InnerDiameter { get; set; } = 40;
        public double Width { get; set; } = 100;
        public double Depth { get; set; } = 80;
        public double Height { get; set; } = 20;
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; } = -20;
        public string Material { get; set; } = "Aco";
    }

    internal sealed class OriginV1
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    internal sealed class ToolV1
    {
        public string Kind { get; set; } = "Fresa";
        public int Number { get; set; } = 1;
        public double Diameter { get; set; } = 10;
        public double CuttingLength { get; set; } = 25;
        public double HolderDiameter { get; set; } = 35;
        public double HolderLength { get; set; } = 45;
    }
}

internal static class SetupMigration
{
    /// <summary>
    /// Formato 1 → 2. O G54 do formato 1 não entrava no cálculo da trajetória (só posicionava
    /// o desenho), por isso é descartado: o bruto já guardava a posição resultante.
    /// </summary>
    public static MachiningSetup FromV1(SetupV1 old)
    {
        var setup = MachiningSetup.CreateDefault(old.ProjectKey);
        if (old.Stock is { } s)
        {
            var stock = new SetupStock
            {
                Kind = s.Kind,
                X = s.X,
                Y = s.Y,
                Z = s.Z,
                Width = s.Width,
                Depth = s.Depth,
                Height = s.Height,
                Diameter = s.Diameter,
                InnerDiameter = s.InnerDiameter,
                Material = s.Material == "Aco" ? "Aço" : s.Material,
            };
            if (stock.IsRound)
            {
                // Formato 1: (X, Y) era o canto do quadrado que envolve o círculo; agora é o centro
                stock.X += s.Diameter / 2;
                stock.Y += s.Diameter / 2;
            }

            setup.Stock = stock;
        }

        setup.Fixtures = old.Fixtures?.Where(f => f is not null).ToList() ?? [];
        setup.Tools = old.Tools?.Where(t => t is not null).Select(t => new SetupTool
        {
            Kind = KindFromV1(t.Kind),
            Number = t.Number,
            Diameter = t.Diameter,
            CuttingLength = t.CuttingLength,
            HolderDiameter = t.HolderDiameter,
            HolderLength = t.HolderLength,
        }).ToList() ?? [];
        return setup;
    }

    private static ToolKind KindFromV1(string? kind) => (kind ?? "").Trim().ToLowerInvariant() switch
    {
        "broca" => ToolKind.Drill,
        "macho" => ToolKind.Tap,
        "fresa esferica" or "fresa esférica" => ToolKind.BallEndMill,
        "fresa de facear" => ToolKind.FaceMill,
        _ => ToolKind.FlatEndMill,
    };
}
