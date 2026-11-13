using CncStudio.Core.Setup;

namespace CncStudio.Core.Cam;

/// <summary>Máquina de um banco de ferramentas: define a faixa de T e os tipos aceitos.</summary>
public enum ToolBankMachine
{
    Mill,
    Lathe,
}

/// <summary>
/// Tipo de ferramenta do banco (UX-04h). É distinto do <see cref="ToolKind"/> do setup: broca de centro, alargador e
/// mandrilhadora existem só para cadastro (não há <see cref="ToolKind"/> para eles). <see cref="BankToolTypes.ToToolKind"/> faz o mapa.
/// </summary>
public enum BankToolType
{
    // ── Fresa ──
    EndMill,
    FaceMill,
    Tap,
    BallEndMill,
    BullNoseEndMill,
    Drill,
    CenterDrill,
    Reamer,
    BoringHead,

    // ── Torno (a broca acima serve aos dois) ──
    TurnExternal,
    TurnInternal,
    PartExternal,
    PartInternal,
    ThreadExternal,
    ThreadInternal,
}

/// <summary>Nomes estáveis no arquivo, mapa para o <see cref="ToolKind"/> e tipos aceitos por máquina.</summary>
public static class BankToolTypes
{
    private static readonly (BankToolType Type, string Code, ToolKind? Kind, bool Mill, bool Lathe)[] Table =
    [
        (BankToolType.EndMill, "topo", ToolKind.FlatEndMill, true, false),
        (BankToolType.FaceMill, "facear", ToolKind.FaceMill, true, false),
        (BankToolType.Tap, "macho", ToolKind.Tap, true, false),
        (BankToolType.BallEndMill, "esferica", ToolKind.BallEndMill, true, false),
        (BankToolType.BullNoseEndMill, "toroidal", ToolKind.BullNoseEndMill, true, false),
        (BankToolType.Drill, "broca", ToolKind.Drill, true, true),
        (BankToolType.CenterDrill, "broca-centro", null, true, false),
        (BankToolType.Reamer, "alargador", null, true, false),
        (BankToolType.BoringHead, "mandrilhadora", null, true, false),
        (BankToolType.TurnExternal, "desbaste-externo", ToolKind.TurnExternal, false, true),
        (BankToolType.TurnInternal, "desbaste-interno", ToolKind.TurnInternal, false, true),
        (BankToolType.PartExternal, "bedame-externo", ToolKind.PartExternal, false, true),
        (BankToolType.PartInternal, "bedame-interno", ToolKind.PartInternal, false, true),
        (BankToolType.ThreadExternal, "rosca-externa", ToolKind.ThreadExternal, false, true),
        (BankToolType.ThreadInternal, "rosca-interna", ToolKind.ThreadInternal, false, true),
    ];

    public static string Code(BankToolType type) => Table.First(r => r.Type == type).Code;

    public static BankToolType? FromCode(string code) =>
        Table.FirstOrDefault(r => r.Code == code) is { Code: not null } row ? row.Type : null;

    /// <summary>Tipo do setup correspondente; null = só cadastro (broca de centro, alargador, mandrilhadora).</summary>
    public static ToolKind? ToToolKind(BankToolType type) => Table.First(r => r.Type == type).Kind;

    public static BankToolType FromToolKind(ToolKind kind) => Table.First(r => r.Kind == kind).Type;

    public static bool Accepts(ToolBankMachine machine, BankToolType type) =>
        Table.First(r => r.Type == type) is var row && (machine == ToolBankMachine.Mill ? row.Mill : row.Lathe);

    public static bool IsParting(BankToolType? type) => type is BankToolType.PartExternal or BankToolType.PartInternal;
}

/// <summary>
/// Ferramenta do banco. Só o T é obrigatório; todo o resto pode faltar (null = o usuário não informou, nada é suposto).
/// O Ø pode faltar aqui (a ferramenta fica incompleta); a exigência de Ø é para escolhê-la numa operação.
/// </summary>
public sealed record BankTool(int Number)
{
    public string? Name { get; init; }
    public BankToolType? Type { get; init; }
    public double? Diameter { get; init; }
    public int? Flutes { get; init; }
    public double? CuttingLength { get; init; }
    public double? OverallLength { get; init; }
    public double? HolderDiameter { get; init; }
    public double? HolderLength { get; init; }
    public double? CornerRadius { get; init; }
    public double? TipAngle { get; init; }

