using System.Text.Json.Nodes;
using CncStudio.Core.Cam;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>
/// UX-04h PR C: o pacote do CAM leva tipo, raio de canto, ângulo da ponta, comprimento de corte e suporte quando o T está no banco
/// ativo sem conflito (só os cadastrados). G-code, .casillas-job e versão do pacote não mudam.
/// </summary>
public class CamHandoffBankTests
{
    private static string Dir => Path.Combine(AppContext.BaseDirectory, "trabalhos-referencia");

    private static string Read(string file) => File.ReadAllText(Path.Combine(Dir, file)).Replace("\r\n", "\n");

    private static CamJob Load(string name)
    {
        JobLoadResult r = CamJobJson.FromJson(Read(name + ".casillas-job"));
        Assert.True(r.Ok, r.Error);
        return r.Job!;
    }

    private static ToolBank Bank(params BankTool[] tools) => new("fresa-a", "Fresa A", ToolBankMachine.Mill) { Tools = tools };

    // a ferramenta do trabalho no banco, com os cinco campos cadastrados (rpm e avanços diferentes: não entram no pacote)
    private static BankTool Full(CamTool t) => new(t.Number)
    {
        Name = t.Name, Diameter = t.Diameter, Type = BankToolType.BullNoseEndMill, CornerRadius = 1.5, TipAngle = 140, CuttingLength = 22,
        HolderDiameter = 32, HolderLength = 50, Rpm = 9999, FeedXY = 1, FeedZ = 1,
    };

    private static CamTool T1(CamJob job) => job.Operations.First(o => o.Enabled).Tool;

    [Fact]
    public void T_no_banco_sem_conflito_preenche_os_cinco_campos_cadastrados()
    {
        CamJob job = Load("fresa-zero-g55-canto");
        CamTool t = T1(job);
        HandoffTool h = CamHandoff.FromBank(t, Bank(Full(t)));
        Assert.Equal(new HandoffTool(t.Number, t.Diameter, t.Name, t.SpindleRpm, t.FeedXY, t.FeedZ)
        {
            Type = "toroidal", CornerRadius = 1.5, TipAngle = 140, CutLength = 22, Holder = "Ø 32 × 50 mm",
        }, h);
    }

    [Fact]
    public void Campo_nao_cadastrado_continua_nulo()
    {
        CamTool t = T1(Load("fresa-zero-g55-canto"));
        HandoffTool h = CamHandoff.FromBank(t, Bank(new BankTool(t.Number) { Name = t.Name, Diameter = t.Diameter, CuttingLength = 22 }));
        Assert.True(h is { Type: null, CornerRadius: null, TipAngle: null, CutLength: 22, Holder: null });
        Assert.Equal("Ø 32 mm", CamHandoff.HolderText(32, null));
        Assert.Equal("comprimento 50 mm", CamHandoff.HolderText(null, 50));
        Assert.Null(CamHandoff.HolderText(null, null));
    }

    [Fact]
    public void Broca_sem_tipo_no_banco_fica_sem_tipo()
    {
        CamTool t = T1(Load("fresa-zero-g55-canto"));
        HandoffTool h = CamHandoff.FromBank(t, Bank(new BankTool(t.Number) { Diameter = t.Diameter, TipAngle = 118 }));
        Assert.Null(h.Type);
        Assert.Equal(118, h.TipAngle);
    }

    [Fact]
    public void Conflito_de_diametro_ou_nome_e_T_fora_do_banco_deixam_os_cinco_nulos()
    {
        CamTool t = T1(Load("fresa-zero-g55-canto"));
        var plain = new HandoffTool(t.Number, t.Diameter, t.Name, t.SpindleRpm, t.FeedXY, t.FeedZ);
        Assert.Equal(plain, CamHandoff.FromBank(t, Bank(Full(t) with { Diameter = t.Diameter + 2 })));
        Assert.Equal(plain, CamHandoff.FromBank(t, Bank(Full(t) with { Diameter = null })));
        Assert.Equal(plain, CamHandoff.FromBank(t, Bank(Full(t) with { Name = "Outra" })));
        Assert.Equal(plain, CamHandoff.FromBank(t, Bank(Full(t) with { Number = t.Number + 50 })));
        Assert.Equal(plain, CamHandoff.FromBank(t, Bank()));
        Assert.Equal(plain, CamHandoff.FromBank(t, null));

        // banco sem nome: só o Ø conta (a mesma regra do aviso do B2)
        Assert.Equal("toroidal", CamHandoff.FromBank(t, Bank(Full(t) with { Name = null })).Type);
    }

    [Fact]
    public void Mesma_comparacao_do_aviso_do_B2()
    {
        CamTool t = T1(Load("fresa-zero-g55-canto"));
        foreach (BankTool b in new[] { Full(t), Full(t) with { Diameter = 3 }, Full(t) with { Name = "X" }, Full(t) with { Name = null }, Full(t) with { Diameter = null } })
        {
            CamOperationSpec op = Load("fresa-zero-g55-canto").Operations.First(o => o.Enabled) with { Tool = t };
            Assert.Equal(ToolBankPick.Conflict(op, Bank(b)) is null, ToolBankPick.SameTool(t, b));
        }
    }

