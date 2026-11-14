using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

/// <summary>
/// UX-04h D1: torno no banco ativo. Migração única das ferramentas de torno da tabela antiga (o T é a posição no magazine: mesmo T com
/// outra ferramenta entra no próximo T livre, opção a do Igor, 14/11), banco ativo do torno no Code e a escolha no Trabalho do torno.
/// </summary>
public class ToolBankLatheMigrationTests
{
    private static LegacyToolRow Row(int t, string name, string kind, double diameter = 10, double insert = 3, double rpm = 0) =>
        new(t, diameter, name, kind, insert, rpm, 0, 0);

    private static ToolBank Lathe(string id, string name, params BankTool[] tools) => new(id, name, ToolBankMachine.Lathe) { Tools = tools };

    private static ToolBank Primary(params BankTool[] tools) => Lathe(ToolBankImport.LatheBankId, ToolBankImport.LatheBankName, tools);

    private static string NewFolder() => Path.Combine(Path.GetTempPath(), "casillas-d1-" + Guid.NewGuid().ToString("N"));

    // ── Regra da migração (seção 2 da ficha) ──

    [Fact]
    public void T_livre_entra_a_conferir_com_os_valores_da_tabela()
    {
        LatheTableMerge m = ToolBankLatheMigration.Merge(Primary(), [Row(7, "Bedame 3", "PartExternal", diameter: 12, insert: 2.5, rpm: 800)]);

        BankTool t = Assert.Single(m.Added);
        Assert.Equal(new BankTool(7) { Name = "Bedame 3", Type = BankToolType.PartExternal, Diameter = 12, InsertWidth = 2.5, Rpm = 800, ToCheck = true }, t);
        Assert.Equal(t, Assert.Single(m.Bank.Tools));
        Assert.Empty(m.Warnings);
        Assert.Null(m.Bank.Validate());
    }

    [Fact]
    public void T_igual_com_a_mesma_ferramenta_nao_entra_de_novo()
    {
        var there = new BankTool(3) { Name = "Desbaste CNMG", Type = BankToolType.TurnExternal, Diameter = 25 };
        LatheTableMerge m = ToolBankLatheMigration.Merge(Primary(there), [Row(3, "  desbaste cnmg ", "TurnExternal")]);

        Assert.Empty(m.Added);
        Assert.Empty(m.Warnings);
        Assert.Equal(there, Assert.Single(m.Bank.Tools)); // a do banco fica como estava (sem "a conferir")
    }

    [Fact]
    public void T_igual_com_outra_ferramenta_entra_no_proximo_T_livre_a_conferir_e_avisa()
    {
        var there = new BankTool(5) { Name = "Desbaste CNMG", Type = BankToolType.TurnExternal };
        ToolBank bank = Primary(there, new BankTool(6) { Type = BankToolType.TurnInternal });

        LatheTableMerge m = ToolBankLatheMigration.Merge(bank, [Row(5, "Rosca 60", "ThreadExternal")]);

        BankTool moved = Assert.Single(m.Added);
        Assert.Equal(7, moved.Number);
        Assert.Equal("Rosca 60", moved.Name);
        Assert.True(moved.ToCheck);
        Assert.Equal(there, m.Bank.Tools.First(t => t.Number == 5)); // nenhuma some, a do banco não muda
        Assert.Equal(3, m.Bank.Tools.Count);
        string w = Assert.Single(m.Warnings);
        Assert.Contains("Posição T5 repetida", w);
        Assert.Contains("\"Rosca 60\"", w);
        Assert.Contains("\"Desbaste CNMG\"", w);
        Assert.Contains("Entrou como T7, a conferir.", w);
    }

    [Fact]
    public void Mesmo_nome_e_outro_tipo_e_outra_ferramenta()
    {
        LatheTableMerge m = ToolBankLatheMigration.Merge(
            Primary(new BankTool(2) { Name = "T2", Type = BankToolType.TurnExternal }), [Row(2, "T2", "TurnInternal")]);
        Assert.Equal(3, Assert.Single(m.Added).Number);
    }

    [Fact]
    public void Banco_cheio_nao_entra_avisa_e_nada_do_banco_some()
    {
        ToolBank full = Primary([.. Enumerable.Range(1, 99).Select(n => new BankTool(n) { Type = BankToolType.TurnExternal })]);
        LatheTableMerge m = ToolBankLatheMigration.Merge(full, [Row(4, "Bedame", "PartExternal")]);

        Assert.Empty(m.Added);
        Assert.Equal(99, m.Bank.Tools.Count);
        Assert.Contains("não há T livre", Assert.Single(m.Warnings));
        Assert.Contains("continua guardada na tabela", m.Warnings[0]);
    }

    [Fact]
    public void T_fora_da_faixa_do_torno_nao_entra_e_avisa()
    {
        LatheTableMerge m = ToolBankLatheMigration.Merge(Primary(), [Row(120, "Longe", "TurnExternal")]);
        Assert.Empty(m.Added);
        Assert.Contains("1 a 99", Assert.Single(m.Warnings));
    }

