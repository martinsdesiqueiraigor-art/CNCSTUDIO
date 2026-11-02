using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CncStudio.Core.Academy;

/// <summary>
/// Converte uma aula do JSON v1 para o v2 (ficha ACAD-03). Mapa: abertura + texto → visao.texto, resumo → visao.conceitos,
/// programa → exemplo.programa, erro_comum → cuidados.erros, aviso → cuidados.alertas, exercicio + quiz → praticar.
/// Trajetória e sintaxe ficam vazias (o v1 não tem esses dados). Dialeto fanuc; máquina torno só na trilha Torno.
/// Cada seção herda o "status" da aula; trajetória e sintaxe vazias ficam "rascunho".
/// </summary>
public static class AcademyConverter
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Devolve o JSON v2 (indentado) ou null com o motivo. Aula que já é v2 volta sem mudança.</summary>
    public static string? ToV2(string v1Json, out string error)
    {
        error = "";
        JsonObject? o;
        try
        {
            o = JsonNode.Parse(v1Json) as JsonObject;
        }
        catch (JsonException e)
        {
            error = "JSON inválido: " + e.Message;
            return null;
        }

        if (o is null)
        {
            error = "O arquivo não é um objeto JSON.";
            return null;
        }

        if (AcademyReader.VersionOf(o) >= 2)
        {
            return o.ToJsonString(Pretty);
        }

        string Str(string key) => o[key] is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "";
        JsonNode? Copy(string key) => o[key]?.DeepClone();
        JsonArray List(params string[] items)
        {
            var a = new JsonArray();
            foreach (string s in items.Where(s => s.Length > 0))
            {
                a.Add(JsonValue.Create(s));
            }

            return a;
        }

        string status = Str("status");
        string opening = Str("abertura"), text = Str("texto");
        var v2 = new JsonObject
        {
            ["versao"] = 2,
            ["id"] = Str("id"),
            ["modulo"] = Str("modulo"),
            ["titulo"] = Str("titulo"),
            ["nivel"] = Str("nivel"),
            ["caminho"] = Str("caminho"),
            ["dialeto"] = "fanuc",
            ["maquina"] = Str("caminho") == "Torno" ? "torno" : "fresa",
            ["estado"] = status,
            ["objetivo"] = Str("objetivo"),
            ["pre_requisito"] = Str("pre_requisito"),
            ["secoes"] = new JsonObject
            {
                ["visao"] = new JsonObject
                {
                    ["estado"] = status,
                    ["texto"] = opening.Length > 0 ? opening + "\n\n" + text : text,
                    ["conceitos"] = Copy("resumo") ?? new JsonArray(),
                },
                ["trajetoria"] = new JsonObject { ["estado"] = "rascunho", ["programa"] = "" },
                ["sintaxe"] = new JsonObject { ["estado"] = "rascunho", ["blocos"] = new JsonArray(), ["parametros"] = new JsonArray() },
                ["exemplo"] = new JsonObject { ["estado"] = status, ["programa"] = Str("programa"), ["etapas"] = new JsonArray() },
                ["cuidados"] = new JsonObject { ["estado"] = status, ["erros"] = List(Str("erro_comum")), ["alertas"] = List(Str("aviso")) },
                ["praticar"] = new JsonObject { ["estado"] = status, ["exercicio"] = Copy("exercicio"), ["quiz"] = Copy("quiz") ?? new JsonArray() },
            },
        };

        // campos opcionais do v1 que a tela usa e que o v2 mantém como estão
        foreach (string key in new[] { "diagramas", "video", "observacoes" })
        {
            if (o[key] is not null)
            {
                v2[key] = Copy(key);
            }
        }

        return v2.ToJsonString(Pretty);
    }
}
