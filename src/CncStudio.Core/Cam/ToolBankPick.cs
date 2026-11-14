using System.Globalization;

namespace CncStudio.Core.Cam;

/// <summary>Uma ferramenta oferecida na escolha da operação: a do banco e o texto da lista.</summary>
public sealed record BankToolChoice(BankTool Tool, string Label);

/// <summary>Resultado de "Adicionar ao banco": o banco novo e a ferramenta, ou o motivo da recusa e o próximo T livre (se houver).</summary>
public sealed record BankAddResult(ToolBank? Bank, BankTool? Tool, string? Error, int? NextFree);

/// <summary>
/// O Trabalho fresa e o banco ativo (UX-04h B2). O trabalho guarda uma cópia dos valores da ferramenta (<see cref="CamTool"/>); a ligação
/// com o banco é só por T e nome. Nada aqui muda o G-code nem o arquivo do trabalho: escolher copia valores, o aviso só avisa.
/// </summary>
public static class ToolBankPick
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    private static string N(double v) => v.ToString("0.###", Br);

    /// <summary>
    /// Banco ativo da fresa: o de <paramref name="activeId"/> se existir e for de fresa; senão, o mesmo que o simulador usa
    /// (<see cref="ToolBankLookup.Primary"/>). Null = não há banco de fresa.
    /// </summary>
    public static ToolBank? ActiveMillBank(IEnumerable<ToolBank> banks, string? activeId)
    {
        var list = banks.ToList();
        return list.FirstOrDefault(b => b.Id == activeId && b.Machine == ToolBankMachine.Mill) ?? ToolBankLookup.Primary(list, ToolBankMachine.Mill);
    }

    /// <summary>
    /// Banco ativo do torno (UX-04h D1): o de <paramref name="activeId"/> se existir e for de torno; senão, o
    /// <see cref="ToolBankLookup.Primary"/> do torno, o mesmo de antes do D1. Null = não há banco de torno.
    /// </summary>
    public static ToolBank? ActiveLatheBank(IEnumerable<ToolBank> banks, string? activeId)
    {
        var list = banks.ToList();
        return list.FirstOrDefault(b => b.Id == activeId && b.Machine == ToolBankMachine.Lathe) ?? ToolBankLookup.Primary(list, ToolBankMachine.Lathe);
    }

    /// <summary>Nome do tipo na tela ("Fresa de topo"); null = sem tipo.</summary>
    public static string? TypeName(BankToolType? type) => type switch
    {
        BankToolType.EndMill => "Fresa de topo",
        BankToolType.FaceMill => "Fresa de facear",
        BankToolType.Tap => "Macho",
        BankToolType.BallEndMill => "Fresa esférica",
        BankToolType.BullNoseEndMill => "Fresa toroidal",
        BankToolType.Drill => "Broca",
        BankToolType.CenterDrill => "Broca de centro",
        BankToolType.Reamer => "Alargador",
        BankToolType.BoringHead => "Mandrilhadora",
        BankToolType.TurnExternal => "Desbaste externo",
        BankToolType.TurnInternal => "Barra de furar",
        BankToolType.PartExternal => "Bedame externo",
        BankToolType.PartInternal => "Bedame interno",
        BankToolType.ThreadExternal => "Rosca externa",
        BankToolType.ThreadInternal => "Rosca interna",
        null => null,
        _ => type.ToString(),
    };

    /// <summary>Tipo que o gerador não distingue (só cadastro): aparece na escolha com a marca.</summary>
    public static bool RegistryOnly(BankTool t) => t.Type is { } type && BankToolTypes.ToToolKind(type) is null;

    /// <summary>
    /// Ferramentas oferecidas na operação: as ativas do banco, em ordem de T. Inativa não aparece; tipo "só cadastro" aparece com a marca.
    /// Ferramenta sem Ø aparece (o Ø fica vazio na operação, que pede o valor).
    /// </summary>
    public static IReadOnlyList<BankToolChoice> Choices(ToolBank? bank) =>
        bank is null ? [] : [.. bank.Tools.Where(t => t.Active).OrderBy(t => t.Number).Select(t => new BankToolChoice(t, Label(t)))];

    /// <summary>"T3 · Fresa topo Z4 · Ø10 · só cadastro · a conferir". Campo ausente não aparece.</summary>
    public static string Label(BankTool t)
    {
        var parts = new List<string> { "T" + t.Number.ToString(CultureInfo.InvariantCulture) };
        if (!string.IsNullOrWhiteSpace(t.Name))
        {
            parts.Add(t.Name!);
        }
        else if (TypeName(t.Type) is { } type)
        {
            parts.Add(type);
        }

        parts.Add(t.Diameter is { } d ? "Ø" + N(d) : "sem Ø");
        if (RegistryOnly(t))
        {
            parts.Add("só cadastro");
        }

        if (t.ToCheck)
        {
            parts.Add("a conferir");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Texto da escolha no Trabalho do torno (UX-04h D1): "T3 · Desbaste CNMG · inserto 3 · a conferir". O Ø não aparece (a operação de
    /// torno não usa); a largura do inserto só nos bedames. Campo ausente não aparece.
    /// </summary>
    public static string LatheLabel(BankTool t)
    {
        var parts = new List<string> { "T" + t.Number.ToString(CultureInfo.InvariantCulture) };
        if (!string.IsNullOrWhiteSpace(t.Name))
        {
            parts.Add(t.Name!);
        }
        else if (TypeName(t.Type) is { } type)
        {
            parts.Add(type);
        }

        if (BankToolTypes.IsParting(t.Type) && t.InsertWidth is { } w)
        {
            parts.Add("inserto " + N(w));
        }

        if (t.ToCheck)
        {
            parts.Add("a conferir");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Escolher a ferramenta do banco numa operação de torno: copia T e nome (sem nome no banco = vazio), e a rotação só se cadastrada
    /// (senão a operação fica como estava), como a escolha da tabela antiga fazia. Velocidade de corte e rotação máxima são da operação.
    /// </summary>
    public static LatheOperationSpec ApplyLathe(LatheOperationSpec s, BankTool t) =>
        s with { Tool = s.Tool with { Number = t.Number, Name = t.Name ?? "", Rpm = t.Rpm is { } rpm && rpm > 0 ? rpm : s.Tool.Rpm } };

    /// <summary>Nome do campo de avanço no plano conforme a operação (o texto no cilindro chama de superfície).</summary>
    public static string FeedXYField(CamOperationSpec s) => s.Kind == JobOpKind.WrapText ? "Avanço na superfície" : "Avanço no plano";

    public const string FeedZField = "Avanço de descida";

    /// <summary>
    /// Campos de ferramenta de uma operação nova (UX-04h B2): T, Ø, rotação e avanços vazios, nada sugerido. Os números guardados
    /// ficam escondidos atrás da marca de vazio (<see cref="CamOperationSpec.MissingFields"/>) e não valem até o usuário escolher ou digitar.
    /// </summary>
    public static CamOperationSpec EmptyTool(CamOperationSpec s)
    {
        foreach (string f in new[] { CamJobInfo.ToolNumberField, CamJobInfo.DiameterField, CamJobInfo.SpindleField, FeedXYField(s), FeedZField })
        {
            s = CamJobInfo.SetFieldState(s, f, true, false);
        }

        return s with { Tool = s.Tool with { Name = "" } };
    }

    /// <summary>
    /// Escolher a ferramenta do banco na operação: copia T, Ø e nome. Ø ausente no banco fica vazio na operação. Rotação e avanços só
    /// entram se cadastrados; senão a operação fica como estava (vazia continua vazia, nada sugerido).
    /// </summary>
    public static CamOperationSpec Apply(CamOperationSpec s, BankTool t)
    {
        s = CamJobInfo.SetFieldState(s, CamJobInfo.ToolNumberField, false, false);
        s = CamJobInfo.SetFieldState(s, CamJobInfo.DiameterField, t.Diameter is null, false);
        CamTool tool = s.Tool with { Number = t.Number, Diameter = t.Diameter ?? s.Tool.Diameter, Name = t.Name ?? "" };
        if (t.Rpm is { } rpm)
        {
            s = CamJobInfo.SetFieldState(s, CamJobInfo.SpindleField, false, false);
            tool = tool with { SpindleRpm = rpm };
        }

        if (t.FeedXY is { } xy)
        {
            s = CamJobInfo.SetFieldState(s, FeedXYField(s), false, false);
            tool = tool with { FeedXY = xy };
        }

        if (t.FeedZ is { } z)
        {
            s = CamJobInfo.SetFieldState(s, FeedZField, false, false);
            tool = tool with { FeedZ = z };
        }

        return s with { Tool = tool };
    }

    /// <summary>
    /// Aviso de T em conflito com o banco ativo (só avisa, não bloqueia; o valor do trabalho vale). Compara o que a operação usa da
    /// ferramenta: o Ø e o nome (só quando o banco tem o nome). Rotação e avanços não entram: são da operação. Null = sem conflito,
    /// sem banco, ou T/Ø da operação vazios.
    /// </summary>
    public static string? Conflict(CamOperationSpec s, ToolBank? bank)
    {
        if (bank is null || CamJobInfo.Unknown(s, CamJobInfo.ToolNumberField))
        {
            return null;
        }

        CamTool job = s.Tool;
        if (bank.Tools.FirstOrDefault(t => t.Number == job.Number) is not { } b)
        {
            return $"T{job.Number} não está no banco \"{bank.Name}\". O trabalho usa a cópia dele.";
        }

        var diffs = new List<string>();
        if (!CamJobInfo.Unknown(s, CamJobInfo.DiameterField) && !SameDiameter(job, b))
        {
            diffs.Add(b.Diameter is { } d ? $"Ø: trabalho {N(job.Diameter)} × banco {N(d)}" : $"Ø: trabalho {N(job.Diameter)} × banco sem Ø");
        }

        if (!SameName(job, b))
        {
            diffs.Add($"nome: trabalho \"{job.Name}\" × banco \"{b.Name}\"");
        }

        string inactive = b.Active ? "" : " (inativa no banco)";
        return diffs.Count == 0 ? null
            : $"T{job.Number} no banco \"{bank.Name}\"{inactive} é diferente do trabalho: {string.Join("; ", diffs)}. O trabalho usa a cópia dele.";
    }

    /// <summary>Ø do trabalho igual ao do banco (banco sem Ø conta como diferente).</summary>
    private static bool SameDiameter(CamTool job, BankTool b) => b.Diameter is { } d && Math.Abs(d - job.Diameter) <= 1e-6;

    /// <summary>Nome igual (só conta quando o banco tem o nome; espaços nas pontas não contam).</summary>
    private static bool SameName(CamTool job, BankTool b) => string.IsNullOrWhiteSpace(b.Name) || b.Name.Trim() == job.Name.Trim();

    /// <summary>
    /// A ferramenta do trabalho é a do banco, sem conflito: a mesma comparação de <see cref="Conflict"/> (Ø e nome), para uma ferramenta
    /// já gerada (sem campo vazio). Usada pelo pacote do CAM (UX-04h C). Inativa no banco não conta como conflito.
    /// </summary>
    public static bool SameTool(CamTool job, BankTool b) => b.Number == job.Number && SameDiameter(job, b) && SameName(job, b);

    /// <summary>
    /// "Adicionar ao banco" a ferramenta digitada na operação: cria no banco com a marca "a conferir" e copia só os campos com valor
    /// (vazio continua ausente; tipo fica ausente). T já existente: recusa com o motivo e oferece o próximo T livre; sem T livre na
    /// faixa, recusa com o motivo. <paramref name="number"/>: usar outro T (o livre oferecido).
    /// </summary>
    public static BankAddResult AddToBank(ToolBank bank, CamOperationSpec s, int? number = null)
    {
        if (number is null && CamJobInfo.Unknown(s, CamJobInfo.ToolNumberField))
        {
            return new BankAddResult(null, null, "Digite o T da ferramenta antes de adicionar ao banco.", null);
        }

        int t = number ?? s.Tool.Number;
        if (bank.Tools.Any(x => x.Number == t))
        {
            int? free = bank.NextFreeNumber(t);
            string reason = $"T{t} já existe no banco \"{bank.Name}\".";
            return new BankAddResult(null, null,
                free is null ? reason + $" Não há T livre neste banco (vai de 1 a {ToolBank.MaxNumber(bank.Machine)})." : reason, free);
        }

        double? Value(string field, double v) => CamJobInfo.Unknown(s, field) ? null : v;
        string name = s.Tool.Name.Trim();
        var tool = new BankTool(t)
        {
            Name = name.Length == 0 ? null : name[..Math.Min(name.Length, ToolBank.MaxNameLength)],
            Diameter = Value(CamJobInfo.DiameterField, s.Tool.Diameter),
            Rpm = Value(CamJobInfo.SpindleField, s.Tool.SpindleRpm),
            FeedXY = Value(FeedXYField(s), s.Tool.FeedXY),
            FeedZ = Value(FeedZField, s.Tool.FeedZ),
            ToCheck = true,
        };
        if (bank.CanAdd(tool) is { } error)
        {
            return new BankAddResult(null, null, error, null);
        }

        return new BankAddResult(bank.With(tool), tool, null, null);
    }
}
