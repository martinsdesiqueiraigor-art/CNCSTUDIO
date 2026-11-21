using System.Diagnostics;
using CncStudio.Core.Cam;
using Xunit.Abstractions;

namespace CncStudio.Core.Tests;

/// <summary>CAM-ADAPT-01a: desbaste adaptativo (motor). Tudo medido por uma grade independente, mais fina que a do motor.</summary>
public class AdaptiveTests(ITestOutputHelper output)
{
    private const double Load = 1.0;
    private const double Allowance = 0.3;
    private static readonly CamTool Tool = new(1, 10, 3000, 1000, 300, "Fresa D10");
    private static readonly CamLevels Levels = new(20, 2, 0, -8, 4);

    private static PocketParams Params(PocketEntry entry = PocketEntry.Helix) => new(60, Allowance)
    {
        Strategy = PocketStrategy.Adaptive,
        Entry = entry,
        HelixDiameter = 16,
        HelixPitch = 2,
        OptimalLoad = Load,
        FloorAllowance = 0.2,
    };

    private static Contour LShape() => Contour.FromPoints([new(0, 0), new(90, 0), new(90, 35), new(40, 35), new(40, 70), new(0, 70)]);

    public static TheoryData<string> Shapes => new() { "retangulo", "circulo", "ilha", "L" };

    private static (Contour Outer, List<Contour> Islands) Shape(string name) => name switch
    {
        "retangulo" => (Contour.Rectangle(0, 0, 80, 50), []),
        "circulo" => (Contour.Circle(new Point2(0, 0), 30), []),
        "ilha" => (Contour.Rectangle(0, 0, 100, 60), [Contour.Circle(new Point2(50, 30), 10)]),
        _ => (LShape(), []),
    };

