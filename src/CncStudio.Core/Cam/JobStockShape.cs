using System.Globalization;

namespace CncStudio.Core.Cam;

/// <summary>Formato novo do bruto da fresa (UX-04f). O bloco absoluto continua sendo o <see cref="JobStock"/> de antes.</summary>
public enum StockShapeKind
{
    /// <summary>Bloco pela sobra em cada lado da peça pronta.</summary>
    Margins,

    /// <summary>Cilindro maciço.</summary>
    Cylinder,

    /// <summary>Tubo (cilindro com furo).</summary>
    Tube,

    /// <summary>Bruto fundido ou forjado lido de um STEP: o arquivo guarda o caminho e o resumo, nunca a malha (UX-04f PR 2).</summary>
    Step,
}

/// <summary>Posição do bruto STEP: como está no arquivo (coordenadas do STEP = do programa) ou com deslocamento digitado.</summary>
public enum StepPlacement
{
    /// <summary>As coordenadas do STEP valem como as do programa (Z0 do programa = Z0 do STEP).</summary>
    AsInFile,

    /// <summary>Deslocamento X, Y, Z digitado sobre as coordenadas do STEP.</summary>
    Typed,
}

/// <summary>
/// Resumo do STEP do bruto guardado no arquivo do trabalho: a caixa (cantos, em mm, nas coordenadas do STEP) e o volume (mm³).
/// Abrir o trabalho usa só isto: o STEP só é lido para mostrar o sólido no 3D.
/// </summary>
public sealed record StepStockSummary(double MinX, double MinY, double MinZ, double MaxX, double MaxY, double MaxZ, double Volume)
{
    public double SizeX => MaxX - MinX;

    public double SizeY => MaxY - MinY;

    public double SizeZ => MaxZ - MinZ;

    /// <summary>Resumo da malha lida (mesmas funções do CAM 3D: <see cref="Cam3D.TriangleMesh.Bounds"/> e <see cref="Cam3D.TriangleMesh.Volume"/>).</summary>
    public static StepStockSummary From(Cam3D.TriangleMesh mesh) =>
        new(mesh.Bounds.Min.X, mesh.Bounds.Min.Y, mesh.Bounds.Min.Z, mesh.Bounds.Max.X, mesh.Bounds.Max.Y, mesh.Bounds.Max.Z, mesh.Volume);

    /// <summary>Caixa com medidas maiores que zero e números finitos (arquivo estragado fica fora).</summary>
    public bool Valid =>
        new[] { MinX, MinY, MinZ, MaxX, MaxY, MaxZ, Volume }.All(double.IsFinite) && SizeX > 0 && SizeY > 0 && SizeZ > 0 && Volume > 0;
}

/// <summary>Eixo do cilindro ou tubo: em pé (Z) ou deitado ao longo de X ou Y.</summary>
public enum StockAxis { Z, X, Y }

/// <summary>
/// Bruto da fresa num formato novo (UX-04f): bloco por sobra, cilindro ou tubo, com deslocamento em relação à posição padrão
/// (centrado na peça pronta). Campo null = vazio: nada é suposto, o bruto fica incompleto e o campo não vai para o arquivo.
/// Medidas em mm, em coordenadas do programa com Z0 no topo da peça pronta. Não entra no G-code.
/// </summary>
public sealed record JobStockShape(StockShapeKind Kind)
{
    // nomes dos campos como aparecem na tela (motivo do "incompleto")
    public const string XMinusField = "X −";
    public const string XPlusField = "X +";
    public const string YMinusField = "Y −";
    public const string YPlusField = "Y +";
    public const string ZTopField = "Z em cima";
    public const string ZBottomField = "Z embaixo";
    public const string DiameterField = "Diâmetro";
    public const string OuterField = "Ø externo";
    public const string InnerField = "Ø interno";
    public const string LengthField = "Comprimento";
    public const string OffsetXField = "Deslocamento X";
    public const string OffsetYField = "Deslocamento Y";
    public const string OffsetZField = "Deslocamento Z";
    public const string StepFileField = "o arquivo STEP";

    /// <summary>Bloco: sobra além da peça em cada lado (≥ 0).</summary>
    public double? XMinus { get; init; }

    public double? XPlus { get; init; }

    public double? YMinus { get; init; }

    public double? YPlus { get; init; }

