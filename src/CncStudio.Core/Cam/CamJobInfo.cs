using System.Globalization;

namespace CncStudio.Core.Cam;

/// <summary>Textos da árvore do Job e operações novas com valores padrão. Fica no Core para ter teste e valer nas duas telas.</summary>
public static class CamJobInfo
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    private static string N(double v) => v.ToString("0.##", Br);

    /// <summary>Tipos que o usuário pode criar na árvore (nome mostrado, tipo e lado do contorno quando houver).</summary>
    public static readonly IReadOnlyList<(string Name, JobOpKind Kind, ContourSide Side)> NewKinds =
    [
        ("Contorno por fora", JobOpKind.Contour, ContourSide.Outside),
        ("Contorno por dentro", JobOpKind.Contour, ContourSide.Inside),
        ("Contorno sobre a linha", JobOpKind.Contour, ContourSide.OnLine),
        ("Bolsão", JobOpKind.Pocket, ContourSide.Outside),
        ("Faceamento", JobOpKind.Facing, ContourSide.Outside),
        ("Furação (círculos do desenho)", JobOpKind.Drilling, ContourSide.Outside),
        ("Texto no cilindro (4º eixo)", JobOpKind.WrapText, ContourSide.Outside),
    ];

    /// <summary>Nome padrão da operação, sem o nome personalizado.</summary>
    public static string KindName(CamOperationSpec s) => s.Kind switch
    {
        JobOpKind.Contour => s.ContourParams?.Side switch
        {
            ContourSide.Inside => "Contorno por dentro",
            ContourSide.OnLine => "Contorno sobre a linha",
            _ => "Contorno por fora",
        },
        JobOpKind.Pocket => "Bolsão",
        JobOpKind.Facing => "Faceamento",
        JobOpKind.Drilling => "Furação",
        _ => "Texto no cilindro",
    };

    /// <summary>
    /// Estado de uma operação na árvore: "OK", "desligada", "erro: motivo" ou, com o limite do fuso informado, "aviso: acima do fuso"
    /// (mesma conferência de <see cref="SpindleWarning"/>).
    /// </summary>
    public static (string Kind, string Text) OpState(JobOpResult? r, CamOperationSpec? spec = null, double? maxRpm = null)
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

        return spec is not null && SpindleWarning(spec.Tool.SpindleRpm, maxRpm) is not null ? ("pending", "aviso: acima do fuso") : ("ok", "OK");
    }

    /// <summary>Operações ligadas com rotação acima do limite do fuso (índices), pela mesma conferência de <see cref="SpindleWarning"/>.</summary>
    public static IReadOnlyList<int> OverSpindle(CamJob job, double? maxRpm) =>
        job.Operations.Select((o, i) => (o, i)).Where(x => x.o.Enabled && SpindleWarning(x.o.Tool.SpindleRpm, maxRpm) is not null).Select(x => x.i).ToList();

    /// <summary>
    /// O que impede de gerar o G-code (vazio = pronto). Não inclui a fixação, que o app nunca verifica. <paramref name="maxRpm"/>: limite do
    /// fuso do perfil da máquina; sem ele, a rotação não vira pendência ("fuso não informado").
    /// </summary>
    public static IReadOnlyList<string> Pending(CamJob job, JobBuild build, double? maxRpm = null)
    {
        var list = new List<string>();
        if (job.Contours.Count == 0)
        {
            list.Add("Falta a peça (abra um DXF)");
        }

        if (job.Stock is null)
        {
            list.Add("Bruto não definido");
        }

        if (job.Operations.Count == 0)
        {
            list.Add("Nenhuma operação");
        }

        for (int i = 0; i < build.Items.Count; i++)
        {
            JobOpResult r = build.Items[i];
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

    /// <summary>Estado do bruto: definido ou pendente.</summary>
    public static (string Kind, string Text) StockState(JobStock? s) => s is null ? ("bad", "falta definir") : ("ok", "OK");

    /// <summary>Linha da árvore: "3 · Bolsão — T1 Ø10". Usa o nome personalizado, se houver.</summary>
    public static string OpTitle(int index, CamOperationSpec s)
    {
        string name = s.Label.Length > 0 ? s.Label : KindName(s);
        string face = s.Face is { } f ? $" · face {f.Axis}{N(f.Angle)}°" : "";
        string tool = s.Tool.Name.Length > 0 ? " " + s.Tool.Name : "";
        return $"{index + 1} · {name} — T{s.Tool.Number} Ø{N(s.Tool.Diameter)}{tool}{face}";
    }

    /// <summary>Linha de um contorno do desenho: "2 · Círculo Ø20 (20 × 20 mm)".</summary>
    public static string ContourTitle(Contour c, int index)
    {
        (double x0, double y0, double x1, double y1) = c.BoundsXY();
        bool circle = c.Segments.Count == 1 && c.Segments[0].IsArc;
        string what = circle ? "Círculo Ø" + N(c.Segments[0].Radius * 2) : "Contorno";
        return $"{index + 1} · {what} ({N(x1 - x0)} × {N(y1 - y0)} mm)";
    }

    /// <summary>Resumo do bruto para a árvore.</summary>
    public static string StockTitle(JobStock? s) => s is null ? "Bruto: não definido" : $"Bruto {N(s.Width)} × {N(s.Length)} × {N(s.Thickness)} mm";

    /// <summary>Bruto digitado na tela: as três medidas precisam ser números maiores que zero (null = válido).</summary>
    public static string? StockError(double width, double length, double thickness)
    {
        bool Bad(double v) => !double.IsFinite(v) || v <= 0;
        if (Bad(width) || Bad(length))
        {
            return "Comprimento e largura do bruto precisam ser maiores que zero.";
        }

        return Bad(thickness) ? "A altura do bruto precisa ser maior que zero." : null;
    }

    /// <summary>Retângulo que envolve todos os contornos do desenho (canto e medidas em X e Y), para preencher o bruto; null sem desenho.</summary>
    public static (double X, double Y, double Width, double Length)? DrawingBox(IReadOnlyList<Contour> contours)
    {
        if (contours.Count == 0)
        {
            return null;
        }

        var b = contours.Select(c => c.BoundsXY()).ToList();
        double x0 = b.Min(x => x.MinX), y0 = b.Min(x => x.MinY), x1 = b.Max(x => x.MaxX), y1 = b.Max(x => x.MaxY);
        return (x0, y0, x1 - x0, y1 - y0);
    }

    /// <summary>
    /// Linha "Origem" da árvore: o deslocamento de trabalho, X0 Y0 do desenho e o Z topo das operações ligadas
    /// ("topo Z0", "topo Z2" ou "topo Z varia por operação"). Sem operação ligada vale o padrão das operações novas (Z0).
    /// </summary>
    public static string OriginTitle(CamJob job)
    {
        List<double> tops = job.Operations.Where(o => o.Enabled && o.Kind != JobOpKind.WrapText).Select(o => o.Levels.TopZ).Distinct().ToList();
        string top = tops.Count switch
        {
            0 => "topo Z0",
            1 => "topo Z" + N(tops[0]),
            _ => "topo Z varia por operação",
        };
        return $"Origem G{job.WorkOffset}: X0 Y0 do desenho, {top}";
    }

    /// <summary>A origem na máquina nunca é conferida pelo app: fica sempre "não verificada".</summary>
    public static (string Kind, string Text) OriginState() => ("pending", "não verificada");

    /// <summary>Linha "Máquina" da árvore: perfil ativo e rotação máxima do fuso informada nele.</summary>
    public static string MachineTitle(MachineProfile? m) => m is null
        ? "Máquina: sem perfil ativo"
        : m.MaxSpindleRpm is { } rpm ? $"Máquina: {m.Name} · fuso até {N(rpm)} rpm" : $"Máquina: {m.Name} · fuso não informado";

    /// <summary>Estado da máquina: OK só com a rotação máxima do fuso informada; sem ela, nada é suposto.</summary>
    public static (string Kind, string Text) MachineState(MachineProfile? m) => m switch
    {
        null => ("pending", "sem perfil"),
        { MaxSpindleRpm: > 0 } => ("ok", "OK"),
        _ => ("pending", "fuso não informado"),
    };

    /// <summary>
    /// Aviso de rotação acima do limite do fuso do perfil da máquina (null = dentro do limite ou limite não informado).
    /// Só confere: não muda a rotação nem o programa.
    /// </summary>
    public static string? SpindleWarning(double rpm, double? maxRpm) =>
        maxRpm is { } max && max > 0 && rpm > max + 1e-9
            ? $"Acima do limite do fuso ({N(max)} rpm). Digite até {N(max)}; o avanço cai na mesma proporção."
            : null;

    /// <summary>Ferramentas distintas usadas pelas operações, em ordem de T.</summary>
    public static IReadOnlyList<CamTool> ToolsUsed(CamJob job) =>
        job.Operations.Select(o => o.Tool).GroupBy(t => (t.Number, t.Diameter)).Select(g => g.First()).OrderBy(t => t.Number).ToList();

    /// <summary>
    /// Operação nova com valores padrão (os mesmos da tela atual do CAM). Contorno, bolsão e faceamento ficam no contorno
    /// <paramref name="contourIndex"/>; a furação usa todos os círculos. O texto no cilindro precisa do perfil da máquina na hora de gerar.
    /// </summary>
    public static CamOperationSpec NewOperation(JobOpKind kind, ContourSide side, int contourIndex, CamTool? tool = null)
    {
        CamTool t = tool ?? new CamTool(1, 10, 3000, 500, 200);
        var levels = new CamLevels(50, 2, 0, -5, 2.5);
        return kind switch
        {
            JobOpKind.Contour => new CamOperationSpec(kind, t, levels) { ContourIndex = contourIndex, ContourParams = new ContourParams(side, true, false, 0, 3) },
            JobOpKind.Pocket => new CamOperationSpec(kind, t, levels) { ContourIndex = contourIndex, PocketParams = new PocketParams(60, 0, 2) },
            JobOpKind.Facing => new CamOperationSpec(kind, t, levels) { ContourIndex = contourIndex, FacingStepOverPercent = 60 },
            JobOpKind.Drilling => new CamOperationSpec(kind, t, levels) { Drill = new DrillSpec(DrillCycleKind.Drill, 0, 0) },
            _ => new CamOperationSpec(JobOpKind.WrapText, t, levels) { Wrap = new WrapSpec("CASILLAS", 6, "A", new WrapSetup(40, 0, 0, 0.3, 0.15, 3, 200, 60)) },
        };
    }
}
