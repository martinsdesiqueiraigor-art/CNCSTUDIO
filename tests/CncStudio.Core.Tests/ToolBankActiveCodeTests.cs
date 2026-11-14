using CncStudio.Core.Cam;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>UX-04h PR C: quem alimenta o simulador do Code lê o banco ativo da fresa no lugar do "principal" (sem escolha, igual a antes).</summary>
public class ToolBankActiveCodeTests
{
    private static ToolBank Mill(string id, string name, double d) => new(id, name, ToolBankMachine.Mill) { Tools = [new BankTool(1) { Diameter = d }] };

    private static readonly ToolBank Migrated = Mill(ToolBankImport.MillBankId, "Ferramentas fresa", 3);
    private static readonly ToolBank Other = Mill("outro", "Outro", 8);
    private static readonly ToolBank Lathe = new(ToolBankImport.LatheBankId, "Ferramentas torno", ToolBankMachine.Lathe)
    {
        Tools = [new BankTool(1) { Diameter = 20 }, new BankTool(2) { Diameter = 25 }],
    };

    [Fact]
    public void Sem_escolha_o_resultado_e_o_de_antes()
    {
        ToolBank[] banks = [Other, Migrated, Lathe];
        Assert.Equal(ToolBankLookup.Diameters(banks).OrderBy(p => p.Key), ToolBankLookup.Diameters(banks, null).OrderBy(p => p.Key));
        Assert.Equal(3, ToolBankLookup.Diameters(banks, null)[1]);
    }

    [Fact]
    public void Com_outro_banco_ativo_o_Code_le_esse_banco()
    {
        Dictionary<int, double> d = ToolBankLookup.Diameters([Other, Migrated, Lathe], "outro");
        Assert.Equal(8, d[1]);
        Assert.Equal(25, d[2]); // o torno continua no banco principal do torno (PR D)
    }

    [Fact]
    public void Banco_ativo_apagado_ou_de_torno_cai_no_principal()
    {
        Assert.Equal(3, ToolBankLookup.Diameters([Other, Migrated], "apagado")[1]);
        Assert.Equal(3, ToolBankLookup.Diameters([Other, Migrated, Lathe], ToolBankImport.LatheBankId)[1]);
    }
}