    public double? ZTop { get; init; }

    public double? ZBottom { get; init; }

    /// <summary>Cilindro e tubo: eixo.</summary>
    public StockAxis Axis { get; init; } = StockAxis.Z;

    /// <summary>Cilindro: diâmetro. Tubo: diâmetro externo.</summary>
    public double? Diameter { get; init; }

    /// <summary>Tubo: diâmetro interno (menor que o externo).</summary>
    public double? InnerDiameter { get; init; }

    /// <summary>Cilindro e tubo: comprimento ao longo do eixo.</summary>
    public double? Length { get; init; }

    /// <summary>Deslocamento da posição padrão (0 = centrado na peça, decisão do Igor de 08/11).</summary>
    public double? OffsetX { get; init; } = 0;

    public double? OffsetY { get; init; } = 0;

    public double? OffsetZ { get; init; } = 0;

    /// <summary>STEP: caminho do arquivo como foi escolhido (absoluto). Null = nenhum arquivo.</summary>
    public string? StepPath { get; init; }

    /// <summary>STEP: caixa e volume lidos do arquivo na importação. Null = nunca lido.</summary>
    public StepStockSummary? StepSummary { get; init; }

    /// <summary>STEP: como está no arquivo (padrão) ou com deslocamento digitado.</summary>
    public StepPlacement Placement { get; init; } = StepPlacement.AsInFile;

    /// <summary>
    /// STEP: motivo de o arquivo não estar disponível (sumiu ou mudou de lugar), conferido ao abrir sem ler o arquivo
    /// (<see cref="CamJobStock.VerifyStepFile"/>). Só da tela: não vai para o arquivo. Deixa o bruto incompleto.
    /// </summary>
    public string? StepFileProblem { get; init; }

    /// <summary>Campos que o formato usa, na ordem da tela, com o valor (null = vazio).</summary>
    public IReadOnlyList<(string Name, double? Value)> Fields() => Kind switch
    {
        StockShapeKind.Margins =>
        [
            (XMinusField, XMinus), (XPlusField, XPlus), (YMinusField, YMinus), (YPlusField, YPlus), (ZTopField, ZTop), (ZBottomField, ZBottom),
            (OffsetXField, OffsetX), (OffsetYField, OffsetY), (OffsetZField, OffsetZ),
        ],
        StockShapeKind.Cylinder => [(DiameterField, Diameter), (LengthField, Length), (OffsetXField, OffsetX), (OffsetYField, OffsetY), (OffsetZField, OffsetZ)],
        // STEP: os números vêm do arquivo; só o deslocamento digitado é campo
        StockShapeKind.Step => Placement == StepPlacement.Typed ? [(OffsetXField, OffsetX), (OffsetYField, OffsetY), (OffsetZField, OffsetZ)] : [],
        _ => [(OuterField, Diameter), (InnerField, InnerDiameter), (LengthField, Length), (OffsetXField, OffsetX), (OffsetYField, OffsetY), (OffsetZField, OffsetZ)],
    };

    /// <summary>Copia com um campo trocado (pelo nome da tela); null = esvaziar.</summary>
    public JobStockShape With(string field, double? value) => field switch
    {
        XMinusField => this with { XMinus = value },
        XPlusField => this with { XPlus = value },
        YMinusField => this with { YMinus = value },
        YPlusField => this with { YPlus = value },
        ZTopField => this with { ZTop = value },
        ZBottomField => this with { ZBottom = value },
        DiameterField or OuterField => this with { Diameter = value },
        InnerField => this with { InnerDiameter = value },
        LengthField => this with { Length = value },
        OffsetXField => this with { OffsetX = value },
        OffsetYField => this with { OffsetY = value },
        OffsetZField => this with { OffsetZ = value },
        _ => throw new ArgumentException("Campo do bruto desconhecido: " + field, nameof(field)),
    };