    [Fact]
    public void Linhas_de_fresa_broca_e_sem_tipo_ficam_de_fora()
    {
        LatheTableMerge m = ToolBankLatheMigration.Merge(Primary(), [Row(1, "Topo", "FlatEndMill"), Row(2, "Broca", "Drill"), Row(3, "?", ""), Row(4, "x", "Inventado")]);
        Assert.Empty(m.Added);
        Assert.Empty(m.Warnings);
    }

    [Fact]
    public void T_repetido_na_tabela_a_ultima_linha_fica_com_o_T_e_a_outra_entra_no_livre()
    {
        LatheTableMerge m = ToolBankLatheMigration.Merge(Primary(), [Row(4, "Antiga", "TurnExternal"), Row(4, "Nova", "TurnExternal"), Row(4, "Nova", "TurnExternal")]);

        Assert.Equal(["4:Nova", "5:Antiga"], m.Added.Select(t => $"{t.Number}:{t.Name}"));
        Assert.Single(m.Warnings);
    }

    [Fact]
    public void Ferramenta_ja_movida_antes_nao_entra_de_novo()
    {
        // segunda passada (por exemplo, o config.json não gravou a marca): a que foi para o T7 não entra outra vez
        ToolBank once = ToolBankLatheMigration.Merge(Primary(new BankTool(5) { Name = "A", Type = BankToolType.TurnExternal }), [Row(5, "B", "TurnExternal")]).Bank;
        LatheTableMerge again = ToolBankLatheMigration.Merge(once, [Row(5, "B", "TurnExternal")]);
        Assert.Empty(again.Added);
        Assert.Empty(again.Warnings);
    }

    // ── Migração única: gravação, marca e nova tentativa ──

