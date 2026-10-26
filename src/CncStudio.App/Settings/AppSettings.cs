using System.Text.Json;
using CncStudio.Core.Execution;
using System.Text.Json.Serialization;
using CncStudio.Core.Geometry;
using CncStudio.Core.Setup;
using CncStudio.Core.Simulation;

namespace CncStudio.App.Settings;

/// <summary>Uma ferramenta cadastrada.</summary>
public sealed class ToolEntry
{
    public int Number { get; set; }
    public double Diameter { get; set; } = 10;
    public string Description { get; set; } = "";

    /// <summary>Tipo da ferramenta de torno (nome de <see cref="ToolKind"/>); vazio = padrão (desbaste externo).</summary>
    public string Kind { get; set; } = "";

    /// <summary>Largura do inserto do bedame (mm).</summary>
    public double InsertWidth { get; set; } = SetupTool.DefaultInsertWidth;

    /// <summary>Dados de corte guardados pelo Casillas CAM (0 = não informado): rotação (rpm) e avanços (mm/min).</summary>
    public double SpindleRpm { get; set; }

    public double FeedXY { get; set; }

    public double FeedZ { get; set; }
}

/// <summary>
/// Bruto informado para um programa, no formato antigo (antes do setup). Só é lido para migrar:
/// ao carregar, cada um vira o bruto do setup do programa.
/// </summary>
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

/// <summary>Setup de um programa (guardado pelo caminho do arquivo), no JSON do modelo do Core.</summary>
public sealed class SetupEntry
{
    public string Key { get; set; } = "";
    public string Json { get; set; } = "";
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

    /// <summary>Tema da interface: "Dark" ou "Light".</summary>
    public string Theme { get; set; } = "Dark";

    /// <summary>Tema opcional CMYK (ciano de destaque, fundo azul-petróleo); vale só no tema escuro.</summary>
    public bool Cmyk { get; set; }

    /// <summary>Arquivos abertos recentemente (o mais recente primeiro).</summary>
    public List<string> RecentFiles { get; set; } = [];

    /// <summary>Formato antigo dos brutos por programa: lido só para migrar (nunca mais gravado).</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<StockEntry>? Stocks { get; set; }

    /// <summary>Setups por programa (os mais recentes no fim). Fonte única do bruto de cada programa.</summary>
    public List<SetupEntry> Setups { get; set; } = [];

    /// <summary>
    /// Cópia do setup com os tipos de ferramenta de torno escolhidos na tabela de ferramentas.
    /// Quem já está cadastrado no próprio setup do programa vale mais que a tabela.
    /// </summary>
    public MachiningSetup WithToolKinds(MachiningSetup setup)
    {
        MachiningSetup copy = setup.Clone();
        foreach (ToolEntry entry in Tools)
        {
            if (Enum.TryParse(entry.Kind, out ToolKind kind) && new SetupTool { Kind = kind }.IsLathe
                && copy.ToolOf(entry.Number) is null && copy.Tools.Count < MachiningSetup.MaxTools)
            {
                copy.Tools.Add(new SetupTool
                {
                    Kind = kind,
                    Number = entry.Number,
                    Diameter = entry.Diameter > 0 ? entry.Diameter : 10,
                    InsertWidth = entry.InsertWidth > 0 ? entry.InsertWidth : SetupTool.DefaultInsertWidth,
                });
            }
        }

        return copy;
    }

    /// <summary>Setup do programa (o padrão, sem bruto informado, se não houver um salvo).</summary>
    public MachiningSetup SetupFor(string key)
    {
        SetupEntry? e = Setups.LastOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
        return SetupJson.LoadOrDefault(e?.Json, key).Setup;
    }

    /// <summary>Guarda o setup do programa (um setup vazio apaga o registro). Mantém os 100 mais recentes.</summary>
    public void SetSetup(string key, MachiningSetup setup)
    {
        Setups.RemoveAll(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
        bool empty = setup.Stock is null && setup.Fixtures.Count == 0 && setup.Tools.Count == 0;
        if (!empty)
        {
            setup.ProjectKey = key;
            Setups.Add(new SetupEntry { Key = key, Json = SetupJson.Serialize(setup) });
        }

        if (Setups.Count > 100)
        {
            Setups.RemoveRange(0, Setups.Count - 100);
        }
    }

    /// <summary>Converte os brutos do formato antigo em setups (só os que ainda não têm setup).</summary>
    private void MigrateLegacyStocks()
    {
        if (Stocks is not { Count: > 0 } legacy)
        {
            Stocks = null;
            return;
        }

        foreach (StockEntry e in legacy)
        {
            var stock = new Stock(e.Machine == "Lathe" ? MachineKind.Lathe : MachineKind.Mill, e.MinX, e.MaxX, e.MinY, e.MaxY, e.MinZ, e.MaxZ);
            bool hasSetup = Setups.Exists(s => string.Equals(s.Key, e.Key, StringComparison.OrdinalIgnoreCase));
            if (stock.IsValid && !hasSetup)
            {
                var setup = MachiningSetup.CreateDefault(e.Key);
                setup.Stock = SetupBinding.FromStock(stock);
                SetSetup(e.Key, setup);
            }
        }

        Stocks = null;
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
            ToolDiameterOf = tool => diameters.TryGetValue(tool, out double d) ? d : 0,
            FallbackToolDiameter = defaultDiameter, // vale depois da tabela e do comentário (FRESA D10)
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
        Setups ??= [];
        Setups.RemoveAll(s => s is null);
        MigrateLegacyStocks();
        RecentFiles ??= [];
        Theme = Theme == "Light" ? "Light" : "Dark";
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
