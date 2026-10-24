namespace CncStudio.Core.Optimizer;

/// <summary>Emits the WHILE loop of a repetition. Constant words keep their original text; a moving word
/// becomes an expression of the loop counters with explicit decimal points, e.g. Z[-1.0+#100*-1.5].
/// #100 counts the copies; a grid adds #101 for the inner loop (DO2) around the repeated part of the body.
/// Simulator review only.</summary>
public static class MacroGenerator
{
    public const string Footer = "(GCO FIM)";
    private static readonly HashSet<char> Dimensions = new("XYZIJKRQ"); // P, L, F, M are raw values

    /// <param name="title">Kind shown in the header; defaults to the detector kind.</param>
    public static string? Generate(IReadOnlyList<NcBlock> blocks, RepetitionCandidate c, NcProfile profile,
        int units, out string? error, string? title = null)
    {
        error = null;
        string outer = "#" + profile.CounterVariable, inner = "#" + (profile.CounterVariable + 1);
        if (c.Polar is not null && units != 21)
        { error = "Padrão circular exige G21 declarado (arredondamento de 0,001 mm)."; return null; }
        Func<int, NcWord[]> words = k => blocks[c.StartIndex + k].Words.Where(w => w.Letter != 'N').ToArray();
        bool grid = c.Kind == RepetitionKind.Grid;
        int innerStart = grid ? c.InnerOffset : -1, innerEnd = grid ? c.InnerOffset + c.InnerPeriod : -1;
        int skipEnd = grid ? c.InnerOffset + c.InnerPeriod * c.InnerCount : -1;
        var body = new List<string>();
        for (int j = 0; j < c.Period; j++)
        {
            if (j == innerStart)
            {
                body.Add(inner + " = 0");
                body.Add("WHILE [" + inner + " LT " + c.InnerCount + "] DO2");
            }
            if (j >= innerEnd && j < skipEnd) continue; // covered by the inner loop
            if (j == skipEnd)
            {
                body.Add(inner + " = " + inner + " + 1");
                body.Add("END2");
            }
            // N words are dropped: the detector ignores them, so all copies align without them.
            var first = words(j);
            var nextOuter = words(j + c.Period);
            var nextInner = j >= innerStart && j < innerEnd ? words(j + c.InnerPeriod) : first;
            var text = new List<string>();
            for (int w = 0; w < first.Length; w++)
            {
                var a = first[w];
                if (c.Polar is { } polar && j == polar.Block && a.Letter is 'X' or 'Y')
                {
                    var trig = a.Letter == 'X' ? "COS" : "SIN";
                    var center = a.Letter == 'X' ? polar.CenterX : polar.CenterY;
                    text.Add(a.Letter + "[" + NcTokenizer.Decimal(center) + "+" + NcTokenizer.Decimal(polar.Radius) + "*" + trig +
                             "[" + NcTokenizer.Decimal(polar.Start) + "+" + outer + "*" + NcTokenizer.Decimal(polar.Step) + "]]");
                    continue;
                }
                double Value(NcWord x) => Dimensions.Contains(x.Letter) ? profile.Dimension(x, units) : x.Value;
                double baseValue = Value(a);
                double stepOuter = Math.Round(Value(nextOuter[w]) - baseValue, 9);
                double stepInner = Math.Round(Value(nextInner[w]) - baseValue, 9);
                if (stepOuter == 0 && stepInner == 0) { text.Add(a.Text); continue; }
                if (Dimensions.Contains(a.Letter) && profile.DecimalInput == DecimalInput.Conventional && units is not (20 or 21) &&
                    new[] { a, nextOuter[w], nextInner[w] }.Any(x => !x.HasDecimalPoint))
                { error = "Palavra sem ponto decimal com G20/G21 desconhecido (entrada convencional)."; return null; }
                var expression = NcTokenizer.Decimal(baseValue);
                if (stepOuter != 0) expression += "+" + outer + "*" + NcTokenizer.Decimal(stepOuter);
                if (stepInner != 0) expression += "+" + inner + "*" + NcTokenizer.Decimal(stepInner);
                text.Add(a.Letter + "[" + expression + "]");
            }
            body.Add(string.Join(" ", text));
        }
        if (grid && skipEnd == c.Period)
        {
            body.Add(inner + " = " + inner + " + 1");
            body.Add("END2");
        }
        var size = grid
            ? c.Period + " BLOCOS X " + c.Count + " COM " + c.InnerPeriod + " BLOCOS X " + c.InnerCount + " INTERNOS"
            : c.Period + " BLOCOS X " + c.Count;
        var rows = new List<string>
        {
            "(GCO INICIO " + (title ?? Describe(c.Kind)) + " " + size + " - SOMENTE SIMULADOR - NAO USAR EM MAQUINA)",
            outer + " = 0",
            "WHILE [" + outer + " LT " + c.Count + "] DO1"
        };
        rows.AddRange(body);
        rows.Add(outer + " = " + outer + " + 1");
        rows.Add("END1");
        rows.Add(Footer);
        return string.Join("\n", rows);
    }

    public static string Describe(RepetitionKind kind) => kind switch
    {
        RepetitionKind.ZLevels => "NIVEIS Z",
        RepetitionKind.XYTranslation => "TRANSLACAO XY",
        RepetitionKind.Linear => "PASSOS LINEARES",
        RepetitionKind.Grid => "GRADE",
        RepetitionKind.Polar => "CIRCULO",
        _ => "REPETICAO IDENTICA"
    };
}
