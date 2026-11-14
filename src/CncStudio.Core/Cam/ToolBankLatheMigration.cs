namespace CncStudio.Core.Cam;

/// <summary>Resultado de juntar a tabela antiga ao banco de torno: o banco novo, o que entrou e os avisos.</summary>
public sealed record LatheTableMerge(ToolBank Bank, IReadOnlyList<BankTool> Added, IReadOnlyList<string> Warnings);

/// <summary>
/// Resultado da migração única do torno (D1). <see cref="Done"/>: pode gravar a marca (nada a fazer, ou o banco gravou).
/// <see cref="Saved"/>: o banco foi gravado (o App relê a pasta). <see cref="Notes"/>: vão para o painel "Avisos da migração".
/// </summary>
public sealed record LatheMigrationOutcome(IReadOnlyList<string> Notes, bool Done, bool Saved);

/// <summary>
/// UX-04h D1: as ferramentas de torno que ficaram só na tabela antiga (o Enviar ao Code do torno gravava lá) entram no banco ativo do
/// torno, uma vez. O T é a posição no magazine, não identifica a ferramenta (Igor, 14/11); a mesma ferramenta é mesmo nome e mesmo tipo.
/// <list type="bullet">
/// <item>T livre no banco: entra, "a conferir".</item>
/// <item>T ocupado pela mesma ferramenta: não entra de novo.</item>
/// <item>T ocupado por outra: entra no próximo T livre, "a conferir", com aviso de posição repetida (opção a do Igor, 14/11).
///   Se ela já está no banco em outra posição (mesmo nome e mesmo tipo), não entra de novo.</item>
/// <item>Banco cheio ou T fora da faixa: não entra, com aviso; continua na tabela antiga (que o D1 não apaga).</item>
/// </list>
/// A tabela é lida de baixo para cima: no T repetido dentro da própria tabela, a última linha fica com o T (como na migração do B1).
/// </summary>
public static class ToolBankLatheMigration
{
    public static LatheTableMerge Merge(ToolBank lathe, IEnumerable<LegacyToolRow> rows)
    {
        var added = new List<BankTool>();
        var warnings = new List<string>();
        ToolBank bank = lathe;
        foreach (LegacyToolRow row in rows.Reverse())
        {
            (BankTool converted, bool toLathe) = ToolBankImport.FromLegacyRow(row, null);
            if (!toLathe)
            {
                continue; // fresa, broca e sem tipo já foram ao banco de fresa no B1
            }

            BankTool tool = converted with { ToCheck = true };
            if (ToolBank.ToolError(ToolBankMachine.Lathe, tool) is { } error)
            {
                warnings.Add($"{error} Veio da tabela antiga e não entrou no banco \"{bank.Name}\"; continua guardada na tabela.");
                continue;
            }

            BankTool? there = bank.Tools.FirstOrDefault(t => t.Number == tool.Number);
            if (there is null)
            {
                bank = bank.With(tool);
                added.Add(tool);
                continue;
            }

            if (Same(there, tool) || bank.Tools.Any(t => Same(t, tool)))
            {
                continue; // a mesma ferramenta já está no banco (no mesmo T ou em outra posição)
            }

            if (bank.NextFreeNumber(tool.Number) is not { } free)
            {
                warnings.Add($"Posição T{tool.Number} repetida: {Describe(tool)} veio da tabela antiga, mas T{tool.Number} no banco \"{bank.Name}\" " +
                    $"já é {Describe(there)} e não há T livre (1 a {ToolBank.MaxNumber(ToolBankMachine.Lathe)}). Não entrou; continua guardada na tabela.");
                continue;
            }

            BankTool moved = tool with { Number = free };
            bank = bank.With(moved);
            added.Add(moved);
            warnings.Add($"Posição T{tool.Number} repetida: {Describe(tool)} veio da tabela antiga como T{tool.Number}, que no banco \"{bank.Name}\" " +
                $"já é {Describe(there)}. Entrou como T{free}, a conferir.");
        }

        return new LatheTableMerge(bank, [.. added.OrderBy(t => t.Number)], warnings);
    }

