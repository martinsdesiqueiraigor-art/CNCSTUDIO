using System.Text.Json;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Simulation;

namespace CncStudio.App.Settings;

/// <summary>Uma ferramenta cadastrada.</summary>
public sealed class ToolEntry
{
    public int Number { get; set; }
    public double Diameter { get; set; } = 10;
    public string Description { get; set; } = "";
}

/// <summary>Bruto informado para um programa (guardado pelo caminho do arquivo).</summary>
public sealed class StockEntry
{
    public string Key { get; set; } = "";
    public string Machine { get; set; } = "Mill";
    public double MinX { get; set; }
    public double MaxX { get; set; }
    public double MinY { get; set; }
    public double MaxY { get; set; }
    public double MinZ { get; set; }
    public double MaxZ { get; set; }
}

/// <summary>Um zero de peça (G54–G59).</summary>
public sealed class WorkOffsetEntry
{
    public int Code { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
}

/// <summary>
/// Configurações do programa, salvas em JSON na pasta do usuário
/// (%APPDATA%\CncStudio\config.json no Windows).
/// </summary>
public sealed class AppSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Diâmetro usado para ferramentas que não estão na tabela.</summary>
    public double DefaultToolDiameter { get; set; } = 10;

    public List<ToolEntry> Tools { get; set; } = [];

    public List<WorkOffsetEntry> WorkOffsets { get; set; } =
        Enumerable.Range(54, 6).Select(code => new WorkOffsetEntry { Code = code }).ToList();

    /// <summary>Fanuc: "X10" = 0,010 mm (true) ou 10 mm (false).</summary>
    public bool FanucIntegersAsThousandths { get; set; }

    public double EditorFontSize { get; set; } = 14;

    /// <summary>Brutos informados, por programa (os mais recentes no fim).</summary>
    public List<StockEntry> Stocks { get; set; } = [];

    /// <summary>Bruto informado para o programa (null = automático, pela trajetória).</summary>
    public Stock? StockFor(string key)
    {
        StockEntry? e = Stocks.LastOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
        if (e is null)
        {
            return null;
        }

        var stock = new Stock(e.Machine == "Lathe" ? MachineKind.Lathe : MachineKind.Mill, e.MinX, e.MaxX, e.MinY, e.MaxY, e.MinZ, e.MaxZ);
        return stock.IsValid ? stock : null;
    }

