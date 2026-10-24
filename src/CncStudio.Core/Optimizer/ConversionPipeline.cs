namespace CncStudio.Core.Optimizer;

/// <param name="Source">Why the operation starts here: INICIO, COMENTARIO, TROCA DE FERRAMENTA, ORIGEM,
/// CORRETOR H or SUBPROGRAMAS. Labels and sources are hints for the reader, never safety evidence.</param>
public sealed record Operation(int Number, int StartLine, int EndLine, string Label, int Tool, string Source,
    int H, string Wcs, int OriginalLines, int ConvertedLines);

/// <summary>A main-program block the interpreter did not understand (coverage report).</summary>
public sealed record UnmodeledBlock(int Line, string Text, string Note);

public sealed record Proposal(int Number, int Operation, string Kind, int StartLine, int EndLine,
    int Period, int Count, int SavedLines, string Status, IReadOnlyList<string> Blockers,
    IReadOnlyList<string> Warnings, string? Fragment, IReadOnlyList<string> Expanded)
{
    public bool Accepted => Status == ConversionPipeline.Accepted;
}

/// <param name="SimulationProgram">Loadable .nc with the accepted loops in place, for the simulator only.
/// Null when nothing was accepted or the whole-program check failed (see SimulationBlockers).</param>
/// <param name="Cam">CAM system recognized from comment signatures (hint only).</param>
/// <param name="Coverage">Share of main-program code blocks the interpreter modeled (0..1).</param>
public sealed record ConversionResult(string Profile, int Lines, IReadOnlyList<Operation> Operations,
    IReadOnlyList<Proposal> Proposals, string Document, int OriginalLines, int ConvertedLines,
    string? SimulationProgram, IReadOnlyList<string> SimulationBlockers,
    string Cam, int CodeBlocks, double Coverage, IReadOnlyList<UnmodeledBlock> Unmodeled)
{
    public bool MachineReady => false;
    public int SavedLines => OriginalLines - ConvertedLines;
}

/// <summary>Read → interpret → segment → detect → generate → verify → audit → review document.
/// The only entry point used by the API, the CLI and the tests.</summary>
public static class ConversionPipeline
{
    public const string Accepted = "PROPOSTA_SIMULADOR";
    public const string Blocked = "BLOQUEADO";

