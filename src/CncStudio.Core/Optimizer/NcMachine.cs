using System.Globalization;

namespace CncStudio.Core.Optimizer;

public enum PosKind { Absolute, RelativeToEntry, Unknown }

/// <summary>Axis position in mm. RelativeToEntry is used when the segment starts from an
/// unknown position (e.g. G91 code after G28) so two interpretations remain comparable.</summary>
public readonly record struct Pos(double Value, PosKind Kind)
{
    public static Pos Entry => new(0, PosKind.RelativeToEntry);
    public static Pos Unknown => new(0, PosKind.Unknown);
    public bool IsAbsolute => Kind == PosKind.Absolute;
    public override string ToString() => Kind switch
    {
        PosKind.Absolute => NcTokenizer.Decimal(Value),
        PosKind.RelativeToEntry => "inicio" + (Value >= 0 ? "+" : "") + NcTokenizer.Decimal(Value),
        _ => "?"
    };
}

/// <summary>Active canned cycle (G73/G74/G76/G81-G89) in G90/G17. R/Z/Q are absolute mm (NaN = not given);
/// Initial is the Z level when the cycle mode started (return level for G98).</summary>
public sealed record CannedCycle(int Code, double R, double Z, double Q, double P, Pos Initial);

/// <summary>Modal state after a block. -1 / "?" means not established.
/// Feed is kept in program units (mm/min, inch/min or per revolution).</summary>
public sealed record NcState(int Motion, int Plane, int Distance, int Units, int FeedMode, string Wcs,
    int ToolLength, int H, int CutterComp, int D, int Tool, int NextTool, double Feed, double Spindle,
    int SpindleDirection, int Coolant, int ReturnMode, CannedCycle? Canned, Pos X, Pos Y, Pos Z, bool Tainted)
{
    /// <summary>Nothing known: used when a fragment is compared out of context.</summary>
    public static NcState Unknown { get; } = new(-1, -1, -1, -1, -1, "?", -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, -1, null,
        Pos.Entry, Pos.Entry, Pos.Entry, false);
}

/// <summary>Geometry of an executed G02/G03 block (mm). Radii are NaN when a position is unknown.
/// Helical: the axis normal to the plane moved during the arc.</summary>
public sealed record NcArc(int Plane, double StartRadius, double EndRadius, bool Helical, bool UsesR);

/// <summary>One executed block: canonical effective words plus the state after it.
/// Two traces are equivalent when every step is equal (line numbers excluded; Arc is derived data).
/// Unmodeled: the block was not understood (positions or modal state may be lost after it).</summary>
public sealed record NcStep(int Line, int Motion, string Words, NcState After, string? Note, NcArc? Arc = null, bool Unmodeled = false)
{
    public bool SameAs(NcStep other) => Motion == other.Motion && Words == other.Words && After == other.After;
}

/// <summary>Restricted FANUC milling interpreter for programmed coordinates. It models the
/// modal groups used by CAM output and marks everything else as tainted instead of guessing.
/// It is not a controller simulation and grants no machine permission.</summary>
public sealed class NcMachine
{
    private static readonly HashSet<char> Dimensions = new("XYZIJKRQ");
    private static readonly HashSet<char> Known = new("GMNOFSTHDXYZIJKRQPL");
    private const int MaxDepth = 10;
    private readonly NcProfile profile;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<NcBlock>> subprograms;
    private (int Program, int Count)? call;
    private int depth;

    /// <param name="subprograms">O-number → blocks (up to and including M99) found in the same file.
    /// M98 calls to them are executed; calls to anything else lose the position.</param>
    public NcMachine(NcProfile profile, NcState? entry = null, IReadOnlyDictionary<int, IReadOnlyList<NcBlock>>? subprograms = null)
    {
        this.profile = profile;
        this.subprograms = subprograms ?? new Dictionary<int, IReadOnlyList<NcBlock>>();
        State = entry ?? NcState.Unknown;
    }

    public NcState State { get; private set; }

    public static IReadOnlyList<NcStep> Run(NcProfile profile, NcState entry, IEnumerable<NcBlock> blocks,
        IReadOnlyDictionary<int, IReadOnlyList<NcBlock>>? subprograms = null)
    {
        var machine = new NcMachine(profile, entry, subprograms);
        return blocks.SelectMany(machine.Execute).ToArray();
    }