    [Fact]
    public void Grava_no_banco_ativo_do_torno_e_resume_o_que_entrou()
    {
        string folder = NewFolder();
        try
        {
            ToolBank primary = Primary(new BankTool(1) { Name = "A", Type = BankToolType.TurnExternal });
            ToolBank other = Lathe("oficina", "Torno oficina", new BankTool(1) { Name = "A", Type = BankToolType.TurnExternal });
            Assert.Null(ToolBankStore.Save(folder, primary));
            Assert.Null(ToolBankStore.Save(folder, other));
            StoredToolBank[] banks = [new(primary), new(other)];

            LatheMigrationOutcome r = ToolBankLatheMigration.MigrateOnce(folder, false, [Row(1, "B", "TurnInternal"), Row(9, "C", "PartExternal")], banks, "oficina")!;

            Assert.True(r.Done);
            Assert.True(r.Saved);
            Assert.StartsWith("Tabela antiga → banco de torno \"Torno oficina\": 2 ferramentas entraram, a conferir (T2, T9).", r.Notes[0]);
            Assert.Contains(r.Notes, n => n.Contains("Posição T1 repetida"));
            ToolBankFolder after = ToolBankStore.Load(folder);
            Assert.Empty(after.Problems);
            Assert.Equal(3, after.Banks.Single(b => b.Bank.Id == "oficina").Bank.Tools.Count);
            Assert.Single(after.Banks.Single(b => b.Bank.Id == ToolBankImport.LatheBankId).Bank.Tools); // o outro banco não muda
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Segunda_abertura_nao_repete()
    {
        Assert.Null(ToolBankLatheMigration.MigrateOnce(NewFolder(), true, [Row(1, "B", "TurnInternal")], [], null));
    }

    [Fact]
    public void Sem_linha_de_torno_marca_sem_gravar()
    {
        LatheMigrationOutcome r = ToolBankLatheMigration.MigrateOnce(NewFolder(), false, [Row(1, "Topo", "FlatEndMill")], [], null)!;
        Assert.True(r.Done);
        Assert.False(r.Saved);
        Assert.Empty(r.Notes);
    }

    [Fact]
    public void Sem_banco_de_torno_cria_o_Ferramentas_torno()
    {
        string folder = NewFolder();
        try
        {
            LatheMigrationOutcome r = ToolBankLatheMigration.MigrateOnce(folder, false, [Row(3, "Bedame", "PartExternal")], [], null)!;
            Assert.True(r.Saved);
            StoredToolBank b = Assert.Single(ToolBankStore.Load(folder).Banks);
            Assert.Equal(ToolBankImport.LatheBankId, b.Bank.Id);
            Assert.Equal(3, Assert.Single(b.Bank.Tools).Number);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Falha_ao_gravar_nao_marca_nao_estraga_o_arquivo_e_tenta_de_novo_na_proxima_abertura()
    {
        string folder = NewFolder();
        try
        {
            ToolBank primary = Primary(new BankTool(1) { Name = "A", Type = BankToolType.TurnExternal });
            Assert.Null(ToolBankStore.Save(folder, primary));
            string file = Path.Combine(folder, ToolBankJson.FileName(primary));
            string before = File.ReadAllText(file);
            Directory.CreateDirectory(file + ".tmp"); // o arquivo temporário não pode ser escrito: falha no meio da gravação
            LegacyToolRow[] table = [Row(1, "B", "TurnInternal")];

            LatheMigrationOutcome failed = ToolBankLatheMigration.MigrateOnce(folder, false, table, [new(primary)], null)!;

            Assert.False(failed.Done); // sem a marca
            Assert.False(failed.Saved);
            Assert.Contains("entram na próxima abertura", Assert.Single(failed.Notes));
            Assert.Equal(before, File.ReadAllText(file)); // o banco anterior continua inteiro

            Directory.Delete(file + ".tmp");
            LatheMigrationOutcome retry = ToolBankLatheMigration.MigrateOnce(folder, failed.Done, table, [new(primary)], null)!;
            Assert.True(retry.Done);
            Assert.Equal(2, ToolBankStore.Load(folder).Banks.Single().Bank.Tools.Count);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Banco_de_torno_so_leitura_nao_e_gravado_e_tenta_de_novo()
    {
        ToolBank primary = Primary();
        bool called = false;
        LatheMigrationOutcome r = ToolBankLatheMigration.MigrateOnce(NewFolder(), false, [Row(1, "B", "TurnInternal")], [new(primary, ReadOnly: true)], null,
            (_, _) => { called = true; return null; })!;
        Assert.False(r.Done);
        Assert.False(called);
        Assert.Contains("só leitura", Assert.Single(r.Notes));
    }

    // ── Banco ativo do torno no Code ──

    [Fact]
    public void Banco_ativo_do_torno_sem_escolha_ou_apagado_e_o_principal()
    {
        ToolBank primary = Primary(new BankTool(1) { Type = BankToolType.TurnExternal, Diameter = 20 });
        ToolBank other = Lathe("oficina", "Oficina", new BankTool(1) { Type = BankToolType.PartExternal, Diameter = 30, InsertWidth = 4 });
        var mill = new ToolBank(ToolBankImport.MillBankId, ToolBankImport.MillBankName, ToolBankMachine.Mill) { Tools = [new BankTool(2) { Diameter = 6 }] };
        ToolBank[] banks = [primary, other, mill];

        Assert.Equal(primary.Id, ToolBankPick.ActiveLatheBank(banks, null)!.Id);
        Assert.Equal(primary.Id, ToolBankPick.ActiveLatheBank(banks, "apagado")!.Id);
        Assert.Equal(primary.Id, ToolBankPick.ActiveLatheBank(banks, mill.Id)!.Id); // banco de fresa não vale no torno
        Assert.Equal("oficina", ToolBankPick.ActiveLatheBank(banks, "oficina")!.Id);

        Assert.Equal(20, ToolBankLookup.Diameters(banks)[1]);
        Assert.Equal(30, ToolBankLookup.Diameters(banks, null, "oficina")[1]);
        Assert.Equal(6, ToolBankLookup.Diameters(banks, null, "oficina")[2]);
        var drawn = ToolBankLookup.LatheSetupTools(banks, "oficina").Single();
        Assert.Equal((1, Setup.ToolKind.PartExternal, 30.0, 4.0), (drawn.Number, drawn.Kind, drawn.Diameter, drawn.InsertWidth));
        Assert.Equal(Setup.ToolKind.TurnExternal, ToolBankLookup.LatheSetupTools(banks).Single().Kind);
    }

    [Fact]
    public void Mesmo_T_na_fresa_e_no_torno_a_fresa_continua_valendo_por_cima()
    {
        ToolBank lathe = Lathe("oficina", "Oficina", new BankTool(1) { Type = BankToolType.TurnExternal, Diameter = 30 });
        var mill = new ToolBank(ToolBankImport.MillBankId, ToolBankImport.MillBankName, ToolBankMachine.Mill) { Tools = [new BankTool(1) { Diameter = 6 }] };
        Assert.Equal(6, ToolBankLookup.Diameters([lathe, mill], null, "oficina")[1]);
    }

    // ── Escolha no Trabalho do torno ──

    [Fact]
    public void Escolher_copia_T_nome_e_rpm_so_se_cadastrado()
    {
        var op = new LatheOperationSpec(LatheOpKind.Grooving, new LatheTool(1, "antes", SurfaceSpeed: 180, MaxRpm: 2500, Rpm: 400));

        LatheOperationSpec a = ToolBankPick.ApplyLathe(op, new BankTool(8) { Name = "Bedame 3", Rpm = 900 });
        Assert.Equal(new LatheTool(8, "Bedame 3", 180, 2500, 900), a.Tool);

        LatheOperationSpec b = ToolBankPick.ApplyLathe(op, new BankTool(9));
        Assert.Equal(new LatheTool(9, "", 180, 2500, 400), b.Tool);
    }

    [Fact]
    public void Texto_da_escolha_do_torno()
    {
        Assert.Equal("T3 · Bedame externo · inserto 2,5 · a conferir",
            ToolBankPick.LatheLabel(new BankTool(3) { Type = BankToolType.PartExternal, InsertWidth = 2.5, Diameter = 10, ToCheck = true }));
        Assert.Equal("T4 · Desbaste CNMG", ToolBankPick.LatheLabel(new BankTool(4) { Name = "Desbaste CNMG", Type = BankToolType.TurnExternal }));
    }
}
