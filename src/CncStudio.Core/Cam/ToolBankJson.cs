using System.Text.Json;
using System.Text.Json.Nodes;

namespace CncStudio.Core.Cam;

/// <summary>
/// Leitura de um arquivo de banco. <see cref="ReadOnly"/>: versão mais nova que a conhecida (abre só para leitura, com
/// <see cref="Warning"/>). Sem banco, <see cref="Error"/> diz o motivo.
/// </summary>
public sealed record ToolBankReadResult(ToolBank? Bank, string? Error, bool ReadOnly = false, string? Warning = null)
{
    public bool Ok => Bank is not null;
}

/// <summary>
/// Arquivo do banco de ferramentas (<c>&lt;id&gt;.casillas-ferramentas</c>), em JSON com versão. Montado à mão com
/// <see cref="JsonObject"/>, como o <see cref="CamJobJson"/>, para servir também à web. Campo sem valor não vai para o arquivo
/// e volta ausente na leitura; chaves desconhecidas são ignoradas. Gravar em disco e escolher a pasta é do App.
/// </summary>
public static class ToolBankJson
{
    public const string Format = "casillas-ferramentas";
    public const int CurrentVersion = 1;
    public const string Extension = ".casillas-ferramentas";

    public const string FutureWarning = "O banco de ferramentas é de uma versão mais nova do Casillas Code: aberto só para leitura.";

    public static string FileName(ToolBank bank) => bank.Id + Extension;

    public static string ToJson(ToolBank bank)
    {
        var tools = new JsonArray();
        foreach (BankTool t in bank.Tools)
        {
            var o = new JsonObject { ["number"] = t.Number };
            Put(o, "name", t.Name);
            Put(o, "type", t.Type is { } type ? BankToolTypes.Code(type) : null);
            Put(o, "diameter", t.Diameter);
            if (t.Flutes is { } flutes)
            {
                o["flutes"] = flutes;
            }

            Put(o, "cuttingLength", t.CuttingLength);
            Put(o, "overallLength", t.OverallLength);
            Put(o, "holderDiameter", t.HolderDiameter);
            Put(o, "holderLength", t.HolderLength);
            Put(o, "cornerRadius", t.CornerRadius);
            Put(o, "tipAngle", t.TipAngle);
            Put(o, "insertWidth", t.InsertWidth);
            Put(o, "isoCode", t.IsoCode);
            Put(o, "noseRadius", t.NoseRadius);
            Put(o, "toolMaterial", t.ToolMaterial);
            Put(o, "rpm", t.Rpm);
            Put(o, "feedXY", t.FeedXY);
            Put(o, "feedZ", t.FeedZ);
            if (t.ToCheck)
            {
                o["toCheck"] = true;
            }

            tools.Add(o);
        }

        var root = new JsonObject
        {
            ["format"] = Format,
            ["version"] = CurrentVersion,
            ["id"] = bank.Id,
            ["name"] = bank.Name,
            ["machine"] = bank.Machine == ToolBankMachine.Mill ? "fresa" : "torno",
            ["tools"] = tools,
        };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>Lê e valida um banco. Nunca lança exceção.</summary>
    public static ToolBankReadResult Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new ToolBankReadResult(null, "O arquivo está vazio.");
        }

        try
        {
            if (JsonNode.Parse(text) is not JsonObject root || OptStr(root, "format") != Format)
            {
                return new ToolBankReadResult(null, "Não é um arquivo de banco de ferramentas do Casillas.");
            }

            int version = (int)CamJobJson.Num(root, "version");
            if (version < 1)
            {
                return new ToolBankReadResult(null, "Versão do banco de ferramentas inválida.");
            }

            bool future = version > CurrentVersion;
            ToolBankMachine machine = CamJobJson.Str(root, "machine") switch
            {
                "fresa" => ToolBankMachine.Mill,
                "torno" => ToolBankMachine.Lathe,
                _ => throw new CamJobJson.FormatException("Máquina do banco desconhecida (fresa ou torno)."),
            };
            var bank = new ToolBank(CamJobJson.Str(root, "id"), CamJobJson.Str(root, "name"), machine)
            {
                Tools = [.. CamJobJson.Arr(root, "tools").Select(n => ToolFrom(n, future))],
            };
            if (bank.Validate() is { } error)
            {
                return new ToolBankReadResult(null, error);
            }

            return future ? new ToolBankReadResult(bank, null, true, FutureWarning) : new ToolBankReadResult(bank, null);
        }
        catch (Exception ex) when (ex is JsonException or CamJobJson.FormatException or InvalidOperationException or OverflowException)
        {
            return new ToolBankReadResult(null, ex is CamJobJson.FormatException ? ex.Message : "O arquivo do banco de ferramentas está estragado.");
        }
    }

    private static BankTool ToolFrom(JsonNode? node, bool future)
    {
        if (node is not JsonObject o)
        {
            throw new CamJobJson.FormatException("Ferramenta do banco inválida.");
        }

        BankToolType? type = null;
        if (OptStr(o, "type") is { } code)
        {
            // versão mais nova pode trazer um tipo que esta não conhece: fica ausente (só leitura); na versão atual, recusa
            type = BankToolTypes.FromCode(code) ?? (future ? null : throw new CamJobJson.FormatException($"Tipo de ferramenta desconhecido: \"{code}\"."));
        }

        double n = CamJobJson.Num(o, "number");
        if (n != Math.Floor(n) || n is < int.MinValue or > int.MaxValue)
        {
            throw new CamJobJson.FormatException("O T da ferramenta precisa ser inteiro.");
        }

        double? flutes = Opt(o, "flutes");
        return new BankTool((int)n)
        {
            Name = OptStr(o, "name"),
            Type = type,
            Diameter = Opt(o, "diameter"),
            Flutes = flutes is { } f ? (f == Math.Floor(f) && f is >= 0 and <= 1000 ? (int)f : throw new CamJobJson.FormatException("Arestas precisa ser inteiro.")) : null,
            CuttingLength = Opt(o, "cuttingLength"),
            OverallLength = Opt(o, "overallLength"),
            HolderDiameter = Opt(o, "holderDiameter"),
            HolderLength = Opt(o, "holderLength"),
            CornerRadius = Opt(o, "cornerRadius"),
            TipAngle = Opt(o, "tipAngle"),
            InsertWidth = Opt(o, "insertWidth"),
            IsoCode = OptStr(o, "isoCode"),
            NoseRadius = Opt(o, "noseRadius"),
            ToolMaterial = OptStr(o, "toolMaterial"),
            Rpm = Opt(o, "rpm"),
            FeedXY = Opt(o, "feedXY"),
            FeedZ = Opt(o, "feedZ"),
            ToCheck = o["toCheck"] is not null && CamJobJson.Bool(o, "toCheck"),
        };
    }

    private static void Put(JsonObject o, string key, double? value)
    {
        if (value is { } v)
        {
            o[key] = v;
        }
    }

    private static void Put(JsonObject o, string key, string? value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            o[key] = value;
        }
    }

    private static double? Opt(JsonObject o, string key) => o[key] is null ? null : CamJobJson.Num(o, key);

    private static string? OptStr(JsonObject o, string key) => o[key] is null ? null : CamJobJson.Str(o, key);
}