    private static OperationResult Run(string name, PocketParams? p = null)
    {
        (Contour outer, List<Contour> islands) = Shape(name);
        return islands.Count == 0
            ? CamOperations.Pocket(outer, Tool, Levels, p ?? Params())
            : CamOperations.PocketWithIslands(outer, islands, Tool, Levels, p ?? Params());
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Largura_de_corte_nunca_passa_da_carga(string name)
    {
        OperationResult r = Run(name);
        Assert.True(r.Ok, r.Error);
        (Contour outer, List<Contour> islands) = Shape(name);
        var sim = new Sim(outer, islands, 0.05);
        double worst = sim.WorstWidth(r.Operation!.Moves, Tool.Radius, Levels.ClearanceZ, out int checkedMoves);
        output.WriteLine($"{name}: pior largura {worst:0.###} mm em {checkedMoves} trechos; {r.Operation.Moves.Count} movimentos");
        for (int k = Math.Max(0, sim.WorstAt - 6); k <= Math.Min(r.Operation.Moves.Count - 1, sim.WorstAt + 2); k++)
        {
            ToolMove m = r.Operation.Moves[k];
            output.WriteLine($"  {(k == sim.WorstAt ? ">" : " ")}{k} {m.Kind} {m.X:0.###} {m.Y:0.###} {m.Z:0.###} c={m.Center.X:0.###},{m.Center.Y:0.###}");
        }
        Assert.True(checkedMoves > 20);
        Assert.True(worst <= (Load * 1.1) + 0.1, $"largura {worst:0.###} > carga");
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Nunca_invade_parede_nem_ilha(string name)
    {
        OperationResult r = Run(name);
        Assert.True(r.Ok, r.Error);
        (Contour outer, List<Contour> islands) = Shape(name);
        double min = new Sim(outer, islands, 0.05).MinWallDistance(r.Operation!.Moves, Levels.ClearanceZ);
        output.WriteLine($"{name}: menor distância do centro à parede {min:0.####} (precisa {Tool.Radius + Allowance})");
        Assert.True(min >= Tool.Radius + Allowance - 0.01);
    }

    [Theory]
    [MemberData(nameof(Shapes))]
    public void Sobra_so_o_sobremetal_e_onde_a_ferramenta_nao_alcanca(string name)
    {
        OperationResult adaptive = Run(name);
        Assert.True(adaptive.Ok, adaptive.Error);
        (Contour outer, List<Contour> islands) = Shape(name);
        var sim = new Sim(outer, islands, 0.1);
        double a = sim.LeftAfterLastLevel(adaptive.Operation!.Moves, Tool.Radius, Allowance);
        output.WriteLine($"{name}: sobra alcançável além do sobremetal {a:0.##} mm²; " + string.Join(" ", sim.Leftovers.Select(q => $"({q.X:0.#},{q.Y:0.#})")));
        Assert.True(a <= 2, $"sobrou {a:0.##} mm² onde a ferramenta alcança");
    }

    [Fact]
    public void Niveis_iguais_e_fundo_com_sobremetal()
    {
        OperationResult r = Run("retangulo");
        Assert.True(r.Ok, r.Error);
        List<double> flat = [.. r.Operation!.Moves.Where(m => m.Kind != MoveKind.Rapid).Select(m => Math.Round(m.Z, 6)).Distinct().Where(z => z < Levels.ClearanceZ)];
        Assert.Equal(-7.8, flat.Min(), 6);
        Assert.Contains(-3.9, flat);
        Assert.DoesNotContain(flat, z => z < -7.8 - 1e-9);
        Assert.Contains(r.Operation.Moves, m => m.Kind == MoveKind.ArcCcw && m.Z > -3.9 && m.Z < 0);
    }

    [Fact]
    public void Erros_com_mensagem()
    {
        Assert.Contains("carga ótima", Run("retangulo", Params() with { OptimalLoad = 0 }).Error);
        Assert.Contains("50%", Run("retangulo", Params() with { OptimalLoad = 6 }).Error);
        Assert.Contains("hélice ou pré-furo", Run("retangulo", Params(PocketEntry.Plunge)).Error);
        Assert.Contains("hélice ou pré-furo", Run("retangulo", Params(PocketEntry.Ramp) with { RampAngle = 3 }).Error);
        Assert.Contains("hélice", Run("retangulo", Params() with { HelixDiameter = 50 }).Error);
        Assert.Contains("diâmetro", Run("retangulo", Params() with { HelixDiameter = 8 }).Error);
        Assert.Contains("sobremetal no fundo", Run("retangulo", Params() with { FloorAllowance = 9 }).Error);
    }

    [Fact]
    public void Pre_furo_entra_no_furo_e_abre_em_espiral()
    {
        OperationResult r = Run("retangulo", Params(PocketEntry.PreDrill) with { PreDrillPoint = new Point2(40, 25), HelixDiameter = 0 });
        Assert.True(r.Ok, r.Error);
        ToolMove plunge = r.Operation!.Moves.First(m => m.Kind == MoveKind.Feed && m.Z < 0);
        Assert.Equal((40.0, 25.0), (plunge.X, plunge.Y));
        // a abertura a partir do furo corta cheia (como a hélice): a carga vale depois dela; aqui só a segurança
        Assert.True(new Sim(Contour.Rectangle(0, 0, 80, 50), [], 0.05).MinWallDistance(r.Operation.Moves, Levels.ClearanceZ) >= Tool.Radius + Allowance - 0.01);

        Assert.Contains("Pré-furo", Run("retangulo", Params(PocketEntry.PreDrill) with { HelixDiameter = 0 }).Error);
    }

    [Fact]
    public void Gcode_em_retas_e_arcos_e_tempo_de_calculo()
    {
        var watch = Stopwatch.StartNew();
        OperationResult r = CamOperations.Pocket(Contour.Rectangle(0, 0, 120, 80), Tool, Levels with { BottomZ = -4 }, Params() with { FloorAllowance = 0 });
        watch.Stop();
        Assert.True(r.Ok, r.Error);
        int arcs = r.Operation!.Moves.Count(m => m.Kind is MoveKind.ArcCw or MoveKind.ArcCcw);
        output.WriteLine($"120 × 80, 1 nível: {watch.ElapsedMilliseconds} ms, {r.Operation.Moves.Count} movimentos, {arcs} arcos");
        Assert.True(arcs > 10);
        Assert.True(watch.Elapsed.TotalSeconds < 6);
    }

    [Fact]
    public void Arquivo_versao_10_so_com_o_adaptativo()
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "trabalhos-referencia");
        JobLoadResult loaded = CamJobJson.FromJson(File.ReadAllText(Path.Combine(dir, "fresa-bloco-siemens.casillas-job")));
        Assert.True(loaded.Ok, loaded.Error);
        CamJob job = loaded.Job!;
        Assert.Contains("\"version\": 1", CamJobJson.ToJson(job));
        int i = job.Operations.FindIndex(o => o.Kind == JobOpKind.Pocket);
        job.Operations[i] = job.Operations[i] with { PocketParams = Params() with { MinRadius = 0.5, BothWays = true } };
        string text = CamJobJson.ToJson(job);
        Assert.Contains("\"version\": 10", text);
        Assert.Contains("\"optimalLoad\": 1", text);
        JobLoadResult back = CamJobJson.FromJson(text);
        Assert.True(back.Ok, back.Error);
        PocketParams pp = back.Job!.Operations[i].PocketParams!;
        Assert.Equal((PocketStrategy.Adaptive, 1.0, 0.5, true, 0.2), (pp.Strategy, pp.OptimalLoad, pp.MinRadius, pp.BothWays, pp.FloorAllowance));
        Assert.Equal(text, CamJobJson.ToJson(back.Job));
        Assert.True(CamJobBuilder.Build(back.Job, null).Operations.Count > 0);
    }

