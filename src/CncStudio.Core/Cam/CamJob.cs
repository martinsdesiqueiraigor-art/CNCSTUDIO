using CncStudio.Core.Generators;

namespace CncStudio.Core.Cam;

/// <summary>Tipo da operação de fresa guardada no Job.</summary>
public enum JobOpKind { Contour, Pocket, Facing, Drilling, WrapText }

/// <summary>Face girada no 4º eixo (mesa): letra do eixo (A ou B), ângulo e distância do centro do eixo até a face virada para cima.</summary>
public sealed record RotaryFaceSpec(string Axis, double Angle, double AxisToFace);

/// <summary>Furação pelos círculos do desenho: tipo de ciclo, bicada e pausa.</summary>
public sealed record DrillSpec(DrillCycleKind Cycle = DrillCycleKind.Drill, double Peck = 0, double DwellSeconds = 0);

/// <summary>Texto enrolado no cilindro (4º eixo contínuo): texto, altura das letras, eixo (A ou B) e dados do cilindro.</summary>
public sealed record WrapSpec(string Text, double LetterHeight, string Axis, WrapSetup Setup);

/// <summary>Bruto retangular da peça (canto, tamanho; o topo fica em Z 0). Fica guardado no Job; as operações ainda não o usam.</summary>
public sealed record JobStock(double X, double Y, double Width, double Length, double Thickness);

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

    /// <summary>Faceamento: passo lateral em % do diâmetro.</summary>
    public double FacingStepOverPercent { get; init; } = 60;

    public DrillSpec? Drill { get; init; }

    public WrapSpec? Wrap { get; init; }

    /// <summary>Leva a operação para uma face girada (não vale para o texto no cilindro, que já gira sozinho).</summary>
    public RotaryFaceSpec? Face { get; init; }
}

/// <summary>
/// Trabalho (Job) de fresa do CAM: desenho, bruto, comando, números do programa e a lista de operações em ordem.
/// É o que se salva e se abre; o programa nasce dele (<see cref="CamJobBuilder"/>). A máquina (perfil) vem de fora.
/// </summary>
public sealed class CamJob
{
    public const int CurrentVersion = 1;

    public string Name { get; set; } = "TRABALHO";

    public CamDialect Dialect { get; set; } = CamDialect.Fanuc;

    public int ProgramNumber { get; set; } = 1001;

    public int WorkOffset { get; set; } = 54;

    public bool Coolant { get; set; } = true;

    public JobStock? Stock { get; set; }

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

        var warnings = new List<string>(CamPost.Check(ops));
        warnings.AddRange(ops.SelectMany(o => o.Warnings));
        string program = CamPost.Generate(ops, new PostOptions(job.Dialect, job.Name, job.ProgramNumber, job.WorkOffset, job.Coolant));
        return new JobBuild(program, ops, items, errors, warnings);
    }

    private static OperationResult BuildOne(CamJob job, CamOperationSpec s, MachineProfile? machine)
    {
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

            result = RotaryIndexing.Place(result.Operation!, machine, face.Axis, new IndexedFace(face.Angle, face.AxisToFace));
            if (!result.Ok)
            {
                return result;
            }
        }

        return s.Label.Length > 0 ? new OperationResult(result.Operation! with { Name = s.Label }, null) : result;
    }

    private static OperationResult Plain(CamJob job, CamOperationSpec s)
    {
        Contour? part = s.ContourIndex >= 0 && s.ContourIndex < job.Contours.Count ? job.Contours[s.ContourIndex] : null;
        switch (s.Kind)
        {
            case JobOpKind.Contour:
                return CamOperations.Contour(part!, s.Tool, s.Levels, s.ContourParams!);
            case JobOpKind.Pocket:
                return CamOperations.Pocket(part!, s.Tool, s.Levels, s.PocketParams!);
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