    /// <summary>Largura do inserto: só bedames.</summary>
    public double? InsertWidth { get; init; }

    /// <summary>Código ISO do inserto (torno, só cadastro).</summary>
    public string? IsoCode { get; init; }

    /// <summary>Raio de ponta do inserto (torno, só cadastro).</summary>
    public double? NoseRadius { get; init; }

    /// <summary>Material da ferramenta (texto livre: metal duro, HSS...).</summary>
    public string? ToolMaterial { get; init; }

    /// <summary>Dados de corte cadastrados pelo usuário (rpm e mm/min). Nunca sugeridos pelo banco.</summary>
    public double? Rpm { get; init; }
    public double? FeedXY { get; init; }
    public double? FeedZ { get; init; }

    /// <summary>Marca "a conferir": algum valor veio de migração ou importação sem como separar o digitado do padrão.</summary>
    public bool ToCheck { get; init; }

    /// <summary>Ativa (padrão). Inativa = fora de uso (quebrou, aguarda reposição): fica no banco, mantém o T e não é oferecida na operação.</summary>
    public bool Active { get; init; } = true;

    /// <summary>"A conferir" vale mais; senão, incompleta quando falta o tipo, o Ø (fresa e broca) ou a largura do inserto (bedame).</summary>
    public BankToolStatus Status =>
        ToCheck ? BankToolStatus.ToCheck
        : Type is not { } type || (BankToolTypes.IsParting(type) ? InsertWidth is null : Diameter is null && BankToolTypes.Accepts(ToolBankMachine.Mill, type))
            ? BankToolStatus.Incomplete
            : BankToolStatus.Complete;
}

/// <summary>Estado mostrado na tela: completa, incompleta (falta algo para escolher na operação) ou "a conferir".</summary>
public enum BankToolStatus
{
    Complete,
    Incomplete,
    ToCheck,
}

/// <summary>Um banco de ferramentas: id (nome do arquivo), nome livre, máquina e a lista. T único dentro do banco.</summary>
public sealed record ToolBank(string Id, string Name, ToolBankMachine Machine)
{
    public const int MaxIdLength = 64;
    public const int MaxNameLength = 100;

    public IReadOnlyList<BankTool> Tools { get; init; } = [];

    public static int MaxNumber(ToolBankMachine machine) => machine == ToolBankMachine.Mill ? 9999 : 99;

    /// <summary>Id novo para um banco criado pelo usuário (só letras minúsculas e números, serve de nome de arquivo).</summary>
    public static string NewId() => Guid.NewGuid().ToString("N");

    /// <summary>Id aceito: 1 a 64 caracteres entre a-z, 0-9 e hífen (vira o nome do arquivo).</summary>
    public static bool IsValidId(string? id) =>
        !string.IsNullOrEmpty(id) && id.Length <= MaxIdLength && id.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-');

    /// <summary>Cópia com a ferramenta: substitui a de mesmo T no lugar dela, ou entra no fim.</summary>
    public ToolBank With(BankTool tool) => this with
    {
        Tools = Tools.Any(t => t.Number == tool.Number) ? [.. Tools.Select(t => t.Number == tool.Number ? tool : t)] : [.. Tools, tool],
    };

    public ToolBank Without(int number) => this with { Tools = [.. Tools.Where(t => t.Number != number)] };

    /// <summary>Próximo T livre depois de <paramref name="after"/> (volta ao 1 no fim da faixa); null = banco cheio.</summary>
    public int? NextFreeNumber(int after)
    {
        int max = MaxNumber(Machine);
        var used = Tools.Select(t => t.Number).ToHashSet();
        for (int k = 1; k <= max; k++)
        {
            int n = ((Math.Clamp(after, 0, max) + k - 1) % max) + 1;
            if (!used.Contains(n))
            {
                return n;
            }
        }

        return null;
    }

