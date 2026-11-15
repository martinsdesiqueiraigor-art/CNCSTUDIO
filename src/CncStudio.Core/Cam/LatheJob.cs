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

    /// <summary>
    /// Campos (nome como na tela) que o usuário deixou vazios ou com texto (UX-04e, a regra do CAM-12 da fresa): a operação fica
    /// Incompleta e não gera (<see cref="LatheJobInfo.MissingError"/>), e o número antigo guardado não vale. Não vai para o arquivo;
    /// Salvar fica bloqueado enquanto houver (<see cref="LatheJobInfo.SaveBlockers"/>).
    /// </summary>
    public IReadOnlyList<string> MissingFields { get; init; } = [];

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
/// Bruto do Trabalho de torno (TORNO-T1): barra com Ø, comprimento e o Z da face do bruto (no referencial do perfil), todos
/// digitados e opcionais (null = não informado; nada suposto). Não entra no G-code: serve às operações novas e ao pacote do Code.
/// </summary>
public sealed record LatheJobStock(double? Diameter = null, double? Length = null, double? FaceZ = null)
{
    /// <summary>Nada informado (não vai para o arquivo).</summary>
    public bool IsEmpty => Diameter is null && Length is null && FaceZ is null;

    /// <summary>Motivo de um valor inválido (null = vale): Ø e comprimento informados precisam ser maiores que zero.</summary>
    public string? Validate()
    {
        static bool Bad(double? v) => v is { } x && !(double.IsFinite(x) && x > 0);
        if (Bad(Diameter))
        {
            return "O Ø do bruto precisa ser maior que zero (ou deixe em branco).";
        }

        if (Bad(Length))
        {
            return "O comprimento do bruto precisa ser maior que zero (ou deixe em branco).";
        }

        return FaceZ is { } z && !double.IsFinite(z) ? "O Z da face do bruto precisa ser um número (ou deixe em branco)." : null;
    }
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

    /// <summary>
    /// Número do programa (null = vazio: trava Gerar e Enviar no Fanuc, <see cref="LatheJobProgram"/>). O valor de fábrica só existe
    /// para o código antigo e os arquivos antigos; a tela cria o trabalho com <see cref="Blank"/> (TORNO-T1: nada suposto).
    /// </summary>
    public int? ProgramNumber { get; set; } = 1002;

    /// <summary>Zero de peça G5x (null = vazio: trava Gerar e Enviar).</summary>
    public int? WorkOffset { get; set; } = 54;

    /// <summary>Refrigeração ligada ou desligada (null = não escolhida: trava Gerar e Enviar).</summary>
    public bool? Coolant { get; set; } = true;

    /// <summary>Bruto do trabalho (TORNO-T1), digitado; null = não informado. As operações novas nascem com o Ø dele.</summary>
    public LatheJobStock? Stock { get; set; }

    /// <summary>Trabalho novo da tela: Programa nº, G5x e refrigeração vazios (nada suposto).</summary>
    public static LatheJob Blank(string name = "TORNO") => new() { Name = name, ProgramNumber = null, WorkOffset = null, Coolant = null };

    /// <summary>Perfil da peça (null = ainda não definido).</summary>
    public LatheProfile? Profile { get; set; }