    /// <summary>Executes one block; an M98 call yields the call step followed by every step of the
    /// subprogram (repeated L times, nested up to 10 levels). M99 returns.</summary>
    public IEnumerable<NcStep> Execute(NcBlock block)
    {
        var step = Step(block);
        yield return step;
        if (call is not { } c) yield break;
        call = null;
        depth++;
        try
        {
            for (int k = 0; k < c.Count; k++)
                foreach (var sub in subprograms[c.Program])
                {
                    if (sub.Words.Any(w => w.Letter == 'M' && w.Value == 99))
                    {
                        var rest = sub with { Words = sub.Words.Where(w => !(w.Letter == 'M' && w.Value == 99)).ToArray() };
                        if (rest.Words.Any(w => w.Letter != 'N')) foreach (var x in Execute(rest)) yield return x;
                        break;
                    }
                    foreach (var x in Execute(sub)) yield return x;
                }
        }
        finally { depth--; }
    }

    public NcStep Step(NcBlock block)
    {
        var s = State;
        if (block.IsDelimiter || (!block.HasCode && block.Unsupported is null))
            return new(block.Line, -1, "", s, null);
        if (block.Unsupported is not null)
            return Taint(block, "Não modelado: " + block.Unsupported, lose: true);

        int? motion = null, cycle = null; bool dwell = false, reference = false; string? note = null; bool lose = false, taint = false;
        // Value words first so that T1 M6 in one block activates tool 1.
        foreach (var w in block.Words)
            switch (w.Letter)
            {
                case 'F': s = s with { Feed = w.Value }; break;
                case 'S': s = s with { Spindle = w.Value }; break;
                case 'T': s = s with { NextTool = (int)w.Value }; break;
                case 'H': s = s with { H = (int)w.Value }; break;
                case 'D': s = s with { D = (int)w.Value }; break;
            }
        foreach (var w in block.Words)
        {
            if (!Known.Contains(w.Letter)) { taint = true; note = "Endereço não modelado: " + w.Letter; }
            if (w.Letter == 'G')
            {
                var g = w.Value;
                switch (g)
                {
                    case 0 or 1 or 2 or 3:
                        if (motion is not null) { taint = true; note = "Dois codigos de movimento no bloco"; }
                        motion = (int)g; s = s with { Canned = null }; break;
                    case 73 or 74 or 76 or (>= 81 and <= 89) when g == Math.Floor(g):
                        if (cycle is not null) { taint = true; note = "Dois ciclos fixos no bloco"; }
                        cycle = (int)g; break;
                    case 4: dwell = true; break;
                    case 17 or 18 or 19: s = s with { Plane = (int)g }; break;
                    case 20 or 21: s = s with { Units = (int)g }; break;
                    case 90 or 91: s = s with { Distance = (int)g }; break;
                    case 94 or 95: s = s with { FeedMode = (int)g }; break;
                    case 40 or 41 or 42: s = s with { CutterComp = (int)g }; break;
                    case 43 or 44 or 49: s = s with { ToolLength = (int)g }; break;
                    case >= 54 and <= 59 when g == Math.Floor(g): s = s with { Wcs = "G" + (int)g }; break;
                    case 28 or 30 or 53: reference = true; break; // reference return / machine coordinates
                    case 80: s = s with { Canned = null }; break;
                    case 98 or 99: s = s with { ReturnMode = (int)g }; break;
                    default:
                        // G28/G30/G53/G92/G52/G68/canned cycles move or redefine axes: positions are lost.
                        taint = lose = true; note = "G" + g.ToString(CultureInfo.InvariantCulture) + " não modelado"; break;
                }
            }
            else if (w.Letter == 'M')
            {
                switch (w.Value)
                {
                    case 3 or 4 or 5: s = s with { SpindleDirection = (int)w.Value }; break;
                    case 6: s = s with { Tool = s.NextTool }; break;
                    case 7 or 8 or 9: s = s with { Coolant = (int)w.Value }; break;
                    case 0 or 1 or 2 or 30: break;
                    case 98:
                        var p = (int)(block.Get('P')?.Value ?? -1);
                        var (program, count) = p > 9999 ? (p % 10000, p / 10000) : (p, 1);
                        if (block.Get('L') is { } l) count = (int)l.Value;
                        if (subprograms.ContainsKey(program) && depth < MaxDepth && count is >= 1 and <= 9999) call = (program, count);
                        else { taint = lose = true; note = "M98 P" + p + ": subprograma fora do arquivo, recursivo ou repeticao inválida"; }
                        break;
                    case 99: taint = lose = true; note = "M99 fora de subprograma não modelado"; break;
                    default: taint = true; note = "M" + w.Value.ToString(CultureInfo.InvariantCulture) + " não modelado"; break;
                }
            }
        }
        var units = block.Words.Where(w => w.Letter == 'G' && w.Value is 20 or 21).Select(w => (int)w.Value).DefaultIfEmpty(s.Units).Last();
        if (motion is not null) s = s with { Motion = motion.Value };
        var words = Canonical(block, units);
        if (motion is not null && cycle is not null) { taint = true; note = "Movimento e ciclo fixo no mesmo bloco"; }
        if (!taint && (cycle is not null || s.Canned is not null))
            return Drill(block, s, cycle, units, words);
        if (taint)
        {
            call = null;
            State = Lose(s, lose) with { Tainted = true };
            return new(block.Line, -1, words, State, note, Unmodeled: true);
        }
        int executed = -1;
        bool axes = block.Words.Any(w => w.Letter is 'X' or 'Y' or 'Z');
        if (reference)
        {
            // G28/G30 go to the machine reference (through the given point); G53 moves in machine
            // coordinates. Either way the named axes end at a position unknown in work coordinates.
            s = s with
            {
                X = block.Get('X') is null ? s.X : Pos.Unknown,
                Y = block.Get('Y') is null ? s.Y : Pos.Unknown,
                Z = block.Get('Z') is null ? s.Z : Pos.Unknown
            };
            State = s;
            return new(block.Line, -1, words, s, null);
        }
        if (axes && !dwell)
        {
            double scale = units == 20 ? 25.4 : 1;
            s = s with
            {
                X = Move(s.X, block.Get('X'), s.Distance, units, scale),
                Y = Move(s.Y, block.Get('Y'), s.Distance, units, scale),
                Z = Move(s.Z, block.Get('Z'), s.Distance, units, scale)
            };
            executed = s.Motion;
            if (s.Distance == -1) { s = s with { Tainted = true }; note = "G90/G91 não estabelecido"; }
        }
        var arc = executed is 2 or 3 ? Arc(block, State, s, units) : null;
        State = s;
        return new(block.Line, executed, words, s, note, arc, Unmodeled: note is not null);
    }

