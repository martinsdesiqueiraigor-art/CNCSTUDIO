using CncStudio.Core.Generators;

namespace CncStudio.Core.Cam;

/// <summary>Tipo da operação de fresa guardada no Job.</summary>
public enum JobOpKind { Contour, Pocket, Facing, Drilling, WrapText }

/// <summary>
/// Face girada no 4º eixo (mesa): letra do eixo (A ou B), ângulo e distância do centro do eixo até a face virada para cima.
/// 3+2 na mesa dupla: <see cref="Axis"/> é o basculante e <see cref="Axis2"/>/<see cref="Angle2"/> o giratório (null = um eixo só).
/// </summary>
public sealed record RotaryFaceSpec(string Axis, double Angle, double AxisToFace)
{
    /// <summary>3+2: letra do segundo eixo da mesa (o giratório, ex.: C). Null = face de um eixo só (4º eixo).</summary>
    public string? Axis2 { get; init; }

    /// <summary>3+2: ângulo do segundo eixo, em graus.</summary>
    public double Angle2 { get; init; }
}

/// <summary>Furação pelos círculos do desenho: tipo de ciclo, bicada e pausa.</summary>
public sealed record DrillSpec(DrillCycleKind Cycle = DrillCycleKind.Drill, double Peck = 0, double DwellSeconds = 0);

/// <summary>Texto enrolado no cilindro (4º eixo contínuo): texto, altura das letras, eixo (A ou B) e dados do cilindro.</summary>
public sealed record WrapSpec(string Text, double LetterHeight, string Axis, WrapSetup Setup);

/// <summary>Bruto retangular da peça (canto, tamanho; o topo fica em Z 0). Fica guardado no Job; as operações ainda não o usam.</summary>
public sealed record JobStock(double X, double Y, double Width, double Length, double Thickness);

/// <summary>
/// Fixação da peça informada pelo usuário: tipo (<see cref="Kinds"/>) e descrição curta opcional. Só registro: o app não
/// verifica fixação nem colisão, e nada é suposto quando falta.
/// </summary>
public sealed record JobFixture(string Kind, string Description = "")
{
    public const int MaxDescription = 80;

    /// <summary>Tipos aceitos (código guardado no arquivo → nome mostrado).</summary>
    public static readonly IReadOnlyList<(string Code, string Name)> Kinds =
    [
        ("morsa", "Morsa"),
        ("grampos", "Grampos"),
        ("placa", "Placa (castanhas)"),
        ("vacuo", "Vácuo"),
        ("dispositivo", "Dispositivo próprio"),
        ("outro", "Outro"),
    ];

    /// <summary>Nome do tipo, ou null se o código não existe.</summary>
    public static string? NameOf(string code) => Kinds.FirstOrDefault(k => k.Code == code).Name;

    /// <summary>Motivo se a fixação é inválida (tipo desconhecido, descrição longa); null se está boa.</summary>
    public string? Validate()
    {
        if (NameOf(Kind) is null)
        {
            return $"tipo \"{Kind}\" desconhecido (use: {string.Join(", ", Kinds.Select(k => k.Code))})";
        }

        if (Kind == "outro" && string.IsNullOrWhiteSpace(Description))
        {
            return "no tipo \"outro\", descreva a fixação";
        }

        return Description.Length > MaxDescription ? $"descrição com mais de {MaxDescription} letras" : null;
    }
}

/// <summary>
/// Uma operação do Job: os <b>parâmetros</b> (não o percurso). O percurso é gerado de novo, sempre com o mesmo código do CAM,
/// por <see cref="CamJobBuilder"/>. Só os campos do <see cref="Kind"/> escolhido são usados.
/// </summary>
public sealed record CamOperationSpec(JobOpKind Kind, CamTool Tool, CamLevels Levels)
{
    public bool Enabled { get; init; } = true;

    /// <summary>Nome mostrado na árvore e no programa; vazio usa o nome padrão da operação.</summary>
    public string Label { get; init; } = "";

