using System.Globalization;
using CncStudio.Core.Geometry;

namespace CncStudio.Core.Cam;

/// <summary>
/// Ponto de X e Y do zero da peça (UX-04g PR B): um dos 9 pontos da caixa da peça pronta (4 cantos, 4 meios de aresta e o centro) ou a
/// origem do desenho (X0 Y0 do DXF). O nome é o que vai para o arquivo; não renomear.
/// </summary>
public enum ZeroPoint { Drawing, XMinYMin, XMidYMin, XMaxYMin, XMinYMid, Center, XMaxYMid, XMinYMax, XMidYMax, XMaxYMax }

/// <summary>Altura do Z0: no topo da peça pronta ou na base. O nome é o que vai para o arquivo.</summary>
public enum ZeroLevel { Top, Bottom }

/// <summary>
/// Zero da peça escolhido no Trabalho de fresa. Campo null = ainda não escolhido (Incompleto). <see cref="AsBefore"/> = arquivo antigo,
/// sem zero escolhido: origem do desenho e Z0 nos níveis digitados, programa idêntico ao de antes (sem linha de cabeçalho).
/// </summary>
public sealed record JobZero(ZeroPoint? Point, ZeroLevel? Level)
{
    /// <summary>Arquivo antigo (versão menor que 3 sem o campo, ou marcado "comoAntes"): "Origem do desenho (como antes)".</summary>
    public bool AsBefore { get; init; }

    /// <summary>O zero de um arquivo antigo: origem do desenho, Z0 no topo, sem nada novo no programa.</summary>
    public static JobZero Legacy { get; } = new(ZeroPoint.Drawing, ZeroLevel.Top) { AsBefore = true };
}

