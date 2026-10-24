namespace CncStudio.Core.Optimizer;

public enum RepetitionKind { Identical, ZLevels, XYTranslation, Linear, Grid, Polar }

/// <summary>Copies of one XY block placed on a circle: point k is at angle Start + k * Step (degrees).</summary>
public sealed record PolarPattern(int Block, double CenterX, double CenterY, double Radius, double Start, double Step);

/// <summary>A run of <see cref="Count"/> consecutive copies of a <see cref="Period"/>-block body.
/// Linear kinds: every numeric word of copy k equals copy 0 + k * (copy 1 - copy 0).
/// Grid: blocks [InnerOffset, InnerOffset + InnerPeriod * InnerCount) of the body are themselves linear
/// copies (an inner loop, e.g. pocket rings inside each Z level, or columns inside each row).
/// Polar: one block moves on a circle, every other word is identical.</summary>
public sealed record RepetitionCandidate(int StartIndex, int Period, int Count, RepetitionKind Kind,
    int InnerPeriod = 0, int InnerCount = 0, PolarPattern? Polar = null, int InnerOffset = 0)
{
    public int EndIndex => StartIndex + Period * Count - 1;
    public int BodyLines => Kind == RepetitionKind.Grid ? Period - InnerPeriod * (InnerCount - 1) : Period;
    /// <summary>Header, counter, WHILE, increment, END, footer (+4 for the inner loop of a grid).</summary>
    public int Overhead => Kind == RepetitionKind.Grid ? 10 : 6;
    public int SavedLines => Period * Count - BodyLines - Overhead;
}

/// <summary>Single detector for repetitions: depth levels, identical passes, XY translation, concentric
/// pocket rings (per-word linear steps), 2D grids (two nested loops) and bolt circles. It reads tokenized
/// words only; every candidate must still pass <see cref="EquivalenceChecker"/> and <see cref="SafetyAudit"/>.</summary>
public sealed class RepetitionDetector
{
    private static readonly HashSet<char> BodyLetters = new("GNXYZIJKRQPFML");
    private static readonly HashSet<double> BodyGCodes = new() { 0, 1, 2, 3, 17, 18, 19, 90, 91 };
    private const double Tolerance = 1e-9;
    private readonly NcProfile profile;
    private int[] shape = Array.Empty<int>();
    private int[] nextBad = Array.Empty<int>();
    private double[][] values = Array.Empty<double[]>();
    private char[][] letters = Array.Empty<char[]>();

    public RepetitionDetector(NcProfile profile) => this.profile = profile;

    public IReadOnlyList<RepetitionCandidate> Detect(IReadOnlyList<NcBlock> blocks)
    {
        int n = blocks.Count;
        shape = new int[n];
        nextBad = new int[n + 1];
        nextBad[n] = n;
        var ids = new Dictionary<string, int>();
        for (int i = n - 1; i >= 0; i--)
        {
            var key = Eligible(blocks[i]) ? Shape(blocks[i]) : null;
            shape[i] = key is null ? -1 : ids.TryGetValue(key, out var id) ? id : ids[key] = ids.Count;
            nextBad[i] = key is null ? i : nextBad[i + 1];
        }
        values = blocks.Select(Values).ToArray();
        letters = blocks.Select(b => b.Words.Where(w => w.Letter is not ('N' or 'G')).Select(w => w.Letter).ToArray()).ToArray();

        var cache = new Dictionary<int, RepetitionCandidate?>();
        RepetitionCandidate? Best(int i) => cache.TryGetValue(i, out var c) ? c : cache[i] = FindBest(i);
        var found = new List<RepetitionCandidate>();
        for (int i = 0; i < n;)
        {
            var best = Best(i);
            if (best is null) { i++; continue; }
            // A run that starts one block later can align better (e.g. a retract shared by two passes).
            if (Best(i + 1) is { } next && next.SavedLines > best.SavedLines) { i++; continue; }
            found.Add(best);
            i = best.EndIndex + 1;
        }
        return found;
    }

    private RepetitionCandidate? FindBest(int i)
    {
        if (i >= shape.Length || shape[i] < 0) return null;
        RepetitionCandidate? best = null;
        int maxPeriod = Math.Min(profile.MaxPeriod, (nextBad[i] - i) / 2);
        for (int p = 1; p <= maxPeriod; p++)
        {
            if (shape[i + p] != shape[i]) continue;
            foreach (var candidate in new[] { Linear(i, p, nextBad[i]), Polar(i, p, nextBad[i]) })
            {
                if (candidate is null) continue;
                var c = Grid(candidate) ?? candidate;
                if (c.SavedLines >= profile.MinSavedLines && (best is null || c.SavedLines > best.SavedLines)) best = c;
            }
        }
        return best;
    }

