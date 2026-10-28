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

    /// <summary>Ferramentas distintas usadas pelas operações, em ordem de T.</summary>
    public static IReadOnlyList<CamTool> ToolsUsed(CamJob job) =>
        job.Operations.Select(o => o.Tool).GroupBy(t => (t.Number, t.Diameter)).Select(g => g.First()).OrderBy(t => t.Number).ToList();

    /// <summary>
    /// Operação nova com valores padrão (os mesmos da tela atual do CAM). Contorno, bolsão e faceamento ficam no contorno
    /// <paramref name="contourIndex"/>; a furação usa todos os círculos. Os números são editados depois no painel de propriedades.
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
            _ => throw new ArgumentOutOfRangeException(nameof(kind), "Texto no cilindro ainda não tem valores padrão (precisa do perfil da máquina)."),
        };
    }
}
