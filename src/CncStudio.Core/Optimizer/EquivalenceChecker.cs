namespace CncStudio.Core.Optimizer;

public sealed record EquivalenceResult(bool Match, int SourceBlocks, int ExpandedBlocks,
    IReadOnlyList<string> Differences, IReadOnlyList<string> ExpandedText);

/// <summary>Executes the fragment with <see cref="MacroInterpreter"/> and compares, block by block,
/// the interpreted state of the expansion with the interpreted state of the original lines,
/// both starting from the same entry state. Line numbers and N words are not compared.</summary>
public static class EquivalenceChecker
{
    public static EquivalenceResult Check(NcProfile profile, NcState entry,
        IReadOnlyList<NcBlock> source, string fragment, IReadOnlyDictionary<int, IReadOnlyList<NcBlock>>? subprograms = null)
    {
        var differences = new List<string>();
        var expansion = MacroInterpreter.Expand(fragment, entry.Units == 20 ? 20 : 21);
        if (!expansion.Ok)
            return new(false, source.Count, expansion.Blocks.Count, new[] { "Macro invalida: " + expansion.Error }, expansion.Text);
        var foreign = expansion.Variables.Keys.Where(v => !profile.Variables.Contains(v)).ToArray();
        if (foreign.Length > 0)
            differences.Add("Variaveis fora do banco do perfil: " + string.Join(", ", foreign.Select(v => "#" + v)));
        var original = NcMachine.Run(profile, entry, source, subprograms);
        var expanded = NcMachine.Run(profile, entry, expansion.Blocks, subprograms);
        if (original.Count != expanded.Count)
            differences.Add("Quantidade de blocos: original " + original.Count + ", expansao " + expanded.Count + ".");
        for (int i = 0; i < Math.Min(original.Count, expanded.Count) && differences.Count < 12; i++)
            if (!original[i].SameAs(expanded[i]))
                differences.Add("Linha " + source[i].Line + ": original [" + original[i].Words + "] expansao [" + expanded[i].Words + "]" +
                    (original[i].Words == expanded[i].Words ? " (estado modal diferente)" : ""));
        if (differences.Count == 0 && original.Count > 0 && original[^1].After != expanded[^1].After)
            differences.Add("Estado final diferente.");
        return new(differences.Count == 0, original.Count, expanded.Count, differences, expansion.Text);
    }
}
