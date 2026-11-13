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
