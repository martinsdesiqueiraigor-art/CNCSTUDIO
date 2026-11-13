namespace CncStudio.Core.Cam;

/// <summary>Um banco lido da pasta. <see cref="ReadOnly"/>: versão mais nova (não se grava por cima).</summary>
public sealed record StoredToolBank(ToolBank Bank, bool ReadOnly = false, string? Warning = null);

/// <summary>Leitura da pasta: os bancos que abriram e o motivo de cada arquivo que não abriu (o App segue sem eles).</summary>
public sealed record ToolBankFolder(IReadOnlyList<StoredToolBank> Banks, IReadOnlyList<string> Problems);

/// <summary>
/// Pasta dos bancos (um <c>&lt;id&gt;.casillas-ferramentas</c> por banco). A pasta é do App (<c>%APPDATA%\CncStudio\ferramentas</c>);
/// aqui só ler, gravar, exportar e a migração única. Nunca lança exceção de disco: devolve o motivo.
/// </summary>
public static class ToolBankStore
{
    public static ToolBankFolder Load(string folder)
    {
        var banks = new List<StoredToolBank>();
        var problems = new List<string>();
        string[] files;
        try
        {
            files = Directory.Exists(folder) ? Directory.GetFiles(folder, "*" + ToolBankJson.Extension) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ToolBankFolder(banks, [$"Não foi possível ler a pasta dos bancos: {ex.Message}"]);
        }

        foreach (string file in files.Order(StringComparer.Ordinal))
        {
            string name = Path.GetFileName(file);
            try
            {
                ToolBankReadResult read = ToolBankJson.Parse(File.ReadAllText(file));
                if (read.Bank is not { } bank)
                {
                    problems.Add($"{name}: {read.Error}");
                }
                else if (ToolBankJson.FileName(bank) != name)
                {
                    problems.Add($"{name}: o nome do arquivo não bate com o identificador do banco ({bank.Id}).");
                }
                else
                {
                    banks.Add(new StoredToolBank(bank, read.ReadOnly, read.Warning));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add($"{name}: não foi possível ler ({ex.Message}).");
            }
        }

        return new ToolBankFolder(banks, problems);
    }

    /// <summary>Grava o banco (arquivo temporário e troca, para uma falha no meio não estragar o anterior). Null = gravou.</summary>
    public static string? Save(string folder, ToolBank bank)
    {
        if (bank.Validate() is { } invalid)
        {
            return invalid;
        }

        string path = Path.Combine(folder, ToolBankJson.FileName(bank));
        try
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(path + ".tmp", ToolBankJson.ToJson(bank));
            File.Move(path + ".tmp", path, true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return $"Não foi possível gravar o banco \"{bank.Name}\": {ex.Message}";
        }
    }

    /// <summary>Exportar = copiar o arquivo do banco como está (inclusive o de versão mais nova). Null = copiou.</summary>
    public static string? Export(string folder, ToolBank bank, string destination)
    {
        try
        {
            File.Copy(Path.Combine(folder, ToolBankJson.FileName(bank)), destination, true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return $"Não foi possível exportar: {ex.Message}";
        }
    }

    /// <summary>
    /// Migração única da tabela antiga: só quando ainda não migrou (<paramref name="alreadyMigrated"/>, guardado pelo App) e não há
    /// nenhum arquivo de banco na pasta. Grava os dois bancos (mesmo vazios). Null = não era para migrar. A tabela não é tocada.
    /// </summary>
    public static (ToolBankMigration Migration, IReadOnlyList<string> SaveErrors)? MigrateOnce(
        string folder, bool alreadyMigrated, IEnumerable<LegacyToolRow> table)
    {
        bool anyFile;
        try
        {
            anyFile = Directory.Exists(folder) && Directory.EnumerateFiles(folder, "*" + ToolBankJson.Extension).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null; // sem ler a pasta, não dá para saber: não migra agora (tenta na próxima abertura)
        }

        if (alreadyMigrated || anyFile)
        {
            return null;
        }

        ToolBankMigration m = ToolBankImport.FromLegacyTable(table);
        string?[] errors = [Save(folder, m.Mill), Save(folder, m.Lathe)];
        return (m, [.. errors.OfType<string>()]);
    }
}
