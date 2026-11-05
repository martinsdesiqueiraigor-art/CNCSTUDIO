namespace CncStudio.Core.Cam;

/// <summary>
/// De onde vieram os valores de uma operação (UX-01, missão 11). Só o Aplicar do Assist grava: operação feita à mão ou
/// arquivo antigo fica sem marca (null). <see cref="CutData"/> diz se rotação e avanços são rascunho da biblioteca
/// ("draft") ou de material revisado ("reviewed"); <see cref="Typed"/> lista o que você digitou ("depth", "heights",
/// "rpm", "feed"). Só registro: nada aqui confere o corte, a fixação ou a colisão.
/// </summary>
public sealed record OpOrigin(string Source, string? CutData, IReadOnlyList<string> Typed)
{
    public const string Assist = "assist";
    public const string Draft = "draft";
    public const string Reviewed = "reviewed";

    /// <summary>Itens aceitos em <see cref="Typed"/> (código guardado no arquivo → nome mostrado), na ordem em que aparecem.</summary>
    public static readonly IReadOnlyList<(string Code, string Name)> TypedKinds =
    [
        ("depth", "profundidade"),
        ("heights", "alturas Z"),
        ("rpm", "rotação"),
        ("feed", "avanço"),
    ];

    /// <summary>Motivo se a marca é inválida (origem, dados de corte ou item digitado desconhecido); null se está boa.</summary>
    public string? Validate()
    {
        if (Source != Assist)
        {
            return $"origem \"{Source}\" desconhecida";
        }

        if (CutData is not null && CutData != Draft && CutData != Reviewed)
        {
            return $"dados de corte \"{CutData}\" desconhecidos";
        }

        return Typed.FirstOrDefault(t => TypedKinds.All(k => k.Code != t)) is { } bad ? $"valor digitado \"{bad}\" desconhecido" : null;
    }

    /// <summary>Acrescenta itens digitados (sem repetir, na ordem de <see cref="TypedKinds"/>).</summary>
    public OpOrigin WithTyped(params string[] codes) =>
        this with { Typed = TypedKinds.Select(k => k.Code).Where(c => Typed.Contains(c) || codes.Contains(c)).ToList() };

    /// <summary>
    /// Texto da marca: "do Assist · dados de corte: rascunho · digitado: profundidade, alturas Z". Estado "pending" (âmbar)
    /// enquanto os dados de corte forem rascunho; senão "neutral" (informado, não verificado). Null sem marca.
    /// </summary>
    public static (string Kind, string Text)? Describe(OpOrigin? o)
    {
        if (o is null)
        {
            return null;
        }

        var parts = new List<string> { "do Assist" };
        if (o.CutData == Draft)
        {
            parts.Add("dados de corte: rascunho");
        }

        if (o.Typed.Count > 0)
        {
            parts.Add("digitado: " + string.Join(", ", TypedKinds.Where(k => o.Typed.Contains(k.Code)).Select(k => k.Name)));
        }

        return (o.CutData == Draft ? "pending" : "neutral", string.Join(" · ", parts));
    }

    /// <summary>
    /// Depois de editar a operação no Trabalho (decisão do Igor: "vira digitado"): mudar rotação ou avanço tira o
    /// "rascunho" e marca o que mudou como digitado; mudar fundo ou passe marca a profundidade; mudar Z seguro,
    /// aproximação ou topo marca as alturas. Operação sem marca continua sem marca.
    /// </summary>
    public static CamOperationSpec Track(CamOperationSpec before, CamOperationSpec after)
    {
        if (after.Origin is not { } o)
        {
            return after;
        }

        var typed = new List<string>();
        bool rpm = after.Tool.SpindleRpm != before.Tool.SpindleRpm;
        bool feed = after.Tool.FeedXY != before.Tool.FeedXY || after.Tool.FeedZ != before.Tool.FeedZ;
        if (rpm)
        {
            typed.Add("rpm");
        }

        if (feed)
        {
            typed.Add("feed");
        }

        CamLevels a = after.Levels, b = before.Levels;
        if (a.BottomZ != b.BottomZ || a.StepDown != b.StepDown)
        {
            typed.Add("depth");
        }

        if (a.SafeZ != b.SafeZ || a.ClearanceZ != b.ClearanceZ || a.TopZ != b.TopZ)
        {
            typed.Add("heights");
        }

        if (typed.Count == 0)
        {
            return after;
        }

        OpOrigin next = o.WithTyped([.. typed]);
        return after with { Origin = rpm || feed ? next with { CutData = null } : next };
    }
}
