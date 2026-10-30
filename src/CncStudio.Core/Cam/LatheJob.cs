using System.Globalization;

namespace CncStudio.Core.Cam;

/// <summary>Tipo da operação de torno guardada no Trabalho.</summary>
public enum LatheOpKind { Turning, Facing, Drilling, Threading, Grooving }

/// <summary>Faceamento: Ø do bruto, Ø interno (0 = até o centro), Z da face do bruto e da face pronta, passe em Z e avanço (mm/rot).</summary>
public sealed record FacingSpec(double StockDiameter, double InnerDiameter, double StockFaceZ, double FinalFaceZ, double DepthOfCut, double Feed);

/// <summary>Furação axial: Z da face, Z do fundo, bicada e avanço (mm/rot).</summary>
public sealed record LatheDrillSpec(double TopZ, double BottomZ, double Peck, double Feed);

/// <summary>
/// Operação do Trabalho de torno: só os <b>parâmetros</b>. O percurso e o programa são gerados de novo por <see cref="LatheJobBuilder"/>.
/// Só o campo do <see cref="Kind"/> escolhido é usado. <see cref="SafeDiameter"/>: Ø seguro para a troca de ferramenta.
/// </summary>
public sealed record LatheOperationSpec(LatheOpKind Kind, LatheTool Tool)
{
    public bool Enabled { get; init; } = true;

    public string Label { get; init; } = "";

    public double SafeZ { get; init; } = 50;

    public double SafeDiameter { get; init; } = 80;

    /// <summary>Folga de aproximação (mm).</summary>
    public double Clearance { get; init; } = 2;

    public TurningParams? Turning { get; init; }

    public FacingSpec? Facing { get; init; }

    public LatheDrillSpec? Drill { get; init; }

    public ThreadingParams? Thread { get; init; }

    public GroovingParams? Groove { get; init; }

    /// <summary>Interno (barra, rosca ou canal interno)?</summary>
    public bool IsInternal => Kind switch
    {
        LatheOpKind.Turning => Turning?.Internal == true,
        LatheOpKind.Threading => Thread?.Internal == true,
        LatheOpKind.Grooving => Groove?.Internal == true,
        _ => false,
    };
}

/// <summary>
/// Trabalho (Job) de torno do CAM: perfil da peça (Z × raio), comando, números do programa e as operações em ordem.
/// É o que se salva e se abre; o programa nasce dele (<see cref="LatheJobBuilder"/>).
/// </summary>
public sealed class LatheJob
{
    public const int CurrentVersion = 1;

    public string Name { get; set; } = "TORNO";

    public CamDialect Dialect { get; set; } = CamDialect.Fanuc;

    public int ProgramNumber { get; set; } = 1002;

    public int WorkOffset { get; set; } = 54;

    public bool Coolant { get; set; } = true;

    /// <summary>Perfil da peça (null = ainda não definido).</summary>
    public LatheProfile? Profile { get; set; }

    public List<LatheOperationSpec> Operations { get; } = [];

    public bool MoveOperation(int from, int to)
    {
        if (from < 0 || from >= Operations.Count || to < 0 || to >= Operations.Count)
        {
            return false;
        }

        LatheOperationSpec spec = Operations[from];
        Operations.RemoveAt(from);
        Operations.Insert(to, spec);
        return true;
    }

    /// <summary>Propaga o nome da ferramenta para todas as operações com o mesmo T (condições de corte ficam em cada uma). Devolve quantas mudaram.</summary>
    public int ApplyToolGeometry(int number, string name)
    {
        int changed = 0;
        for (int i = 0; i < Operations.Count; i++)
        {
            LatheTool t = Operations[i].Tool;
            if (t.Number == number && t.Name != name)
            {
                Operations[i] = Operations[i] with { Tool = t with { Name = name } };
                changed++;
            }
        }

        return changed;
    }

    public int DuplicateOperation(int index)
    {
        if (index < 0 || index >= Operations.Count)
        {
            return -1;
        }

        Operations.Insert(index + 1, Operations[index]);
        return index + 1;
    }

    /// <summary>Confere o que não depende dos números do corte. Lista vazia = estrutura boa.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        for (int i = 0; i < Operations.Count; i++)
        {
            LatheOperationSpec s = Operations[i];
            string at = $"Operação {i + 1}: ";
            bool missing = s.Kind switch
            {
                LatheOpKind.Turning => s.Turning is null,
                LatheOpKind.Facing => s.Facing is null,
                LatheOpKind.Drilling => s.Drill is null,
                LatheOpKind.Threading => s.Thread is null,
                _ => s.Groove is null,
            };
            if (missing)
            {
                errors.Add(at + "faltam os dados da operação.");
            }
            else if (s.Kind == LatheOpKind.Turning && Profile is null)
            {
                errors.Add(at + "informe o perfil da peça primeiro.");
            }
        }