    /// <summary>
    /// Motivo de um valor digitado ser inválido para o campo (null = vale): sobra negativa, medida ≤ 0 ou Ø interno ≥ Ø externo.
    /// A mensagem diz o motivo, como no CAM-12.
    /// </summary>
    public string? InvalidReason(string field)
    {
        double? v = Fields().FirstOrDefault(f => f.Name == field).Value;
        if (v is not { } x)
        {
            return null;
        }

        if (!double.IsFinite(x))
        {
            return $"{field}: digite um número.";
        }

        return field switch
        {
            XMinusField or XPlusField or YMinusField or YPlusField or ZTopField or ZBottomField when x < 0 => $"{field}: a sobra não pode ser negativa.",
            DiameterField or OuterField or LengthField when x <= 0 => $"{field} precisa ser maior que zero.",
            InnerField when x <= 0 => "Ø interno precisa ser maior que zero.",
            InnerField when Diameter is { } outer && x >= outer => "Ø interno precisa ser menor que o Ø externo.",
            _ => null,
        };
    }
}

/// <summary>Forma resolvida do bruto, pronta para desenhar: caixa ou cilindro/tubo (com a caixa que o envolve).</summary>
public enum StockSolidKind { Box, Cylinder, Tube, Step }

/// <summary>
/// Bruto resolvido em coordenadas do programa. Na caixa, Min/Max são os cantos. No cilindro e no tubo, Min/Max envolvem o sólido:
/// o centro é o meio da caixa e o comprimento é a medida ao longo de <see cref="Axis"/>.
/// </summary>
public sealed record StockSolid(StockSolidKind Kind, double MinX, double MaxX, double MinY, double MaxY, double MinZ, double MaxZ)
{
    public StockAxis Axis { get; init; } = StockAxis.Z;

    public double Diameter { get; init; }

    public double InnerDiameter { get; init; }

    /// <summary>
    /// STEP: o sólido lido, já na posição do programa (<see cref="CamJobStock.PlaceStepMesh"/>). Null enquanto o STEP não foi lido
    /// nesta sessão: o 3D mostra só a caixa do resumo.
    /// </summary>
    public Cam3D.TriangleMesh? Mesh { get; init; }

    /// <summary>STEP: volume do resumo (mm³).</summary>
    public double Volume { get; init; }

    public double SizeX => MaxX - MinX;

    public double SizeY => MaxY - MinY;

    public double SizeZ => MaxZ - MinZ;

    /// <summary>Comprimento ao longo do eixo (cilindro e tubo).</summary>
    public double Length => Axis switch { StockAxis.X => SizeX, StockAxis.Y => SizeY, _ => SizeZ };

    /// <summary>Caixa (canto mínimo e medidas).</summary>
    public static StockSolid Box(double x0, double y0, double z0, double sx, double sy, double sz) => new(StockSolidKind.Box, x0, x0 + sx, y0, y0 + sy, z0, z0 + sz);

    /// <summary>Cilindro ou tubo com centro (cx, cy, cz), eixo, diâmetros e comprimento.</summary>
    public static StockSolid Round(bool tube, StockAxis axis, double cx, double cy, double cz, double diameter, double inner, double length)
    {
        double r = diameter / 2, h = length / 2;
        (double hx, double hy, double hz) = axis switch { StockAxis.X => (h, r, r), StockAxis.Y => (r, h, r), _ => (r, r, h) };
        return new StockSolid(tube ? StockSolidKind.Tube : StockSolidKind.Cylinder, cx - hx, cx + hx, cy - hy, cy + hy, cz - hz, cz + hz)
        {
            Axis = axis, Diameter = diameter, InnerDiameter = tube ? inner : 0,
        };
    }
}

/// <summary>
/// Estado do bruto do Trabalho fresa: resolvido (pronto para o 3D) ou incompleto, com o motivo. O bruto nunca entra no G-code;
/// incompleto trava Gerar e Enviar, como o "Bruto não definido" (adendo 2 do UX-04f, 08/11), mas nunca trava Salvar.
/// </summary>
public sealed record StockCheck(StockSolid? Solid, IReadOnlyList<string> Missing, IReadOnlyList<string> Invalid)
{
    /// <summary>Nenhum bruto escolhido (nem bloco absoluto, nem formato novo).</summary>
    public bool NotDefined { get; init; }

    public bool Complete => Solid is not null;
}

