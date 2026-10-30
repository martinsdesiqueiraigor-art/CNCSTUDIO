using System.Text.Json;
using System.Text.Json.Nodes;

namespace CncStudio.Core.Generators;

/// <summary>Ponte em JSON entre os geradores e a tela em HTML da web (catálogo, geração e validação).</summary>
public static class GeneratorsJson
{
    /// <summary>
    /// Seção do formulário em que o campo aparece: "geometry", "depth" ou "cutting". Regra só de apresentação, pelo
    /// identificador e pelo nome do campo (os valores e o cálculo não dependem dela).
    /// </summary>
    public static string GroupOf(GeneratorParameter p)
    {
        if (p.Id is "F" or "S" or "T" || p.Label.StartsWith("Avanço", StringComparison.Ordinal) || p.Label.StartsWith("Diâmetro da fresa", StringComparison.Ordinal))
        {
            return "cutting";
        }

        return p.Id is "W" or "Z" or "R" or "Q" ? "depth" : "geometry";
    }

    /// <summary>Catálogo para montar o formulário: [{id,title,description,macro,params:[{id,label,unit,def,min,max,integer,hint,group}],texts:[{id,label,def,max,hint}]}].</summary>
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
                    ["min"] = p.Min, ["max"] = p.Max, ["integer"] = p.IsInteger, ["hint"] = p.Hint, ["group"] = GroupOf(p),
                });
            }

            var texts = new JsonArray();
            foreach (GeneratorTextParameter t in g.TextParameters)
            {
                texts.Add(new JsonObject { ["id"] = t.Id, ["label"] = t.Label, ["def"] = t.Default, ["max"] = t.MaxLength, ["hint"] = t.Hint });
            }

            array.Add(new JsonObject { ["id"] = g.Id, ["title"] = g.Title, ["description"] = g.Description, ["params"] = parameters, ["texts"] = texts, ["macro"] = g.SupportsMacro });
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
        Dictionary<string, string> texts = [];
        try
        {
            if (JsonNode.Parse(valuesJson) is JsonObject obj)
            {
                foreach ((string key, JsonNode? node) in obj)
                {
                    if (node is JsonValue sv && sv.TryGetValue(out string? str))
                    {
                        texts[key] = str ?? "";
                        continue;
                    }

                    values[key] = node is JsonValue v && v.TryGetValue(out double d) ? d : double.NaN;
                }
            }
        }
        catch (JsonException)
        {
            return Result(false, "", ["Valores inválidos."]);
        }

        GeneratorResult result = generator.Generate(values, texts, mode == "macro" && generator.SupportsMacro ? GeneratorOutput.MacroCall : GeneratorOutput.Plain);
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
