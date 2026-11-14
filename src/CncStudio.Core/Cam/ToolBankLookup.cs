using CncStudio.Core.Setup;

namespace CncStudio.Core.Cam;

/// <summary>
/// O que o simulador do Code lê do banco (UX-04h B1), no lugar da tabela antiga. Vale um banco por máquina: o migrado
/// ("Ferramentas fresa" / "Ferramentas torno"); sem ele, o primeiro em ordem de nome. Campo ausente continua ausente (sem padrão novo).
/// </summary>
public static class ToolBankLookup
{
    public static ToolBank? Primary(IEnumerable<ToolBank> banks, ToolBankMachine machine)
    {
        string id = machine == ToolBankMachine.Mill ? ToolBankImport.MillBankId : ToolBankImport.LatheBankId;
        var same = banks.Where(b => b.Machine == machine).ToList();
        return same.FirstOrDefault(b => b.Id == id)
            ?? same.OrderBy(b => b.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(b => b.Id, StringComparer.Ordinal).FirstOrDefault();
    }

    /// <summary>
    /// Ferramentas que o simulador enxerga: as do banco de torno e, por cima (mesmo T), as do banco de fresa. Inativas contam.
    /// O banco de fresa é o ativo (<paramref name="activeMillId"/>, UX-04h C); sem escolha ou banco apagado, o <see cref="Primary"/>
    /// (<see cref="ToolBankPick.ActiveMillBank"/>), o mesmo de antes. O de torno é o ativo do torno (<paramref name="activeLatheId"/>,
    /// UX-04h D1), com a mesma regra (<see cref="ToolBankPick.ActiveLatheBank"/>). A fresa continua valendo por cima no mesmo T.
    /// </summary>
    public static Dictionary<int, BankTool> Tools(IEnumerable<ToolBank> banks, string? activeMillId = null, string? activeLatheId = null)
    {
        var list = banks.ToList();
        var tools = new Dictionary<int, BankTool>();
        foreach (ToolBank? bank in new[] { ToolBankPick.ActiveLatheBank(list, activeLatheId), ToolBankPick.ActiveMillBank(list, activeMillId) })
        {
            foreach (BankTool t in bank?.Tools ?? [])
            {
                tools[t.Number] = t;
            }
        }

        return tools;
    }

    /// <summary>Ø por T (só os informados), como a tabela antiga dava ao executor.</summary>
    public static Dictionary<int, double> Diameters(IEnumerable<ToolBank> banks, string? activeMillId = null, string? activeLatheId = null) =>
        Tools(banks, activeMillId, activeLatheId).Where(p => p.Value.Diameter is not null).ToDictionary(p => p.Key, p => p.Value.Diameter!.Value);

    /// <summary>
    /// Ferramentas de torno do banco para o desenho do simulador do torno (antes vinha do tipo escolhido na tabela). Ø e inserto
    /// ausentes usam os mesmos números de antes (10 e 3), que o desenho exige; não são gravados no banco. Lê o banco ativo do torno
    /// (UX-04h D1; sem escolha ou banco apagado, o <see cref="Primary"/>).
    /// </summary>
    public static IEnumerable<SetupTool> LatheSetupTools(IEnumerable<ToolBank> banks, string? activeLatheId = null)
    {
        foreach (BankTool t in ToolBankPick.ActiveLatheBank(banks, activeLatheId)?.Tools ?? [])
        {
            if (t.Type is { } type && BankToolTypes.ToToolKind(type) is { } kind && new SetupTool { Kind = kind }.IsLathe)
            {
                yield return new SetupTool
                {
                    Kind = kind,
                    Number = t.Number,
                    Diameter = t.Diameter ?? 10,
                    InsertWidth = t.InsertWidth ?? SetupTool.DefaultInsertWidth,
                };
            }
        }
    }
}
