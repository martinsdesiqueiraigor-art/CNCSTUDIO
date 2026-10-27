using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CncStudio.Core.Cam;

/// <summary>Onde o eixo rotativo fica: na mesa (gira a peça) ou no cabeçote (inclina a ferramenta).</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RotaryCarrier>))]
public enum RotaryCarrier
{
    [JsonStringEnumMemberName("mesa")]
    Table,

    [JsonStringEnumMemberName("cabecote")]
    Head,
}

/// <summary>Como o programa de 4 e 5 eixos é escrito.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RotaryOutputMode>))]
public enum RotaryOutputMode
{
    /// <summary>O CAM calcula X, Y e Z já girados; a máquina só recebe posições (serve em qualquer máquina com eixo rotativo).</summary>
    [JsonStringEnumMemberName("calculado")]
    Calculated,

    /// <summary>Usa função da máquina (G68.2 / CYCLE800 / TCP). Só vale se o controle tiver o opcional.</summary>
    [JsonStringEnumMemberName("funcao")]
    Native,
}

/// <summary>Tipo da máquina pela quantidade e posição dos eixos rotativos.</summary>
public enum MachineConfiguration
{
    ThreeAxis,
    FourAxisTable,
    FourAxisHead,
    FiveAxisTableTable,
    FiveAxisHeadHead,
    FiveAxisMixed,
}

/// <summary>
/// Um eixo rotativo. A letra define em torno de qual eixo linear ele gira: A = X, B = Y, C = Z.
/// O pivô é um ponto da linha de giro, em coordenadas da máquina (mm).
/// </summary>
public sealed class RotaryAxisConfig
{
    public string Letter { get; set; } = "A";

    public RotaryCarrier Carrier { get; set; } = RotaryCarrier.Table;

    public double MinAngle { get; set; } = -120;

    public double MaxAngle { get; set; } = 120;

    /// <summary>Sentido do giro invertido em relação à regra da mão direita.</summary>
    public bool Reversed { get; set; }

    public double PivotX { get; set; }

    public double PivotY { get; set; }

    public double PivotZ { get; set; }

    /// <summary>Eixo linear em torno do qual gira: "X", "Y" ou "Z".</summary>
    [JsonIgnore]
    public string LinearAxis => Letter switch { "A" => "X", "B" => "Y", "C" => "Z", _ => "?" };

    public bool Contains(double angle) => angle >= MinAngle - 1e-9 && angle <= MaxAngle + 1e-9;

    public RotaryAxisConfig Clone() => (RotaryAxisConfig)MemberwiseClone();
}

/// <summary>
/// Perfil de máquina para 4 e 5 eixos: comando, eixos rotativos (de 0 a 2) e modo de saída.
/// A ordem de <see cref="Axes"/> vai do lado da máquina para o lado da peça (para mesa dupla: o eixo que
/// carrega o outro vem primeiro). O programador escolhe a configuração; nada é suposto do modelo da máquina.
/// </summary>
public sealed class MachineProfile
{
    public const int MaxRotaryAxes = 2;

    public string Name { get; set; } = "Minha máquina";

    [JsonConverter(typeof(JsonStringEnumConverter<CamDialect>))]
    public CamDialect Controller { get; set; } = CamDialect.Fanuc;

    public RotaryOutputMode Mode { get; set; } = RotaryOutputMode.Calculated;

    public List<RotaryAxisConfig> Axes { get; set; } = [];

    [JsonIgnore]
    public MachineConfiguration Configuration
    {
        get
        {
            int tables = Axes.Count(a => a.Carrier == RotaryCarrier.Table);
            int heads = Axes.Count - tables;
            return Axes.Count switch
            {
                0 => MachineConfiguration.ThreeAxis,
                1 => tables == 1 ? MachineConfiguration.FourAxisTable : MachineConfiguration.FourAxisHead,
                _ => tables == 2 ? MachineConfiguration.FiveAxisTableTable : heads == 2 ? MachineConfiguration.FiveAxisHeadHead : MachineConfiguration.FiveAxisMixed,
            };
        }
    }

    /// <summary>Texto curto, ex.: "5 eixos mista: cabeçote B + mesa C".</summary>
    public string Describe()
    {
        string kind = Configuration switch
        {
            MachineConfiguration.ThreeAxis => "3 eixos (sem eixo rotativo)",
            MachineConfiguration.FourAxisTable => "4 eixos, rotativo na mesa",
            MachineConfiguration.FourAxisHead => "4 eixos, rotativo no cabeçote",
            MachineConfiguration.FiveAxisTableTable => "5 eixos, dois rotativos na mesa",
            MachineConfiguration.FiveAxisHeadHead => "5 eixos, dois rotativos no cabeçote",
            _ => "5 eixos mista",
        };
        if (Axes.Count == 0)
        {
            return kind;
        }

        string axes = string.Join(" + ", Axes.Select(a => $"{(a.Carrier == RotaryCarrier.Table ? "mesa" : "cabeçote")} {a.Letter}"));
        return $"{kind}: {axes}";
    }

    public RotaryAxisConfig? AxisOf(string letter) => Axes.FirstOrDefault(a => a.Letter == letter);