    [Fact]
    public void Ferramenta_inativa_preenche()
    {
        CamTool t = T1(Load("fresa-zero-g55-canto"));
        Assert.Equal(1.5, CamHandoff.FromBank(t, Bank(Full(t) with { Active = false })).CornerRadius);
    }

    [Fact]
    public void Sem_banco_ou_banco_vazio_da_o_pacote_de_hoje()
    {
        CamJob job = Load("fresa-zero-g55-canto");
        string program = CamJobBuilder.Build(job, null).Program;
        string today = CamHandoffJson.ToJson(CamHandoff.Create(job, program, null));
        Assert.Equal(today, CamHandoffJson.ToJson(CamHandoff.Create(job, program, null, Bank())));
        Assert.Equal(today, CamHandoffJson.ToJson(CamHandoff.Create(job, program, null, null)));
    }

    public static TheoryData<string> MillJobs => new() { "fresa-sem-bruto", "fresa-bloco-siemens", "fresa-fixacao", "fresa-3mais2", "fresa-texto-cilindro",
        "fresa-zero-centro-base", "fresa-zero-g55-canto" };

    [Theory]
    [MemberData(nameof(MillJobs))]
    public void GCode_e_casillas_job_iguais_com_banco_carregado_vazio_ou_em_conflito_e_o_pacote_so_difere_nos_cinco_campos(string name)
    {
        string json = Read(name + ".casillas-job");
        CamJob job = Load(name);
        string program = CamJobBuilder.Build(job, null).Program;
        List<CamTool> used = [.. job.Operations.Where(o => o.Enabled).Select(o => o.Tool)];
        ToolBank full = Bank([.. used.GroupBy(t => t.Number).Select(g => Full(g.First()))]);
        ToolBank conflict = Bank([.. used.GroupBy(t => t.Number).Select(g => Full(g.First()) with { Diameter = g.First().Diameter + 1 })]);

        string plain = CamHandoffJson.ToJson(CamHandoff.Create(job, program, null));
        foreach (ToolBank? bank in new[] { full, Bank(), conflict, null })
        {
            CamHandoff p = CamHandoff.Create(job, program, null, bank);
            Assert.Equal(program, CamJobBuilder.Build(job, null).Program);
            Assert.Equal(json, CamJobJson.ToJson(job).Replace("\r\n", "\n"));
            Assert.True(p.Matches(program));

            // o pacote só difere nos cinco campos das ferramentas
            var a = (JsonObject)JsonNode.Parse(plain)!;
            var b = (JsonObject)JsonNode.Parse(CamHandoffJson.ToJson(p))!;
            foreach (JsonObject tool in ((JsonArray)a["ferramentas"]!).Concat((JsonArray)b["ferramentas"]!).Cast<JsonObject>())
            {
                foreach (string key in new[] { "tipo", "raioCanto", "anguloPonta", "comprimentoCorte", "suporte" })
                {
                    tool.Remove(key);
                }
            }

            Assert.Equal(a.ToJsonString(), b.ToJsonString());
        }

        Assert.All(CamHandoff.Create(job, program, null, full).Tools, t => Assert.Equal("toroidal", t.Type));
        Assert.All(CamHandoff.Create(job, program, null, conflict).Tools, t => Assert.Null(t.Type));
    }

    [Fact]
    public void Versao_1_mantida_ida_e_volta_com_os_campos_e_leitor_ignora_chave_desconhecida()
    {
        CamJob job = Load("fresa-zero-g55-canto");
        string program = CamJobBuilder.Build(job, null).Program;
        CamTool t = T1(job);
        CamHandoff p = CamHandoff.Create(job, program, null, Bank(Full(t)));
        string json = CamHandoffJson.ToJson(p);
        Assert.Equal(1, (int)JsonNode.Parse(json)!["versao"]!);

        HandoffReadResult back = CamHandoffJson.Parse(json);
        Assert.Null(back.Warning);
        Assert.Equal(json, CamHandoffJson.ToJson(back.Package!));
        Assert.Equal(p.Tools[0], back.Package!.Tools[0]);

        // chave que o leitor não conhece (de um pacote futuro da mesma versão): ignorada, sem aviso
        var root = (JsonObject)JsonNode.Parse(json)!;
        root["chaveNova"] = "x";
        ((JsonObject)((JsonArray)root["ferramentas"]!)[0]!)["outraChave"] = 3;
        HandoffReadResult extra = CamHandoffJson.Parse(root.ToJsonString());
        Assert.Null(extra.Warning);
        Assert.Equal(p.Tools[0], extra.Package!.Tools[0]);

        // CRLF e LF: o mesmo hash
        Assert.Equal(CamHandoff.Hash(program.Replace("\r\n", "\n")), CamHandoff.Hash(program.Replace("\r\n", "\n").Replace("\n", "\r\n")));
    }
}
