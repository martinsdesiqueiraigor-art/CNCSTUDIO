using System.Text.Json;
using System.Text.Json.Nodes;

namespace CncStudio.Core.Cam;

/// <summary>Resultado de ler um pacote do CAM: o pacote ou nada, com o aviso quando ele não serviu (o programa abre como hoje).</summary>
public sealed record HandoffReadResult(CamHandoff? Package, string? Warning);

/// <summary>
/// Pacote do CAM (<see cref="CamHandoff"/>) em JSON, montado à mão com <see cref="JsonObject"/> (sem reflexão, funciona no WebAssembly),
/// como o <see cref="CamJobJson"/>. Campo ausente na leitura = null (nada suposto); os campos que o CAM não tem são gravados como null.
/// </summary>
public static class CamHandoffJson
{
    public const string Format = "casillas-cam";

    public const string FutureWarning = "O arquivo de dados do CAM é de uma versão mais nova e foi ignorado.";

    public const string InvalidWarning = "O arquivo de dados do CAM está ilegível e foi ignorado.";

    public static string ToJson(CamHandoff p)
    {
        var root = new JsonObject
        {
            ["formato"] = Format,
            ["versao"] = p.Version,
            ["hashPrograma"] = p.ProgramHash,
            ["controle"] = p.Controller.ToString(),
            ["maquina"] = p.Machine,
            ["perfilMaquina"] = p.MachineProfile,
            ["programaNumero"] = p.ProgramNumber,
            ["g5x"] = p.WorkOffset,
            ["zero"] = p.Zero is { } z
                ? new JsonObject { ["ponto"] = z.Point.ToString(), ["z0"] = z.Level.ToString(), ["comoAntes"] = z.AsBefore, ["resumo"] = z.Summary }
                : null,
            ["fixacao"] = p.Fixture is { } f
                ? new JsonObject { ["tipo"] = f.Kind, ["nome"] = JobFixture.NameOf(f.Kind), ["descricao"] = f.Description }
                : null,
            ["bruto"] = p.Stock is { } s ? StockToJson(s) : null,
            ["ferramentas"] = new JsonArray([.. p.Tools.Select(ToolToJson)]),
        };
        if (p.BankName is { } bank)
        {
            // UX-04h C2: chave opcional, só informativa; sem banco não é gravada (o leitor v1 antigo ignora chave desconhecida)
            root["banco"] = bank;
        }

        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject StockToJson(HandoffStock s)
    {
        var o = new JsonObject { ["forma"] = KindName(s.Kind) };
        if (s.Kind == HandoffStockKind.Step)
        {
            StepStockSummary? b = s.StepSummary;
            o["caminho"] = s.StepPath;
            o["resumo"] = b is null ? null : new JsonObject
            {
                ["minX"] = b.MinX, ["minY"] = b.MinY, ["minZ"] = b.MinZ, ["maxX"] = b.MaxX, ["maxY"] = b.MaxY, ["maxZ"] = b.MaxZ, ["volume"] = b.Volume,
            };
            o["naoDesenhado"] = true;
            return o;
        }

        if (s.Kind != HandoffStockKind.Prism)
        {
            o["eixo"] = s.Axis.ToString();
        }

        o["x"] = s.X;
        o["y"] = s.Y;
        o["z"] = s.Z;
        if (s.Kind == HandoffStockKind.Prism)
        {
            o["largura"] = s.Width;
            o["profundidade"] = s.Depth;
        }
        else
        {
            o["diametro"] = s.Diameter;
            if (s.Kind == HandoffStockKind.Tube)
            {
                o["diametroInterno"] = s.InnerDiameter;
            }
        }

        o["altura"] = s.Height;
        return o;
    }

    private static JsonObject ToolToJson(HandoffTool t) => new()
    {
        ["t"] = t.Number,
        ["diametro"] = t.Diameter,
        ["nome"] = t.Name,
        ["rpm"] = t.SpindleRpm,
        ["avancoXY"] = t.FeedXY,
        ["avancoZ"] = t.FeedZ,
        // o CAM não tem estes dados: null, nunca um valor suposto
        ["tipo"] = t.Type,
        ["raioCanto"] = t.CornerRadius,
        ["anguloPonta"] = t.TipAngle,
        ["comprimentoCorte"] = t.CutLength,
        ["suporte"] = t.Holder,
    };

    private static string KindName(HandoffStockKind k) => k switch
    {
        HandoffStockKind.Prism => "prisma",
        HandoffStockKind.Round => "redondo",
        HandoffStockKind.Tube => "tubo",
        _ => "step",
    };

    /// <summary>
    /// Lê um pacote. Texto vazio = sem pacote (sem aviso). Versão mais nova que a conhecida: ignorado com aviso (como o setup). Estragado:
    /// ignorado com aviso. Nunca lança exceção.
    /// </summary>
    public static HandoffReadResult Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new HandoffReadResult(null, null);
        }