    /// <summary>Canned cycle block: R/Z/Q/P are retained until G80; a block with X/Y/Z/R drills one hole.
    /// After the hole Z is the return level: initial level (G98) or R (G99). Only G90/G17 is modeled.</summary>
    private NcStep Drill(NcBlock block, NcState s, int? code, int units, string words)
    {
        if (s.Distance != 90 || s.Plane != 17)
        {
            State = Lose(s, true) with { Tainted = true };
            return new(block.Line, -1, words, State, "Ciclo fixo fora de G90/G17 não modelado", Unmodeled: true);
        }
        double scale = units == 20 ? 25.4 : 1;
        double Dim(char letter, double old) => block.Get(letter) is { } w ? Math.Round(profile.Dimension(w, units) * scale, 6) : old;
        var c = s.Canned ?? new CannedCycle(code!.Value, double.NaN, double.NaN, double.NaN, double.NaN, s.Z);
        c = c with
        {
            Code = code ?? c.Code, R = Dim('R', c.R), Z = Dim('Z', c.Z), Q = Dim('Q', c.Q),
            P = block.Get('P')?.Value ?? c.P
        };
        s = s with { Canned = c };
        string? note = null;
        int executed = -1;
        if (block.Words.Any(w => w.Letter is 'X' or 'Y' or 'Z' or 'R'))
        {
            executed = c.Code;
            s = s with
            {
                X = Move(s.X, block.Get('X'), 90, units, scale),
                Y = Move(s.Y, block.Get('Y'), 90, units, scale),
                Z = double.IsNaN(c.R) || double.IsNaN(c.Z) ? Pos.Unknown
                    : s.ReturnMode == 99 ? new(c.R, PosKind.Absolute)
                    : s.ReturnMode == 98 ? c.Initial : Pos.Unknown
            };
            if (s.Z.Kind == PosKind.Unknown) note = "Ciclo fixo sem R/Z ou sem G98/G99: nivel de retorno desconhecido";
        }
        State = s;
        return new(block.Line, executed, words, s, note);
    }

