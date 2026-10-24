using System.Reflection;

namespace CncStudio.Core.Academy;

/// <summary>Módulo previsto no piloto (existe mesmo sem conteúdo; aparece como "em breve").</summary>
public sealed record PlannedModule(string Id, string Title, string Track, string Level);

/// <summary>Plano do piloto (ficha ACAD-01), o conteúdo carregado e o acesso às aulas.</summary>
public sealed class AcademyLibrary
{
    public static readonly IReadOnlyList<string> Tracks = ["Comum", "Fresa", "Torno"];
    public static readonly IReadOnlyList<string> Levels = ["Introdução", "Iniciante", "Básico", "Intermediário", "Avançado", "Especialista"];

    public static readonly IReadOnlyList<PlannedModule> Plan =
    [
        new("acad-m1", "Introdução ao CNC", "Comum", "Introdução"),
        new("acad-m2", "Fresa: primeiros movimentos", "Fresa", "Iniciante"),
        new("acad-m3", "Fresa: ciclos, subprogramas e compensação", "Fresa", "Básico"),
        new("acad-m4", "Fresa: macro", "Fresa", "Avançado"),
        new("acad-m5", "Fresa: apalpador e ciclos próprios", "Fresa", "Especialista"),
        new("acad-m6", "Torno: primeiros movimentos", "Torno", "Iniciante"),
        new("acad-m7", "Torno: G71", "Torno", "Intermediário"),
    ];

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