    /// <summary>
    /// Duplicar: cópia de todos os campos (ausentes continuam ausentes, "a conferir" vai junto) no próximo T livre, com "(cópia)"
    /// no nome; a cópia nasce ativa. Sem T livre na faixa, devolve o motivo.
    /// </summary>
    public (ToolBank? Bank, BankTool? Copy, string? Error) Duplicate(int number)
    {
        if (Tools.FirstOrDefault(t => t.Number == number) is not { } source)
        {
            return (null, null, $"T{number} não existe neste banco.");
        }

        if (NextFreeNumber(number) is not { } free)
        {
            return (null, null, $"Não há T livre neste banco (vai de 1 a {MaxNumber(Machine)}).");
        }

        const string suffix = " (cópia)";
        string? name = source.Name is { } n ? n[..Math.Min(n.Length, MaxNameLength - suffix.Length)] + suffix : null;
        BankTool copy = source with { Number = free, Name = name, Active = true };
        return (With(copy), copy, null);
    }

    /// <summary>Motivo de não aceitar <paramref name="tool"/> no banco (null = aceita). <paramref name="replacing"/>: T que ela substitui.</summary>
    public string? CanAdd(BankTool tool, int? replacing = null) =>
        Tools.Any(t => t.Number == tool.Number && t.Number != replacing)
            ? $"T{tool.Number} já existe neste banco."
            : ToolError(Machine, tool);

    /// <summary>Null quando o banco inteiro é válido; senão, o primeiro problema (em português).</summary>
    public string? Validate()
    {
        if (!IsValidId(Id))
        {
            return "Identificador do banco inválido.";
        }

        if (string.IsNullOrWhiteSpace(Name) || Name.Length > MaxNameLength)
        {
            return $"O nome do banco precisa ter de 1 a {MaxNameLength} caracteres.";
        }

        var seen = new HashSet<int>();
        foreach (BankTool tool in Tools)
        {
            if (!seen.Add(tool.Number))
            {
                return $"T{tool.Number} repetido no banco.";
            }

            if (ToolError(Machine, tool) is { } error)
            {
                return error;
            }
        }

        return null;
    }

    /// <summary>Regras de uma ferramenta: faixa de T da máquina, tipo da máquina e medidas válidas quando informadas.</summary>
    public static string? ToolError(ToolBankMachine machine, BankTool tool)
    {
        int max = MaxNumber(machine);
        string where = machine == ToolBankMachine.Mill ? "fresa" : "torno";
        if (tool.Number < 1 || tool.Number > max)
        {
            return $"T{tool.Number}: no banco de {where} o T vai de 1 a {max}.";
        }

        string t = $"T{tool.Number}";
        if (tool.Type is { } type && (!Enum.IsDefined(type) || !BankToolTypes.Accepts(machine, type)))
        {
            return $"{t}: este tipo não é de {where}.";
        }

        if ((tool.Name?.Length ?? 0) > MaxNameLength || (tool.IsoCode?.Length ?? 0) > 40 || (tool.ToolMaterial?.Length ?? 0) > 60)
        {
            return $"{t}: texto longo demais.";
        }

        if (!Positive(tool.Diameter, tool.CuttingLength, tool.OverallLength, tool.HolderDiameter, tool.HolderLength,
                tool.InsertWidth, tool.Rpm, tool.FeedXY, tool.FeedZ))
        {
            return $"{t}: as medidas e os dados de corte precisam ser maiores que zero.";
        }

        if (tool.Flutes is < 1 or > 100)
        {
            return $"{t}: o número de arestas vai de 1 a 100.";
        }

        if (tool.CornerRadius is { } r && (!double.IsFinite(r) || r < 0 || (tool.Diameter is { } d && r > d / 2)))
        {
            return $"{t}: o raio de canto vai de 0 até metade do diâmetro.";
        }

        if (tool.TipAngle is { } a && (!double.IsFinite(a) || a <= 0 || a >= 180))
        {
            return $"{t}: o ângulo da ponta precisa estar entre 0° e 180°.";
        }

        if (tool.NoseRadius is { } nr && (!double.IsFinite(nr) || nr < 0 || nr > 50))
        {
            return $"{t}: o raio de ponta vai de 0 a 50 mm.";
        }

        if (tool.InsertWidth is not null && tool.Type is not null && !BankToolTypes.IsParting(tool.Type))
        {
            return $"{t}: a largura do inserto só vale para bedames.";
        }

        if (machine == ToolBankMachine.Mill && (tool.IsoCode is not null || tool.NoseRadius is not null))
        {
            return $"{t}: código ISO e raio de ponta são só do torno.";
        }

        return null;
    }

    private static bool Positive(params double?[] values) =>
        values.All(v => v is null || (double.IsFinite(v.Value) && v.Value > 0 && v.Value <= 100_000));
}