    /// <summary>Start/end radius of an arc (FANUC: I/J/K are always incremental from the start point).</summary>
    private NcArc Arc(NcBlock block, NcState before, NcState after, int units)
    {
        double scale = units == 20 ? 25.4 : 1;
        double Word(char letter) => block.Get(letter) is { } w ? profile.Dimension(w, units) * scale : 0;
        var (a, b, normal, ia, ib) = before.Plane switch
        {
            18 => ('Z', 'X', 'Y', 'K', 'I'),
            19 => ('Y', 'Z', 'X', 'J', 'K'),
            _ => ('X', 'Y', 'Z', 'I', 'J')
        };
        Pos Get(NcState st, char axis) => axis switch { 'X' => st.X, 'Y' => st.Y, _ => st.Z };
        bool helical = Get(before, normal) != Get(after, normal);
        Pos sa = Get(before, a), sb = Get(before, b), ea = Get(after, a), eb = Get(after, b);
        bool known = sa.Kind != PosKind.Unknown && sa.Kind == ea.Kind && sb.Kind != PosKind.Unknown && sb.Kind == eb.Kind;
        bool usesR = block.Get('R') is not null;
        if (!known) return new(before.Plane, double.NaN, double.NaN, helical, usesR);
        if (usesR)
        {
            double r = Math.Abs(Word('R')), half = Math.Sqrt(Math.Pow(ea.Value - sa.Value, 2) + Math.Pow(eb.Value - sb.Value, 2)) / 2;
            return new(before.Plane, r, Math.Max(r, half), helical, true);
        }
        double ca = sa.Value + Word(ia), cb = sb.Value + Word(ib);
        double start = Math.Sqrt(Math.Pow(sa.Value - ca, 2) + Math.Pow(sb.Value - cb, 2));
        double end = Math.Sqrt(Math.Pow(ea.Value - ca, 2) + Math.Pow(eb.Value - cb, 2));
        return new(before.Plane, Math.Round(start, 6), Math.Round(end, 6), helical, false);
    }

    private NcStep Taint(NcBlock block, string note, bool lose)
    {
        State = Lose(State, lose) with { Tainted = true };
        return new(block.Line, -1, block.Raw.Trim(), State, note, Unmodeled: true);
    }

    private static NcState Lose(NcState s, bool lose) =>
        lose ? s with { X = Pos.Unknown, Y = Pos.Unknown, Z = Pos.Unknown } : s;

    private Pos Move(Pos current, NcWord? word, int distance, int units, double scale)
    {
        if (word is null) return current;
        var value = Math.Round(profile.Dimension(word, units) * scale, 6);
        return distance switch
        {
            90 => new(value, PosKind.Absolute),
            91 when current.Kind != PosKind.Unknown => new(Math.Round(current.Value + value, 6), current.Kind),
            _ => Pos.Unknown
        };
    }

    /// <summary>Order-independent effective words: dimensions converted by the decimal-input rule
    /// (+ 0.0 turns -0 into 0: X-0. and X0. are the same position).</summary>
    private string Canonical(NcBlock block, int units) => string.Join(" ", block.Words
        .Where(w => w.Letter is not ('N' or 'O'))
        .Select(w => (w.Letter, Value: (Dimensions.Contains(w.Letter) ? Math.Round(profile.Dimension(w, units), 6) : w.Value) + 0.0))
        .OrderBy(w => w.Letter).ThenBy(w => w.Value)
        .Select(w => w.Letter + w.Value.ToString("R", CultureInfo.InvariantCulture)));
}