        return errors;
    }
}

/// <summary>Programa gerado do Trabalho de torno.</summary>
public sealed record LatheBuild(string Program, IReadOnlyList<LatheOperation> Operations, IReadOnlyList<LatheOpResult> Items, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool Ok => Errors.Count == 0;
}

/// <summary>Resultado de uma operação do Trabalho de torno. <see cref="Skipped"/>: desligada.</summary>
public sealed record LatheOpResult(int Index, bool Skipped, LatheResult? Result)
{
    public bool Ok => Skipped || Result is { Ok: true };

    public string? Error => Result?.Error;
}

/// <summary>Gera percurso e programa de um <see cref="LatheJob"/> com o mesmo código que a tela do torno já usava.</summary>
public static class LatheJobBuilder
{
    public static IReadOnlyList<LatheOpResult> BuildOperations(LatheJob job)
    {
        var items = new List<LatheOpResult>();
        for (int i = 0; i < job.Operations.Count; i++)
        {
            LatheOperationSpec s = job.Operations[i];
            items.Add(s.Enabled ? new LatheOpResult(i, false, BuildOne(job, s)) : new LatheOpResult(i, true, null));
        }

        return items;
    }

    public static LatheBuild Build(LatheJob job)
    {
        IReadOnlyList<string> structure = job.Validate();
        if (structure.Count > 0)
        {
            return new LatheBuild("", [], [], structure, []);
        }

        IReadOnlyList<LatheOpResult> items = BuildOperations(job);
        var errors = items.Where(x => !x.Ok).Select(x => $"Operação {x.Index + 1}: {x.Error}").ToList();
        List<LatheOperation> ops = items.Where(x => !x.Skipped && x.Ok).Select(x => x.Result!.Operation!).ToList();
        if (errors.Count == 0 && ops.Count == 0)
        {
            errors.Add("Não há operação ligada para gerar.");
        }

        if (errors.Count > 0)
        {
            return new LatheBuild("", ops, items, errors, []);
        }

        List<string> warnings = ops.SelectMany(o => o.Warnings).Distinct().ToList();
        string program = LathePost.Generate(ops, new LathePostOptions(job.Dialect, job.Name, job.ProgramNumber, job.WorkOffset, job.Coolant));
        return new LatheBuild(program, ops, items, errors, warnings);
    }

    private static LatheResult BuildOne(LatheJob job, LatheOperationSpec s)
    {
        if (s.Tool.Validate() is { } toolError)
        {
            return new LatheResult(null, toolError);
        }

        LatheResult result;
        double safeR = s.SafeDiameter / 2;
        switch (s.Kind)
        {
            case LatheOpKind.Turning:
                result = LatheOperations.Turning(job.Profile!, s.Tool, s.SafeZ, safeR, s.Turning! with { Clearance = s.Clearance });
                break;
            case LatheOpKind.Facing:
                FacingSpec f = s.Facing!;
                result = LatheOperations.Facing(s.Tool, s.SafeZ, safeR, f.StockDiameter, f.StockFaceZ, f.FinalFaceZ, f.DepthOfCut, f.Feed, f.InnerDiameter, s.Clearance);
                break;
            case LatheOpKind.Drilling:
                LatheDrillSpec d = s.Drill!;
                result = LatheOperations.Drilling(s.Tool, s.SafeZ, safeR, d.TopZ, d.BottomZ, d.Peck, d.Feed, s.Clearance);
                break;
            case LatheOpKind.Threading:
                result = LatheOperations.Threading(s.Tool, s.SafeZ, safeR, s.Thread! with { Clearance = s.Clearance });
                break;
            default:
                result = LatheOperations.Grooving(s.Tool, s.SafeZ, safeR, s.Groove! with { Clearance = s.Clearance });
                break;
        }

        if (!result.Ok || s.Label.Length == 0)
        {
            return result;
        }

        return new LatheResult(result.Operation! with { Name = s.Label }, null);
    }
}

