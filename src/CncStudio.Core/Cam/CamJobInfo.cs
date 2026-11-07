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
            list.Add("Falta a peça (abra um DXF ou desenhe uma forma)");
        }

        if (job.Stock is null)
        {
            list.Add("Bruto não definido");
        }

        if (job.Fixture is null)
        {
            list.Add(FixtureMissing);
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

    /// <summary>Pendência da fixação: aparece na barra, mas só avisa (não impede gerar nem enviar ao Code).</summary>
    public const string FixtureMissing = "Fixação não definida";

    /// <summary>
    /// Se a pendência impede gerar e enviar o G-code. Só a fixação não definida é aviso (decisão do Igor, 02/11); o resto trava,
    /// como antes.
    /// </summary>
    public static bool Blocks(string pending) => pending != FixtureMissing;

    /// <summary>Linha "Fixação" da árvore: "Fixação: não definida" ou "Fixação: Morsa · descrição".</summary>
    public static string FixtureTitle(JobFixture? f) => f is null
        ? "Fixação: não definida"
        : "Fixação: " + (JobFixture.NameOf(f.Kind) ?? f.Kind) + (f.Description.Length > 0 ? " · " + f.Description : "");

    /// <summary>
    /// Estado da fixação: pendente sem ela; definida pelo usuário continua "não verificada" (o app nunca confere fixação nem colisão).
    /// </summary>
    public static (string Kind, string Text) FixtureState(JobFixture? f) =>
        f is null ? ("pending", "não definida") : ("neutral", "informada · não verificada");

    /// <summary>Estado do bruto: definido ou pendente.</summary>
    public static (string Kind, string Text) StockState(JobStock? s) => s is null ? ("bad", "falta definir") : ("ok", "OK");

    // ---------------------------------------------------------------- campos vazios ou inválidos (CAM-12)

    /// <summary>Nomes dos campos (como aparecem na tela) que têm regra própria de uso.</summary>
    public const string ToolNumberField = "Ferramenta (T)";
    public const string DiameterField = "Diâmetro";
    public const string PeckField = "Bicada (Q)";
    public const string DwellField = "Pausa";
    public const string SpindleField = "Rotação";

    /// <summary>
    /// Nomes dos campos da face girada na tela, na ordem: ângulo, (segundo ângulo no 3+2), distância do centro do eixo até a face.
    /// Sem face, nenhum. Fonte única para a tela e para saber se um campo vazio da face ainda vale.
    /// </summary>
    public static IReadOnlyList<string> FaceFields(RotaryFaceSpec? f) => f switch
    {
        null => [],
        { Axis2: { } axis2 } => [$"Ângulo do basculante ({f.Axis})", $"Ângulo do giratório ({axis2})", $"Do centro do eixo {f.Axis} até a face"],
        _ => ["Ângulo da face", "Do centro do eixo até a face"],
    };

    private static bool IsFaceField(string field) =>
        field.StartsWith("Ângulo da face", StringComparison.Ordinal) || field.StartsWith("Ângulo do basculante", StringComparison.Ordinal)
        || field.StartsWith("Ângulo do giratório", StringComparison.Ordinal) || field.StartsWith("Do centro do eixo", StringComparison.Ordinal);

    /// <summary>
    /// Campos vazios ou inválidos que a operação usa agora. Bicada (Q) só conta em G83/G73 e Pausa só em G82 (decisão do Igor);
    /// campos da face só contam com a face ligada e no modo atual (simples ou 3+2).
    /// </summary>
    public static IReadOnlyList<string> MissingFields(CamOperationSpec s) => s.MissingFields.Where(f => Counts(s, f)).ToList();

    /// <summary>Campos com valor inválido que a operação usa agora (mesma regra de uso de <see cref="MissingFields(CamOperationSpec)"/>).</summary>
    public static IReadOnlyList<string> InvalidFields(CamOperationSpec s) => s.InvalidFields.Where(f => Counts(s, f)).ToList();

    /// <summary>
    /// Se a operação usa o campo agora: Bicada (Q) só em G83/G73, Pausa só em G82, campos da face só com a face ligada e no modo
    /// atual. A tela usa isto para só marcar de vermelho o campo que conta.
    /// </summary>
    public static bool Counts(CamOperationSpec s, string field) => field switch
    {
        PeckField when s.Kind == JobOpKind.Drilling => s.Drill?.Cycle is DrillCycleKind.Peck or DrillCycleKind.ChipBreak,
        DwellField when s.Kind == JobOpKind.Drilling => s.Drill?.Cycle == DrillCycleKind.Dwell,
        _ when IsFaceField(field) => FaceFields(s.Face).Contains(field),
        _ => true,
    };

    /// <summary>Texto do campo com valor inválido ("T inválido: use inteiro ≥ 1").</summary>
    public static string InvalidMessage(string field) => field == ToolNumberField ? "T inválido: use inteiro ≥ 1" : $"{field} inválido";

    /// <summary>
    /// Motivo da operação Incompleta por campo vazio ou inválido ("Falta Z fundo", "Falta Diâmetro e Z fundo",
    /// "T inválido: use inteiro ≥ 1"); null = nenhum.
    /// </summary>
    public static string? MissingError(CamOperationSpec s)
    {
        IReadOnlyList<string> m = MissingFields(s);
        var parts = new List<string>();
        if (m.Count > 0)
        {
            parts.Add(m.Count == 1 ? "Falta " + m[0] : "Falta " + string.Join(", ", m.Take(m.Count - 1)) + " e " + m[^1]);
        }

        parts.AddRange(InvalidFields(s).Select(InvalidMessage));
        return parts.Count == 0 ? null : string.Join("; ", parts);
    }

    /// <summary>O campo está vazio ou inválido (guardado na operação, conte ou não agora): a tela mostra "?" no lugar do número.</summary>
    public static bool Unknown(CamOperationSpec s, string field) => s.MissingFields.Contains(field) || s.InvalidFields.Contains(field);

    /// <summary>Marca (ou desmarca) um campo como inválido na operação. Não mexe no valor guardado.</summary>
    public static CamOperationSpec SetInvalid(CamOperationSpec s, string field, bool invalid)
    {
        bool has = s.InvalidFields.Contains(field);
        if (has == invalid)
        {
            return s;
        }

        return s with { InvalidFields = invalid ? [.. s.InvalidFields, field] : s.InvalidFields.Where(f => f != field).ToList() };
    }

    /// <summary>Estado do campo na operação: vazio, inválido ou válido (null). Um exclui o outro.</summary>
    public static CamOperationSpec SetFieldState(CamOperationSpec s, string field, bool missing, bool invalid) =>
        SetInvalid(SetMissing(s, field, missing), field, invalid);

    /// <summary>
    /// Linhas do nó "Ferramentas" da árvore ("T1 · Ø10 · 3000 rpm"). T, Ø ou rotação vazios ou inválidos numa operação aparecem
    /// como "?" (nunca o número antigo); a mesma ferramenta aparece uma vez.
    /// </summary>
    public static IReadOnlyList<string> ToolLines(CamJob job) =>
        job.Operations.Select(o => (
                Number: Unknown(o, ToolNumberField) ? int.MaxValue : o.Tool.Number,
                Text: $"T{(Unknown(o, ToolNumberField) ? "?" : o.Tool.Number.ToString(CultureInfo.InvariantCulture))}"
                    + $" · Ø{(Unknown(o, DiameterField) ? "?" : N(o.Tool.Diameter))}"
                    + $" · {(Unknown(o, SpindleField) ? "?" : o.Tool.SpindleRpm.ToString("0", CultureInfo.InvariantCulture))} rpm",
                Key: (Unknown(o, ToolNumberField) ? "?" : o.Tool.Number.ToString(CultureInfo.InvariantCulture), Unknown(o, DiameterField) ? "?" : N(o.Tool.Diameter))))
            .GroupBy(x => x.Key).Select(g => g.First()).OrderBy(x => x.Number).Select(x => x.Text).ToList();

    /// <summary>Marca (ou desmarca) um campo como vazio/inválido na operação. Não mexe no valor guardado.</summary>
    public static CamOperationSpec SetMissing(CamOperationSpec s, string field, bool missing)
    {
        bool has = s.MissingFields.Contains(field);
        if (has == missing)
        {
            return s;
        }

        return s with { MissingFields = missing ? [.. s.MissingFields, field] : s.MissingFields.Where(f => f != field).ToList() };
    }

    /// <summary>
    /// O que impede salvar o Trabalho: uma linha por operação com campo vazio ("Operação 2 (Bolsão): falta Z fundo"), inclusive as
    /// desligadas, porque o arquivo guardaria o número antigo que o usuário não vê. Vazio = pode salvar.
    /// </summary>
    public static IReadOnlyList<string> SaveBlockers(CamJob job)
    {
        var list = new List<string>();
        for (int i = 0; i < job.Operations.Count; i++)
        {
            CamOperationSpec s = job.Operations[i];
            if (MissingError(s) is { } why)
            {
                string name = s.Label.Length > 0 ? s.Label : KindName(s);
                list.Add($"Operação {i + 1} ({name}): " + (why.StartsWith("Falta ", StringComparison.Ordinal) ? "f" + why[1..] : why));
            }
        }

        return list;
    }

    /// <summary>Número de ferramenta (T): só inteiro ≥ 1. Devolve o erro para mostrar no campo, ou null se vale.</summary>
    public static string? ToolNumberError(double v) =>
        double.IsFinite(v) && v >= 1 && Math.Abs(v - Math.Round(v)) < 1e-9 ? null : InvalidMessage(ToolNumberField);

    /// <summary>Linha da árvore: "3 · Bolsão — T1 Ø10". Usa o nome personalizado, se houver.</summary>
    public static string OpTitle(int index, CamOperationSpec s)
    {
        string name = s.Label.Length > 0 ? s.Label : KindName(s);
        string Ang(RotaryFaceSpec f, int k, double v) => s.MissingFields.Contains(FaceFields(f)[k]) ? "?" : N(v);
        string face = s.Face is { } f ? $" · face {f.Axis}{Ang(f, 0, f.Angle)}°" + (f.Axis2 is { } a2 ? $" {a2}{Ang(f, 1, f.Angle2)}° (3+2)" : "") : "";
        string tool = s.Tool.Name.Length > 0 ? " " + s.Tool.Name : "";
        // campo vazio não mostra o número antigo (CAM-12)
        string t = Unknown(s, ToolNumberField) ? "?" : s.Tool.Number.ToString(CultureInfo.InvariantCulture);
        string d = Unknown(s, DiameterField) ? "?" : N(s.Tool.Diameter);
        return $"{index + 1} · {name} — T{t} Ø{d}{tool}{face}";
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

    /// <summary>Retângulo desenhado sem DXF (canto X, Y, largura, altura): números válidos e medidas maiores que zero (null = válido).</summary>
    public static string? RectangleError(double x, double y, double width, double height) =>
        !double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(width) || !double.IsFinite(height)
            ? "Retângulo: digite números válidos em X, Y, largura e altura."
            : width <= 0 || height <= 0 ? "Retângulo: largura e altura precisam ser maiores que zero." : null;

    /// <summary>Círculo desenhado sem DXF (centro X, Y, diâmetro): números válidos e diâmetro maior que zero (null = válido).</summary>
    public static string? CircleError(double cx, double cy, double diameter) =>
        !double.IsFinite(cx) || !double.IsFinite(cy) || !double.IsFinite(diameter)
            ? "Círculo: digite números válidos no centro e no diâmetro."
            : diameter <= 0 ? "Círculo: o diâmetro precisa ser maior que zero." : null;

    /// <summary>Ferramenta que vai para o banco: T inteiro de 1 a 9999 e diâmetro maior que zero (mesma regra da aba Fresa antiga).</summary>
    public static string? BankToolError(int number, double diameter) =>
        number is < 1 or > 9999 ? "o T precisa ser de 1 a 9999 para ir ao banco."
            : !double.IsFinite(diameter) || diameter <= 0 ? "o diâmetro precisa ser maior que zero para ir ao banco." : null;

    /// <summary>
    /// Ferramentas do programa cujo T está no banco do Code com outro diâmetro (o simulador do Code usa o do banco).
    /// Um aviso por T, na ordem do programa; T fora do banco não avisa (entra no banco ao abrir no Code).
    /// </summary>
    public static IReadOnlyList<string> BankDiameterWarnings(IEnumerable<CamTool> used, IEnumerable<(int Number, double Diameter)> bank)
    {
        var list = bank.ToList();
        var warnings = new List<string>();
        foreach (CamTool t in used.GroupBy(t => t.Number).Select(g => g.First()))
        {
            int at = list.FindIndex(e => e.Number == t.Number);
            if (at >= 0 && Math.Abs(list[at].Diameter - t.Diameter) > 1e-6)
            {
                warnings.Add($"T{t.Number} no banco do Code tem Ø{list[at].Diameter.ToString("0.###", Br)}, mas o CAM usou Ø{t.Diameter.ToString("0.###", Br)}: o simulador do Code usa o do banco.");
            }
        }

        return warnings;
    }

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