    /// <summary>Simulação 2D independente do motor: grade fina do material da área.</summary>
    private sealed class Sim
    {
        private readonly Contour _outer;
        private readonly List<Contour> _islands;
        private readonly List<Point2> _outerPoly;
        private readonly List<List<Point2>> _islandPolys;
        private readonly double _h, _ox, _oy;
        private readonly int _cols, _rows;
        private bool[] _material = [];

        public Sim(Contour outer, List<Contour> islands, double h)
        {
            _outer = outer;
            _islands = islands;
            _outerPoly = outer.Sample(0.005);
            _islandPolys = [.. islands.Select(i => i.Sample(0.005))];
            (double minX, double minY, double maxX, double maxY) = outer.BoundsXY();
            _h = h;
            _ox = minX;
            _oy = minY;
            _cols = (int)Math.Ceiling((maxX - minX) / h);
            _rows = (int)Math.Ceiling((maxY - minY) / h);
            Reset();
        }

        private void Reset()
        {
            if (_initial is null)
            {
                _initial = new bool[_cols * _rows];
                var xs = new List<double>();
                for (int r = 0; r < _rows; r++)
                {
                    double y = _oy + ((r + 0.5) * _h);
                    foreach ((List<Point2> poly, bool value) in _islandPolys.Select(i => (i, false)).Prepend((_outerPoly, true)))
                    {
                        xs.Clear();
                        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                        {
                            Point2 a = poly[i], b = poly[j];
                            if ((a.Y > y) != (b.Y > y))
                            {
                                xs.Add(a.X + ((y - a.Y) * (b.X - a.X) / (b.Y - a.Y)));
                            }
                        }

                        xs.Sort();
                        for (int t = 0; t + 1 < xs.Count; t += 2)
                        {
                            for (int c = 0; c < _cols; c++)
                            {
                                double x = _ox + ((c + 0.5) * _h);
                                if (x >= xs[t] && x <= xs[t + 1])
                                {
                                    _initial[(r * _cols) + c] = value;
                                }
                            }
                        }
                    }
                }
            }

            _material = (bool[])_initial.Clone();
        }

        private bool[]? _initial;

        public int WorstAt { get; private set; }

        public List<Point2> Leftovers { get; } = [];

        public double WorstWidth(IReadOnlyList<ToolMove> moves, double radius, double clearanceZ, out int count)
        {
            double worst = 0;
            count = 0;
            List<double> flats = [.. moves.Where((m, k) => k > 0 && m.Kind != MoveKind.Rapid && m.Z < clearanceZ - 1e-9 && Math.Abs(moves[k - 1].Z - m.Z) < 1e-9)
                .Select(m => Math.Round(m.Z, 6)).Distinct().OrderDescending()];
            int next = 0;
            double? level = null;
            for (int i = 1; i < moves.Count; i++)
            {
                ToolMove a = moves[i - 1], b = moves[i];
                if (b.Kind == MoveKind.Rapid || b.Z >= clearanceZ - 1e-9)
                {
                    continue;
                }

                if (Math.Abs(a.Z - b.Z) > 1e-9 || a.Z >= clearanceZ - 1e-9)
                {
                    // descida (hélice, mergulho): corta, sem conferir largura; nível novo começa do material inteiro
                    if ((level is null || b.Z < level.Value - 1e-9) && next < flats.Count)
                    {
                        Reset();
                        level = flats[next++];
                    }

                    Sweep(a, b, radius);
                    continue;
                }

                double len = Length(a, b);
                int cut = Sweep(a, b, radius);
                if (len >= 0.5)
                {
                    if (cut * _h * _h / len > worst)
                    {
                        WorstAt = i;
                    }

                    worst = Math.Max(worst, cut * _h * _h / len);
                    count++;
                }
            }

            return worst;
        }

        public double MinWallDistance(IReadOnlyList<ToolMove> moves, double clearanceZ)
        {
            double min = double.MaxValue;
            for (int i = 1; i < moves.Count; i++)
            {
                ToolMove a = moves[i - 1], b = moves[i];
                if (b.Kind == MoveKind.Rapid || b.Z >= clearanceZ - 1e-9)
                {
                    continue;
                }

                foreach (Point2 q in Points(a, b))
                {
                    double d = Wall(q);
                    min = Math.Min(min, Inside(_outerPoly, q) && !_islandPolys.Any(p => Inside(p, q)) ? d : -d);
                }
            }

            return min;
        }