/// <summary>Textos da árvore do Trabalho de torno e operações novas com valores padrão (os mesmos da tela do torno).</summary>
public static class LatheJobInfo
{
    /// <summary>O que impede de gerar o G-code do torno (vazio = pronto). Não inclui fixação nem colisão, que o app não verifica.</summary>
    public static IReadOnlyList<string> Pending(LatheJob job, LatheBuild build)
    {
        var list = new List<string>();
        if (job.Profile is null)
        {
            list.Add("Falta o perfil da peça (DXF ou pontos)");
        }

        if (job.Operations.Count == 0)
        {
            list.Add("Nenhuma operação");
        }

        for (int i = 0; i < build.Items.Count; i++)
        {
            LatheOpResult r = build.Items[i];
            if (!r.Ok)
            {
                list.Add($"Operação {i + 1}: " + (r.Error ?? "não foi possível gerar"));
            }
        }

        return list;
    }

    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    private static string N(double v) => v.ToString("0.##", Br);

    /// <summary>Tipos que o usuário pode criar na árvore: nome mostrado, tipo e se é interno.</summary>
    public static readonly IReadOnlyList<(string Name, LatheOpKind Kind, bool Internal)> NewKinds =
    [
        ("Torneamento externo", LatheOpKind.Turning, false),
        ("Torneamento interno", LatheOpKind.Turning, true),
        ("Faceamento", LatheOpKind.Facing, false),
        ("Furação axial", LatheOpKind.Drilling, false),
        ("Rosca externa", LatheOpKind.Threading, false),
        ("Rosca interna", LatheOpKind.Threading, true),
        ("Canal externo", LatheOpKind.Grooving, false),
        ("Canal interno", LatheOpKind.Grooving, true),
    ];

    public static string KindName(LatheOperationSpec s) => s.Kind switch
    {
        LatheOpKind.Turning => s.IsInternal ? "Torneamento interno" : "Torneamento externo",
        LatheOpKind.Facing => "Faceamento",
        LatheOpKind.Drilling => "Furação axial",
        LatheOpKind.Threading => s.IsInternal ? "Rosca interna" : "Rosca externa",
        _ => s.IsInternal ? "Canal interno" : "Canal externo",
    };

    /// <summary>Linha da árvore: "2 · Rosca externa — T3".</summary>
    public static string OpTitle(int index, LatheOperationSpec s)
    {
        string name = s.Label.Length > 0 ? s.Label : KindName(s);
        string tool = s.Tool.Name.Length > 0 ? " " + s.Tool.Name : "";
        return $"{index + 1} · {name} — T{s.Tool.Number}{tool}";
    }

    /// <summary>Resumo do perfil para a árvore.</summary>
    public static string ProfileTitle(LatheProfile? p)
    {
        if (p is null)
        {
            return "Perfil: não definido";
        }

        (double min, double max) = p.RadiusRange();
        return $"Perfil: {p.Segments.Count} trecho(s), Z {N(p.Start.X)} a {N(p.End.X)}, Ø {N(min * 2)} a {N(max * 2)}";
    }

    /// <summary>Ferramentas distintas, em ordem de T.</summary>
    public static IReadOnlyList<LatheTool> ToolsUsed(LatheJob job) =>
        job.Operations.Select(o => o.Tool).GroupBy(t => t.Number).Select(g => g.First()).OrderBy(t => t.Number).ToList();

    /// <summary>Operação nova com valores padrão (os da tela atual do torno). Os números se editam depois.</summary>
    public static LatheOperationSpec NewOperation(LatheOpKind kind, bool isInternal, LatheTool? tool = null)
    {
        var t = tool ?? new LatheTool(1, "", 200, 3000, 0);
        var spec = new LatheOperationSpec(kind, t);
        return kind switch
        {
            LatheOpKind.Turning => spec with { Turning = new TurningParams(isInternal ? 14 : 40, isInternal, true, true, 2, 1.5, 0.5, 0.2, 0.1, 0.25, 0.1) },
            LatheOpKind.Facing => spec with { Facing = new FacingSpec(40, 0, 2, 0, 1, 0.15) },
            LatheOpKind.Drilling => spec with { Drill = new LatheDrillSpec(0, -25, 8, 0.12) },
            LatheOpKind.Threading => spec with { Thread = new ThreadingParams(isInternal, isInternal ? 16 : 20, 1.5, 0, -18, 600) },
            _ => spec with { Groove = new GroovingParams(isInternal, isInternal ? 20 : 30, isInternal ? 26 : 22, -20, 8, 3, 2, 0.08, 0.5, 0.1, true, 2) },
        };
    }
}