    public static ConversionResult Run(IReadOnlyList<string> lines, NcProfile profile)
    {
        var blocks = NcTokenizer.Tokenize(lines);
        var (mainEnd, subprograms) = Structure(blocks);
        var machine = new NcMachine(profile, NcState.Unknown, subprograms);
        var entries = new NcState[blocks.Count];
        var unmodeled = new List<UnmodeledBlock>();
        int codeBlocks = 0, notModeled = 0;
        for (int i = 0; i < blocks.Count; i++)
        {
            entries[i] = machine.State;
            if (i >= mainEnd) continue;
            var miss = machine.Execute(blocks[i]).FirstOrDefault(x => x.Unmodeled);
            if (!blocks[i].HasCode && blocks[i].Unsupported is null) continue;
            codeBlocks++;
            if (miss is null) continue;
            notModeled++;
            if (unmodeled.Count < 200) unmodeled.Add(new(blocks[i].Line, blocks[i].Raw.Trim(), miss.Note ?? "Nao modelado"));
        }
        var segments = Segment(blocks, entries, mainEnd);

        var proposals = new List<Proposal>();
        // Subprogram bodies are executed through M98 but never rewritten themselves.
        foreach (var c in new RepetitionDetector(profile).Detect(blocks.Take(mainEnd).ToArray()))
        {
            var entry = entries[c.StartIndex];
            var source = blocks.Skip(c.StartIndex).Take(c.Period * c.Count).ToArray();
            var blockers = new List<string>();
            var warnings = new List<string>();
            var steps = NcMachine.Run(profile, entry, source, subprograms);
            var kind = steps.Any(s => s.Arc?.Helical == true) ? "HELICE" : MacroGenerator.Describe(c.Kind);
            var fragment = MacroGenerator.Generate(blocks, c, profile, entry.Units, out var error, kind);
            IReadOnlyList<string> expanded = Array.Empty<string>();
            if (fragment is null) blockers.Add(error!);
            else
            {
                var check = EquivalenceChecker.Check(profile, entry, source, fragment, subprograms);
                expanded = check.ExpandedText;
                if (!check.Match) blockers.AddRange(check.Differences.Prepend("Expansao independente diverge do original."));
            }
            var audit = SafetyAudit.Check(entry, steps);
            blockers.AddRange(audit.Blockers);
            warnings.AddRange(audit.Warnings);
            if (source.Any(b => b.Words.Any(w => w.Letter == 'N')))
                warnings.Add("Numeros N do trecho removidos no corpo do loop.");
            var startLine = blocks[c.StartIndex].Line;
            var ok = blockers.Count == 0;
            proposals.Add(new(proposals.Count + 1,
                segments.LastOrDefault(o => o.StartLine <= startLine)?.Number ?? 0,
                kind, startLine, blocks[c.EndIndex].Line, c.Period, c.Count,
                ok ? c.Period * c.Count - fragment!.Split('\n').Length : 0,
                ok ? Accepted : Blocked, blockers, warnings, fragment, expanded));
        }
        var document = Document(lines, proposals, profile, out var converted);
        var simulation = Simulation(lines, blocks, mainEnd, subprograms, proposals, profile, out var simulationBlockers);
        var operations = segments.Select(o => o with
        {
            ConvertedLines = o.OriginalLines - proposals.Where(p => p.Accepted && p.Operation == o.Number).Sum(p => p.SavedLines)
        }).ToArray();
        return new(profile.Name, lines.Count, operations, proposals, document, lines.Count, converted,
            simulation, simulationBlockers, CamSignature.Detect(blocks), codeBlocks,
            codeBlocks == 0 ? 1 : Math.Round(1.0 - (double)notModeled / codeBlocks, 4), unmodeled);
    }

    /// <summary>Operation boundaries: a comment header once the current operation has moved, or, once it has
    /// cut (G1/G2/G3 or a drilled hole), a tool change (M6), a different work offset (G54-G59) or a
    /// different length offset (G43/G44 H); positioning before G43 never splits an operation.
    /// Works without CAM comments. Subprograms after M30 form their own section.</summary>
    private static IReadOnlyList<Operation> Segment(IReadOnlyList<NcBlock> blocks, NcState[] entries, int mainEnd)
    {
        var starts = new List<(int Index, string Source)> { (0, "INICIO") };
        bool cut = false, moved = false;
        for (int i = 0; i < blocks.Count; i++)
        {
            var b = blocks[i];
            var before = entries[i];
            var after = i + 1 < entries.Length ? entries[i + 1] : before;
            bool afterComment = i > 0 && blocks[i - 1].IsCommentOnly;
            string? source =
                i == mainEnd ? "SUBPROGRAMAS"
                : b.IsCommentOnly && i > 0 && !afterComment ? "COMENTARIO"
                : afterComment ? null
                : b.Words.Any(w => w.Letter == 'M' && w.Value == 6) ? "TROCA DE FERRAMENTA"
                : b.Words.FirstOrDefault(w => w.Letter == 'G' && w.Value is >= 54 and <= 59) is { } g && before.Wcs != "?" && before.Wcs != "G" + g.Value ? "ORIGEM"
                : b.Words.Any(w => w.Letter == 'G' && w.Value is 43 or 44) && b.Get('H') is { } h && before.H >= 0 && before.H != (int)h.Value ? "CORRETOR H"
                : null;
            bool open = source switch { "SUBPROGRAMAS" => true, "COMENTARIO" => moved, null => false, _ => cut };
            if (open) { starts.Add((i, source!)); cut = moved = false; }
            if (b.Words.Any(w => w.Letter is 'X' or 'Y' or 'Z'))
            {
                moved = true;
                if (after.Motion is 1 or 2 or 3 || after.Canned is not null) cut = true;
            }
        }
        var result = new List<Operation>();
        for (int k = 0; k < starts.Count; k++)
        {
            int s = starts[k].Index, e = k + 1 < starts.Count ? starts[k + 1].Index - 1 : blocks.Count - 1;
            var label = blocks.Skip(s).Take(e - s + 1).FirstOrDefault(b => b.Comment is not null)?.Comment ?? "";
            if (starts[k].Source == "SUBPROGRAMAS") label = "Subprogramas" + (label.Length > 0 ? " - " + label : "");
            var state = entries[e];
            if (label.Length == 0) label = state.Tool >= 0 ? "Ferramenta T" + state.Tool : "Trecho " + (result.Count + 1);
            result.Add(new(result.Count + 1, blocks[s].Line, blocks[e].Line, label.Length > 120 ? label[..120] : label,
                state.Tool, starts[k].Source, state.H, state.Wcs, e - s + 1, e - s + 1));
        }
        return result;
    }