    /// <summary>Arithmetic copies. Uniform steps (one per axis, nothing else moving) accept 2 copies;
    /// per-word steps such as concentric pocket rings need at least 3 so a pair of unrelated
    /// contours with the same shape is never turned into a loop.</summary>
    private RepetitionCandidate? Linear(int start, int p, int limit)
    {
        double sx = 0, sy = 0, sz = 0;
        bool uniform = true, any = false, onlyZ = true;
        bool thirdFailed = start + 3 * p > limit;
        for (int j = 0; j < p; j++)
        {
            int a = start + j, b = start + p + j;
            if (shape[a] != shape[b]) return null;
            var va = values[a]; var vb = values[b];
            if (!thirdFailed && (shape[b + p] != shape[a] || !OnLine(va, vb, values[b + p], 2))) thirdFailed = true;
            for (int w = 0; w < va.Length; w++)
            {
                var d = Math.Round(vb[w] - va[w], 9);
                if (d == 0) continue;
                any = true;
                var letter = letters[a][w];
                if (letter is 'F' or 'M' or 'P' or 'L') return null; // feed and subprogram calls never vary between copies
                if (letter != 'Z') onlyZ = false;
                ref double step = ref (letter == 'X' ? ref sx : ref (letter == 'Y' ? ref sy : ref sz));
                if (letter is not ('X' or 'Y' or 'Z')) uniform = false;
                else if (step != 0 && Math.Abs(step - d) > Tolerance) uniform = false;
                else step = d;
            }
            // Fail fast: this runs for every (start, period) pair, mostly on same-shape surface blocks.
            if (!uniform && thirdFailed) return null;
        }
        int count = 2;
        while (start + (count + 1) * p <= limit && Continues(start, p, count)) count++;
        var kind = !any ? RepetitionKind.Identical
            : uniform && onlyZ ? RepetitionKind.ZLevels
            : uniform ? RepetitionKind.XYTranslation
            : RepetitionKind.Linear;
        if (kind == RepetitionKind.Linear && count < 3) return null;
        return new(start, p, count, kind);
    }

    private static bool OnLine(double[] v0, double[] v1, double[] vk, int k)
    {
        for (int w = 0; w < v0.Length; w++)
            if (Math.Abs(vk[w] - (v0[w] + k * (v1[w] - v0[w]))) > Tolerance) return false;
        return true;
    }

    private bool Continues(int start, int p, int count)
    {
        int c = start + count * p;
        for (int j = 0; j < p; j++)
        {
            if (shape[c + j] != shape[start + j]) return false;
            if (!OnLine(values[start + j], values[start + p + j], values[c + j], count)) return false;
        }
        return true;
    }

    /// <summary>Part of the body of a linear run is itself a linear run: two nested loops
    /// (rows x columns, Z levels x pocket rings). Searched only for bodies up to 300 blocks.</summary>
    private RepetitionCandidate? Grid(RepetitionCandidate outer)
    {
        if (outer.Kind == RepetitionKind.Polar || outer.Period > 300) return null;
        RepetitionCandidate? best = null;
        for (int o = 0; o < outer.Period; o++)
        {
            int at = outer.StartIndex + o, room = outer.Period - o;
            for (int q = 1; q <= room / 2; q++)
            {
                if (shape[at + q] != shape[at]) continue;
                var inner = Linear(at, q, at + room);
                if (inner is null) continue;
                var grid = outer with { Kind = RepetitionKind.Grid, InnerOffset = o, InnerPeriod = q, InnerCount = inner.Count };
                if (grid.SavedLines > (best ?? outer).SavedLines) best = grid;
            }
        }
        return best;
    }

