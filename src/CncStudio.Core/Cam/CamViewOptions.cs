namespace CncStudio.Core.Cam;

/// <summary>Como o bruto aparece no 3D do Trabalho fresa.</summary>
public enum StockDisplay { Solid, Transparent, Hidden }

/// <summary>Como a peça aparece no 3D do Trabalho fresa.</summary>
public enum PartDisplay { Solid, Wireframe, Hidden, Transparent }

/// <summary>
/// Escolhas de exibição do 3D do Trabalho fresa (chips sobre a vista). Só visualização: não mexe na
/// trajetória nem remove material. Guardadas no config local do app (<see cref="ToText"/>), nunca no .casillas-job.
/// </summary>
public sealed record CamViewOptions
{
    /// <summary>Dica do chip "Peça sólida" enquanto o Trabalho só tem contornos 2D.</summary>
    public const string MissingPartHeight = "A peça sólida dos contornos do DXF ainda não existe. A peça em STEP já aparece sólida no 3D (DESK-01).";

    /// <summary>Padrões aprovados pelo Igor (UX-04d).</summary>
    public static CamViewOptions Default { get; } = new();

    public StockDisplay Stock { get; init; } = StockDisplay.Transparent;
    public PartDisplay Part { get; init; } = PartDisplay.Solid;
    public bool Edges { get; init; } = true;
    public bool Path { get; init; } = true;
    public bool Rapids { get; init; } = true;
    public bool Axes { get; init; } = true;

    /// <summary>Os rápidos só aparecem com a trajetória ligada.</summary>
    public bool ShowsRapids => Path && Rapids;

    /// <summary>Modo da peça que dá para desenhar: sem peça sólida (só contornos 2D) não há sólido nem transparente, e a peça vira aramado (contorno em Z 0).</summary>
    public PartDisplay EffectivePart(bool hasSolidPart) => Part is PartDisplay.Solid or PartDisplay.Transparent && !hasSolidPart ? PartDisplay.Wireframe : Part;

    /// <summary>Texto curto para o config ("bruto=transparente;peca=solida;arestas=1;...").</summary>
    public string ToText() => string.Join(';',
        "bruto=" + Stock switch { StockDisplay.Solid => "solido", StockDisplay.Hidden => "oculto", _ => "transparente" },
        "peca=" + Part switch { PartDisplay.Wireframe => "aramado", PartDisplay.Hidden => "oculta", PartDisplay.Transparent => "transparente", _ => "solida" },
        "arestas=" + Bit(Edges), "trajetoria=" + Bit(Path), "rapidos=" + Bit(Rapids), "eixos=" + Bit(Axes));

    /// <summary>Lê o texto do config; o que faltar ou vier errado fica no padrão.</summary>
    public static CamViewOptions Parse(string? text)
    {
        CamViewOptions o = Default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return o;
        }

        foreach (string pair in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            string key = pair[..eq].Trim().ToLowerInvariant(), value = pair[(eq + 1)..].Trim().ToLowerInvariant();
            bool? flag = value switch { "1" => true, "0" => false, _ => null };
            o = key switch
            {
                "bruto" => value switch
                {
                    "solido" => o with { Stock = StockDisplay.Solid },
                    "transparente" => o with { Stock = StockDisplay.Transparent },
                    "oculto" => o with { Stock = StockDisplay.Hidden },
                    _ => o,
                },
                "peca" => value switch
                {
                    "solida" => o with { Part = PartDisplay.Solid },
                    "aramado" => o with { Part = PartDisplay.Wireframe },
                    "oculta" => o with { Part = PartDisplay.Hidden },
                    "transparente" => o with { Part = PartDisplay.Transparent }, // CAM-ILHAS-02; programa antigo ignora e fica no padrão
                    _ => o,
                },
                "arestas" when flag is { } f => o with { Edges = f },
                "trajetoria" when flag is { } f => o with { Path = f },
                "rapidos" when flag is { } f => o with { Rapids = f },
                "eixos" when flag is { } f => o with { Axes = f },
                _ => o,
            };
        }

        return o;
    }

    private static string Bit(bool value) => value ? "1" : "0";
}