    /// <summary>Contorno do desenho (índice em <see cref="CamJob.Contours"/>); -1 = nenhum. Furação: -1 usa todos os círculos.</summary>
    public int ContourIndex { get; init; } = -1;

    public ContourParams? ContourParams { get; init; }

    public PocketParams? PocketParams { get; init; }

    /// <summary>Bolsão: contornar como ilhas os contornos do desenho que ficam por inteiro dentro dele.</summary>
    public bool Islands { get; init; }

    /// <summary>Faceamento: passo lateral em % do diâmetro.</summary>
    public double FacingStepOverPercent { get; init; } = 60;

    public DrillSpec? Drill { get; init; }

    public WrapSpec? Wrap { get; init; }

    /// <summary>Leva a operação para uma face girada (não vale para o texto no cilindro, que já gira sozinho).</summary>
    public RotaryFaceSpec? Face { get; init; }

    /// <summary>De onde vieram os valores (só o Assist marca); null = sem marca. Campo opcional do arquivo.</summary>
    public OpOrigin? Origin { get; init; }

    /// <summary>
    /// Campos numéricos que o usuário deixou vazios ou com valor inválido (nome como aparece na tela). Enquanto houver um que a
    /// operação usa, ela fica Incompleta e não gera (<see cref="CamJobInfo.MissingError"/>): o número antigo guardado não vale.
    /// Não vai para o arquivo; o Salvar fica bloqueado enquanto houver (<see cref="CamJobInfo.SaveBlockers"/>).
    /// </summary>
    public IReadOnlyList<string> MissingFields { get; init; } = [];

    /// <summary>
    /// Campos com valor digitado mas inválido (hoje só o T: texto, decimal ou menor que 1). Mesmo efeito de <see cref="MissingFields"/>
    /// (Incompleta, não gera, não salva), com motivo próprio (<see cref="CamJobInfo.InvalidMessage"/>). Não vai para o arquivo.
    /// </summary>
    public IReadOnlyList<string> InvalidFields { get; init; } = [];
}

/// <summary>
/// Trabalho (Job) de fresa do CAM: desenho, bruto, comando, números do programa e a lista de operações em ordem.
/// É o que se salva e se abre; o programa nasce dele (<see cref="CamJobBuilder"/>). A máquina (perfil) vem de fora.
/// </summary>
public sealed class CamJob
{
    /// <summary>
    /// Versão mais nova que o app lê. Um arquivo só sai na versão 2 quando usa algo do UX-04f (formato novo do bruto ou altura da
    /// peça) e na versão 3 quando o Programa nº ou o G5x está vazio (UX-04g); sem isso continua na versão 1, com o mesmo texto de antes
    /// (<see cref="CamJobStock.NeedsVersion2"/>, <see cref="CamJobProgram.NeedsVersion3"/>).
    /// </summary>
    public const int CurrentVersion = 3;

    public string Name { get; set; } = "TRABALHO";

    public CamDialect Dialect { get; set; } = CamDialect.Fanuc;

    /// <summary>Número do programa (Fanuc O1 a O9999). Null = vazio: nada suposto, o trabalho fica Incompleto (<see cref="CamJobProgram"/>).</summary>
    public int? ProgramNumber { get; set; }

    /// <summary>Zero de peça (54 = G54). Null = vazio: nada suposto, o trabalho fica Incompleto (<see cref="CamJobProgram"/>).</summary>
    public int? WorkOffset { get; set; }

    public bool Coolant { get; set; } = true;

    /// <summary>Bloco absoluto (canto e medidas, topo em Z0): o bruto de antes. Arquivo antigo abre assim, sem mudança.</summary>
    public JobStock? Stock { get; set; }

    /// <summary>
    /// Formato novo do bruto (UX-04f): bloco por sobra, cilindro ou tubo. Quando existe, vale no lugar de <see cref="Stock"/>
    /// (só um dos dois fica preenchido; <see cref="SetStock"/>). Não entra no G-code.
    /// </summary>
    public JobStockShape? StockShape { get; set; }

