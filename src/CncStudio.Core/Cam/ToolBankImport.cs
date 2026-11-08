using CncStudio.Core.Setup;

namespace CncStudio.Core.Cam;

/// <summary>
/// Uma linha da tabela de ferramentas de hoje (o <c>ToolEntry</c> do App), sem depender do App: o App preenche no PR B1.
/// <paramref name="Kind"/> é o nome do <see cref="ToolKind"/> em texto ("" = não escolhido).
/// </summary>
public sealed record LegacyToolRow(int Number, double Diameter, string Description, string Kind, double InsertWidth, double Rpm, double FeedXY, double FeedZ);

/// <summary>Resultado da migração ou da leitura da web: o que entrou e os avisos do que ficou de fora.</summary>
public sealed record ToolBankMigration(ToolBank Mill, ToolBank Lathe, IReadOnlyList<string> Warnings);

/// <summary>Importar um arquivo de banco, migrar a tabela de hoje e ler as "minhas ferramentas" da web. Tudo puro, sem disco.</summary>
public static class ToolBankImport
{
    public const string MillBankId = "ferramentas-fresa";
    public const string LatheBankId = "ferramentas-torno";
    public const string MillBankName = "Ferramentas fresa";
    public const string LatheBankName = "Ferramentas torno";

    /// <summary>Ø que a tabela antiga e o editor da web preenchiam sozinhos: igual a ele, entra "a conferir".</summary>
    public const double OldDefaultDiameter = 10;

    /// <summary>
    /// Importa um arquivo: valida e, se o id ou o nome já existem, vira cópia (id "-2", "-3"... e nome "(cópia)"), sem sobrescrever.
    /// Versão mais nova entra só para leitura e só sem conflito (a cópia exigiria regravar o arquivo e perder o que esta versão não conhece).
    /// </summary>
    public static ToolBankReadResult Import(string? text, IEnumerable<ToolBank> existing)
    {
        ToolBankReadResult read = ToolBankJson.Parse(text);
        if (read.Bank is not { } bank)
        {
            return read;
        }

        var banks = existing.ToList();
        bool idTaken = banks.Any(b => b.Id == bank.Id);
        bool nameTaken = banks.Any(b => SameName(b.Name, bank.Name));
        if (!idTaken && !nameTaken)
        {
            return read;
        }

        if (read.ReadOnly)
        {
            return new ToolBankReadResult(null, "Este banco é de uma versão mais nova e já existe um banco com o mesmo nome ou identificador: atualize o Casillas Code para importar.");
        }

        string id = bank.Id;
        for (int i = 2; banks.Any(b => b.Id == id); i++)
        {
            string suffix = "-" + i;
            id = bank.Id[..Math.Min(bank.Id.Length, ToolBank.MaxIdLength - suffix.Length)] + suffix;
        }

        string name = bank.Name;
        for (int i = 1; banks.Any(b => SameName(b.Name, name)); i++)
        {
            string suffix = i == 1 ? " (cópia)" : $" (cópia {i})";
            name = bank.Name[..Math.Min(bank.Name.Length, ToolBank.MaxNameLength - suffix.Length)] + suffix;
        }

        return new ToolBankReadResult(bank with { Id = id, Name = name }, null);
    }

    private static bool SameName(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.CurrentCultureIgnoreCase);