    /// <summary>Whole program with the accepted loops substituted and a simulator-only header after the
    /// program number. The complete file is re-executed by <see cref="MacroInterpreter"/> and compared
    /// block by block with the complete original; any difference withholds the file.</summary>
    private static string? Simulation(IReadOnlyList<string> lines, IReadOnlyList<NcBlock> blocks, int mainEnd,
        IReadOnlyDictionary<int, IReadOnlyList<NcBlock>> subprograms, IReadOnlyList<Proposal> proposals,
        NcProfile profile, out IReadOnlyList<string> blockers)
    {
        var accepted = proposals.Where(p => p.Accepted).OrderBy(p => p.StartLine).ToArray();
        if (accepted.Length == 0)
        {
            blockers = new[] { "Nenhuma proposta aceita: o programa para simulacao seria igual ao original." };
            return null;
        }
        // A program that already uses macro statements cannot be re-executed as a whole by the restricted interpreter.
        if (blocks.Take(mainEnd).FirstOrDefault(b => b.Unsupported is not null) is { } foreign)
        {
            blockers = new[] { "Linha " + foreign.Line + " (" + foreign.Unsupported + "): o programa original nao pode ser conferido inteiro; use o documento de revisao." };
            return null;
        }
        var header = new[]
        {
            "(GCODEOPTIMIZER - PROGRAMA PARA SIMULACAO)",
            "(SOMENTE SIMULADOR - NAO USAR EM MAQUINA CNC)",
            "(" + Comment(profile.Name) + ")",
            "(" + Comment("VARIAVEIS #" + profile.Variables[0] + " A #" + profile.Variables[^1] + " - " + accepted.Length +
                " LOOPS - " + lines.Count + " LINHAS ORIGINAIS") + ")"
        };
        // After "%" and the O-number line, so the file still starts like a FANUC program.
        var at = blocks.TakeWhile(b => !b.HasCode || b.IsDelimiter || b.Words.All(w => w.Letter == 'O'))
            .Where(b => b.IsDelimiter || b.Words.Any(w => w.Letter == 'O')).Select(b => b.Line).DefaultIfEmpty(0).Last();
        var output = new List<string>();
        int cursor = 1;
        void Copy(int until)
        {
            for (; cursor < until; cursor++)
            {
                output.Add(lines[cursor - 1]);
                if (cursor == at) output.AddRange(header);
            }
        }
        if (at == 0) output.AddRange(header);
        foreach (var p in accepted)
        {
            Copy(p.StartLine);
            output.AddRange(p.Fragment!.Split('\n'));
            cursor = p.EndLine + 1;
        }
        Copy(blocks[mainEnd - 1].Line + 1);
        // Only the main program is re-executed: subprograms (unchanged text) run through its M98 calls.
        var main = string.Join("\n", output);
        Copy(lines.Count + 1);
        while (output.Count > 0 && output[^1].Length == 0) output.RemoveAt(output.Count - 1);
        var text = string.Join("\n", output);

        var original = blocks.Take(mainEnd).Where(b => b.HasCode || b.IsDelimiter || b.Unsupported is not null).ToArray();
        var check = EquivalenceChecker.Check(profile, NcState.Unknown, original, main, subprograms);
        blockers = check.Match ? Array.Empty<string>() : check.Differences.Prepend("Programa completo diverge do original.").ToArray();
        return check.Match ? text + "\n" : null;
    }