    /// <summary>Altura Z da peça pronta, digitada (um DXF não informa altura). Null = vazia: nada suposto. Não entra no G-code.</summary>
    public double? PartHeight { get; set; }

    /// <summary>Troca o bruto: bloco absoluto ou formato novo (o outro fica vazio, para haver uma fonte só).</summary>
    public void SetStock(JobStock? absolute, JobStockShape? shape)
    {
        Stock = shape is null ? absolute : null;
        StockShape = shape;
    }

    /// <summary>Fixação informada pelo usuário; null = "não definida" (arquivos antigos abrem assim). Fonte única: o Assist lê daqui.</summary>
    public JobFixture? Fixture { get; set; }

    public List<Contour> Contours { get; } = [];

    public List<CamOperationSpec> Operations { get; } = [];

    /// <summary>Move uma operação de lugar (posições da lista). Devolve false se algum índice não existe.</summary>
    public bool MoveOperation(int from, int to)
    {
        if (from < 0 || from >= Operations.Count || to < 0 || to >= Operations.Count)
        {
            return false;
        }

        CamOperationSpec spec = Operations[from];
        Operations.RemoveAt(from);
        Operations.Insert(to, spec);
        return true;
    }

    /// <summary>Copia a operação logo abaixo dela. Devolve a posição da cópia ou -1.</summary>
    /// <summary>
    /// Propaga a geometria da ferramenta (diâmetro e nome) para todas as operações com o mesmo T.
    /// Rotação e avanços ficam em cada operação (condição de corte). Devolve quantas operações mudaram.
    /// </summary>
    public int ApplyToolGeometry(int number, double diameter, string name)
    {
        int changed = 0;
        for (int i = 0; i < Operations.Count; i++)
        {
            CamTool t = Operations[i].Tool;
            if (t.Number == number && (t.Diameter != diameter || t.Name != name))
            {
                Operations[i] = Operations[i] with { Tool = t with { Diameter = diameter, Name = name } };
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

    /// <summary>Confere o que não depende da máquina nem dos números do corte. Lista vazia = estrutura boa.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        for (int i = 0; i < Operations.Count; i++)
        {
            CamOperationSpec s = Operations[i];
            string at = $"Operação {i + 1}: ";
            switch (s.Kind)
            {
                case JobOpKind.Contour when s.ContourParams is null:
                    errors.Add(at + "faltam os dados do contorno.");
                    break;
                case JobOpKind.Pocket when s.PocketParams is null:
                    errors.Add(at + "faltam os dados do bolsão.");
                    break;
                case JobOpKind.Drilling when s.Drill is null:
                    errors.Add(at + "faltam os dados da furação.");
                    break;
                case JobOpKind.WrapText when s.Wrap is null:
                    errors.Add(at + "faltam os dados do texto no cilindro.");
                    break;
            }

            bool needsContour = s.Kind is JobOpKind.Contour or JobOpKind.Pocket or JobOpKind.Facing;
            if (needsContour && (s.ContourIndex < 0 || s.ContourIndex >= Contours.Count))
            {
                errors.Add(at + "escolha um contorno do desenho.");
            }

            if (s.Kind == JobOpKind.Drilling && s.ContourIndex >= Contours.Count)
            {
                errors.Add(at + "o contorno escolhido não existe.");
            }

            if (s.Face is { } f && f.Axis is not ("A" or "B"))
            {
                errors.Add(at + "o eixo da face girada deve ser A ou B.");
            }

            if (s.Face is { Axis2: { } a2 } f2 && (a2 is not ("A" or "B" or "C") || a2 == f2.Axis))
            {
                errors.Add(at + "o segundo eixo da face 3+2 deve ser A, B ou C e diferente do primeiro.");
            }

            if (s.Wrap is { } w && w.Axis is not ("A" or "B"))
            {
                errors.Add(at + "o eixo do texto no cilindro deve ser A ou B.");
            }
        }

        return errors;
    }
}

/// <summary>Resultado de gerar uma operação do Job. <see cref="Skipped"/>: operação desligada (não entra no programa).</summary>
public sealed record JobOpResult(int Index, bool Skipped, OperationResult? Result)
{
    public bool Ok => Skipped || Result is { Ok: true };

    public string? Error => Result?.Error;
}

/// <summary>Programa gerado do Job: o texto, as operações geradas, os avisos e os erros (por operação).</summary>
public sealed record JobBuild(string Program, IReadOnlyList<CamOperation> Operations, IReadOnlyList<JobOpResult> Items, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool Ok => Errors.Count == 0;
}

/// <summary>Gera o percurso e o programa de um <see cref="CamJob"/> com o mesmo código que a tela do CAM já usava.</summary>
public static class CamJobBuilder
{
    /// <summary>Percurso de cada operação ligada. <paramref name="machine"/> só é necessário para face girada e texto no cilindro.</summary>
    public static IReadOnlyList<JobOpResult> BuildOperations(CamJob job, MachineProfile? machine)
    {
        var items = new List<JobOpResult>();
        for (int i = 0; i < job.Operations.Count; i++)
        {
            CamOperationSpec s = job.Operations[i];
            items.Add(s.Enabled ? new JobOpResult(i, false, BuildOne(job, s, machine)) : new JobOpResult(i, true, null));
        }

        return items;
    }

    /// <summary>Gera o programa inteiro. Se alguma operação ligada falhar, não gera texto e devolve os erros.</summary>
    public static JobBuild Build(CamJob job, MachineProfile? machine)
    {
        IReadOnlyList<string> structure = job.Validate();
        if (structure.Count > 0)
        {
            return new JobBuild("", [], [], structure, []);
        }

        IReadOnlyList<JobOpResult> items = BuildOperations(job, machine);
        var errors = items.Where(x => !x.Ok).Select(x => $"Operação {x.Index + 1}: {x.Error}").ToList();
        List<CamOperation> ops = items.Where(x => !x.Skipped && x.Ok).Select(x => x.Result!.Operation!).ToList();
        if (errors.Count == 0 && ops.Count == 0)
        {
            errors.Add("Não há operação ligada para gerar.");
        }

        if (errors.Count > 0)
        {
            return new JobBuild("", ops, items, errors, []);
        }

        // Programa nº e G5x: só o digitado (UX-04g). Faltando, as operações continuam geradas (a árvore mostra o estado), mas não há programa
        IReadOnlyList<string> program0 = CamJobProgram.Problems(job);
        if (program0.Count > 0)
        {
            return new JobBuild("", ops, items, program0, []);
        }

        var warnings = new List<string>(CamPost.Check(ops, machine));
        warnings.AddRange(ops.SelectMany(o => o.Warnings));
        // valor de arquivo antigo fora da faixa ou da lista: gera como antes, só avisa
        warnings.AddRange(CamJobProgram.Warnings(job));
        // Siemens não escreve o número (sai só o nome): o valor passado não aparece no programa
        int number = job.ProgramNumber ?? 0;
        string program = CamPost.Generate(ops, new PostOptions(job.Dialect, job.Name, number, job.WorkOffset!.Value, job.Coolant, machine));
        return new JobBuild(program, ops, items, errors, warnings);
    }

    private static OperationResult BuildOne(CamJob job, CamOperationSpec s, MachineProfile? machine)
    {
        if (CamJobInfo.MissingError(s) is { } missing)
        {
            // campo vazio ou inválido: não gera com o número antigo (regra "nada suposto")
            return new OperationResult(null, missing);
        }

        if (s.Tool.Validate() is { } toolError)
        {
            return new OperationResult(null, toolError);
        }

        OperationResult result;
        if (s.Kind == JobOpKind.WrapText)
        {
            result = Wrap(s, machine);
        }
        else
        {
            if (s.Levels.Validate() is { } levelError)
            {
                return new OperationResult(null, levelError);
            }

            result = Plain(job, s);
        }

        if (!result.Ok)
        {
            return result;
        }

        if (s.Face is { } face && s.Kind != JobOpKind.WrapText)
        {
            if (machine is null)
            {
                return new OperationResult(null, "Face girada: escolha o perfil da máquina (aba Máquina).");
            }

            result = face.Axis2 is { } axis2 ? PlaceTilted(result.Operation!, machine, face, axis2)
                : RotaryIndexing.Place(result.Operation!, machine, face.Axis, new IndexedFace(face.Angle, face.AxisToFace));
            if (!result.Ok)
            {
                return result;
            }
        }

        return s.Label.Length > 0 ? new OperationResult(result.Operation! with { Name = s.Label }, null) : result;
    }

    // 3+2: os eixos guardados na operação têm de ser os do perfil, na mesma ordem (basculante e depois giratório)
    private static OperationResult PlaceTilted(CamOperation op, MachineProfile machine, RotaryFaceSpec face, string axis2)
    {
        if (machine.Axes.Count != 2 || machine.Axes[0].Letter != face.Axis || machine.Axes[1].Letter != axis2)
        {
            string profile = machine.Axes.Count == 0 ? "sem eixo rotativo" : string.Join(" + ", machine.Axes.Select(a => a.Letter));
            return new OperationResult(null, $"Face 3+2 usa {face.Axis} + {axis2}, mas o perfil da máquina tem {profile}.");
        }

        return RotaryIndexing.PlaceTilted(op, machine, new TiltedFace(face.Angle, face.Angle2, face.AxisToFace));
    }

    private static OperationResult Plain(CamJob job, CamOperationSpec s)
    {
        Contour? part = s.ContourIndex >= 0 && s.ContourIndex < job.Contours.Count ? job.Contours[s.ContourIndex] : null;
        switch (s.Kind)
        {
            case JobOpKind.Contour:
                return CamOperations.Contour(part!, s.Tool, s.Levels, s.ContourParams!);
            case JobOpKind.Pocket:
                return s.Islands
                    ? CamOperations.PocketWithIslands(part!, CamOperations.FindIslands(part!, job.Contours), s.Tool, s.Levels, s.PocketParams!)
                    : CamOperations.Pocket(part!, s.Tool, s.Levels, s.PocketParams!);
            case JobOpKind.Facing:
                (double x0, double y0, double x1, double y1) = part!.BoundsXY();
                return CamOperations.Facing(s.Tool, s.Levels, x0, y0, x1 - x0, y1 - y0, s.FacingStepOverPercent);
            default:
                IEnumerable<Contour> source = part is null ? job.Contours : [part];
                IReadOnlyList<DrillHole> holes = CamOperations.HolesFromCircles(source);
                if (holes.Count == 0)
                {
                    return new OperationResult(null, "Nenhum círculo no desenho para furar.");
                }

                DrillSpec d = s.Drill!;
                return CamOperations.Drilling(s.Tool, s.Levels.SafeZ, holes.Select(h => h.Center), d.Cycle, s.Levels.TopZ, s.Levels.BottomZ, s.Levels.ClearanceZ, d.Peck, d.DwellSeconds);
        }
    }

    private static OperationResult Wrap(CamOperationSpec s, MachineProfile? machine)
    {
        if (machine is null)
        {
            return new OperationResult(null, "Texto no cilindro: escolha e salve o perfil da máquina (aba Máquina) com o eixo A ou B na mesa.");
        }

        WrapSpec w = s.Wrap!;
        string text = StrokeFont.Normalize(w.Text).Trim();
        if (text.Length == 0 || text.Any(c => !StrokeFont.TryGet(c, out _)))
        {
            return new OperationResult(null, "Texto vazio ou com caractere que a fonte não desenha. Aceito: " + StrokeFont.Supported + ".");
        }

        if (w.LetterHeight <= 0)
        {
            return new OperationResult(null, "A altura das letras precisa ser maior que zero.");
        }

        return RotaryWrap.Engrave("Texto no cilindro", s.Tool, machine, w.Axis, RotaryWrap.TextStrokes(text, w.LetterHeight), w.Setup);
    }
}