    /// <summary>Bolt circle: exactly one block of the body has both X and Y moving, on a circle with a
    /// constant angle step; every other word is identical. Source values may be rounded to 0.001,
    /// so points are matched within half an increment; the exact check is done after generation.</summary>
    private RepetitionCandidate? Polar(int start, int p, int limit)
    {
        if (start + 3 * p > limit) return null;
        int moving = -1, xw = -1, yw = -1;
        for (int j = 0; j < p; j++)
        {
            for (int k = 1; k <= 2; k++)
            {
                int a = start + j, b = start + k * p + j;
                if (shape[a] != shape[b]) return null;
                for (int w = 0; w < values[a].Length; w++)
                {
                    if (Math.Abs(values[b][w] - values[a][w]) <= Tolerance) continue;
                    var letter = letters[a][w];
                    if (letter is not ('X' or 'Y') || (moving >= 0 && moving != j)) return null;
                    moving = j;
                    if (letter == 'X') xw = w; else yw = w;
                }
            }
        }
        if (moving < 0 || xw < 0 || yw < 0) return null;
        (double X, double Y) Point(int k) => (values[start + k * p + moving][xw], values[start + k * p + moving][yw]);
        var (p0, p1, p2) = (Point(0), Point(1), Point(2));
        double d = 2 * (p0.X * (p1.Y - p2.Y) + p1.X * (p2.Y - p0.Y) + p2.X * (p0.Y - p1.Y));
        if (Math.Abs(d) < 1e-9) return null; // collinear: the linear detector covers it
        double s0 = p0.X * p0.X + p0.Y * p0.Y, s1 = p1.X * p1.X + p1.Y * p1.Y, s2 = p2.X * p2.X + p2.Y * p2.Y;
        double cx = (s0 * (p1.Y - p2.Y) + s1 * (p2.Y - p0.Y) + s2 * (p0.Y - p1.Y)) / d;
        double cy = (s0 * (p2.X - p1.X) + s1 * (p0.X - p2.X) + s2 * (p1.X - p0.X)) / d;
        double r = Math.Sqrt((p0.X - cx) * (p0.X - cx) + (p0.Y - cy) * (p0.Y - cy));
        // CAM circles have round centers/radii; the source points are rounded, so snap before predicting.
        (cx, cy, r) = (Math.Round(cx, 3), Math.Round(cy, 3), Math.Round(r, 3));
        double Angle((double X, double Y) pt) => Math.Atan2(pt.Y - cy, pt.X - cx) * 180 / Math.PI;
        double Wrap(double a) => a - 360 * Math.Round(a / 360);
        double a0 = Math.Round(Angle(p0), 4), step = Math.Round(Wrap(Angle(p1) - Angle(p0)), 4);
        if (Math.Abs(step) < 1e-6) return null;
        bool Matches(int k)
        {
            var (x, y) = Point(k);
            double a = (a0 + k * step) * Math.PI / 180;
            return Math.Abs(x - (cx + r * Math.Cos(a))) <= 0.0005 + 1e-9 && Math.Abs(y - (cy + r * Math.Sin(a))) <= 0.0005 + 1e-9;
        }
        if (!Matches(0) || !Matches(1) || !Matches(2)) return null;
        int count = 3;
        while (start + (count + 1) * p <= limit && Math.Abs(step) * count < 360 - 1e-6 && SameExcept(start, p, count, moving) && Matches(count)) count++;
        return new(start, p, count, RepetitionKind.Polar, Polar: new(moving, cx, cy, r, a0, step));
    }

    private bool SameExcept(int start, int p, int k, int moving)
    {
        for (int j = 0; j < p; j++)
        {
            int a = start + j, b = start + k * p + j;
            if (shape[a] != shape[b]) return false;
            for (int w = 0; w < values[a].Length; w++)
                if (Math.Abs(values[b][w] - values[a][w]) > Tolerance && !(j == moving && letters[a][w] is 'X' or 'Y')) return false;
        }
        return true;
    }

    private static bool Eligible(NcBlock b) =>
        b.Unsupported is null && b.Comment is null && b.HasCode && !b.IsDelimiter &&
        b.Words.All(w => BodyLetters.Contains(w.Letter) && (w.Letter != 'G' || BodyGCodes.Contains(w.Value)) &&
                         (w.Letter != 'M' || w.Value == 98)) &&
        b.Words.Any(w => w.Letter != 'N');

    /// <summary>Letters in order with G values fixed; N is ignored (it is dropped from loop bodies).</summary>
    private static string Shape(NcBlock b) =>
        string.Join(" ", b.Words.Where(w => w.Letter != 'N').Select(w => w.Letter == 'G' ? "G" + w.Value : w.Letter.ToString()));

    private double[] Values(NcBlock b) => b.Words.Where(w => w.Letter is not ('N' or 'G'))
        .Select(w => w.Letter is 'F' or 'P' or 'L' or 'M' ? w.Value : Math.Round(profile.Dimension(w, 21), 9)).ToArray();
}