        public double LeftAfterLastLevel(IReadOnlyList<ToolMove> moves, double radius, double allowance)
        {
            double last = moves.Where(m => m.Kind != MoveKind.Rapid).Min(m => m.Z);
            for (int i = 1; i < moves.Count; i++)
            {
                if (moves[i].Kind != MoveKind.Rapid && Math.Abs(moves[i].Z - last) < 1e-9)
                {
                    Sweep(moves[i - 1], moves[i], radius);
                }
            }

            int left = 0;
            for (int r = 0; r < _rows; r++)
            {
                for (int c = 0; c < _cols; c++)
                {
                    var p = new Point2(_ox + ((c + 0.5) * _h), _oy + ((r + 0.5) * _h));
                    if (_material[(r * _cols) + c] && Wall(p) > allowance + (2 * _h) && Reachable(p, radius, allowance))
                    {
                        left++;
                        if (left % 40 == 1)
                        {
                            Leftovers.Add(p);
                        }
                    }
                }
            }

            return left * _h * _h;
        }

        // existe um centro a r + sobremetal das paredes que cobre o ponto (com folga de 2 células): senão é canto que nenhuma fresa pega
        private bool Reachable(Point2 p, double radius, double allowance)
        {
            double need = radius + allowance;
            for (int k = 0; k < 72; k++)
            {
                for (double rho = 0; rho <= radius - (2 * _h); rho += _h)
                {
                    var q = new Point2(p.X + (rho * Math.Cos(k * Math.PI / 36)), p.Y + (rho * Math.Sin(k * Math.PI / 36)));
                    if (Inside(_outerPoly, q) && !_islandPolys.Any(i => Inside(i, q)) && Wall(q) >= need)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private double Wall(Point2 q)
        {
            double best = double.MaxValue;
            foreach (List<Point2> poly in _islandPolys.Prepend(_outerPoly))
            {
                for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
                {
                    best = Math.Min(best, Seg(q, poly[j], poly[i]));
                }
            }

            return best;
        }

        private static double Seg(Point2 p, Point2 a, Point2 b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, l2 = (dx * dx) + (dy * dy);
            double t = l2 < 1e-18 ? 0 : Math.Clamp((((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / l2, 0, 1);
            return new Point2(p.X - a.X - (t * dx), p.Y - a.Y - (t * dy)).Length;
        }

        private static double Length(ToolMove a, ToolMove b) => b.Kind == MoveKind.Feed
            ? a.XY.DistanceTo(b.XY)
            : Segment2.Arc(a.XY, b.XY, b.Center, b.Kind == MoveKind.ArcCcw).Length;

        private static IEnumerable<Point2> Points(ToolMove a, ToolMove b) => b.Kind == MoveKind.Feed
            ? Enumerable.Range(0, 11).Select(k => new Point2(a.X + ((b.X - a.X) * k / 10), a.Y + ((b.Y - a.Y) * k / 10)))
            : Segment2.Arc(a.XY, b.XY, b.Center, b.Kind == MoveKind.ArcCcw).Sample(0.005);

        private int Sweep(ToolMove a, ToolMove b, double radius)
        {
            List<Point2> pts = [.. Points(a, b)];
            var dense = new List<Point2> { pts[0] };
            for (int k = 1; k < pts.Count; k++)
            {
                int n = Math.Max(1, (int)Math.Ceiling(pts[k - 1].DistanceTo(pts[k]) / (_h * 0.5)));
                for (int t = 1; t <= n; t++)
                {
                    dense.Add(new Point2(pts[k - 1].X + ((pts[k].X - pts[k - 1].X) * t / n), pts[k - 1].Y + ((pts[k].Y - pts[k - 1].Y) * t / n)));
                }
            }

            int cut = 0;
            foreach (Point2 p in dense)
            {
                int c0 = Math.Max(0, (int)((p.X - radius - _ox) / _h)), c1 = Math.Min(_cols - 1, (int)((p.X + radius - _ox) / _h));
                int r0 = Math.Max(0, (int)((p.Y - radius - _oy) / _h)), r1 = Math.Min(_rows - 1, (int)((p.Y + radius - _oy) / _h));
                for (int r = r0; r <= r1; r++)
                {
                    double dy = _oy + ((r + 0.5) * _h) - p.Y;
                    for (int c = c0; c <= c1; c++)
                    {
                        int k = (r * _cols) + c;
                        if (!_material[k])
                        {
                            continue;
                        }

                        double dx = _ox + ((c + 0.5) * _h) - p.X;
                        if ((dx * dx) + (dy * dy) <= radius * radius)
                        {
                            _material[k] = false;
                            cut++;
                        }
                    }
                }
            }

            return cut;
        }

        private static bool Inside(List<Point2> poly, Point2 p)
        {
            bool inside = false;
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                Point2 a = poly[i], b = poly[j];
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < ((b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y)) + a.X)
                {
                    inside = !inside;
                }
            }

            return inside;
        }
    }
}