/// <summary>
/// Regras do zero da peça (UX-04g PR B). O Z digitado nas operações continua medido do topo da peça pronta (referencial interno); o zero
/// escolhido só desloca as operações já geradas, antes do pós-processador: X e Y pelo ponto escolhido, Z pela altura da peça quando o Z0
/// fica na base. Bruto, Assist e o 3D da peça ficam no referencial interno; só o programa e as setas do zero mudam.
/// </summary>
public static class CamJobZero
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Pendência de zero não escolhido.</summary>
    public const string Missing = "Falta o zero da peça";

    /// <summary>Pendência de X e Y do zero não escolhidos.</summary>
    public const string PointMissing = "Falta o X e Y do zero da peça";

    /// <summary>Pendência de Z0 não escolhido.</summary>
    public const string LevelMissing = "Falta o Z0 do zero da peça (topo ou base)";

    /// <summary>Pendência de Z0 na base sem a altura da peça.</summary>
    public const string HeightMissing = "Zero com Z0 na base: falta a altura da peça";

    /// <summary>Pendência de zero fora da origem com operação que gira a peça.</summary>
    public const string RotaryBlocked = "Zero da peça: com face girada, texto no cilindro ou 3+2 só vale \"Origem do desenho\" com Z0 no topo";

    /// <summary>Motivo mostrado no cartão quando o zero fica preso na origem.</summary>
    public const string RotaryReason = "Com face girada, texto no cilindro ou 3+2, só vale \"Origem do desenho\". Os pivôs do perfil são medidos a partir do zero da peça.";

    /// <summary>Os 9 pontos na ordem da grade do cartão (linha de cima = Y maior).</summary>
    public static IReadOnlyList<ZeroPoint> Grid { get; } =
    [
        ZeroPoint.XMinYMax, ZeroPoint.XMidYMax, ZeroPoint.XMaxYMax,
        ZeroPoint.XMinYMid, ZeroPoint.Center, ZeroPoint.XMaxYMid,
        ZeroPoint.XMinYMin, ZeroPoint.XMidYMin, ZeroPoint.XMaxYMin,
    ];

    /// <summary>Nome do ponto como na tela.</summary>
    public static string PointName(ZeroPoint p) => p switch
    {
        ZeroPoint.Drawing => "origem do desenho",
        ZeroPoint.XMinYMin => "canto inferior esquerdo",
        ZeroPoint.XMidYMin => "meio da aresta inferior",
        ZeroPoint.XMaxYMin => "canto inferior direito",
        ZeroPoint.XMinYMid => "meio da aresta esquerda",
        ZeroPoint.Center => "centro",
        ZeroPoint.XMaxYMid => "meio da aresta direita",
        ZeroPoint.XMinYMax => "canto superior esquerdo",
        ZeroPoint.XMidYMax => "meio da aresta superior",
        _ => "canto superior direito",
    };

    /// <summary>
    /// Operações que medem pivôs a partir do zero da peça (<see cref="RotaryIndexing"/>): face girada (4º eixo), 3+2 e texto no cilindro.
    /// Só as ligadas contam (desligada não entra no programa). Lista com o nome de cada caso achado.
    /// </summary>
    public static IReadOnlyList<string> RotaryUses(CamJob job)
    {
        var list = new List<string>();
        IEnumerable<CamOperationSpec> on = job.Operations.Where(o => o.Enabled);
        if (on.Any(o => o.Kind != JobOpKind.WrapText && o.Face is { Axis2: null }))
        {
            list.Add("face girada");
        }

        if (on.Any(o => o.Kind == JobOpKind.WrapText))
        {
            list.Add("texto no cilindro");
        }

        if (on.Any(o => o.Kind != JobOpKind.WrapText && o.Face is { Axis2: not null }))
        {
            list.Add("3+2");
        }

        return list;
    }

    /// <summary>O zero só pode ser a origem do desenho (há operação que gira a peça)?</summary>
    public static bool OnlyDrawing(CamJob job) => RotaryUses(job).Count > 0;

    /// <summary>O zero escolhido desloca o programa (ponto fora da origem ou Z0 na base)?</summary>
    private static bool Moves(JobZero z) => z.Point is not (null or ZeroPoint.Drawing) || z.Level == ZeroLevel.Bottom;

    /// <summary>
    /// O que falta no zero (vazio = pronto). Cada linha trava Gerar e Enviar; nenhuma trava Salvar. Nada é trocado sozinho: com operação
    /// que gira a peça e zero fora da origem, o trabalho fica Incompleto até o usuário escolher.
    /// </summary>
    public static IReadOnlyList<string> Problems(CamJob job)
    {
        var list = new List<string>();
        if (job.Zero is not { } z || (z.Point is null && z.Level is null))
        {
            list.Add(Missing);
            return list;
        }

        if (z.Point is null)
        {
            list.Add(PointMissing);
        }

        if (z.Level is null)
        {
            list.Add(LevelMissing);
        }
        else if (z.Level == ZeroLevel.Bottom)
        {
            if (job.PartHeight is not { } h)
            {
                list.Add(HeightMissing);
            }
            else if (CamJobStock.PartHeightError(h) is { } bad)
            {
                list.Add("Zero com Z0 na base: " + bad);
            }
        }

        if (Moves(z) && OnlyDrawing(job))
        {
            list.Add(RotaryBlocked);
        }

        return list;
    }

    /// <summary>A pendência é do zero (a barra leva ao cartão Zero)?</summary>
    public static bool IsZeroProblem(string pending) =>
        pending is Missing or PointMissing or LevelMissing or HeightMissing or RotaryBlocked || pending.StartsWith("Zero com Z0 na base: ", StringComparison.Ordinal);

    /// <summary>
    /// Onde fica o zero no referencial interno (X e Y do desenho, Z medido do topo da peça pronta). Null se o zero está incompleto, ou se o
    /// ponto precisa da caixa da peça e não há desenho.
    /// </summary>
    public static Point3? Position(CamJob job)
    {
        if (Problems(job).Count > 0 || job.Zero is not { Point: { } p, Level: { } level })
        {
            return null;
        }

        double z = level == ZeroLevel.Bottom ? -job.PartHeight!.Value : 0;
        if (p == ZeroPoint.Drawing)
        {
            return new Point3(0, 0, z);
        }

        if (CamJobInfo.DrawingBox(job.Contours) is not { } b)
        {
            return null;
        }

        (int ix, int iy) = Cell(p);
        return new Point3(b.X + b.Width * ix / 2, b.Y + b.Length * iy / 2, z);
    }

    // coluna (0 = X menor, 2 = X maior) e linha (0 = Y menor, 2 = Y maior) do ponto na caixa
    private static (int X, int Y) Cell(ZeroPoint p) => p switch
    {
        ZeroPoint.XMinYMin => (0, 0),
        ZeroPoint.XMidYMin => (1, 0),
        ZeroPoint.XMaxYMin => (2, 0),
        ZeroPoint.XMinYMid => (0, 1),
        ZeroPoint.Center => (1, 1),
        ZeroPoint.XMaxYMid => (2, 1),
        ZeroPoint.XMinYMax => (0, 2),
        ZeroPoint.XMidYMax => (1, 2),
        _ => (2, 2),
    };

    /// <summary>
    /// Desloca as operações geradas para o zero escolhido: cada coordenada perde a posição do zero (X − x0, Y − y0, Z − z0). Origem do
    /// desenho com Z0 no topo devolve a mesma lista (sem translação).
    /// </summary>
    public static IReadOnlyList<CamOperation> Translate(IReadOnlyList<CamOperation> ops, Point3 zero)
    {
        if (zero.X == 0 && zero.Y == 0 && zero.Z == 0)
        {
            return ops;
        }

        double dx = zero.X, dy = zero.Y, dz = zero.Z;
        return ops.Select(op => op with
        {
            Moves = op.Moves.Select(m => m with
            {
                X = m.X - dx,
                Y = m.Y - dy,
                Z = m.Z - dz,
                Center = m.IsArc ? new Point2(m.Center.X - dx, m.Center.Y - dy) : m.Center,
            }).ToList(),
            Drill = op.Drill is { } d ? d with
            {
                Points = d.Points.Select(p => new Point2(p.X - dx, p.Y - dy)).ToList(),
                TopZ = d.TopZ - dz,
                BottomZ = d.BottomZ - dz,
                RetractZ = d.RetractZ - dz,
                SafeZ = d.SafeZ - dz,
            } : null,
        }).ToList();
    }

    /// <summary>
    /// Linha de comentário do cabeçalho (sem acento, como o resto do pós), só quando o zero foi escolhido pelo jeito novo. Arquivo antigo
    /// ("como antes") ou zero incompleto: null (nenhuma linha).
    /// </summary>
    public static string? HeaderNote(CamJob job)
    {
        if (job.Zero is not { AsBefore: false, Point: { } p, Level: { } level } || job.WorkOffset is not { } g)
        {
            return null;
        }

        string where = p == ZeroPoint.Drawing ? "ORIGEM DO DESENHO" : PointName(p).ToUpperInvariant() + " DA PECA";
        return $"ZERO G{g.ToString(CultureInfo.InvariantCulture)}: {where}, Z0 {(level == ZeroLevel.Top ? "NO TOPO" : "NA BASE")}";
    }

    /// <summary>Resumo do cartão: "Zero em X 50 · Y 40 do desenho, Z0 no topo"; arquivo antigo: "Origem do desenho (como antes)".</summary>
    public static string Summary(CamJob job)
    {
        if (job.Zero is { AsBefore: true })
        {
            return "Origem do desenho (como antes)";
        }

        if (job.Zero is not { Point: { } p, Level: { } level })
        {
            return "Zero da peça: falta escolher";
        }

        string z = level == ZeroLevel.Top ? "Z0 no topo" : "Z0 na base";
        if (p == ZeroPoint.Drawing)
        {
            return $"Zero na origem do desenho (X 0 · Y 0), {z}";
        }

        return CamJobInfo.DrawingBox(job.Contours) is { } b
            ? $"Zero em X {N(b.X + b.Width * Cell(p).X / 2)} · Y {N(b.Y + b.Length * Cell(p).Y / 2)} do desenho, {z}"
            : $"Zero no {PointName(p)} da peça (falta o desenho), {z}";
    }

    /// <summary>Texto curto do rodapé e da árvore: "zero: centro, Z0 topo" ou "zero: falta".</summary>
    public static string ShortText(CamJob job) => job.Zero switch
    {
        { AsBefore: true } => "zero: origem (como antes)",
        { Point: { } p, Level: { } l } => $"zero: {PointName(p)}, Z0 {(l == ZeroLevel.Top ? "topo" : "base")}",
        _ => "zero: falta",
    };

    /// <summary>
    /// Precisa da versão 3 do arquivo: zero vazio (a v3 sem o campo é lida como vazio) ou escolhido pelo jeito novo (o app antigo ignoraria o
    /// campo e geraria sem a translação). "Como antes" não pede versão nova.
    /// </summary>
    public static bool NeedsVersion3(CamJob job) => job.Zero is not { AsBefore: true };

    private static string N(double v) => v.ToString("0.###", Br);
}