        try
        {
            if (JsonNode.Parse(text) is not JsonObject root || OptStr(root, "formato") != Format)
            {
                return new HandoffReadResult(null, InvalidWarning);
            }

            int version = (int)N(root, "versao");
            if (version > CamHandoff.CurrentVersion)
            {
                return new HandoffReadResult(null, FutureWarning);
            }

            string machine = CamJobJson.Str(root, "maquina");
            if (version < 1 || machine is not (CamHandoff.Mill or CamHandoff.Lathe))
            {
                return new HandoffReadResult(null, InvalidWarning);
            }

            var p = new CamHandoff(CamJobJson.Str(root, "hashPrograma"), CamJobJson.Enum<CamDialect>(CamJobJson.Str(root, "controle"), "controle"))
            {
                Version = version,
                Machine = machine,
                MachineProfile = OptStr(root, "perfilMaquina"),
                ProgramNumber = OptInt(root, "programaNumero"),
                WorkOffset = OptInt(root, "g5x"),
                Zero = root["zero"] is JsonObject z ? ZeroFrom(z) : null,
                Fixture = root["fixacao"] is JsonObject f ? new JobFixture(CamJobJson.Str(f, "tipo"), OptStr(f, "descricao") ?? "") : null,
                Stock = root["bruto"] is JsonObject s ? StockFrom(s) : null,
                Tools = root["ferramentas"] is JsonArray tools ? [.. tools.Select(ToolFrom)] : [],
                BankName = OptStr(root, "banco"),
            };
            return new HandoffReadResult(p, null);
        }
        catch (Exception ex) when (ex is JsonException or CamJobJson.FormatException or InvalidOperationException or OverflowException)
        {
            return new HandoffReadResult(null, InvalidWarning);
        }
    }

    private static HandoffZero ZeroFrom(JsonObject z) => new(
        CamJobJson.Enum<ZeroPoint>(CamJobJson.Str(z, "ponto"), "ponto do zero"),
        CamJobJson.Enum<ZeroLevel>(CamJobJson.Str(z, "z0"), "Z0 do zero"),
        z["comoAntes"] is not null && CamJobJson.Bool(z, "comoAntes"),
        OptStr(z, "resumo") ?? "");

    private static HandoffStock StockFrom(JsonObject o)
    {
        string kind = CamJobJson.Str(o, "forma");
        if (kind == "step")
        {
            StepStockSummary? summary = o["resumo"] is JsonObject b
                ? new StepStockSummary(N(b, "minX"), N(b, "minY"), N(b, "minZ"), N(b, "maxX"), N(b, "maxY"), N(b, "maxZ"), N(b, "volume"))
                : null;
            return new HandoffStock(HandoffStockKind.Step) { StepPath = OptStr(o, "caminho"), StepSummary = summary };
        }

        if (kind == "prisma")
        {
            return new HandoffStock(HandoffStockKind.Prism)
            {
                X = N(o, "x"), Y = N(o, "y"), Z = N(o, "z"), Width = N(o, "largura"), Depth = N(o, "profundidade"), Height = N(o, "altura"),
            };
        }

        HandoffStockKind round = kind switch
        {
            "redondo" => HandoffStockKind.Round,
            "tubo" => HandoffStockKind.Tube,
            _ => throw new CamJobJson.FormatException($"Forma do bruto desconhecida: \"{kind}\"."),
        };
        return new HandoffStock(round)
        {
            Axis = CamJobJson.Enum<StockAxis>(CamJobJson.Str(o, "eixo"), "eixo do bruto"),
            X = N(o, "x"),
            Y = N(o, "y"),
            Z = N(o, "z"),
            Height = N(o, "altura"),
            Diameter = N(o, "diametro"),
            InnerDiameter = round == HandoffStockKind.Tube ? N(o, "diametroInterno") : 0,
        };
    }

    private static HandoffTool ToolFrom(JsonNode? node)
    {
        JsonObject o = node as JsonObject ?? throw new CamJobJson.FormatException("Ferramenta inválida no pacote.");
        return new HandoffTool((int)N(o, "t"), N(o, "diametro"), OptStr(o, "nome") ?? "", N(o, "rpm"), N(o, "avancoXY"), N(o, "avancoZ"))
        {
            Type = OptStr(o, "tipo"),
            CornerRadius = OptNum(o, "raioCanto"),
            TipAngle = OptNum(o, "anguloPonta"),
            CutLength = OptNum(o, "comprimentoCorte"),
            Holder = OptStr(o, "suporte"),
        };
    }

    private static double N(JsonObject o, string key) => CamJobJson.Num(o, key);

    // ausente ou null = null; presente com outro tipo = pacote estragado
    private static string? OptStr(JsonObject o, string key) => o[key] is null ? null : CamJobJson.Str(o, key);

    private static double? OptNum(JsonObject o, string key) => o[key] is null ? null : N(o, key);

    private static int? OptInt(JsonObject o, string key) => o[key] is null ? null : (int)N(o, key);
}
