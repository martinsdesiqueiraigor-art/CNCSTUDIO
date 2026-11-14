using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

/// <summary>UX-04h B2: escolher a ferramenta do banco ativo no Trabalho fresa, operação nova vazia, aviso de T em conflito e "Adicionar ao banco".</summary>
public class ToolBankPickTests
{
    private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "trabalhos-referencia");

    private static ToolBank Bank(params BankTool[] tools) => new("fresa-a", "Fresa A", ToolBankMachine.Mill) { Tools = tools };

    private static CamOperationSpec NewOp(JobOpKind kind = JobOpKind.Pocket) =>
        ToolBankPick.EmptyTool(CamJobInfo.NewOperation(kind, ContourSide.Outside, 0));

    // ---------------------------------------------------------------- banco ativo

    [Fact]
    public void Banco_ativo_e_o_escolhido_ou_o_do_simulador()
    {
        var a = new ToolBank(ToolBankImport.MillBankId, "Ferramentas fresa", ToolBankMachine.Mill);
        var b = new ToolBank("outro", "Outro", ToolBankMachine.Mill);
        var lathe = new ToolBank("torno", "Torno", ToolBankMachine.Lathe);
        ToolBank[] banks = [a, b, lathe];
        Assert.Equal("outro", ToolBankPick.ActiveMillBank(banks, "outro")!.Id);
        Assert.Equal(a.Id, ToolBankPick.ActiveMillBank(banks, null)!.Id);
        Assert.Equal(a.Id, ToolBankPick.ActiveMillBank(banks, "sumiu")!.Id);
        Assert.Equal(a.Id, ToolBankPick.ActiveMillBank(banks, "torno")!.Id); // banco de torno não vale na fresa
        Assert.Null(ToolBankPick.ActiveMillBank([lathe], null));
    }

    // ---------------------------------------------------------------- escolher

    [Fact]
    public void Escolher_preenche_T_diametro_e_nome_e_rotacao_so_se_cadastrada()
    {
        var t = new BankTool(7) { Name = "Topo Z4", Type = BankToolType.EndMill, Diameter = 8, CuttingLength = 20 };
        CamOperationSpec s = ToolBankPick.Apply(NewOp(), t);
        Assert.Equal(7, s.Tool.Number);
        Assert.Equal(8, s.Tool.Diameter);
        Assert.Equal("Topo Z4", s.Tool.Name);
        Assert.DoesNotContain(CamJobInfo.ToolNumberField, s.MissingFields);
        Assert.DoesNotContain(CamJobInfo.DiameterField, s.MissingFields);
        // rotação e avanços não cadastrados: continuam vazios, a operação exige valor
        Assert.Contains(CamJobInfo.SpindleField, s.MissingFields);
        Assert.Contains("Avanço no plano", s.MissingFields);
        Assert.Contains("Avanço de descida", s.MissingFields);
        Assert.Equal("Falta Rotação, Avanço no plano e Avanço de descida", CamJobInfo.MissingError(s));
    }

    [Fact]
    public void Escolher_com_dados_de_corte_cadastrados_preenche_todos()
    {
        var t = new BankTool(2) { Diameter = 6, Rpm = 8000, FeedXY = 900, FeedZ = 300 };
        CamOperationSpec s = ToolBankPick.Apply(NewOp(JobOpKind.WrapText), t);
        Assert.Empty(s.MissingFields);
        Assert.Equal(new CamTool(2, 6, 8000, 900, 300, ""), s.Tool);
    }

    [Fact]
    public void Diametro_ausente_no_banco_fica_ausente_na_operacao()
    {
        var digitada = CamJobInfo.NewOperation(JobOpKind.Pocket, ContourSide.Outside, 0, new CamTool(1, 10, 3000, 500, 200, "x"));
        CamOperationSpec s = ToolBankPick.Apply(digitada, new BankTool(4) { Type = BankToolType.EndMill });
        Assert.Equal(4, s.Tool.Number);
        Assert.Contains(CamJobInfo.DiameterField, s.MissingFields);
        Assert.Equal("", s.Tool.Name); // nome ausente no banco: nada suposto
        // rotação e avanços digitados antes continuam (não cadastrados no banco)
        Assert.Equal(3000, s.Tool.SpindleRpm);
        Assert.DoesNotContain(CamJobInfo.SpindleField, s.MissingFields);
    }

    [Fact]
    public void Inativa_nao_aparece_e_so_cadastro_aparece_marcada()
    {
        ToolBank bank = Bank(
            new BankTool(3) { Diameter = 10, Active = false },
            new BankTool(5) { Type = BankToolType.CenterDrill, Diameter = 3 },
            new BankTool(1) { Name = "Topo", Diameter = 10, ToCheck = true });
        IReadOnlyList<BankToolChoice> c = ToolBankPick.Choices(bank);
        Assert.Equal([1, 5], c.Select(x => x.Tool.Number));
        Assert.Equal("T1 · Topo · Ø10 · a conferir", c[0].Label);
        Assert.Equal("T5 · Broca de centro · Ø3 · só cadastro", c[1].Label);
        Assert.Empty(ToolBankPick.Choices(null));
    }

    // ---------------------------------------------------------------- operação nova

    [Fact]
    public void Operacao_nova_nasce_com_a_ferramenta_vazia()
    {
        foreach (JobOpKind kind in Enum.GetValues<JobOpKind>())
        {
            CamOperationSpec s = NewOp(kind);
            Assert.Contains(CamJobInfo.ToolNumberField, s.MissingFields);
            Assert.Contains(CamJobInfo.DiameterField, s.MissingFields);
            Assert.Contains(CamJobInfo.SpindleField, s.MissingFields);
            Assert.Contains(ToolBankPick.FeedXYField(s), s.MissingFields);
            Assert.Contains("Avanço de descida", s.MissingFields);
            Assert.Equal("", s.Tool.Name);
            Assert.Equal("T? Ø?", CamJobInfo.OpTitle(0, s).Split(" — ")[1]);
        }
    }

    // ---------------------------------------------------------------- aviso de T em conflito

    [Fact]
    public void Aviso_de_T_fora_do_banco_e_de_diametro_diferente()
    {
        var op = CamJobInfo.NewOperation(JobOpKind.Pocket, ContourSide.Outside, 0, new CamTool(5, 10, 3000, 500, 200, "Topo"));
        Assert.Equal("T5 não está no banco \"Fresa A\". O trabalho usa a cópia dele.", ToolBankPick.Conflict(op, Bank(new BankTool(1) { Diameter = 10 })));
        Assert.Equal("T5 no banco \"Fresa A\" é diferente do trabalho: Ø: trabalho 10 × banco 8. O trabalho usa a cópia dele.",
            ToolBankPick.Conflict(op, Bank(new BankTool(5) { Diameter = 8 })));
        Assert.Contains("nome: trabalho \"Topo\" × banco \"Topo Z2\"", ToolBankPick.Conflict(op, Bank(new BankTool(5) { Name = "Topo Z2", Diameter = 10 })));
        Assert.Contains("(inativa no banco)", ToolBankPick.Conflict(op, Bank(new BankTool(5) { Diameter = 6, Active = false })));
        // igual (nome ausente no banco não conta; rotação diferente não conta) = sem aviso
        Assert.Null(ToolBankPick.Conflict(op, Bank(new BankTool(5) { Diameter = 10, Rpm = 9999 })));
        Assert.Null(ToolBankPick.Conflict(op, null));
        Assert.Null(ToolBankPick.Conflict(NewOp(), Bank())); // T vazio: nada a comparar
    }

    // ---------------------------------------------------------------- G-code e arquivo iguais

    public static TheoryData<string> MillJobs => new() { "fresa-sem-bruto", "fresa-bloco-siemens", "fresa-fixacao", "fresa-3mais2", "fresa-texto-cilindro", "fresa-zero-centro-base", "fresa-zero-g55-canto" };

    [Theory]
    [MemberData(nameof(MillJobs))]
    public void Banco_carregado_ou_vazio_nao_muda_o_programa_nem_o_arquivo(string name)
    {
        string json = File.ReadAllText(Path.Combine(Dir, name + ".casillas-job")).Replace("\r\n", "\n");
        MachineProfile? machine = File.Exists(Path.Combine(Dir, name + ".maquina.json"))
            ? MachineProfileJson.Parse(File.ReadAllText(Path.Combine(Dir, name + ".maquina.json"))).Profile : null;
        CamJob job = CamJobJson.FromJson(json).Job!;
        string program = CamJobBuilder.Build(job, machine).Program;

        // banco com todos os T do trabalho em outro Ø: há aviso, mas o trabalho e o programa não mudam
        ToolBank conflicting = Bank([.. CamJobInfo.ToolsUsed(job).Select(t => new BankTool(t.Number) { Diameter = t.Diameter + 1 }).DistinctBy(t => t.Number)]);
        foreach (ToolBank? bank in new[] { null, Bank(), conflicting })
        {
            foreach (CamOperationSpec op in job.Operations)
            {
                _ = ToolBankPick.Conflict(op, bank);
            }
        }

        Assert.All(job.Operations, op => Assert.NotNull(ToolBankPick.Conflict(op, conflicting)));
        Assert.All(job.Operations, op => Assert.NotNull(ToolBankPick.Conflict(op, Bank())));
        Assert.Equal(program, CamJobBuilder.Build(job, machine).Program);
        Assert.Equal(json, CamJobJson.ToJson(job).Replace("\r\n", "\n"));
    }

    [Fact]
    public void Escolher_no_banco_nao_poe_campo_novo_no_arquivo()
    {
        var job = new CamJob { ProgramNumber = 1, WorkOffset = 54 };
        job.Contours.Add(Contour.Rectangle(0, 0, 40, 30));
        var withDefaults = CamJobInfo.NewOperation(JobOpKind.Pocket, ContourSide.Outside, 0, new CamTool(7, 8, 3000, 500, 200, "Topo Z4"));
        job.Operations.Add(withDefaults);
        string typed = CamJobJson.ToJson(job);
        job.Operations[0] = ToolBankPick.Apply(NewOp() with { Levels = withDefaults.Levels },
            new BankTool(7) { Name = "Topo Z4", Diameter = 8, Rpm = 3000, FeedXY = 500, FeedZ = 200, CuttingLength = 22, Flutes = 4 });
        Assert.Equal(typed, CamJobJson.ToJson(job));
        Assert.DoesNotContain("banco", typed, StringComparison.OrdinalIgnoreCase);
    }

    // ---------------------------------------------------------------- adicionar ao banco

    [Fact]
    public void Adicionar_ao_banco_cria_a_conferir_so_com_os_campos_preenchidos()
    {
        CamOperationSpec op = CamJobInfo.NewOperation(JobOpKind.Pocket, ContourSide.Outside, 0, new CamTool(12, 6, 5000, 700, 250, " Topo Z3 "));
        op = CamJobInfo.SetMissing(op, CamJobInfo.SpindleField, true);
        BankAddResult r = ToolBankPick.AddToBank(Bank(new BankTool(1) { Diameter = 10 }), op);
        Assert.Null(r.Error);
        Assert.Equal(new BankTool(12) { Name = "Topo Z3", Diameter = 6, FeedXY = 700, FeedZ = 250, ToCheck = true }, r.Tool);
        Assert.Null(r.Tool!.Type);
        Assert.Equal(BankToolStatus.ToCheck, r.Tool.Status);
        Assert.Equal([1, 12], r.Bank!.Tools.Select(t => t.Number));
    }

    [Fact]
    public void Adicionar_T_repetido_recusa_e_oferece_o_proximo_livre()
    {
        CamOperationSpec op = CamJobInfo.NewOperation(JobOpKind.Pocket, ContourSide.Outside, 0, new CamTool(2, 6, 5000, 700, 250));
        ToolBank bank = Bank(new BankTool(2), new BankTool(3));
        BankAddResult r = ToolBankPick.AddToBank(bank, op);
        Assert.Null(r.Bank);
        Assert.Equal("T2 já existe no banco \"Fresa A\".", r.Error);
        Assert.Equal(4, r.NextFree);
        BankAddResult again = ToolBankPick.AddToBank(bank, op, r.NextFree);
        Assert.Equal(4, again.Tool!.Number);
        Assert.True(again.Tool.ToCheck);
    }

    [Fact]
    public void Adicionar_sem_T_livre_recusa_com_o_motivo()
    {
        ToolBank full = Bank([.. Enumerable.Range(1, 9999).Select(n => new BankTool(n))]);
        CamOperationSpec op = CamJobInfo.NewOperation(JobOpKind.Pocket, ContourSide.Outside, 0, new CamTool(9, 6, 5000, 700, 250));
        BankAddResult r = ToolBankPick.AddToBank(full, op);
        Assert.Null(r.NextFree);
        Assert.Equal("T9 já existe no banco \"Fresa A\". Não há T livre neste banco (vai de 1 a 9999).", r.Error);
        Assert.NotNull(ToolBankPick.AddToBank(Bank(), NewOp()).Error); // T vazio
    }
}
