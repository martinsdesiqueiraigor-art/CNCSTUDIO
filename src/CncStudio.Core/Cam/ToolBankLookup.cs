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

    /// <summary>Ferramentas que o simulador enxerga: as do banco de torno e, por cima (mesmo T), as do banco de fresa. Inativas contam.</summary>
    public static Dictionary<int, BankTool> Tools(IEnumerable<ToolBank> banks)
    {
        var list = banks.ToList();
        var tools = new Dictionary<int, BankTool>();
        foreach (ToolBank? bank in new[] { Primary(list, ToolBankMachine.Lathe), Primary(list, ToolBankMachine.Mill) })
        {
            foreach (BankTool t in bank?.Tools ?? [])
            {
                tools[t.Number] = t;
            }
        }

        return tools;
    }

    /// <summary>Ø por T (só os informados), como a tabela antiga dava ao executor.</summary>
    public static Dictionary<int, double> Diameters(IEnumerable<ToolBank> banks) =>
        Tools(banks).Where(p => p.Value.Diameter is not null).ToDictionary(p => p.Key, p => p.Value.Diameter!.Value);

    /// <summary>
    /// Ferramentas de torno do banco para o desenho do simulador do torno (antes vinha do tipo escolhido na tabela). Ø e inserto
    /// ausentes usam os mesmos números de antes (10 e 3), que o desenho exige; não são gravados no banco.
    /// </summary>
    public static IEnumerable<SetupTool> LatheSetupTools(IEnumerable<ToolBank> banks)
    {
        foreach (BankTool t in Primary(banks, ToolBankMachine.Lathe)?.Tools ?? [])
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
