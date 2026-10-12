using System.Text.Json;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;

namespace CncStudio.App.Settings;

/// <summary>Uma ferramenta cadastrada.</summary>
public sealed class ToolEntry
{
    public int Number { get; set; }
    public double Diameter { get; set; } = 10;
    public string Description { get; set; } = "";
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