    /// <summary>Guarda (ou apaga, com null) o bruto do programa. Mantém os 100 mais recentes.</summary>
    public void SetStock(string key, Stock? stock)
    {
        Stocks.RemoveAll(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
        if (stock is not null)
        {
            Stocks.Add(new StockEntry
            {
                Key = key,
                Machine = stock.Machine == MachineKind.Lathe ? "Lathe" : "Mill",
                MinX = stock.MinX, MaxX = stock.MaxX, MinY = stock.MinY, MaxY = stock.MaxY, MinZ = stock.MinZ, MaxZ = stock.MaxZ,
            });
        }

        if (Stocks.Count > 100)
        {
            Stocks.RemoveRange(0, Stocks.Count - 100);
        }
    }

    /// <summary>Mostrar a explicação do código ao parar o mouse no editor.</summary>
    public bool ShowCodeHelp { get; set; } = true;

    /// <summary>Verificar limites de curso (coordenadas do programa; no torno, X em diâmetro).</summary>
    public bool TravelLimitsEnabled { get; set; }
    public double? LimitMinX { get; set; }
    public double? LimitMaxX { get; set; }
    public double? LimitMinY { get; set; }
    public double? LimitMaxY { get; set; }
    public double? LimitMinZ { get; set; }
    public double? LimitMaxZ { get; set; }

    /// <summary>Tipo de máquina: "Auto" (pelo programa), "Mill" ou "Lathe".</summary>
    public string Machine { get; set; } = "Auto";

    /// <summary>Rotação máxima do fuso (rpm) no G96 quando o programa não limita.</summary>
    public double MaxSpindleSpeed { get; set; } = 4000;

    /// <summary>Velocidade de rápido (G00) em mm/min, usada na estimativa de tempo.</summary>
    public double RapidRate { get; set; } = 10_000;

    public static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CncStudio", "config.json");

    /// <summary>Carrega do disco; se não existir ou estiver corrompido, usa os padrões.</summary>
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOptions);
                if (settings is not null)
                {
                    settings.Normalize();
                    return settings;
                }
            }
        }
        catch (Exception)
        {
            // Arquivo inválido: segue com os padrões (não vale travar o programa por isso)
        }

        return new AppSettings();
    }

    /// <summary>Salva no disco. Falhas são ignoradas (ex.: pasta sem permissão).</summary>
    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch (Exception)
        {
            // Sem onde salvar: as configurações valem só nesta sessão
        }
    }

    /// <summary>Diâmetro da ferramenta T (ou o padrão, se não cadastrada).</summary>
    public double DiameterOf(int tool)
    {
        foreach (ToolEntry entry in Tools)
        {
            if (entry.Number == tool && entry.Diameter > 0)
            {
                return entry.Diameter;
            }
        }

        return DefaultToolDiameter;
    }

    /// <summary>
    /// Configuração usada pelo executor do Core. "programFolder" é a pasta do programa aberto,
    /// onde os subprogramas externos (O1234.nc, L10.SPF, FURO.SPF...) são procurados.
    /// </summary>
    public MachineSettings ToMachineSettings(string? programFolder)
    {
        // Cópia dos diâmetros: a análise roda em outra thread e não deve ver edições no meio
        var diameters = Tools.Where(t => t.Diameter > 0).GroupBy(t => t.Number).ToDictionary(g => g.Key, g => g.Last().Diameter);
        double defaultDiameter = DefaultToolDiameter;

        return new MachineSettings
        {
            WorkOffsets = WorkOffsets.ToDictionary(o => o.Code, o => new Point3(o.X, o.Y, o.Z)),
            RapidRate = RapidRate,
            TravelLimits = TravelLimitsEnabled
                ? new TravelLimits(LimitMinX, LimitMaxX, LimitMinY, LimitMaxY, LimitMinZ, LimitMaxZ)
                : null,
            MaxSpindleSpeed = MaxSpindleSpeed,
            Machine = Machine switch
            {
                "Mill" => MachineKind.Mill,
                "Lathe" => MachineKind.Lathe,
                _ => null,
            },
            FanucIntegers = FanucIntegersAsThousandths ? FanucIntegerMode.Thousandths : FanucIntegerMode.Millimeters,
            ToolDiameterOf = tool => diameters.TryGetValue(tool, out double d) ? d : defaultDiameter,
            LoadSubprogram = programFolder is null ? null : name => SubprogramFiles.Load(programFolder, name),
        };
    }

    /// <summary>Cópia independente (para o diálogo editar sem mexer no original até confirmar).</summary>
    public AppSettings Clone() =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(this, JsonOptions), JsonOptions)!;

    /// <summary>Garante G54–G59 presentes e valores válidos.</summary>
    private void Normalize()
    {
        Tools ??= [];
        Stocks ??= [];
        WorkOffsets ??= [];
        for (int code = 54; code <= 59; code++)
        {
            if (!WorkOffsets.Exists(o => o.Code == code))
            {
                WorkOffsets.Add(new WorkOffsetEntry { Code = code });
            }
        }

        WorkOffsets.Sort((a, b) => a.Code.CompareTo(b.Code));
        if (DefaultToolDiameter <= 0)
        {
            DefaultToolDiameter = 10;
        }

        if (MaxSpindleSpeed <= 0)
        {
            MaxSpindleSpeed = 4000;
        }

        Machine ??= "Auto";

        if (RapidRate <= 0)
        {
            RapidRate = 10_000;
        }

        if (EditorFontSize < 6 || EditorFontSize > 60)
        {
            EditorFontSize = 14;
        }
    }
}
