using System.Text.Json;
using System.Text.Json.Nodes;

namespace CncStudio.Core.Generators;

/// <summary>Ponte em JSON entre os geradores e a tela em HTML da web (catálogo, geração e validação).</summary>
public static class GeneratorsJson
{
    /// <summary>Catálogo para montar o formulário: [{id,title,description,params:[{id,label,unit,def,min,max,integer,hint}]}].</summary>
    public static string Catalog()
    {
        var array = new JsonArray();
        foreach (ProgramGenerator g in Generators.All)
        {
            var parameters = new JsonArray();
            foreach (GeneratorParameter p in g.Parameters)
            {
                parameters.Add(new JsonObject
                {
                    ["id"] = p.Id, ["label"] = p.Label, ["unit"] = p.Unit, ["def"] = p.Default,
                    ["min"] = p.Min, ["max"] = p.Max, ["integer"] = p.IsInteger, ["hint"] = p.Hint,
                });
            }

            array.Add(new JsonObject { ["id"] = g.Id, ["title"] = g.Title, ["description"] = g.Description, ["params"] = parameters });
        }

        return array.ToJsonString();
    }

    /// <summary>
    /// Gera o programa. <paramref name="valuesJson"/>: {"X":50,"Y":null,…} (null = campo vazio ou inválido);
    /// <paramref name="mode"/>: "plain" ou "macro". Devolve {"ok":bool,"program":"…","errors":["…"]}.
    /// </summary>
    public static string Generate(string id, string valuesJson, string mode)
    {
        ProgramGenerator? generator = Generators.ById(id);
        if (generator is null)
        {
            return Result(false, "", ["Gerador desconhecido."]);
        }

        Dictionary<string, double> values = [];
        try
        {
            if (JsonNode.Parse(valuesJson) is JsonObject obj)
            {
                foreach ((string key, JsonNode? node) in obj)
                {
                    values[key] = node is JsonValue v && v.TryGetValue(out double d) ? d : double.NaN;
                }
            }
        }
        catch (JsonException)
        {
            return Result(false, "", ["Valores inválidos."]);
        }

        GeneratorResult result = generator.Generate(values, mode == "macro" ? GeneratorOutput.MacroCall : GeneratorOutput.Plain);
        return Result(result.Ok, result.Program, result.Errors);
    }

    private static string Result(bool ok, string program, IReadOnlyList<string> errors) =>
        new JsonObject { ["ok"] = ok, ["program"] = program, ["errors"] = new JsonArray([.. errors.Select(e => (JsonNode?)JsonValue.Create(e))]) }.ToJsonString();

    /// <summary>Junta os valores do último uso (por gerador) para o formulário lembrar do que a pessoa digitou.</summary>
    public static string RememberLast(string? previousJson, string id, string mode, JsonNode? values)
    {
        JsonObject root;
        try { root = JsonNode.Parse(string.IsNullOrWhiteSpace(previousJson) ? "{}" : previousJson) as JsonObject ?? []; }
        catch (JsonException) { root = []; }

        JsonObject all = root["values"] as JsonObject ?? [];
        all[id] = values?.DeepClone();
        root["values"] = all;
        root["id"] = id;
        root["mode"] = mode;
        return root.ToJsonString();
    }
}
