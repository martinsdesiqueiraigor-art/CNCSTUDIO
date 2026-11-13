using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

/// <summary>UX-04h PR B1: ativa/inativa, duplicar e estado da ferramenta.</summary>
public class ToolBankB1Tests
{
    private static ToolBank Mill(params BankTool[] tools) => new("banco-a", "Banco A", ToolBankMachine.Mill) { Tools = tools };

    // ── Ativa / inativa ──

    [Fact]
    public void Inativa_vai_e_volta_no_arquivo_e_ausente_e_ativa()
    {
        ToolBank bank = Mill(new BankTool(1) { Diameter = 6 }, new BankTool(2) { Diameter = 8, Active = false });
        string json = ToolBankJson.ToJson(bank);
        Assert.Equal(2, json.Split("\"active\"").Length); // só a inativa grava a chave
        Assert.Contains("\"active\": false", json);

        ToolBank back = ToolBankJson.Parse(json).Bank!;
        Assert.Equal(bank.Tools, back.Tools);
        Assert.True(back.Tools[0].Active);
        Assert.False(back.Tools[1].Active);
        Assert.Contains("\"version\": 1", json);
    }

    [Fact]
    public void Inativa_mantem_o_T_reservado()
    {
        ToolBank bank = Mill(new BankTool(5) { Diameter = 6, Active = false });
        Assert.Equal("T5 já existe neste banco.", bank.CanAdd(new BankTool(5) { Diameter = 8 }));
        Assert.Equal(6, bank.NextFreeNumber(4));
    }

    // ── Duplicar ──

    [Fact]
    public void Duplicar_vai_ao_proximo_T_livre_e_copia_tudo_inclusive_ausentes_e_a_conferir()
    {
        var source = new BankTool(3) { Name = "Topo 6", Type = BankToolType.EndMill, Diameter = 6, Rpm = 9000, ToCheck = true, Active = false };
        ToolBank bank = Mill(source, new BankTool(4) { Diameter = 4 });

        (ToolBank? after, BankTool? copy, string? error) = bank.Duplicate(3);

        Assert.Null(error);
        Assert.Equal(source with { Number = 5, Name = "Topo 6 (cópia)", Active = true }, copy);
        Assert.Null(copy!.CuttingLength);
        Assert.Equal(3, after!.Tools.Count);
        Assert.Null(after.Validate());
        Assert.Null(Mill(new BankTool(9)).Duplicate(9).Copy!.Name);
    }

    [Fact]
    public void Duplicar_sem_T_livre_e_recusado_com_o_motivo()
    {
        var lathe = new ToolBank("t", "Torno", ToolBankMachine.Lathe) { Tools = [.. Enumerable.Range(1, 99).Select(n => new BankTool(n))] };
        (ToolBank? after, _, string? error) = lathe.Duplicate(10);
        Assert.Null(after);
        Assert.Contains("1 a 99", error);

        var almost = new ToolBank("t", "Torno", ToolBankMachine.Lathe) { Tools = [.. Enumerable.Range(2, 98).Select(n => new BankTool(n))] };
        Assert.Equal(1, almost.Duplicate(99).Copy!.Number); // volta ao começo da faixa
    }

    // ── Estado ──

    [Fact]
    public void Estado_completa_incompleta_e_a_conferir()
    {
        Assert.Equal(BankToolStatus.Complete, new BankTool(1) { Type = BankToolType.EndMill, Diameter = 6 }.Status);
        Assert.Equal(BankToolStatus.Incomplete, new BankTool(1) { Type = BankToolType.EndMill }.Status);
        Assert.Equal(BankToolStatus.Incomplete, new BankTool(1) { Diameter = 6 }.Status);
        Assert.Equal(BankToolStatus.Incomplete, new BankTool(1) { Type = BankToolType.PartExternal }.Status);
        Assert.Equal(BankToolStatus.Complete, new BankTool(1) { Type = BankToolType.PartExternal, InsertWidth = 3 }.Status);
        Assert.Equal(BankToolStatus.Complete, new BankTool(1) { Type = BankToolType.TurnExternal }.Status);
        Assert.Equal(BankToolStatus.ToCheck, new BankTool(1) { Type = BankToolType.EndMill, Diameter = 10, ToCheck = true }.Status);
    }
}