    /// <summary>Fixação da peça (null = não informada). Só informativa: não entra no programa.</summary>
    public LatheFixation? Fixation { get; set; }

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
        // Programa nº, G5x e refrigeração vazios travam o programa (TORNO-T1); as operações seguem calculadas para a árvore
        List<string> errors = [.. LatheJobProgram.Problems(job)];
        errors.AddRange(items.Where(x => !x.Ok).Select(x => $"Operação {x.Index + 1}: {x.Error}"));
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
        string program = LathePost.Generate(ops, new LathePostOptions(job.Dialect, job.Name, job.ProgramNumber ?? 0, job.WorkOffset!.Value, job.Coolant!.Value));
        return new LatheBuild(program, ops, items, errors, warnings);
    }

    private static LatheResult BuildOne(LatheJob job, LatheOperationSpec s)
    {
        if (LatheJobInfo.MissingError(s) is { } missing)
        {
            return new LatheResult(null, missing);
        }

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
    /// <summary>
    /// O que impede de gerar o G-code do torno (vazio = pronto). Não inclui fixação nem colisão, que o app não verifica.
    /// <paramref name="maxRpm"/>: limite do fuso do perfil da máquina; sem ele, a rotação não vira pendência (como na fresa).
    /// </summary>
    public static IReadOnlyList<string> Pending(LatheJob job, LatheBuild build, double? maxRpm = null)
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

        list.AddRange(LatheJobProgram.Problems(job));
        for (int i = 0; i < build.Items.Count; i++)
        {
            LatheOpResult r = build.Items[i];
            if (!r.Ok)
            {
                list.Add($"Operação {i + 1}: " + (r.Error ?? "não foi possível gerar"));
            }
        }

        foreach (int i in OverSpindle(job, maxRpm))
        {
            list.Add($"rotação acima do fuso na operação {i + 1}");
        }

        return list;
    }

    /// <summary>
    /// Linha "Origem" da árvore do torno: o deslocamento de trabalho, X0 no centro da peça e Z0 o do perfil.
    /// O estado é o mesmo da fresa (<see cref="CamJobInfo.OriginState"/>): nunca conferido pelo app.
    /// </summary>
    public static string OriginTitle(LatheJob job) => $"Origem {WorkOffsetName(job.WorkOffset)}: X0 no centro da peça, Z0 do perfil";

    /// <summary>"G54", ou "G5x (falta)" quando vazio (como na fresa).</summary>
    public static string WorkOffsetName(int? workOffset) => workOffset is { } g ? "G" + g.ToString(CultureInfo.InvariantCulture) : "G5x (falta)";

    /// <summary>Maior rotação que a operação pode pedir: a fixa ou, em G96, o limite G50 (<see cref="LatheTool.MaxRpm"/>).</summary>
    public static double SpindleRpm(LatheTool t) => t.Rpm > 0 ? t.Rpm : t.MaxRpm;

    /// <summary>
    /// Aviso de rotação acima do limite do fuso do perfil da máquina (null = dentro do limite ou limite não informado), mesma conferência
    /// de <see cref="CamJobInfo.SpindleWarning"/>. O avanço do torno é por rotação, então não muda. Só confere: não altera o programa.
    /// </summary>
    public static string? SpindleWarning(LatheTool t, double? maxRpm)
    {
        if (CamJobInfo.SpindleWarning(SpindleRpm(t), maxRpm) is null)
        {
            return null;
        }

        string what = t.Rpm > 0 ? "Rotação fixa" : "Rotação máxima (G50)";
        return $"{what} acima do limite do fuso ({N(maxRpm!.Value)} rpm). Digite até {N(maxRpm.Value)}.";
    }

    /// <summary>Operações ligadas com rotação acima do limite do fuso (índices).</summary>
    public static IReadOnlyList<int> OverSpindle(LatheJob job, double? maxRpm) =>
        job.Operations.Select((o, i) => (o, i)).Where(x => x.o.Enabled && SpindleWarning(x.o.Tool, maxRpm) is not null).Select(x => x.i).ToList();

    /// <summary>Estado de uma operação na árvore (o mesmo texto da fresa): "OK", "desligada", "erro: motivo" ou "aviso: acima do fuso".</summary>
    public static (string Kind, string Text) OpState(LatheOpResult? r, LatheOperationSpec? spec = null, double? maxRpm = null)
    {
        if (r is null)
        {
            return ("pending", "a calcular");
        }

        if (r.Skipped)
        {
            return ("off", "desligada");
        }

        if (!r.Ok)
        {
            return ("bad", "erro: " + (r.Error ?? "não foi possível gerar"));
        }

        return spec is not null && SpindleWarning(spec.Tool, maxRpm) is not null ? ("pending", "aviso: acima do fuso") : ("ok", "OK");
    }

    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    private static string N(double v) => v.ToString("0.##", Br);

    // ---------------------------------------------------------------- campos vazios (UX-04e, regra do CAM-12)

    /// <summary>Motivo da operação Incompleta por campo vazio ("Falta Passo", "Falta Passo e Z do fim"); null = nenhum.</summary>
    public static string? MissingError(LatheOperationSpec s)
    {
        IReadOnlyList<string> m = s.MissingFields;
        if (m.Count == 0)
        {
            return null;
        }

        return m.Count == 1 ? "Falta " + m[0] : "Falta " + string.Join(", ", m.Take(m.Count - 1)) + " e " + m[^1];
    }

    /// <summary>Marca (ou desmarca) um campo como vazio na operação. Não mexe no valor guardado.</summary>
    public static LatheOperationSpec SetMissing(LatheOperationSpec s, string field, bool missing)
    {
        bool has = s.MissingFields.Contains(field);
        if (has == missing)
        {
            return s;
        }

        return s with { MissingFields = missing ? [.. s.MissingFields, field] : s.MissingFields.Where(f => f != field).ToList() };
    }

    /// <summary>
    /// O que impede salvar: uma linha por operação com campo vazio ("Operação 2 (Rosca externa): falta Passo"), inclusive as
    /// desligadas, porque o arquivo guardaria o número antigo que o usuário não vê. Vazio = pode salvar.
    /// </summary>
    public static IReadOnlyList<string> SaveBlockers(LatheJob job)
    {
        var list = new List<string>();
        for (int i = 0; i < job.Operations.Count; i++)
        {
            LatheOperationSpec s = job.Operations[i];
            if (MissingError(s) is { } why)
            {
                list.Add($"Operação {i + 1} ({KindName(s)}): {char.ToLowerInvariant(why[0])}{why[1..]}");
            }
        }

        return list;
    }

    /// <summary>
    /// Linha do item Bruto (barra) da árvore do torno: o torno não tem bruto próprio; o Ø do bruto é o digitado em cada operação de
    /// torneamento externo e faceamento. Só leitura; nada é suposto ("Bruto: Ø 40 (op. 1, 2)", "Bruto: nas operações (nenhuma)").
    /// </summary>
    public static string StockTitle(LatheJob job)
    {
        var groups = StockDiameters(job);
        if (groups.Count == 0)
        {
            return "Bruto (barra): sem operação com Ø do bruto";
        }

        return "Bruto (barra): " + string.Join(" · ", groups.Select(g => $"Ø {g.Text} (op. {string.Join(", ", g.Ops.Select(o => o + 1))})"));
    }

    /// <summary>
    /// Ø do bruto digitados nas operações de torneamento externo e faceamento, agrupados ("?" quando o campo está vazio), na ordem
    /// em que aparecem. Interno não entra (lá o campo é o Ø do pré-furo).
    /// </summary>
    public static IReadOnlyList<(string Text, IReadOnlyList<int> Ops)> StockDiameters(LatheJob job)
    {
        var list = new List<(string Text, List<int> Ops)>();
        for (int i = 0; i < job.Operations.Count; i++)
        {
            LatheOperationSpec s = job.Operations[i];
            double? d = s.Kind switch
            {
                LatheOpKind.Turning when !s.IsInternal => s.Turning?.StockDiameter,
                LatheOpKind.Facing => s.Facing?.StockDiameter,
                _ => null,
            };
            if (d is null)
            {
                continue;
            }

            string text = s.MissingFields.Contains(StockField) ? "?" : N(d.Value);
            int at = list.FindIndex(x => x.Text == text);
            if (at < 0)
            {
                list.Add((text, [i]));
            }
            else
            {
                list[at].Ops.Add(i);
            }
        }

        return list.Select(x => (x.Text, (IReadOnlyList<int>)x.Ops)).ToList();
    }

    /// <summary>
    /// Linhas do nó Ferramentas ("T1 · G96 200 m/min · máx. 3000 rpm"); T, velocidade ou rotação vazios aparecem como "?" (nunca o
    /// número antigo). A mesma ferramenta aparece uma vez, em ordem de T.
    /// </summary>
    public static IReadOnlyList<string> ToolLines(LatheJob job)
    {
        string Q(LatheOperationSpec o, string field, string value) => o.MissingFields.Contains(field) ? "?" : value;
        return job.Operations.Select(o =>
            {
                LatheTool t = o.Tool;
                bool noT = o.MissingFields.Contains(ToolNumberField);
                string speed = o.MissingFields.Contains(FixedRpmField) ? "? rpm"
                    : t.Rpm > 0 ? $"{N(t.Rpm)} rpm" : $"G96 {Q(o, SurfaceSpeedField, N(t.SurfaceSpeed))} m/min";
                return (Order: noT ? int.MaxValue : t.Number, Key: noT ? "?" : t.Number.ToString(CultureInfo.InvariantCulture),
                    Text: $"T{(noT ? "?" : t.Number.ToString(CultureInfo.InvariantCulture))} · {speed} · máx. {Q(o, MaxRpmField, N(t.MaxRpm))} rpm");
            })
            .GroupBy(x => x.Key).Select(g => g.First()).OrderBy(x => x.Order).Select(x => x.Text).ToList();
    }

    /// <summary>Nomes dos campos da ferramenta na tela (os mesmos rótulos do painel).</summary>
    public const string ToolNumberField = "Ferramenta (T)";
    public const string SurfaceSpeedField = "Velocidade de corte (G96)";
    public const string MaxRpmField = "Rotação máxima";
    public const string FixedRpmField = "Rotação fixa (0 = G96)";

    /// <summary>Nome do campo do Ø do bruto na tela (torneamento externo e faceamento).</summary>
    public const string StockField = "Ø do bruto";

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

    // ---------------------------------------------------------------- operação nova vazia e bruto do trabalho (TORNO-T1)

    public const string SafeZField = "Z seguro";
    public const string SafeDiameterField = "Ø seguro (troca)";
    public const string ClearanceField = "Folga de aproximação";
    public const string StockFaceZField = "Z da face do bruto";

    /// <summary>
    /// Campos numéricos da operação, com o nome da tela (os rótulos do painel), na ordem do painel. A rotação fixa não entra: 0 é a
    /// escolha "G96", não um número de corte.
    /// </summary>
    public static IReadOnlyList<string> NumericFields(LatheOperationSpec s)
    {
        var list = new List<string> { ToolNumberField, SurfaceSpeedField, MaxRpmField, SafeZField, SafeDiameterField, ClearanceField };
        list.AddRange(KindFields(s));
        return list;
    }

    private static string[] KindFields(LatheOperationSpec s) => s.Kind switch
    {
        LatheOpKind.Turning =>
        [
            s.IsInternal ? "Ø do pré-furo" : StockField, "Profundidade de corte (raio)", "Recuo", "Sobremetal radial", "Sobremetal em Z",
            "Avanço do desbaste", "Avanço do acabamento",
        ],
        LatheOpKind.Facing => [StockField, "Ø interno (0 = até o centro)", StockFaceZField, "Z da face pronta", "Passe em Z", "Avanço"],
        LatheOpKind.Drilling => ["Z da face", "Z do fundo", "Bicada", "Avanço"],
        LatheOpKind.Threading =>
        [
            s.IsInternal ? "Ø nominal (raiz)" : "Ø nominal (crista)", "Passo", "Z do início", "Z do fim", "Rotação (fixa)",
            "Altura do filete (0 = ISO)", "Passes de acabamento", "Sobremetal de acabamento",
        ],
        _ =>
        [
            s.IsInternal ? "Ø do furo" : "Ø da superfície", "Ø do fundo do canal", "Z da parede de cima", "Largura do canal",
            "Largura da ferramenta", "Mergulho por passe", "Sobremetal de acabamento (0 = sem)", "Avanço",
        ],
    };

    /// <summary>
    /// Operação nova da tela (TORNO-T1): nasce vazia, como na fresa. Todo campo numérico fica marcado como vazio (valor guardado 0,
    /// nunca usado) e a operação fica Incompleta até ser digitado. Com <paramref name="tool"/> (a da operação anterior), T, velocidade
    /// e rotação vêm dela. Com bruto no trabalho, o Ø do bruto (torneamento externo e faceamento) e o Z da face do bruto (faceamento)
    /// vêm dele.
    /// </summary>
    public static LatheOperationSpec NewBlank(LatheOpKind kind, bool isInternal, LatheTool? tool = null, LatheJobStock? stock = null)
    {
        var spec = new LatheOperationSpec(kind, tool ?? new LatheTool(0, "", 0, 0, 0)) { SafeZ = 0, SafeDiameter = 0, Clearance = 0 };
        spec = kind switch
        {
            LatheOpKind.Turning => spec with { Turning = new TurningParams(0, isInternal, true, true, 0, 0, 0, 0, 0, 0, 0) },
            LatheOpKind.Facing => spec with { Facing = new FacingSpec(0, 0, 0, 0, 0, 0) },
            LatheOpKind.Drilling => spec with { Drill = new LatheDrillSpec(0, 0, 0, 0) },
            LatheOpKind.Threading => spec with { Thread = new ThreadingParams(isInternal, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0) },
            _ => spec with { Groove = new GroovingParams(isInternal, 0, 0, 0, 0, 0, 0, 0, 0.5, 0, false, 0) },
        };
        var missing = NumericFields(spec).ToList();
        if (tool is not null)
        {
            missing.RemoveAll(f => f is ToolNumberField or SurfaceSpeedField or MaxRpmField);
        }

        if (stock?.Diameter is { } d && (kind == LatheOpKind.Facing || (kind == LatheOpKind.Turning && !isInternal)))
        {
            missing.Remove(StockField);
            spec = kind == LatheOpKind.Facing ? spec with { Facing = spec.Facing! with { StockDiameter = d } } : spec with { Turning = spec.Turning! with { StockDiameter = d } };
        }

        if (stock?.FaceZ is { } z && kind == LatheOpKind.Facing)
        {
            missing.Remove(StockFaceZField);
            spec = spec with { Facing = spec.Facing! with { StockFaceZ = z } };
        }

        return spec with { MissingFields = missing };
    }

    private static string Q(double? v) => v is { } x ? N(x) : "?";

    /// <summary>Linha do bruto do trabalho: "Bruto do trabalho: Ø 50 × 120 mm · face em Z 2" ("?" no que falta); null = sem bruto.</summary>
    public static string? JobStockTitle(LatheJobStock? stock) => stock is null || stock.IsEmpty
        ? null
        : $"Bruto do trabalho: Ø {Q(stock.Diameter)} × {Q(stock.Length)} mm · face em Z {Q(stock.FaceZ)}";
}