/// <summary>Regras do bruto da fresa (UX-04f): resolver a forma, o motivo do incompleto e os textos da árvore.</summary>
public static class CamJobStock
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    private static string N(double v) => v.ToString("0.##", Br);

    /// <summary>Nome do campo da altura da peça pronta (como na tela).</summary>
    public const string PartHeightField = "Altura Z da peça";

    /// <summary>Começo do texto da pendência do bruto incompleto (trava Gerar e Enviar; não trava Salvar).</summary>
    public const string IncompletePrefix = "Bruto incompleto";

    /// <summary>
    /// Resolve o bruto. Bloco absoluto (arquivo antigo): os valores do arquivo, com o topo em Z0, sem depender da peça.
    /// Formatos novos: partem da caixa do desenho (X, Y) e da altura digitada da peça (Z de −altura a 0). A posição padrão
    /// "centrado na peça" depende da altura também no cilindro e no tubo (o centro em Z é −altura/2), então os três exigem a altura.
    /// </summary>
    public static StockCheck Check(CamJob job)
    {
        if (job.StockShape is not { } shape)
        {
            return job.Stock is { } st
                ? new StockCheck(StockSolid.Box(st.X, st.Y, -st.Thickness, st.Width, st.Length, st.Thickness), [], [])
                : new StockCheck(null, [], []) { NotDefined = true };
        }

        if (shape.Kind == StockShapeKind.Step)
        {
            return CheckStep(shape);
        }

        var missing = new List<string>();
        (double X, double Y, double Width, double Length)? box = CamJobInfo.DrawingBox(job.Contours);
        if (box is null)
        {
            missing.Add("o desenho da peça");
        }

        var invalid = new List<string>();
        if (job.PartHeight is not { } h)
        {
            missing.Add(PartHeightField);
        }
        else if (PartHeightError(h) is { } bad)
        {
            invalid.Add(bad);
        }

        foreach ((string name, double? value) in shape.Fields())
        {
            if (value is null)
            {
                missing.Add(name);
            }
            else if (shape.InvalidReason(name) is { } why)
            {
                invalid.Add(why);
            }
        }

        if (missing.Count > 0 || invalid.Count > 0 || box is not { } b || job.PartHeight is not { } height)
        {
            return new StockCheck(null, missing, invalid);
        }

        double dx = shape.OffsetX!.Value, dy = shape.OffsetY!.Value, dz = shape.OffsetZ!.Value;
        if (shape.Kind == StockShapeKind.Margins)
        {
            double x0 = b.X - shape.XMinus!.Value, y0 = b.Y - shape.YMinus!.Value, z0 = -height - shape.ZBottom!.Value;
            double sx = b.Width + shape.XMinus.Value + shape.XPlus!.Value;
            double sy = b.Length + shape.YMinus.Value + shape.YPlus!.Value;
            double sz = height + shape.ZBottom.Value + shape.ZTop!.Value;
            return new StockCheck(StockSolid.Box(x0 + dx, y0 + dy, z0 + dz, sx, sy, sz), [], []);
        }

        double cx = b.X + b.Width / 2 + dx, cy = b.Y + b.Length / 2 + dy, cz = -height / 2 + dz;
        bool tube = shape.Kind == StockShapeKind.Tube;
        return new StockCheck(StockSolid.Round(tube, shape.Axis, cx, cy, cz, shape.Diameter!.Value, shape.InnerDiameter ?? 0, shape.Length!.Value), [], []);
    }

    /// <summary>
    /// Bruto STEP: a caixa do resumo nas coordenadas do STEP (como está no arquivo) ou somada ao deslocamento digitado. Não depende
    /// do desenho nem da altura da peça (o STEP não compartilha sistema de coordenadas garantido com o DXF: a tela mostra a caixa
    /// para o usuário conferir; nada é alinhado sozinho). Sem arquivo, sem resumo ou arquivo que sumiu: incompleto.
    /// </summary>
    private static StockCheck CheckStep(JobStockShape shape)
    {
        var missing = new List<string>();
        var invalid = new List<string>();
        if (string.IsNullOrWhiteSpace(shape.StepPath) || shape.StepSummary is null)
        {
            missing.Add(JobStockShape.StepFileField);
        }
        else if (!shape.StepSummary.Valid)
        {
            invalid.Add("o resumo do STEP é inválido: importe o arquivo de novo");
        }

        if (shape.StepFileProblem is { } problem)
        {
            invalid.Add(problem);
        }

        foreach ((string name, double? value) in shape.Fields())
        {
            if (value is null)
            {
                missing.Add(name);
            }
            else if (!double.IsFinite(value.Value))
            {
                invalid.Add($"{name}: digite um número");
            }
        }

        if (missing.Count > 0 || invalid.Count > 0)
        {
            return new StockCheck(null, missing, invalid);
        }

        (double dx, double dy, double dz) = StepOffset(shape);
        StepStockSummary b = shape.StepSummary!;
        return new StockCheck(new StockSolid(StockSolidKind.Step, b.MinX + dx, b.MaxX + dx, b.MinY + dy, b.MaxY + dy, b.MinZ + dz, b.MaxZ + dz) { Volume = b.Volume }, [], []);
    }

    /// <summary>Deslocamento do bruto STEP: zero "como está no arquivo"; o digitado no outro modo (vazio conta como incompleto antes).</summary>
    private static (double X, double Y, double Z) StepOffset(JobStockShape s) =>
        s.Placement == StepPlacement.Typed ? (s.OffsetX ?? 0, s.OffsetY ?? 0, s.OffsetZ ?? 0) : (0, 0, 0);

    /// <summary>A malha lida do STEP colocada na posição do programa (mesmo deslocamento da caixa).</summary>
    public static Cam3D.TriangleMesh PlaceStepMesh(JobStockShape shape, Cam3D.TriangleMesh mesh)
    {
        (double dx, double dy, double dz) = StepOffset(shape);
        return dx == 0 && dy == 0 && dz == 0 ? mesh : mesh.Translated(dx, dy, dz);
    }

    /// <summary>Bruto STEP novo (importado ou localizado de novo): caminho e resumo da malha lida, mantendo a posição escolhida.</summary>
    public static JobStockShape ImportStep(JobStockShape? current, string path, Cam3D.TriangleMesh mesh)
    {
        JobStockShape start = current is { Kind: StockShapeKind.Step } ? current : new JobStockShape(StockShapeKind.Step);
        return start with { StepPath = path, StepSummary = StepStockSummary.From(mesh), StepFileProblem = null };
    }

    /// <summary>
    /// Confere, sem ler o STEP, se o arquivo do bruto ainda está no caminho guardado (ao abrir o trabalho). Sumiu ou mudou de lugar:
    /// o bruto fica incompleto com o motivo e a tela pede "Localizar de novo" (nada suposto). Devolve se mudou algo.
    /// </summary>
    public static bool VerifyStepFile(CamJob job, Func<string, bool> exists)
    {
        if (job.StockShape is not { Kind: StockShapeKind.Step, StepPath: { } path } shape || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string? problem = exists(path) ? null : $"arquivo STEP não encontrado em {path} (sumiu ou mudou de lugar): use Localizar de novo";
        if (problem == shape.StepFileProblem)
        {
            return false;
        }

        job.StockShape = shape with { StepFileProblem = problem };
        return true;
    }

    /// <summary>Nome do arquivo STEP do bruto (sem a pasta), ou null.</summary>
    public static string? StepFileName(CamJob job) =>
        job.StockShape is { Kind: StockShapeKind.Step, StepPath: { } p } ? FileNameOf(p) : null;

    /// <summary>
    /// Nome do arquivo sem a pasta, tratando "\" e "/" igualmente (o trabalho salvo no Windows abre no Linux e vice-versa;
    /// Path.GetFileName só separa pela barra do sistema em que roda).
    /// </summary>
    public static string FileNameOf(string path) => path[(path.LastIndexOfAny(['\\', '/']) + 1)..];

    /// <summary>Volume em cm³ para a tela ("20,42 cm³").</summary>
    public static string VolumeText(double mm3) => (mm3 / 1000).ToString("0.##", Br) + " cm³";

    /// <summary>Altura da peça digitada: precisa ser maior que zero (null = vale).</summary>
    public static string? PartHeightError(double h) => double.IsFinite(h) && h > 0 ? null : "A altura Z da peça precisa ser maior que zero.";

    /// <summary>Motivo do bruto incompleto ("Falta Ø externo, Comprimento" / "Ø interno precisa ser menor…"); null se completo ou não definido.</summary>
    public static string? IncompleteReason(StockCheck c)
    {
        if (c.Complete || c.NotDefined)
        {
            return null;
        }

        var parts = new List<string>();
        if (c.Missing.Count > 0)
        {
            parts.Add("falta " + string.Join(", ", c.Missing));
        }

        // sem o ponto final de cada motivo: quem mostra o texto completa a frase
        parts.AddRange(c.Invalid.Select(i => i.TrimEnd('.')));
        return string.Join("; ", parts);
    }

    /// <summary>Nome do formato mostrado na tela.</summary>
    public static string KindName(CamJob job) => job.StockShape?.Kind switch
    {
        StockShapeKind.Margins => "Bloco por sobra",
        StockShapeKind.Cylinder => "Cilindro",
        StockShapeKind.Tube => "Tubo",
        StockShapeKind.Step => "STEP",
        _ => job.Stock is null ? "Bruto" : "Bloco absoluto",
    };

    /// <summary>Resumo para a árvore e o cartão: "Bloco por sobra 80 × 60 × 25 mm", "Tubo Ø80/Ø60 × 120 mm (eixo X)", "Bruto: não definido".</summary>
    public static string Title(CamJob job)
    {
        StockCheck c = Check(job);
        if (c.NotDefined)
        {
            return "Bruto: não definido";
        }

        string kind = KindName(job);
        if (c.Solid is not { } s)
        {
            return kind + ": incompleto";
        }

        string axis = s.Axis == StockAxis.Z ? "" : $" (eixo {s.Axis})";
        return s.Kind switch
        {
            StockSolidKind.Cylinder => $"{kind} Ø{N(s.Diameter)} × {N(s.Length)} mm{axis}",
            StockSolidKind.Tube => $"{kind} Ø{N(s.Diameter)}/Ø{N(s.InnerDiameter)} × {N(s.Length)} mm{axis}",
            StockSolidKind.Step => $"{kind} {StepFileName(job)} {N(s.SizeX)} × {N(s.SizeY)} × {N(s.SizeZ)} mm, {VolumeText(s.Volume)}",
            _ => $"{kind} {N(s.SizeX)} × {N(s.SizeY)} × {N(s.SizeZ)} mm",
        };
    }

    /// <summary>Estado para a árvore (selo): OK, incompleto com o motivo, ou não definido (pendente).</summary>
    public static (string Kind, string Text) State(CamJob job)
    {
        StockCheck c = Check(job);
        if (c.NotDefined)
        {
            return ("pending", "bruto não definido");
        }

        return c.Complete ? ("ok", "OK") : ("pending", "incompleto: " + IncompleteReason(c));
    }

    /// <summary>
    /// Caixa do bruto como <see cref="JobStock"/> para quem só lê a espessura (o Assist): o bloco absoluto como está; o bloco por
    /// sobra resolvido (STEP, como cilindro e tubo, não entrega espessura). Na convenção do <see cref="JobStock"/> a espessura desce de Z0; no bloco por sobra o topo fica acima de Z0
    /// (sobra em cima), então a espessura é de Z0 até o fundo (−MinZ), não a altura total. Cilindro, tubo, incompleto ou fundo
    /// acima de Z0: null (nada suposto).
    /// </summary>
    public static JobStock? BlockBox(CamJob job)
    {
        if (job.StockShape is null)
        {
            return job.Stock;
        }

        return Check(job).Solid is { Kind: StockSolidKind.Box } s && s.MinZ < 0 ? new JobStock(s.MinX, s.MinY, s.SizeX, s.SizeY, -s.MinZ) : null;
    }

    /// <summary>
    /// Aviso depois de trocar o desenho: o bloco por sobra é recalculado com as mesmas sobras (o desenho mudou a caixa da peça).
    /// Null se não há bloco por sobra ou se o bruto não mudou.
    /// </summary>
    public static string? RecalculatedNotice(CamJob job, StockSolid? before)
    {
        if (job.StockShape is not { Kind: StockShapeKind.Margins } || Check(job).Solid is not { } after || after == before)
        {
            return null;
        }

        return $"Bruto recalculado com as mesmas sobras: {N(after.SizeX)} × {N(after.SizeY)} × {N(after.SizeZ)} mm.";
    }

    /// <summary>Se o Trabalho usa algo do UX-04f (formato novo ou altura da peça): o arquivo sai na versão 2.</summary>
    public static bool NeedsVersion2(CamJob job) => job.StockShape is not null || job.PartHeight is not null;

    /// <summary>Lê um número digitado (vírgula ou ponto); vazio ou texto = null (nada suposto).</summary>
    public static double? ParseTyped(string? text)
    {
        string t = (text ?? "").Trim().Replace(',', '.');
        return t.Length > 0 && double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) ? v : null;
    }
}