/// <summary>UX-04h PR B1: pasta dos bancos e migração única (em pasta temporária).</summary>
public sealed class ToolBankStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "casillas-banco-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, true);
        }
    }

    private static ToolBank Bank(string id = "banco-a") =>
        new(id, "Banco " + id, ToolBankMachine.Mill) { Tools = [new BankTool(1) { Diameter = 6, Active = false }, new BankTool(7) { Name = "Broca" }] };

    private static LegacyToolRow Row(int n, double d, string kind = "") => new(n, d, "T" + n, kind, 3, 0, 0, 0);

    [Fact]
    public void Grava_e_le_de_volta()
    {
        Assert.Null(ToolBankStore.Save(_dir, Bank()));
        ToolBankFolder folder = ToolBankStore.Load(_dir);
        Assert.Empty(folder.Problems);
        ToolBank back = folder.Banks.Single().Bank;
        Assert.Equal(("banco-a", "Banco banco-a", ToolBankMachine.Mill), (back.Id, back.Name, back.Machine));
        Assert.Equal(Bank().Tools, back.Tools);
        Assert.False(File.Exists(Path.Combine(_dir, "banco-a.casillas-ferramentas.tmp")));
    }

    [Fact]
    public void Falha_de_disco_diz_o_motivo_e_nao_estraga_o_anterior()
    {
        Assert.Null(ToolBankStore.Save(_dir, Bank()));
        string path = Path.Combine(_dir, "banco-a.casillas-ferramentas");
        string before = File.ReadAllText(path);
        Directory.CreateDirectory(path + ".tmp"); // o temporário vira pasta: a gravação falha

        string? error = ToolBankStore.Save(_dir, Bank() with { Name = "Outro" });

        Assert.Contains("Não foi possível gravar", error);
        Assert.Equal(before, File.ReadAllText(path));
        Assert.Contains("Não foi possível gravar", ToolBankStore.Save(path, Bank())); // a "pasta" é um arquivo
    }

    [Fact]
    public void Arquivo_invalido_na_pasta_nao_derruba_e_diz_qual()
    {
        Assert.Null(ToolBankStore.Save(_dir, Bank()));
        File.WriteAllText(Path.Combine(_dir, "estragado.casillas-ferramentas"), "{x");
        File.WriteAllText(Path.Combine(_dir, "outro-nome.casillas-ferramentas"), ToolBankJson.ToJson(Bank("banco-b")));
        File.WriteAllText(Path.Combine(_dir, "notas.txt"), "nada");

        ToolBankFolder folder = ToolBankStore.Load(_dir);

        Assert.Equal("banco-a", folder.Banks.Single().Bank.Id);
        Assert.Equal(2, folder.Problems.Count);
        Assert.Contains(folder.Problems, p => p.StartsWith("estragado.casillas-ferramentas: "));
        Assert.Empty(ToolBankStore.Load(Path.Combine(_dir, "nao-existe")).Banks);
    }

    [Fact]
    public void Exportar_copia_o_arquivo()
    {
        Assert.Null(ToolBankStore.Save(_dir, Bank()));
        string dest = Path.Combine(_dir, "copia.json");
        Assert.Null(ToolBankStore.Export(_dir, Bank(), dest));
        Assert.Equal(File.ReadAllText(Path.Combine(_dir, "banco-a.casillas-ferramentas")), File.ReadAllText(dest));
        Assert.NotNull(ToolBankStore.Export(_dir, Bank("sem-arquivo"), dest));
    }

    [Fact]
    public void Migracao_roda_uma_vez_grava_os_dois_bancos_e_nao_toca_a_tabela()
    {
        LegacyToolRow[] table = [Row(1, 10), Row(2, 6), Row(3, 4, "PartExternal"), Row(300, 8, "TurnExternal")];
        LegacyToolRow[] copy = [.. table];

        var result = ToolBankStore.MigrateOnce(_dir, false, table);

        Assert.NotNull(result);
        Assert.Empty(result.Value.SaveErrors);
        Assert.Equal(copy, table);
        Assert.Contains(result.Value.Migration.Warnings, w => w.Contains("T300"));
        ToolBankFolder folder = ToolBankStore.Load(_dir);
        Assert.Equal(["ferramentas-fresa", "ferramentas-torno"], folder.Banks.Select(b => b.Bank.Id));
        Assert.True(folder.Banks[0].Bank.Tools[0].ToCheck); // Ø 10
        Assert.Null(folder.Banks[0].Bank.Tools[0].Type); // tipo vazio fica ausente

        Assert.Null(ToolBankStore.MigrateOnce(_dir, false, table)); // já há banco em disco
    }

    [Fact]
    public void Nao_migra_de_novo_mesmo_se_o_usuario_apagou_todos_os_bancos()
    {
        Assert.Null(ToolBankStore.MigrateOnce(_dir, true, [Row(1, 6)]));
        Assert.False(Directory.Exists(_dir));

        // tabela vazia também cria os dois bancos (vazios): a próxima abertura já vê banco em disco
        Assert.NotNull(ToolBankStore.MigrateOnce(_dir, false, []));
        Assert.Equal(2, ToolBankStore.Load(_dir).Banks.Count);
    }
}