    /// <summary>
    /// Migração única do torno: só sem a marca (<paramref name="alreadyMigrated"/>, guardada pelo App). Grava o banco ativo do torno
    /// (<see cref="ToolBankPick.ActiveLatheBank"/>) por <paramref name="save"/> (padrão <see cref="ToolBankStore.Save"/>: arquivo
    /// temporário trocado no fim). Sem banco de torno, cria o "Ferramentas torno". Null = não era para migrar.
    /// <see cref="LatheMigrationOutcome.Done"/> falso = não gravou: sem a marca, tenta de novo na próxima abertura.
    /// </summary>
    public static LatheMigrationOutcome? MigrateOnce(
        string folder, bool alreadyMigrated, IEnumerable<LegacyToolRow> table, IReadOnlyList<StoredToolBank> banks, string? activeLatheId,
        Func<string, ToolBank, string?>? save = null)
    {
        if (alreadyMigrated)
        {
            return null;
        }

        var rows = table.ToList();
        if (!rows.Any(r => ToolBankImport.FromLegacyRow(r, null).ToLathe))
        {
            return new LatheMigrationOutcome([], true, false);
        }

        ToolBank? active = ToolBankPick.ActiveLatheBank(banks.Select(b => b.Bank), activeLatheId);
        if (active is not null && banks.First(b => b.Bank.Id == active.Id).ReadOnly)
        {
            return new LatheMigrationOutcome(
                [$"O banco de torno \"{active.Name}\" é de uma versão mais nova (só leitura): as ferramentas de torno da tabela antiga não entraram. Tenta de novo na próxima abertura."],
                false, false);
        }

        ToolBank target = active ?? NewLatheBank(folder);
        LatheTableMerge merge = Merge(target, rows);
        if (merge.Added.Count == 0)
        {
            return new LatheMigrationOutcome(merge.Warnings, true, false);
        }

        if ((save ?? ToolBankStore.Save)(folder, merge.Bank) is { } saveError)
        {
            return new LatheMigrationOutcome([saveError + " As ferramentas de torno da tabela antiga entram na próxima abertura."], false, false);
        }

        int n = merge.Added.Count;
        string list = string.Join(", ", merge.Added.Select(t => "T" + t.Number));
        string summary = n == 1
            ? $"Tabela antiga → banco de torno \"{merge.Bank.Name}\": 1 ferramenta entrou, a conferir ({list})."
            : $"Tabela antiga → banco de torno \"{merge.Bank.Name}\": {n} ferramentas entraram, a conferir ({list}).";
        return new LatheMigrationOutcome([summary, .. merge.Warnings], true, true);
    }

    /// <summary>Mesmo nome (sem diferença de maiúsculas e espaços nas pontas; sem nome = sem nome) e mesmo tipo.</summary>
    public static bool Same(BankTool a, BankTool b) =>
        a.Type == b.Type && string.Equals((a.Name ?? "").Trim(), (b.Name ?? "").Trim(), StringComparison.CurrentCultureIgnoreCase);

    private static string Describe(BankTool t)
    {
        string name = string.IsNullOrWhiteSpace(t.Name) ? "sem nome" : $"\"{t.Name}\"";
        return t.Type is { } type ? $"{name} ({BankToolTypes.Code(type)})" : $"{name} (sem tipo)";
    }

    /// <summary>"Ferramentas torno" novo; se já houver um arquivo com esse id (que não abriu), outro id, para não gravar por cima.</summary>
    private static ToolBank NewLatheBank(string folder)
    {
        bool taken;
        try
        {
            taken = File.Exists(Path.Combine(folder, ToolBankImport.LatheBankId + ToolBankJson.Extension));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            taken = true;
        }

        return new ToolBank(taken ? ToolBank.NewId() : ToolBankImport.LatheBankId, ToolBankImport.LatheBankName, ToolBankMachine.Lathe);
    }
}
