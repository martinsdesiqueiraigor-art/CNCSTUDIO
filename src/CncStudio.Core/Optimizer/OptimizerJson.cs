using System.Text.Json;
using System.Text.Json.Nodes;

namespace CncStudio.Core.Optimizer;

/// <summary>
/// Ponte em JSON entre o Otimizador (Core) e a tela em HTML da web. Comandos: <c>analyze</c> {text, profile} e <c>anonymize</c> {text}.
/// Sem reflexão: o JSON é montado à mão (a web compilada com corte de código não tem metadados para serializar tipos).
/// Tudo que sai daqui é só para simulador: <c>machineReady</c> é sempre falso.
/// </summary>
public static class OptimizerJson
{
    public const int MaxLines = 50_000;
    public const int MaxChars = 4_000_000;
    private const int ExpandedPreview = 14;

    public const string Warning = "Propostas Macro B somente para simulador. Não executar na máquina.";

    public static string Dispatch(string command, string argsJson)
    {
        try
        {
            JsonObject args;
            try
            {
                args = JsonNode.Parse(argsJson) as JsonObject ?? [];
            }
            catch (JsonException)
            {
                args = [];
            }

            string text = args["text"] is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "";
            string profile = args["profile"] is JsonValue p && p.TryGetValue(out string? ps) ? ps ?? "" : "";
            return command switch
            {
                "analyze" => Analyze(text, profile),
                "anonymize" => Anonymize(text),
                _ => Error("Comando desconhecido: " + command),
            };
        }
        catch (Exception e)
        {
            return Error(e.GetType().Name + ": " + e.Message);
        }
    }

    private static string Error(string message) =>
        new JsonObject { ["ok"] = false, ["error"] = message }.ToJsonString();

    private static string[]? SplitLines(string text, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "O programa está vazio.";
            return null;
        }

        if (text.Length > MaxChars)
        {
            error = "Programa grande demais (limite de 4 milhões de caracteres).";
            return null;
        }

        string[] lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Length > MaxLines)
        {
            error = "Limite de 50 mil linhas.";
            return null;
        }

        return lines;
    }

    private static JsonArray Strings(IEnumerable<string> items)
    {
        var array = new JsonArray();
        foreach (string item in items)
        {
            array.Add(JsonValue.Create(item));
        }

        return array;
    }

    public static string Anonymize(string text)
    {
        string[]? lines = SplitLines(text, out string? error);
        if (lines is null)
        {
            return Error(error!);
        }

        return new JsonObject
        {
            ["ok"] = true,
            ["text"] = string.Join("\n", NcAnonymizer.Anonymize(lines)),
        }.ToJsonString();
    }

    public static string Analyze(string text, string profileName)
    {
        string[]? lines = SplitLines(text, out string? error);
        if (lines is null)
        {
            return Error(error!);
        }

        NcProfile profile = profileName == "convencional" ? NcProfile.Fanuc0iMfConventional : NcProfile.Fanuc0iMfCalculator;
        ConversionResult r = ConversionPipeline.Run(lines, profile);

        var operations = new JsonArray();
        foreach (Operation o in r.Operations)
        {
            operations.Add(new JsonObject
            {
                ["number"] = o.Number,
                ["startLine"] = o.StartLine,
                ["endLine"] = o.EndLine,
                ["label"] = o.Label,
                ["tool"] = o.Tool,
                ["source"] = o.Source,
                ["h"] = o.H,
                ["wcs"] = o.Wcs,
                ["originalLines"] = o.OriginalLines,
                ["convertedLines"] = o.ConvertedLines,
            });
        }

        var proposals = new JsonArray();
        foreach (Proposal p in r.Proposals)
        {
            proposals.Add(new JsonObject
            {
                ["number"] = p.Number,
                ["operation"] = p.Operation,
                ["kind"] = p.Kind,
                ["startLine"] = p.StartLine,
                ["endLine"] = p.EndLine,
                ["period"] = p.Period,
                ["count"] = p.Count,
                ["savedLines"] = p.SavedLines,
                ["accepted"] = p.Accepted,
                ["blockers"] = Strings(p.Blockers),
                ["warnings"] = Strings(p.Warnings),
                ["fragment"] = p.Fragment is null ? null : JsonValue.Create(p.Fragment),
                ["expandedTotal"] = p.Expanded.Count,
                ["expanded"] = Strings(p.Expanded.Take(ExpandedPreview)),
            });
        }

        var unmodeled = new JsonArray();
        foreach (UnmodeledBlock u in r.Unmodeled)
        {
            unmodeled.Add(new JsonObject { ["line"] = u.Line, ["text"] = u.Text, ["note"] = u.Note });
        }

        return new JsonObject
        {
            ["ok"] = true,
            ["profile"] = r.Profile,
            ["lineCount"] = lines.Length,
            ["cam"] = r.Cam,
            ["codeBlocks"] = r.CodeBlocks,
            ["coverage"] = r.Coverage,
            ["operations"] = operations,
            ["proposals"] = proposals,
            ["unmodeled"] = unmodeled,
            ["originalLines"] = r.OriginalLines,
            ["convertedLines"] = r.ConvertedLines,
            ["savedLines"] = r.SavedLines,
            ["accepted"] = r.Proposals.Count(p => p.Accepted),
            ["blocked"] = r.Proposals.Count(p => !p.Accepted),
            ["document"] = r.Document,
            ["simulationProgram"] = r.SimulationProgram is null ? null : JsonValue.Create(r.SimulationProgram),
            ["simulationBlockers"] = Strings(r.SimulationBlockers),
            ["machineReady"] = false,
            ["warning"] = Warning,
        }.ToJsonString();
    }
}