    /// <summary>Main program and subprograms of one file: when M30/M02 is followed by O-number programs
    /// ending in M99, the main program stops at M30 and each O block becomes a callable subprogram.</summary>
    private static (int MainEnd, IReadOnlyDictionary<int, IReadOnlyList<NcBlock>> Subprograms) Structure(IReadOnlyList<NcBlock> blocks)
    {
        var subprograms = new Dictionary<int, IReadOnlyList<NcBlock>>();
        int end = blocks.ToList().FindIndex(b => b.Words.Any(w => w.Letter == 'M' && w.Value is 30 or 2));
        if (end < 0) return (blocks.Count, subprograms);
        for (int i = end + 1; i < blocks.Count; i++)
        {
            if (blocks[i].Get('O') is not { } o) continue;
            int close = -1;
            for (int k = i + 1; k < blocks.Count && close < 0; k++)
            {
                if (blocks[k].Get('O') is not null) break;
                if (blocks[k].Words.Any(w => w.Letter == 'M' && w.Value == 99)) close = k;
            }
            if (close < 0) continue;
            subprograms[(int)o.Value] = blocks.Skip(i + 1).Take(close - i).ToArray();
            i = close;
        }
        return (subprograms.Count > 0 ? end + 1 : blocks.Count, subprograms);
    }

    private static string Comment(string text) =>
        new(text.ToUpperInvariant().Select(c => char.IsAsciiLetterOrDigit(c) || c is ' ' or '-' or '=' or '#' or '.' or ',' ? c : ' ').ToArray());

    private static string Document(IReadOnlyList<string> lines, IReadOnlyList<Proposal> proposals, NcProfile profile, out int convertedLines)
    {
        var output = new List<string>
        {
            "GCODEOPTIMIZER - PROPOSTA DE CONVERSAO PARA REVISAO",
            "NAO EXECUTAVEL - NAO IMPORTAR EM CONTROLE CNC - SOMENTE SIMULADOR",
            "PERFIL: " + profile.Name,
            "LINHAS FORA DAS CONVERSOES PRESERVADAS SEM ALTERACAO",
            ""
        };
        int cursor = 1; convertedLines = 0;
        foreach (var p in proposals.Where(p => p.Accepted).OrderBy(p => p.StartLine))
        {
            while (cursor < p.StartLine) { output.Add(lines[cursor - 1]); convertedLines++; cursor++; }
            output.Add("===== INICIO PROPOSTA " + p.Number + " - " + p.Kind + " - LINHAS " + p.StartLine + "-" + p.EndLine + " =====");
            var fragment = p.Fragment!.Split('\n');
            output.AddRange(fragment); convertedLines += fragment.Length;
            output.Add("===== FIM PROPOSTA " + p.Number + " =====");
            cursor = p.EndLine + 1;
        }
        while (cursor <= lines.Count) { output.Add(lines[cursor - 1]); convertedLines++; cursor++; }
        output.Add("");
        output.Add("===== BLOQUEIOS E AVISOS =====");
        foreach (var p in proposals)
        {
            foreach (var b in p.Blockers) output.Add("Proposta " + p.Number + " (linhas " + p.StartLine + "-" + p.EndLine + ") BLOQUEADA: " + b);
            foreach (var w in p.Warnings) output.Add("Proposta " + p.Number + " aviso: " + w);
        }
        output.Add("FIM DO DOCUMENTO - NAO EXECUTAVEL");
        return string.Join("\n", output);
    }
}