    /// <summary>Erros que impedem usar o perfil (lista vazia = válido).</summary>
    public List<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name))
        {
            errors.Add("Dê um nome ao perfil da máquina.");
        }

        if (Axes.Count > MaxRotaryAxes)
        {
            errors.Add($"No máximo {MaxRotaryAxes} eixos rotativos.");
        }

        var seen = new HashSet<string>();
        foreach (RotaryAxisConfig a in Axes)
        {
            if (a.Letter is not ("A" or "B" or "C"))
            {
                errors.Add($"Eixo rotativo \"{a.Letter}\" inválido: use A, B ou C.");
                continue;
            }

            if (!seen.Add(a.Letter))
            {
                errors.Add($"O eixo {a.Letter} aparece duas vezes.");
            }

            if (!double.IsFinite(a.MinAngle) || !double.IsFinite(a.MaxAngle) || a.MinAngle >= a.MaxAngle)
            {
                errors.Add($"Eixo {a.Letter}: o limite mínimo precisa ser menor que o máximo.");
            }
            else if (a.MaxAngle - a.MinAngle > 3600 || Math.Abs(a.MinAngle) > 3600 || Math.Abs(a.MaxAngle) > 3600)
            {
                errors.Add($"Eixo {a.Letter}: limites fora do razoável (até ±3600°).");
            }

            if (!double.IsFinite(a.PivotX) || !double.IsFinite(a.PivotY) || !double.IsFinite(a.PivotZ))
            {
                errors.Add($"Eixo {a.Letter}: o pivô tem valor inválido.");
            }
        }

        return errors;
    }

    /// <summary>Avisos (não impedem o uso).</summary>
    public List<string> Warnings()
    {
        var warnings = new List<string>();
        if (Mode == RotaryOutputMode.Native)
        {
            warnings.Add(Controller == CamDialect.Fanuc
                ? "Função da máquina (G68.2/TCP) no Fanuc só funciona com o opcional. O Fanuc 21i costuma não ter; confirme antes de rodar."
                : "Função da máquina (CYCLE800/TRAORI) só funciona se a opção estiver ativa no controle; confirme antes de rodar.");
        }

        if (Axes.Any(a => a.MinAngle > 0 || a.MaxAngle < 0))
        {
            warnings.Add("Um dos eixos não passa pelo ângulo zero: confira os limites.");
        }

        return warnings;
    }

    public MachineProfile Clone() => new()
    {
        Name = Name,
        Controller = Controller,
        Mode = Mode,
        Axes = Axes.Select(a => a.Clone()).ToList(),
    };

    // ----- Modelos prontos (o programador ajusta cursos e pivôs depois) -----

    public static MachineProfile ThreeAxis(string name = "3 eixos") => new() { Name = name };

    public static MachineProfile FourAxisTableA(string name = "4º eixo na mesa (A)") =>
        new() { Name = name, Axes = [new RotaryAxisConfig { Letter = "A", Carrier = RotaryCarrier.Table, MinAngle = -360, MaxAngle = 360 }] };

    public static MachineProfile FiveAxisTableTable(string name = "5 eixos: mesa A + C") => new()
    {
        Name = name,
        Axes =
        [
            new RotaryAxisConfig { Letter = "A", Carrier = RotaryCarrier.Table, MinAngle = -30, MaxAngle = 110 },
            new RotaryAxisConfig { Letter = "C", Carrier = RotaryCarrier.Table, MinAngle = -360, MaxAngle = 360 },
        ],
    };

    public static MachineProfile FiveAxisMixed(string name = "5 eixos mista: cabeçote B + mesa C") => new()
    {
        Name = name,
        Axes =
        [
            new RotaryAxisConfig { Letter = "B", Carrier = RotaryCarrier.Head, MinAngle = -110, MaxAngle = 110 },
            new RotaryAxisConfig { Letter = "C", Carrier = RotaryCarrier.Table, MinAngle = -360, MaxAngle = 360 },
        ],
    };
}

/// <summary>Perfil de máquina em JSON (arquivo para compartilhar e cópia nas configurações).</summary>
public static class MachineProfileJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.Strict,
    };

    public static string Serialize(MachineProfile profile) => JsonSerializer.Serialize(profile, Options);

    /// <summary>Lê e valida um perfil. Em caso de erro, devolve mensagem em português e perfil nulo.</summary>
    public static (MachineProfile? Profile, string? Error) Parse(string json)
    {
        try
        {
            MachineProfile? profile = JsonSerializer.Deserialize<MachineProfile>(json, Options);
            if (profile is null)
            {
                return (null, "Arquivo de perfil vazio.");
            }

            profile.Axes ??= [];
            profile.Axes.RemoveAll(a => a is null);
            foreach (RotaryAxisConfig axis in profile.Axes)
            {
                axis.Letter = (axis.Letter ?? "").Trim().ToUpper(CultureInfo.InvariantCulture);
            }

            List<string> errors = profile.Validate();
            return errors.Count > 0 ? (null, string.Join(" ", errors)) : (profile, null);
        }
        catch (JsonException ex)
        {
            return (null, "Perfil de máquina inválido: " + ex.Message);
        }
    }
}
