using CncStudio.Core.Cam;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>BANCO-01: conflitos do banco resolvidos com um clique, sem o armário.</summary>
public class Banco01Tests
{
    private static ToolBank Bank() => new(ToolBankImport.MillBankId, "Ferramentas fresa", ToolBankMachine.Mill)
    {
        Tools = [new BankTool(1) { Name = "Topo 20", Diameter = 20, Flutes = 4 }, new BankTool(2) { Name = "Broca 9" }],
    };

    private static CamOperationSpec Op(int t, double d, string name) =>
        CamJobInfo.NewOperation(JobOpKind.Drilling, ContourSide.Outside, 0) with { Tool = new CamTool(t, d, 1000, 100, 80, name) };

    [Fact]
    public void AtualizarOBancoTrocaODiametroEONomeEMarcaAConferir()
    {
        ToolBank changed = ToolBankPick.UpdateBank(Bank(), Op(1, 9, "Broca 9 HSS"))!;
        BankTool t = changed.Tools.Single(x => x.Number == 1);
        Assert.Equal((9.0, "Broca 9 HSS", true, 4), (t.Diameter!.Value, t.Name, t.ToCheck, t.Flutes!.Value));
        Assert.Null(ToolBankPick.Conflict(Op(1, 9, "Broca 9 HSS"), changed));
        // T fora do banco: é o Adicionar, não o Atualizar
        Assert.Null(ToolBankPick.UpdateBank(Bank(), Op(7, 9, "x")));
    }

    [Fact]
    public void BancoRecebeODiametroDoCamSoNosTQueJaEstaoNele()
    {
        var p = new CamHandoff("h", CamDialect.Fanuc)
        {
            Tools = [new HandoffTool(1, 9, "Broca 9", 1000, 100, 80), new HandoffTool(2, 9, "Broca 9", 1000, 100, 80), new HandoffTool(5, 6, "Topo 6", 1000, 100, 80)],
        };
        ToolBank changed = CamHandoffCode.BankWithCamDiameters(Bank(), p)!;
        Assert.Equal(9, changed.Tools.Single(x => x.Number == 1).Diameter);
        Assert.Equal(9, changed.Tools.Single(x => x.Number == 2).Diameter); // banco sem Ø recebe o do CAM
        Assert.DoesNotContain(changed.Tools, x => x.Number == 5);
        Assert.Null(CamHandoffCode.BankWithCamDiameters(changed, p)); // nada mais a mudar
    }
}