    /// <summary>
    /// Migra a tabela de hoje para dois bancos (fresa e torno). Campo a campo: tipo "" fica ausente; tipo de torno vai ao banco do torno;
    /// Ø igual a 10 entra "a conferir"; rpm e avanços 0 ficam ausentes; largura do inserto só nos bedames. A lista de entrada não muda.
    /// T fora da faixa do banco não entra (com aviso); T repetido na tabela: vale a última linha, como no simulador (com aviso).
    /// </summary>
    public static ToolBankMigration FromLegacyTable(IEnumerable<LegacyToolRow> rows)
    {
        var mill = new ToolBank(MillBankId, MillBankName, ToolBankMachine.Mill);
        var lathe = new ToolBank(LatheBankId, LatheBankName, ToolBankMachine.Lathe);
        var warnings = new List<string>();
        foreach (LegacyToolRow row in rows)
        {
            BankToolType? type = null;
            if (!string.IsNullOrEmpty(row.Kind))
            {
                if (Enum.TryParse(row.Kind, false, out ToolKind kind) && Enum.IsDefined(kind))
                {
                    type = BankToolTypes.FromToolKind(kind);
                }
                else
                {
                    warnings.Add($"T{row.Number}: tipo \"{row.Kind}\" desconhecido; ficou sem tipo.");
                }
            }

            bool toLathe = type is { } t && !BankToolTypes.Accepts(ToolBankMachine.Mill, t);
            var tool = new BankTool(row.Number)
            {
                Name = string.IsNullOrWhiteSpace(row.Description) ? null : row.Description.Trim(),
                Type = type,
                Diameter = Given(row.Diameter),
                InsertWidth = BankToolTypes.IsParting(type) ? Given(row.InsertWidth) : null,
                Rpm = Given(row.Rpm),
                FeedXY = Given(row.FeedXY),
                FeedZ = Given(row.FeedZ),
                ToCheck = row.Diameter == OldDefaultDiameter,
            };
            if (toLathe)
            {
                lathe = Add(lathe, tool, warnings);
            }
            else
            {
                mill = Add(mill, tool, warnings);
            }
        }

        return new ToolBankMigration(mill, lathe, warnings);
    }

    /// <summary>
    /// Lê as "minhas ferramentas" da web (<c>cc.mytools.v1</c>: o JSON do setup só com a lista, no máximo 20). O editor web preenche
    /// Ø 10, ângulo 118, comprimento de corte 25, suporte Ø 35 × 45 e inserto 3 sem dar para separar do digitado: esses valores entram
    /// ausentes, e o Ø 10 entra "a conferir". T, tipo e raio de canto entram como vieram; a web não tem nome. Broca vai ao banco de fresa.
    /// </summary>
    public static (ToolBankMigration? Result, string? Error) FromWebMyTools(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (null, "Não há ferramentas para importar.");
        }

        SetupReadResult read = SetupJson.Parse(json);
        if (read.Setup is not { } setup)
        {
            return (null, read.Warning == SetupJson.FutureWarning
                ? "As ferramentas da web são de uma versão mais nova do Casillas Code."
                : $"A lista de ferramentas da web está estragada ou passa de {MachiningSetup.MaxTools} ferramentas.");
        }

        var mill = new ToolBank(MillBankId, MillBankName, ToolBankMachine.Mill);
        var lathe = new ToolBank(LatheBankId, LatheBankName, ToolBankMachine.Lathe);
        var warnings = new List<string>();
        foreach (SetupTool s in setup.Tools)
        {
            BankToolType type = BankToolTypes.FromToolKind(s.Kind);
            var tool = new BankTool(s.Number)
            {
                Type = type,
                Diameter = s.Diameter,
                CornerRadius = s.CornerRadius,
                TipAngle = NotDefault(s.TipAngle, SetupTool.DefaultDrillTipAngle),
                CuttingLength = NotDefault(s.CuttingLength, 25),
                HolderDiameter = NotDefault(s.HolderDiameter, 35),
                HolderLength = NotDefault(s.HolderLength, 45),
                InsertWidth = BankToolTypes.IsParting(type) ? NotDefault(s.InsertWidth, SetupTool.DefaultInsertWidth) : null,
                ToCheck = s.Diameter == OldDefaultDiameter,
            };
            if (s.IsLathe)
            {
                lathe = Add(lathe, tool, warnings);
            }
            else
            {
                mill = Add(mill, tool, warnings);
            }
        }

        return (new ToolBankMigration(mill, lathe, warnings), null);
    }

    private static ToolBank Add(ToolBank bank, BankTool tool, List<string> warnings)
    {
        if (ToolBank.ToolError(bank.Machine, tool) is { } error)
        {
            warnings.Add(error + " Não entrou no banco.");
            return bank;
        }

        if (bank.Tools.Any(t => t.Number == tool.Number))
        {
            warnings.Add($"T{tool.Number} aparece mais de uma vez; ficou a última linha.");
        }

        return bank.With(tool);
    }

    /// <summary>0 (ou inválido) = não informado.</summary>
    private static double? Given(double value) => double.IsFinite(value) && value > 0 ? value : null;

    private static double? NotDefault(double value, double webDefault) => value == webDefault ? null : Given(value);
}
