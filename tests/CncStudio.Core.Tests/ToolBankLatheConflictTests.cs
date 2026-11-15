using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

/// <summary>TORNO-T0: aviso de T fora do banco ativo do torno (a regra da fresa, B2, sem Ø; só avisa).</summary>
public class ToolBankLatheConflictTests
{
    private static ToolBank Bank(string name, params int[] numbers) =>
        new("t-" + name, name, ToolBankMachine.Lathe) { Tools = numbers.Select(n => new BankTool(n)).ToList() };

    private static LatheOperationSpec Op(int t) => new(LatheOpKind.Turning, new LatheTool(t));

    [Fact]
    public void T_que_nao_esta_no_banco_avisa_com_o_texto_da_fresa()
    {
        string? msg = ToolBankPick.LatheConflict(Op(5), Bank("Torno A", 1, 2));
        Assert.Equal("T5 não está no banco \"Torno A\". O trabalho usa a cópia dele.", msg);
    }

    [Fact]
    public void T_no_banco_nao_avisa()
    {
        Assert.Null(ToolBankPick.LatheConflict(Op(2), Bank("Torno A", 1, 2)));
    }

    [Fact]
    public void Trocar_o_banco_ativo_liga_e_desliga_o_aviso()
    {
        LatheOperationSpec op = Op(5);
        ToolBank a = Bank("Torno A", 1, 5);
        ToolBank b = Bank("Torno B", 1, 2);
        Assert.Null(ToolBankPick.LatheConflict(op, a));
        Assert.NotNull(ToolBankPick.LatheConflict(op, b));
        Assert.Null(ToolBankPick.LatheConflict(op, a));
    }

    [Fact]
    public void Sem_banco_ou_com_T_vazio_nao_avisa()
    {
        Assert.Null(ToolBankPick.LatheConflict(Op(5), null));
        LatheOperationSpec empty = Op(5) with { MissingFields = [LatheJobInfo.ToolNumberField] };
        Assert.Null(ToolBankPick.LatheConflict(empty, Bank("Torno A", 1)));
    }

    [Fact]
    public void O_aviso_nao_troca_o_T_nem_a_ferramenta()
    {
        LatheOperationSpec op = Op(5);
        _ = ToolBankPick.LatheConflict(op, Bank("Torno A", 1));
        Assert.Equal(5, op.Tool.Number);
    }
}
