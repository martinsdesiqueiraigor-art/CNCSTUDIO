using System.Reflection;
using System.Text.Json.Nodes;

namespace CncStudio.Core.Academy;

/// <summary>Módulo previsto no plano (existe mesmo sem conteúdo; aparece como "em breve").</summary>
public sealed record PlannedModule(string Id, string Title, string Track, string Level, string Description = "", bool Soon = false);

/// <summary>Plano da Academy: trilhas, níveis e módulos, lido de academy-plano.json (módulo novo entra só no JSON).</summary>
public sealed record AcademyPlan(IReadOnlyList<string> Tracks, IReadOnlyList<string> Levels, IReadOnlyList<PlannedModule> Modules)
{
    /// <summary>Lê o plano; devolve null e o motivo se o JSON estiver quebrado ou incompleto.</summary>
    public static AcademyPlan? FromJson(string json, out string error)
    {
        error = "";
        JsonObject? o;
        try
        {
            o = JsonNode.Parse(json) as JsonObject;
        }
        catch (System.Text.Json.JsonException e)
        {
            error = "JSON inválido: " + e.Message;
            return null;
        }

        List<string> Strings(string key) => o?[key] is JsonArray a ? [.. a.Select(n => n is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "").Where(s => s.Length > 0)] : [];
        string Str(JsonObject m, string key) => m[key] is JsonValue v && v.TryGetValue(out string? s) ? s ?? "" : "";
        List<string> tracks = Strings("trilhas"), levels = Strings("niveis");
        List<PlannedModule> modules = [];
        if (o?["modulos"] is JsonArray list)
        {
            foreach (JsonObject m in list.OfType<JsonObject>())
            {
                modules.Add(new PlannedModule(Str(m, "id"), Str(m, "titulo"), Str(m, "trilha"), Str(m, "nivel"), Str(m, "descricao"),
                    m["em_breve"] is JsonValue sv && sv.TryGetValue(out bool soon) && soon));
            }
        }

        if (tracks.Count == 0 || levels.Count == 0)
        {
            error = "O plano precisa de \"trilhas\" e \"niveis\".";
            return null;
        }

        if (modules.FirstOrDefault(m => m.Id.Length == 0 || m.Title.Length == 0 || !tracks.Contains(m.Track) || !levels.Contains(m.Level)) is { } bad)
        {
            error = $"Módulo \"{bad.Id}\": precisa de id, titulo, uma trilha e um nível do plano.";
            return null;
        }

        if (modules.GroupBy(m => m.Id).FirstOrDefault(g => g.Count() > 1) is { } dup)
        {
            error = $"Módulo \"{dup.Key}\" repetido.";
            return null;
        }

        return new AcademyPlan(tracks, levels, modules);
    }

    /// <summary>O plano embutido no Core (academy-plano.json).</summary>
    public static AcademyPlan Embedded()
    {
        using Stream s = typeof(AcademyPlan).Assembly.GetManifestResourceStream("AcademyPlan.json")
            ?? throw new InvalidOperationException("academy-plano.json não embutido no Core.");
        using StreamReader reader = new(s);
        return FromJson(reader.ReadToEnd(), out string error) ?? throw new InvalidOperationException("academy-plano.json: " + error);
    }
}

/// <summary>Plano do piloto, o conteúdo carregado e o acesso às aulas.</summary>
public sealed class AcademyLibrary
{
    private static readonly AcademyPlan EmbeddedPlan = AcademyPlan.Embedded();

    public static IReadOnlyList<string> Tracks => EmbeddedPlan.Tracks;

    public static IReadOnlyList<string> Levels => EmbeddedPlan.Levels;

    public static IReadOnlyList<PlannedModule> Plan => EmbeddedPlan.Modules;

    private readonly Dictionary<string, AcademyModule> _modules = [];
    private readonly Dictionary<string, string> _programs = [];

    /// <summary>Problemas achados ao carregar (arquivo quebrado, módulo desconhecido).</summary>
    public List<ContentIssue> LoadIssues { get; } = [];

    public IReadOnlyDictionary<string, string> Programs => _programs;

    public AcademyModule? Module(string id) => _modules.GetValueOrDefault(id);

    public Lesson? FindLesson(string lessonId) =>
        _modules.Values.SelectMany(m => m.Lessons).FirstOrDefault(l => l.Id == lessonId);

    /// <summary>Monta a biblioteca a partir de arquivos (nome → texto): aulas e testes .json e programas .nc.</summary>
    public static AcademyLibrary FromFiles(IEnumerable<(string Name, string Text)> files)
    {
        AcademyLibrary lib = new();
        Dictionary<string, List<Lesson>> lessons = [];
        Dictionary<string, FinalTest> finals = [];

        foreach ((string name, string text) in files.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            if (name.EndsWith(".nc", StringComparison.OrdinalIgnoreCase))
            {
                lib._programs["samples/academy/" + Path.GetFileName(name)] = text;
            }
            else if (name.EndsWith("-teste-final.json", StringComparison.OrdinalIgnoreCase))
            {
                FinalTest? f = AcademyReader.ReadFinal(text, out string err);
                if (f is null)
                {
                    lib.LoadIssues.Add(new ContentIssue(name, err));
                }
                else
                {
                    finals[f.ModuleId] = f;
                }
            }
            else if (name.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                Lesson? l = AcademyReader.ReadLesson(text, out string err);
                if (l is null)
                {
                    lib.LoadIssues.Add(new ContentIssue(name, err));
                }
                else
                {
                    if (!lessons.TryGetValue(l.ModuleId, out List<Lesson>? list))
                    {
                        lessons[l.ModuleId] = list = [];
                    }

                    list.Add(l);
                }
            }
        }

        foreach (string moduleId in lessons.Keys.Union(finals.Keys))
        {
            PlannedModule? plan = Plan.FirstOrDefault(p => p.Id == moduleId);
            if (plan is null)
            {
                lib.LoadIssues.Add(new ContentIssue(moduleId, "Módulo fora do plano do piloto."));
                continue;
            }

            lib._modules[moduleId] = new AcademyModule(
                moduleId, plan.Title, plan.Track, plan.Level,
                [.. lessons.GetValueOrDefault(moduleId, []).OrderBy(l => l.Id, StringComparer.Ordinal)],
                finals.GetValueOrDefault(moduleId));
        }

        return lib;
    }

    /// <summary>Conteúdo embutido no Core (content/academy e samples/academy).</summary>
    public static AcademyLibrary FromEmbedded()
    {
        Assembly asm = typeof(AcademyLibrary).Assembly;
        List<(string, string)> files = [];
        foreach (string res in asm.GetManifestResourceNames().Where(n => n.StartsWith("Academy.", StringComparison.Ordinal) || n.StartsWith("AcademyProgram.", StringComparison.Ordinal)))
        {
            using Stream? s = asm.GetManifestResourceStream(res);
            if (s is null)
            {
                continue;
            }

            using StreamReader reader = new(s);
            string name = res[(res.IndexOf('.') + 1)..];
            files.Add((name, reader.ReadToEnd()));
        }

        return FromFiles(files);
    }

    /// <summary>Confere todo o conteúdo carregado contra as regras da ficha.</summary>
    public List<ContentIssue> Validate()
    {
        List<ContentIssue> all = [.. LoadIssues];
        foreach (AcademyModule m in _modules.Values.OrderBy(m => m.Id, StringComparer.Ordinal))
        {
            all.AddRange(AcademyValidator.Validate(m.Lessons, m.Final, _programs).Select(i => i with { Where = m.Id + " / " + i.Where }));
        }

        return all;
    }
}
