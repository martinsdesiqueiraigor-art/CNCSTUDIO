using CncStudio.Core.Cam;
using CncStudio.Core.Setup;

namespace CncStudio.Core.Tests;

/// <summary>UX-04h PR A: banco de ferramentas no Core (modelo, arquivo, importação, migração da tabela e leitor da web).</summary>
public class ToolBankTests
{
    private static ToolBank Mill(params BankTool[] tools) => new("banco-a", "Banco A", ToolBankMachine.Mill) { Tools = tools };

    private static BankTool Full() => new(12)
    {
        Name = "Fresa 8 metal duro", Type = BankToolType.BullNoseEndMill, Diameter = 8, Flutes = 4, CuttingLength = 20, OverallLength = 75,
        HolderDiameter = 32, HolderLength = 60, CornerRadius = 0.5, TipAngle = 140, ToolMaterial = "metal duro", Rpm = 8000, FeedXY = 1200.5,
        FeedZ = 300, ToCheck = true,
    };

    // ── G-code fixo ──

    [Theory]
    [InlineData("fresa-sem-bruto")]
    [InlineData("fresa-zero-g55-canto")]
    public void Gerar_com_banco_carregado_ou_vazio_da_o_mesmo_programa(string name)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "trabalhos-referencia");
        string expected = File.ReadAllText(Path.Combine(dir, name + ".nc")).Replace("\r\n", "\n");
        CamJob job = CamJobJson.FromJson(File.ReadAllText(Path.Combine(dir, name + ".casillas-job"))).Job!;

        string empty = CamJobBuilder.Build(job, null).Program;
        ToolBank loaded = Mill(Full(), new BankTool(1) { Diameter = 3 }, new BankTool(2) { Diameter = 50, Rpm = 100 });
        Assert.Null(loaded.Validate());
        string withBank = CamJobBuilder.Build(job, null).Program;

        Assert.Equal(expected, empty.Replace("\r\n", "\n"));
        Assert.Equal(empty, withBank);
    }

    // ── JSON ──

    [Fact]
    public void Ida_e_volta_mantem_tudo_e_o_ausente_continua_ausente()
    {
        var lathe = new ToolBank("torno-1", "Torno da oficina", ToolBankMachine.Lathe)
        {
            Tools = [new BankTool(3) { Type = BankToolType.PartExternal, InsertWidth = 2.5, IsoCode = "N123G2-0300", NoseRadius = 0.2 }, new BankTool(4)],
        };
        foreach (ToolBank bank in new[] { Mill(Full(), new BankTool(1)), lathe })
        {
            string json = ToolBankJson.ToJson(bank);
            ToolBankReadResult read = ToolBankJson.Parse(json);
            Assert.True(read.Ok, read.Error);
            Assert.False(read.ReadOnly);
            Assert.Equal(bank.Tools, read.Bank!.Tools);
            Assert.Equal((bank.Id, bank.Name, bank.Machine), (read.Bank.Id, read.Bank.Name, read.Bank.Machine));
            Assert.Equal(json, ToolBankJson.ToJson(read.Bank));
        }

        string bare = ToolBankJson.ToJson(Mill(new BankTool(7)));
        Assert.DoesNotContain("diameter", bare);
        Assert.DoesNotContain("rpm", bare);
        Assert.DoesNotContain("toCheck", bare);
        Assert.Equal(new BankTool(7), ToolBankJson.Parse(bare).Bank!.Tools[0]);
        Assert.Contains("\"format\": \"casillas-ferramentas\"", bare);
        Assert.Equal("banco-a.casillas-ferramentas", ToolBankJson.FileName(Mill()));
    }

    [Fact]
    public void Numeros_com_ponto_em_qualquer_idioma()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("pt-BR");
            Assert.Contains("1200.5", ToolBankJson.ToJson(Mill(Full())));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = culture;
        }
    }

    [Fact]
    public void Versao_mais_nova_abre_so_para_leitura_e_chave_desconhecida_nao_derruba()
    {
        const string json = """
            {"format":"casillas-ferramentas","version":2,"id":"x","name":"Novo","machine":"fresa","corDoBanco":"azul",
             "tools":[{"number":1,"type":"tipo-do-futuro","diameter":6,"vidaUtil":90}]}
            """;
        ToolBankReadResult read = ToolBankJson.Parse(json);
        Assert.True(read.Ok, read.Error);
        Assert.True(read.ReadOnly);
        Assert.Equal(ToolBankJson.FutureWarning, read.Warning);
        Assert.Null(read.Bank!.Tools[0].Type);
        Assert.Equal(6, read.Bank.Tools[0].Diameter);

        ToolBankReadResult current = ToolBankJson.Parse(json.Replace("\"version\":2", "\"version\":1").Replace("tipo-do-futuro", "topo"));
        Assert.True(current.Ok, current.Error);
        Assert.False(current.ReadOnly);
        Assert.Null(current.Bank!.Tools[0].Name);
    }

    [Theory]
    [InlineData("", "vazio")]
    [InlineData("{", "estragado")]
    [InlineData("""{"format":"casillas-job","version":1}""", "Não é um arquivo")]
    [InlineData("""{"format":"casillas-ferramentas","version":1,"id":"a","name":"A","machine":"plasma","tools":[]}""", "Máquina")]
    [InlineData("""{"format":"casillas-ferramentas","version":1,"id":"A B","name":"A","machine":"fresa","tools":[]}""", "Identificador")]
    [InlineData("""{"format":"casillas-ferramentas","version":1,"id":"a","name":"A","machine":"fresa","tools":[{"number":1,"type":"xyz"}]}""", "Tipo de ferramenta desconhecido")]
    [InlineData("""{"format":"casillas-ferramentas","version":1,"id":"a","name":"A","machine":"fresa","tools":[{"number":1.5}]}""", "inteiro")]
    [InlineData("""{"format":"casillas-ferramentas","version":1,"id":"a","name":"A","machine":"fresa","tools":[{"number":1},{"number":1}]}""", "repetido")]
    [InlineData("""{"format":"casillas-ferramentas","version":1,"id":"a","name":"A","machine":"torno","tools":[{"number":100}]}""", "1 a 99")]
    public void Arquivo_invalido_e_recusado_com_o_motivo(string json, string reason)
    {
        ToolBankReadResult read = ToolBankJson.Parse(json);
        Assert.False(read.Ok);
        Assert.Contains(reason, read.Error);
    }

    // ── Regras do banco ──

    [Fact]
    public void T_repetido_no_mesmo_banco_e_recusado_e_em_outro_banco_e_permitido()
    {
        ToolBank a = Mill(new BankTool(5));
        Assert.Equal("T5 já existe neste banco.", a.CanAdd(new BankTool(5) { Diameter = 6 }));
        Assert.Null(a.CanAdd(new BankTool(5) { Diameter = 6 }, replacing: 5));
        Assert.Null(new ToolBank("banco-b", "Banco B", ToolBankMachine.Mill).CanAdd(new BankTool(5)));
        Assert.Contains("repetido", (a with { Tools = [new BankTool(5), new BankTool(5)] }).Validate());
    }

    [Theory]
    [InlineData(ToolBankMachine.Mill, 0, false)]
    [InlineData(ToolBankMachine.Mill, 1, true)]
    [InlineData(ToolBankMachine.Mill, 9999, true)]
    [InlineData(ToolBankMachine.Mill, 10000, false)]
    [InlineData(ToolBankMachine.Lathe, 99, true)]
    [InlineData(ToolBankMachine.Lathe, 100, false)]
    public void Faixa_de_T_por_maquina(ToolBankMachine machine, int number, bool ok)
    {
        string? error = new ToolBank("b", "B", machine).CanAdd(new BankTool(number));
        Assert.Equal(ok, error is null);
        if (!ok)
        {
            Assert.Contains(machine == ToolBankMachine.Mill ? "1 a 9999" : "1 a 99", error);
        }
    }

    [Fact]
    public void Tipo_e_campo_de_outra_maquina_sao_recusados()
    {
        Assert.NotNull(ToolBank.ToolError(ToolBankMachine.Lathe, new BankTool(1) { Type = BankToolType.EndMill }));
        Assert.NotNull(ToolBank.ToolError(ToolBankMachine.Mill, new BankTool(1) { Type = BankToolType.TurnExternal }));
        Assert.Null(ToolBank.ToolError(ToolBankMachine.Lathe, new BankTool(1) { Type = BankToolType.Drill }));
        Assert.Null(ToolBank.ToolError(ToolBankMachine.Mill, new BankTool(1) { Type = BankToolType.Reamer }));
        Assert.NotNull(ToolBank.ToolError(ToolBankMachine.Mill, new BankTool(1) { NoseRadius = 0.4 }));
        Assert.NotNull(ToolBank.ToolError(ToolBankMachine.Lathe, new BankTool(1) { Type = BankToolType.TurnExternal, InsertWidth = 3 }));
        Assert.NotNull(ToolBank.ToolError(ToolBankMachine.Mill, new BankTool(1) { Diameter = 0 }));
        Assert.NotNull(ToolBank.ToolError(ToolBankMachine.Mill, new BankTool(1) { Diameter = 6, CornerRadius = 4 }));
        Assert.Null(BankToolTypes.ToToolKind(BankToolType.CenterDrill));
        Assert.Equal(ToolKind.TurnInternal, BankToolTypes.ToToolKind(BankToolType.TurnInternal));
    }

    // ── Importar ──

    [Fact]
    public void Importar_com_id_ou_nome_repetido_vira_copia()
    {
        string json = ToolBankJson.ToJson(Mill(new BankTool(1)));
        ToolBankReadResult free = ToolBankImport.Import(json, [new ToolBank("outro", "Outro", ToolBankMachine.Mill)]);
        Assert.Equal(("banco-a", "Banco A"), (free.Bank!.Id, free.Bank.Name));

        ToolBankReadResult sameId = ToolBankImport.Import(json, [new ToolBank("banco-a", "Diferente", ToolBankMachine.Mill)]);
        Assert.Equal(("banco-a-2", "Banco A"), (sameId.Bank!.Id, sameId.Bank.Name));

        ToolBankReadResult sameName = ToolBankImport.Import(json, [new ToolBank("x", "banco a", ToolBankMachine.Lathe)]);
        Assert.Equal(("banco-a", "Banco A (cópia)"), (sameName.Bank!.Id, sameName.Bank.Name));

        ToolBankReadResult twice = ToolBankImport.Import(json, [Mill(), new ToolBank("banco-a-2", "Banco A (cópia)", ToolBankMachine.Mill)]);
        Assert.Equal(("banco-a-3", "Banco A (cópia 2)"), (twice.Bank!.Id, twice.Bank.Name));
        Assert.Equal(Mill(new BankTool(1)).Tools, twice.Bank.Tools);
    }

    [Fact]
    public void Importar_arquivo_invalido_ou_futuro_em_conflito_e_recusado_com_o_motivo()
    {
        Assert.Contains("Não é um arquivo", ToolBankImport.Import("{\"a\":1}", []).Error);
        string future = ToolBankJson.ToJson(Mill()).Replace("\"version\": 1", "\"version\": 9");
        Assert.True(ToolBankImport.Import(future, []).ReadOnly);
        ToolBankReadResult clash = ToolBankImport.Import(future, [Mill()]);
        Assert.False(clash.Ok);
        Assert.Contains("versão mais nova", clash.Error);
    }

    // ── Migração da tabela de hoje ──

    [Fact]
    public void Migracao_campo_a_campo()
    {
        List<LegacyToolRow> rows =
        [
            new(1, 10, "Fresa 10", "", 3, 0, 0, 0),
            new(2, 6, "  ", "", 3, 4500, 800, 0),
            new(3, 25, "Desbaste", nameof(ToolKind.TurnExternal), 3, 0, 0, 0),
            new(4, 10, "Bedame 3", nameof(ToolKind.PartExternal), 3, 0, 0.15, 0),
            new(5, 0, "", nameof(ToolKind.ThreadInternal), 4, 0, 0, 0),
        ];
        List<LegacyToolRow> copy = [.. rows];

        ToolBankMigration m = ToolBankImport.FromLegacyTable(rows);

        Assert.Equal(copy, rows);
        Assert.Empty(m.Warnings);
        Assert.Equal((ToolBankImport.MillBankName, ToolBankImport.LatheBankName), (m.Mill.Name, m.Lathe.Name));
        Assert.Null(m.Mill.Validate());
        Assert.Null(m.Lathe.Validate());
        Assert.Equal([1, 2], m.Mill.Tools.Select(t => t.Number));
        Assert.Equal([3, 4, 5], m.Lathe.Tools.Select(t => t.Number));

        Assert.Equal(new BankTool(1) { Name = "Fresa 10", Diameter = 10, ToCheck = true }, m.Mill.Tools[0]);
        Assert.Equal(new BankTool(2) { Diameter = 6, Rpm = 4500, FeedXY = 800 }, m.Mill.Tools[1]);
        Assert.Equal(new BankTool(3) { Name = "Desbaste", Type = BankToolType.TurnExternal, Diameter = 25 }, m.Lathe.Tools[0]);
        Assert.Equal(new BankTool(4) { Name = "Bedame 3", Type = BankToolType.PartExternal, Diameter = 10, InsertWidth = 3, FeedXY = 0.15, ToCheck = true },
            m.Lathe.Tools[1]);
        Assert.Equal(new BankTool(5) { Type = BankToolType.ThreadInternal }, m.Lathe.Tools[2]);
    }

    [Fact]
    public void Migracao_avisa_o_que_nao_coube()
    {
        ToolBankMigration m = ToolBankImport.FromLegacyTable(
        [
            new(150, 20, "", nameof(ToolKind.TurnExternal), 3, 0, 0, 0),
            new(7, 6, "", "Laser", 3, 0, 0, 0),
            new(8, 6, "a", "", 3, 0, 0, 0),
            new(8, 8, "b", "", 3, 0, 0, 0),
        ]);
        Assert.Empty(m.Lathe.Tools);
        Assert.Equal([7, 8], m.Mill.Tools.Select(t => t.Number));
        Assert.Null(m.Mill.Tools[0].Type);
        Assert.Equal("b", m.Mill.Tools[1].Name);
        Assert.Equal(3, m.Warnings.Count);
        Assert.Contains(m.Warnings, w => w.Contains("1 a 99"));
    }

    [Fact]
    public void Migracao_inserto_3_no_bedame_entra_a_conferir()
    {
        ToolBankMigration m = ToolBankImport.FromLegacyTable(
        [
            new(1, 25, "", nameof(ToolKind.PartExternal), 3, 0, 0, 0),
            new(2, 25, "", nameof(ToolKind.PartInternal), 2.5, 0, 0, 0),
            new(3, 25, "", nameof(ToolKind.TurnExternal), 3, 0, 0, 0),
        ]);
        Assert.Equal(new BankTool(1) { Type = BankToolType.PartExternal, Diameter = 25, InsertWidth = 3, ToCheck = true }, m.Lathe.Tools[0]);
        Assert.False(m.Lathe.Tools[1].ToCheck);
        Assert.Equal(2.5, m.Lathe.Tools[1].InsertWidth);
        Assert.False(m.Lathe.Tools[2].ToCheck);
        Assert.Null(m.Lathe.Tools[2].InsertWidth);
    }

    // ── Minhas ferramentas da web ──

    private static string WebJson(params SetupTool[] tools) => SetupJson.SerializeMyTools(new MachiningSetup { Tools = [.. tools] });

    [Fact]
    public void Web_valores_iguais_ao_padrao_do_editor_entram_ausentes()
    {
        string json = WebJson(
            new SetupTool { Number = 1, Kind = ToolKind.FlatEndMill },
            new SetupTool { Number = 2, Kind = ToolKind.BullNoseEndMill, Diameter = 12, CornerRadius = 1, TipAngle = 90, CuttingLength = 30, HolderDiameter = 40, HolderLength = 50 },
            new SetupTool { Number = 3, Kind = ToolKind.PartInternal, Diameter = 16, InsertWidth = 2 },
            new SetupTool { Number = 4, Kind = ToolKind.PartExternal },
            new SetupTool { Number = 5, Kind = ToolKind.Drill, Diameter = 5 });

        (ToolBankMigration? r, string? error) = ToolBankImport.FromWebMyTools(json);

        Assert.Null(error);
        Assert.Empty(r!.Warnings);
        Assert.Equal(new BankTool(1) { Type = BankToolType.EndMill, Diameter = 10, ToCheck = true }, r.Mill.Tools[0]);
        Assert.Equal(new BankTool(2)
        {
            Type = BankToolType.BullNoseEndMill, Diameter = 12, CornerRadius = 1, TipAngle = 90, CuttingLength = 30, HolderDiameter = 40, HolderLength = 50,
        }, r.Mill.Tools[1]);
        Assert.Equal(new BankTool(5) { Type = BankToolType.Drill, Diameter = 5 }, r.Mill.Tools[2]);
        Assert.Equal(new BankTool(3) { Type = BankToolType.PartInternal, Diameter = 16, InsertWidth = 2 }, r.Lathe.Tools[0]);
        Assert.Equal(new BankTool(4) { Type = BankToolType.PartExternal, Diameter = 10, ToCheck = true }, r.Lathe.Tools[1]);
        Assert.Null(r.Mill.Validate());
        Assert.Null(r.Lathe.Validate());
    }

    [Fact]
    public void Web_raio_de_canto_0_entra_ausente()
    {
        (ToolBankMigration? r, _) = ToolBankImport.FromWebMyTools(WebJson(
            new SetupTool { Number = 1, Kind = ToolKind.FlatEndMill, Diameter = 6, CornerRadius = 0 },
            new SetupTool { Number = 2, Kind = ToolKind.BullNoseEndMill, Diameter = 6, CornerRadius = 0.5 }));
        Assert.Null(r!.Mill.Tools[0].CornerRadius);
        Assert.Equal(0.5, r.Mill.Tools[1].CornerRadius);
        Assert.Equal(new BankTool(1) { Type = BankToolType.EndMill, Diameter = 6 }, r.Mill.Tools[0]);
    }

    [Fact]
    public void Web_limite_de_20_e_lista_estragada()
    {
        SetupTool[] twenty = [.. Enumerable.Range(1, 20).Select(n => new SetupTool { Number = n, Diameter = 6 })];
        Assert.Equal(20, ToolBankImport.FromWebMyTools(WebJson(twenty)).Result!.Mill.Tools.Count);

        string tooMany = WebJson(twenty).Replace("\"Tools\":[", "\"Tools\":[{\"Kind\":\"fresa\",\"Number\":21,\"Diameter\":6},");
        (ToolBankMigration? r, string? error) = ToolBankImport.FromWebMyTools(tooMany);
        Assert.Null(r);
        Assert.Contains("20", error);
        Assert.NotNull(ToolBankImport.FromWebMyTools("{x").Error);
        Assert.NotNull(ToolBankImport.FromWebMyTools("").Error);
    }
}
