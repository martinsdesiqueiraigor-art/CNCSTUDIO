using System.Text.Json;
using System.Text.Json.Nodes;

namespace CncStudio.Core.Assist;

/// <summary>Resultado de abrir um arquivo de biblioteca do Assist.</summary>
public sealed record AssistLoadResult(AssistLibrary? Library, string? Error)
{
    public bool Ok => Library is not null;
}

/// <summary>Salvar e abrir o <see cref="AssistLibrary"/> em JSON com versão, montado à mão (sem reflexão).</summary>
public static class AssistLibraryJson
{
    private const string Format = "casillas-assist-biblioteca";

    public static string ToJson(AssistLibrary lib)
    {
        var materials = new JsonArray();
        foreach (MaterialEntry m in lib.Materials)
        {
            materials.Add(new JsonObject { ["name"] = m.Name, ["group"] = m.Group.ToString(), ["surfaceSpeed"] = m.SurfaceSpeed, ["feedPerTooth"] = m.FeedPerTooth, ["depthFactor"] = m.DepthFactor, ["draft"] = m.Draft });
        }

        var vises = new JsonArray();
        foreach (ViseEntry v in lib.Vises)
        {
            vises.Add(new JsonObject { ["name"] = v.Name, ["maxOpening"] = v.MaxOpening, ["jawHeight"] = v.JawHeight, ["jawLength"] = v.JawLength, ["bodyWidth"] = v.BodyWidth, ["bodyHeight"] = v.BodyHeight, ["draft"] = v.Draft });
        }

        var jaws = new JsonArray();
        foreach (ChuckJawEntry j in lib.Jaws)
        {
            jaws.Add(new JsonObject { ["name"] = j.Name, ["chuckDiameter"] = j.ChuckDiameter, ["externalMin"] = j.ExternalMin, ["externalMax"] = j.ExternalMax, ["internalMin"] = j.InternalMin, ["internalMax"] = j.InternalMax, ["jawHeight"] = j.JawHeight, ["draft"] = j.Draft });
        }

        var root = new JsonObject { ["format"] = Format, ["version"] = AssistLibrary.CurrentVersion, ["materials"] = materials, ["vises"] = vises, ["jaws"] = jaws };
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    public static AssistLoadResult FromJson(string text)
    {
        try
        {
            if (JsonNode.Parse(text) is not JsonObject root || Str(root, "format") != Format)
            {
                return new AssistLoadResult(null, "O arquivo não é uma biblioteca do Casillas Assist.");
            }

            int version = (int)Num(root, "version");
            if (version > AssistLibrary.CurrentVersion)
            {
                return new AssistLoadResult(null, $"Esta biblioteca é de uma versão mais nova ({version}). Atualize o Casillas Code para abrir.");
            }

            if (version < 1)
            {
                return new AssistLoadResult(null, "Versão da biblioteca inválida.");
            }

            var lib = new AssistLibrary();
            foreach (JsonNode? n in Arr(root, "materials"))
            {
                JsonObject o = n as JsonObject ?? throw new FormatException("Material inválido.");
                if (!Enum.TryParse(Str(o, "group"), false, out MaterialGroup group) || !Enum.IsDefined(group))
                {
                    throw new FormatException($"Grupo de material desconhecido: \"{Str(o, "group")}\".");
                }

                lib.Materials.Add(new MaterialEntry(Str(o, "name"), group, Num(o, "surfaceSpeed"), Num(o, "feedPerTooth"), Num(o, "depthFactor"), Bool(o, "draft")));
            }

            foreach (JsonNode? n in Arr(root, "vises"))
            {
                JsonObject o = n as JsonObject ?? throw new FormatException("Morsa inválida.");
                lib.Vises.Add(new ViseEntry(Str(o, "name"), Num(o, "maxOpening"), Num(o, "jawHeight"), Num(o, "jawLength"), Num(o, "bodyWidth"), Num(o, "bodyHeight"), Bool(o, "draft")));
            }

            foreach (JsonNode? n in Arr(root, "jaws"))
            {
                JsonObject o = n as JsonObject ?? throw new FormatException("Castanha inválida.");
                lib.Jaws.Add(new ChuckJawEntry(Str(o, "name"), Num(o, "chuckDiameter"), Num(o, "externalMin"), Num(o, "externalMax"), Num(o, "internalMin"), Num(o, "internalMax"), Num(o, "jawHeight"), Bool(o, "draft")));
            }

            IReadOnlyList<string> problems = lib.Validate();
            return problems.Count > 0 ? new AssistLoadResult(null, problems[0]) : new AssistLoadResult(lib, null);
        }
        catch (JsonException)
        {
            return new AssistLoadResult(null, "O arquivo está corrompido (não é um JSON válido).");
        }
        catch (FormatException ex)
        {
            return new AssistLoadResult(null, ex.Message);
        }
    }

    private static JsonArray Arr(JsonObject o, string key) => o[key] as JsonArray ?? throw new FormatException($"Falta a lista \"{key}\" no arquivo.");

    private static string Str(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue(out string? s) && s is not null ? s : throw new FormatException($"Falta o texto \"{key}\" no arquivo.");

    private static double Num(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue(out double d) && double.IsFinite(d) ? d : throw new FormatException($"Falta o número \"{key}\" no arquivo.");

    private static bool Bool(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue(out bool b) ? b : throw new FormatException($"Falta o valor sim/não \"{key}\" no arquivo.");
}
