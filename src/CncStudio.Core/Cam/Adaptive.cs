namespace CncStudio.Core.Cam;

/// <summary>
/// CAM-ADAPT-01a: desbaste adaptativo (referência: Fusion 2D Adaptive Clearing). Por nível de Z, uma grade 2D do material diz
/// onde o centro da ferramenta pode andar e o que ainda falta cortar; a cada passo pequeno a ferramenta escolhe a direção cuja
/// largura de corte fica mais perto da carga ótima sem passar dela. Acabada uma região, liga por baixo (caminho já limpo) até a
/// próxima; sem caminho, sobe e entra de novo em hélice. Fecha o nível com uma volta na parede e nas ilhas. Toda posição de corte é
/// conferida contra o contorno exato antes de sair (nunca invade parede nem ilha).
/// </summary>
public static partial class CamOperations
{
    /// <summary>Confere os campos do desbaste adaptativo. Null = bom.</summary>
    internal static string? AdaptiveError(PocketParams p, CamTool tool)
    {
        if (p.OptimalLoad <= 0)
        {
            return "Falta a carga ótima (largura de corte) do desbaste adaptativo.";
        }

        if (p.OptimalLoad > (tool.Diameter / 2) + 1e-9)
        {
            return "A carga ótima precisa ficar até 50% do diâmetro da ferramenta.";
        }

        if (p.MinRadius < 0 || p.FloorAllowance < 0 || p.FinishAllowance < 0)
        {
            return "Raio mínimo e sobremetais não podem ser negativos.";
        }

        return p.Entry is PocketEntry.Helix or PocketEntry.PreDrill || p.AdaptiveStock is not null
            ? null
            : "O desbaste adaptativo entra em hélice ou pré-furo: escolha a entrada.";
    }

    private static OperationResult AdaptivePocket(Contour boundary, IReadOnlyList<Contour> islands, CamTool tool, CamLevels levels, PocketParams p)
    {
        if (p.Entry == PocketEntry.Helix && p.HelixPitch <= 0 && p.AdaptiveStock is null)
        {
            return Fail("O passo da hélice precisa ser maior que zero.");
        }

        CamLevels cut = levels with { BottomZ = levels.BottomZ + p.FloorAllowance };
        if (cut.BottomZ >= levels.TopZ - 1e-9)
        {
            return Fail("O sobremetal no fundo não deixa nada a cortar entre o topo e o fundo.");
        }

        Contour outer = boundary.IsCounterClockwise ? boundary : boundary.Reversed();
        List<Contour> holes = [.. islands.Select(i => i.IsCounterClockwise ? i : i.Reversed())];
        var grid = AdaptiveGrid.Build(outer, holes, tool.Radius, p.FinishAllowance, p.OptimalLoad, p.AdaptiveStock);
        if (grid.AllowedCount == 0)
        {
            return Fail("O bolsão não comporta a ferramenta (com o sobremetal).");
        }

        var warnings = new List<string>();
        var moves = new List<ToolMove>();
        double previousZ = cut.TopZ;
        foreach (double z in cut.Depths())
        {
            var level = new AdaptiveLevel(grid, tool, cut, p, z, previousZ, moves);
            string? error = level.Run();
            if (error is not null)
            {
                return Fail(error);
            }

            AdaptiveWalls(moves, warnings, outer, holes, grid, tool, cut, p, z);
            previousZ = z;
        }

        if (moves.Count == 0)
        {
            return Fail("Nada a cortar no desbaste adaptativo.");
        }

        moves.Add(Rapid(moves[^1].XY, levels.SafeZ));
        if (grid.UnsafeCount(moves, levels.ClearanceZ) is > 0 and var bad)
        {
            // rede de segurança: nunca sai trajetória que invade parede ou ilha
            return Fail($"Desbaste adaptativo: {bad} ponto(s) da trajetória chegariam perto demais da parede ou de uma ilha. Erro interno: avise o suporte.");
        }

        warnings.Add("Desbaste adaptativo: simule antes de rodar e confira o comprimento de corte da ferramenta para o passe em Z.");
        return new OperationResult(new CamOperation("Desbaste adaptativo", tool, moves, null, warnings), null);
    }

    // volta na parede e nas ilhas no fim do nível (concordante: parede no sentido anti-horário, ilhas no horário)
    private static void AdaptiveWalls(List<ToolMove> moves, List<string> warnings, Contour outer, List<Contour> holes, AdaptiveGrid grid, CamTool tool,
        CamLevels levels, PocketParams p, double z)
    {
        double d = tool.Radius + p.FinishAllowance;
        var loops = new List<Contour>();
        OffsetResult wall = outer.OffsetOutside(-d);
        // borda aberta: o contorno de fora é só espaço livre em volta do bruto, sem parede para acabar
        if (wall.Ok && p.AdaptiveStock is null)
        {
            loops.Add(wall.Contour!);
        }

        foreach (Contour hole in holes)
        {
            OffsetResult around = hole.OffsetOutside(d);
            if (around.Ok)
            {
                loops.Add(around.Contour!.Reversed());
            }
        }

        foreach (Contour loop in loops)
        {
            if (!grid.LoopIsSafe(loop))
            {
                AddOnce(warnings, "Volta na parede", "Volta na parede: um trecho ficaria perto demais de outra parede ou ilha e foi deixado de fora; o sobremetal ali pode ser maior.");
                continue;
            }

            Point2 start = loop.Segments[0].Start;
            if (moves.Count > 0 && moves[^1].Z < levels.ClearanceZ - 1e-9)
            {
                moves.Add(Rapid(moves[^1].XY, levels.ClearanceZ));
            }

            moves.Add(Rapid(start, levels.ClearanceZ));
            moves.Add(Feed(start, z, tool.FeedZ));
            foreach (Segment2 s in loop.Segments)
            {
                moves.Add(Along(s, z, tool.FeedXY));
            }
        }

        if (moves.Count > 0 && moves[^1].Z < levels.ClearanceZ - 1e-9)
        {
            moves.Add(Rapid(moves[^1].XY, levels.ClearanceZ));
        }
    }

    /// <summary>Um nível de Z do desbaste adaptativo.</summary>
    private sealed class AdaptiveLevel(AdaptiveGrid grid, CamTool tool, CamLevels levels, PocketParams p, double z, double fromZ,
        List<ToolMove> moves)
    {
        private const double AngleStep = 2.0 * Math.PI / 180;
        private const int AngleCount = 65;

        // _real: todo o material do nível (largura de corte); _uncut: o que o adaptativo ainda precisa tirar (sem a faixa da parede)
        private readonly bool[] _real = grid.FreshRegion();
        private readonly bool[] _uncut = grid.FreshMaterial();
        private readonly double _r = tool.Radius;
        private readonly double _load = p.OptimalLoad;
        private readonly double _step = Math.Min(Math.Max(p.OptimalLoad, 3 * grid.Cell), Math.Max(tool.Radius * 0.5, grid.Cell));
        private Point2 _pos;
        private double _heading;
        private bool _usedPreDrill;
        private int _links;


        public string? Run()
        {
            var path = new List<Point2>();
            int guard = 0;
            int limit = (grid.Columns * grid.Rows) + 10000;
            bool down = false;
            while (guard++ < limit)
            {
                if (!down)
                {
                    DropSlivers();
                    if (!AnyUncut())
                    {
                        break;
                    }

                    string? error = Enter();
                    if (error is not null)
                    {
                        return error;
                    }

                    down = true;
                    path.Clear();
                    path.Add(_pos);
                }

                // concordante para a frente, depois para trás; só então (sem "os dois sentidos") um passo discordante
                // ordem: suave (raio mínimo, preferência) antes de fechar a curva; sem "os dois sentidos", concordante antes de discordante
                bool climb = TryStep(out Point2 next, false, true) || TryStep(out next, false, false) || TryReverse(out next, false);
                // sem saída concordante: um passo discordante curto (ponta de faixa) em vez de uma ligação; o teste mede a parte concordante
                bool conventional = !climb && !p.BothWays && (TryStep(out next, true, true) || TryStep(out next, true, false) || TryReverse(out next, true));
                if (climb || conventional)
                {
                    Cut(_pos, next);
                    _heading = Math.Atan2(next.Y - _pos.Y, next.X - _pos.X);
                    _pos = next;
                    path.Add(next);
                    continue;
                }

                Flush(path);
                if (++_links % 32 == 0)
                {
                    DropSlivers();
                }

                if (!AnyUncut())
                {
                    break;
                }

                List<Point2>? link = Link();

                if (link is null)
                {
                    Retract();
                    down = false;
                    continue;
                }

                foreach (Point2 q in link)
                {
                    Cut(_pos, q);
                    moves.Add(Feed(q, z, tool.FeedXY));
                    _pos = q;
                }

                _heading = HeadingAlongMaterial(_pos);
                path.Clear();
                path.Add(_pos);
            }

            Flush(path);
            return guard >= limit ? "Desbaste adaptativo não terminou (limite de passos): avise o suporte." : null;
        }

        // ---- entrada: pré-furo (só a primeira região do primeiro nível) ou hélice
        private string? Enter()
        {
            Retract();

            // borda aberta: desce no ar fora do bruto, no ponto livre mais perto do material, e entra de lado
            if (p.AdaptiveStock is not null && grid.OpenEntry(_real, _uncut, _r) is { } outside)
            {
                StartAt(outside);
                moves.Add(Rapid(outside, levels.ClearanceZ));
                moves.Add(Feed(outside, z, tool.FeedZ));
                _pos = outside;
                _heading = HeadingAlongMaterial(outside);
                return null;
            }

            if (p.AdaptiveStock is not null && p.Entry != PocketEntry.Helix && p.Entry != PocketEntry.PreDrill)
            {
                return "Desbaste adaptativo: uma região do bolsão não se alcança por fora do bruto. Informe a hélice (Ø e passo) para entrar nela.";
            }

            if (p.Entry == PocketEntry.PreDrill && !_usedPreDrill && p.PreDrillPoint is { } hole && grid.IsAllowed(hole) && grid.HasMaterialNear(_uncut, hole, _r))
            {
                _usedPreDrill = true;
                StartAt(hole);
                moves.Add(Rapid(hole, levels.ClearanceZ));
                moves.Add(Feed(hole, fromZ, tool.FeedZ));
                moves.Add(Feed(hole, z, tool.FeedZ));
                _pos = hole;
                CutDisk(hole, _r);
                SpiralOut(hole, Math.Max(2 * _load, tool.Diameter * 0.25));
                return null;
            }

            if (p.Entry == PocketEntry.PreDrill && p.HelixDiameter <= tool.Diameter)
            {
                return p.PreDrillPoint is null
                    ? "Pré-furo: nenhuma Furação anterior cai dentro do bolsão. O desbaste adaptativo não entra sem pré-furo ou hélice."
                    : "Pré-furo: há outra região do bolsão sem furo. Informe também a hélice (Ø e passo) para entrar nela.";
            }

            if (p.HelixDiameter <= tool.Diameter || p.HelixPitch <= 0)
            {
                return "Desbaste adaptativo: falta a hélice (Ø maior que a ferramenta e passo) para entrar numa região do bolsão.";
            }

            double rp = (p.HelixDiameter - tool.Diameter) / 2;
            if (grid.HelixCenter(_uncut, rp) is not { } c)
            {

                return $"A hélice Ø{p.HelixDiameter:0.###} não cabe numa região do bolsão: diminua o Ø da hélice.";
            }

            var p0 = new Point2(c.X + rp, c.Y);
            StartAt(p0);
            double dz = fromZ - z;
            int laps = Math.Max(1, (int)Math.Ceiling((dz / p.HelixPitch) - 1e-9));
            moves.Add(Rapid(p0, levels.ClearanceZ));
            moves.Add(Feed(p0, fromZ, tool.FeedZ));
            var opposite = new Point2(c.X - rp, c.Y);
            for (int k = 1; k <= 2 * laps; k++)
            {
                moves.Add(new ToolMove(MoveKind.ArcCcw, k % 2 == 1 ? opposite.X : p0.X, k % 2 == 1 ? opposite.Y : p0.Y, fromZ - (dz * k / (2 * laps)), c, tool.FeedXY));
            }

            // uma volta no fundo para tirar o cone
            moves.Add(new ToolMove(MoveKind.ArcCcw, opposite.X, opposite.Y, z, c, tool.FeedXY));
            moves.Add(new ToolMove(MoveKind.ArcCcw, p0.X, p0.Y, z, c, tool.FeedXY));
            CutDisk(c, rp + _r);
            _pos = p0;
            _heading = Math.PI / 2;
            return null;
        }

        private void Cut(Point2 a, Point2 b)
        {
            grid.CutStadium(_real, a, b, _r);
            grid.CutStadium(_uncut, a, b, _r);
        }

        private void CutDisk(Point2 c, double radius)
        {
            grid.CutDisk(_real, c, radius);
            grid.CutDisk(_uncut, c, radius);
        }

        private void Retract()
        {
            if (moves.Count > 0 && moves[^1].Z < levels.ClearanceZ - 1e-9)
            {
                moves.Add(Rapid(moves[^1].XY, levels.ClearanceZ));
            }
        }

        // primeira entrada da operação: chega pela altura segura
        private void StartAt(Point2 at)
        {
            if (moves.Count == 0)
            {
                moves.Add(Rapid(at, levels.SafeZ));
            }
        }

        // espiral de Arquimedes para fora a partir do pré-furo, com passo menor que a carga
        private void SpiralOut(Point2 c, double radius)
        {
            double pitch = _load * 0.9;
            double turns = radius / pitch;
            int n = Math.Max(8, (int)Math.Ceiling(turns * 2 * Math.PI * radius / Math.Max(_step, 1e-3)));
            var pts = new List<Point2> { c };
            for (int i = 1; i <= n; i++)
            {
                double t = turns * 2 * Math.PI * i / n;
                double rr = pitch * t / (2 * Math.PI);
                var q = new Point2(c.X + (rr * Math.Cos(t)), c.Y + (rr * Math.Sin(t)));
                if (!grid.IsAllowed(q))
                {
                    break;
                }

                Cut(pts[^1], q);
                pts.Add(q);
            }

            Flush(pts);
            _pos = pts[^1];
            double ang = Math.Atan2(_pos.Y - c.Y, _pos.X - c.X);
            _heading = ang + (Math.PI / 2);
        }

        // ---- passo: direção de largura mais perto da carga sem passar (concordante: preferir a da direita)
        private bool TryStep(out Point2 next, bool anySide, bool smooth = false)
        {
            next = default;
            int n = (2 * AngleCount) + 1;
            Span<double> qx = stackalloc double[n];
            Span<double> qy = stackalloc double[n];
            Span<bool> valid = stackalloc bool[n];
            Span<int> area = stackalloc int[n];
            Span<int> work = stackalloc int[n];
            Span<int> left = stackalloc int[n];
            // raio mínimo (preferência, não garantia): primeiro só direções dentro da curva desse raio; sem nenhuma, o passo livre
            double maxTurn = p.MinRadius > 0 && smooth ? 2 * Math.Asin(Math.Min(1, _step / (2 * p.MinRadius))) : double.MaxValue;
            for (int k = 0; k < n; k++)
            {
                double a = _heading + ((k - AngleCount) * AngleStep);
                qx[k] = _pos.X + (_step * Math.Cos(a));
                qy[k] = _pos.Y + (_step * Math.Sin(a));
                var q = new Point2(qx[k], qy[k]);
                valid[k] = Math.Abs((k - AngleCount) * AngleStep) <= maxTurn + 1e-9
                    && grid.IsAllowed(q) && grid.IsAllowed(new Point2((_pos.X + q.X) / 2, (_pos.Y + q.Y) / 2));
            }

            grid.CountNew(_real, _uncut, _pos, _r, _step, _heading, AngleStep, AngleCount, area, work, left);
            double cellArea = grid.Cell * grid.Cell;
            double target = _load * 0.85;
            // só concordante (sem "os dois sentidos"): primeiro só as direções com o material novo à direita; sem nenhuma, a ligação
            // por baixo procura outro ponto. Com os dois sentidos, vale qualquer lado.
            int best = Pick(n, valid, area, work, left, cellArea, target, climbOnly: !p.BothWays && !anySide);

            if (best < 0)
            {
                return false;
            }

            next = new Point2(qx[best], qy[best]);
            return true;
        }

        private int Pick(int n, Span<bool> valid, Span<int> area, Span<int> work, Span<int> left, double cellArea, double target, bool climbOnly)
        {
            int best = -1;
            double bestWidth = 0;
            for (int k = 0; k < n; k++)
            {
                if (!valid[k] || work[k] < 1 || (climbOnly && left[k] > 0.3 * area[k]))
                {
                    continue;
                }

                double width = area[k] * cellArea / _step;
                if (width > target)
                {
                    continue;
                }

                // da direita (k pequeno) para a esquerda: fica com a primeira bem perto da carga; senão, a de maior largura
                if (best < 0 || width > bestWidth + (0.15 * target))
                {
                    best = k;
                    bestWidth = width;
                }
            }

            return best;
        }

        // sem saída para a frente: olha para trás (volta pelo mesmo lado, como na ponta de uma faixa junto da parede)
        private bool TryReverse(out Point2 next, bool anySide)
        {

            double keep = _heading;
            _heading += Math.PI;
            if (TryStep(out next, anySide))
            {
                return true;
            }

            _heading = keep;
            return false;
        }

        // ---- ligação por baixo até a borda do material mais perto (pelo que já está limpo)
        private List<Point2>? Link()
        {
            List<Point2>? cells = grid.PathToMaterial(_uncut, _pos, _r);
            if (cells is null || cells.Count == 0)
            {
                return null;
            }

            return grid.Simplify(_uncut, _pos, cells, _r);
        }

        private double HeadingAlongMaterial(Point2 at)
        {
            Point2 g = grid.TowardMaterial(_uncut, at, _r + (3 * grid.Cell));
            // material à direita: direção = vetor do material girado +90°
            return Math.Atan2(g.X, -g.Y);
        }

        private void DropSlivers() => grid.DropSlivers(_uncut, Math.Max(4, (int)(_load * _step / (grid.Cell * grid.Cell) * 0.5)));

        private bool AnyUncut() => Array.IndexOf(_uncut, true) >= 0;

        private void Flush(List<Point2> path)
        {
            if (path.Count >= 2)
            {
                foreach (ToolMove m in AdaptiveFit.Moves(path, z, tool.FeedXY, 0.01))
                {
                    moves.Add(m);
                }
            }

            if (path.Count > 0)
            {
                Point2 last = path[^1];
                path.Clear();
                path.Add(last);
            }
        }
    }
}

/// <summary>Grade 2D do desbaste adaptativo: área do bolsão, onde o centro pode andar e o material de um nível.</summary>
internal sealed class AdaptiveGrid
{
    private readonly bool[] _allowed;
    private readonly bool[] _region;
    private readonly bool[] _toCut;
    private readonly float[] _clearance; // distância (mm) até fora da área do bolsão
    private readonly List<List<Point2>> _walls;
    private readonly double _minDistance;

    private AdaptiveGrid(double originX, double originY, int columns, int rows, double cell, bool[] allowed, bool[] region, bool[] toCut, float[] clearance,
        List<List<Point2>> walls, double minDistance)
    {
        OriginX = originX;
        OriginY = originY;
        Columns = columns;
        Rows = rows;
        Cell = cell;
        _allowed = allowed;
        _region = region;
        _toCut = toCut;
        _clearance = clearance;
        _walls = walls;
        _minDistance = minDistance;
        AllowedCount = allowed.Count(a => a);
    }

    public double OriginX { get; }

    public double OriginY { get; }

    public int Columns { get; }

    public int Rows { get; }

    public double Cell { get; }

    public int AllowedCount { get; }

    /// <summary>
    /// <paramref name="load"/>: a faixa junto da parede (até ~60% da carga) fica para a volta na parede, que a tira com largura menor
    /// que a carga; o adaptativo limpa o miolo.
    /// </summary>
    public static AdaptiveGrid Build(Contour outer, List<Contour> islands, double radius, double allowance, double load, Contour? stock = null)
    {
        (double minX, double minY, double maxX, double maxY) = outer.BoundsXY();
        double size = Math.Max(maxX - minX, maxY - minY);
        double cell = Math.Max(Math.Min(radius * 2 * 0.02, 0.25), Math.Max(size / 1500, 0.01));
        double ox = minX - (3 * cell), oy = minY - (3 * cell);
        int cols = (int)Math.Ceiling((maxX - minX) / cell) + 6, rows = (int)Math.Ceiling((maxY - minY) / cell) + 6;
        List<Point2> outerPoly = outer.Sample(cell / 8);
        List<List<Point2>> islandPolys = [.. islands.Select(i => i.Sample(cell / 8))];
        var region = new bool[cols * rows];
        Fill(region, outerPoly, ox, oy, cols, rows, cell, true);
        foreach (List<Point2> poly in islandPolys)
        {
            Fill(region, poly, ox, oy, cols, rows, cell, false);
        }

        float[] clearance = Distance(region, cols, rows, cell, feature: false);
        double need = radius + allowance;
        // faixa junto da parede: o adaptativo não chega nela; a volta na parede a tira com largura ≤ ~60% da carga
        double band = Math.Max(0, (0.6 * load) - cell);
        var allowed = new bool[cols * rows];
        for (int k = 0; k < allowed.Length; k++)
        {
            allowed[k] = region[k] && clearance[k] >= need + cell + band;
        }

        float[] toAllowed = Distance(allowed, cols, rows, cell, feature: true);
        bool[] material = region;
        if (stock is not null)
        {
            // borda aberta: material só dentro do bruto
            var inside = new bool[cols * rows];
            Fill(inside, (stock.IsCounterClockwise ? stock : stock.Reversed()).Sample(cell / 8), ox, oy, cols, rows, cell, true);
            material = new bool[cols * rows];
            for (int k = 0; k < material.Length; k++)
            {
                material[k] = region[k] && inside[k];
            }
        }

        var toCut = new bool[cols * rows];
        for (int k = 0; k < toCut.Length; k++)
        {
            // margem de uma célula: o que fica na borda do alcance é da volta na parede (nunca uma tira solta impossível de pegar)
            toCut[k] = material[k] && toAllowed[k] <= radius - cell;
        }

        List<List<Point2>> walls = [outer.Sample(0.005), .. islands.Select(i => i.Sample(0.005))];
        return new AdaptiveGrid(ox, oy, cols, rows, cell, allowed, material, toCut, clearance, walls, need);
    }

    public bool[] FreshMaterial() => (bool[])_toCut.Clone();

    /// <summary>Todo o material do bolsão no nível (inclusive o que só a volta na parede tira): conta na largura de corte.</summary>
    public bool[] FreshRegion() => (bool[])_region.Clone();

    public int IndexOf(Point2 p, out int c, out int r)
    {
        c = (int)Math.Floor((p.X - OriginX) / Cell);
        r = (int)Math.Floor((p.Y - OriginY) / Cell);
        return c < 0 || r < 0 || c >= Columns || r >= Rows ? -1 : (r * Columns) + c;
    }

    public bool IsAllowed(Point2 p)
    {
        int k = IndexOf(p, out _, out _);
        return k >= 0 && _allowed[k];
    }

    private double Cx(int c) => OriginX + ((c + 0.5) * Cell);

    private double Cy(int r) => OriginY + ((r + 0.5) * Cell);

    public bool HasMaterialNear(bool[] uncut, Point2 p, double radius)
    {
        bool any = false;
        Visit(p, radius, (k, _) => any |= uncut[k]);
        return any;
    }

    public void CutDisk(bool[] uncut, Point2 p, double radius) => Visit(p, radius, (k, _) => uncut[k] = false);

    /// <summary>Tira o material dentro do raio do segmento a–b (a área varrida pela ferramenta).</summary>
    public void CutStadium(bool[] uncut, Point2 a, Point2 b, double radius)
    {
        double minX = Math.Min(a.X, b.X) - radius, maxX = Math.Max(a.X, b.X) + radius;
        double minY = Math.Min(a.Y, b.Y) - radius, maxY = Math.Max(a.Y, b.Y) + radius;
        int c0 = Math.Max(0, (int)Math.Floor((minX - OriginX) / Cell)), c1 = Math.Min(Columns - 1, (int)Math.Floor((maxX - OriginX) / Cell));
        int r0 = Math.Max(0, (int)Math.Floor((minY - OriginY) / Cell)), r1 = Math.Min(Rows - 1, (int)Math.Floor((maxY - OriginY) / Cell));
        double dx = b.X - a.X, dy = b.Y - a.Y, len2 = (dx * dx) + (dy * dy), r2 = radius * radius;
        for (int r = r0; r <= r1; r++)
        {
            double y = Cy(r);
            for (int c = c0; c <= c1; c++)
            {
                int k = (r * Columns) + c;
                if (!uncut[k])
                {
                    continue;
                }

                double x = Cx(c);
                double t = len2 < 1e-18 ? 0 : Math.Clamp((((x - a.X) * dx) + ((y - a.Y) * dy)) / len2, 0, 1);
                double ex = x - (a.X + (t * dx)), ey = y - (a.Y + (t * dy));
                if ((ex * ex) + (ey * ey) <= r2)
                {
                    uncut[k] = false;
                }
            }
        }
    }

    /// <summary>
    /// Para cada candidato (ângulo heading + (k − count)·step), quantas células de material entram no disco da ferramenta no ponto
    /// novo. Só as células do anel entre r e r + passo podem entrar; cada uma cobre um intervalo contínuo de ângulos (soma por diferenças).
    /// </summary>
    public void CountNew(bool[] uncut, bool[] pending, Point2 from, double radius, double step, double heading, double angleStep, int count,
        Span<int> area, Span<int> work, Span<int> left)
    {
        int n = area.Length;
        Span<int> da = stackalloc int[n + 1];
        Span<int> dw = stackalloc int[n + 1];
        Span<int> dl = stackalloc int[n + 1];
        double reach = radius + step;
        int c0 = Math.Max(0, (int)Math.Floor((from.X - reach - OriginX) / Cell)), c1 = Math.Min(Columns - 1, (int)Math.Floor((from.X + reach - OriginX) / Cell));
        int r0 = Math.Max(0, (int)Math.Floor((from.Y - reach - OriginY) / Cell)), r1 = Math.Min(Rows - 1, (int)Math.Floor((from.Y + reach - OriginY) / Cell));
        double rr = radius * radius, reach2 = reach * reach;
        for (int r = r0; r <= r1; r++)
        {
            double dy = Cy(r) - from.Y;
            int row = r * Columns;
            for (int c = c0; c <= c1; c++)
            {
                if (!uncut[row + c])
                {
                    continue;
                }

                double dx = Cx(c) - from.X;
                double d2 = (dx * dx) + (dy * dy);
                if (d2 <= rr || d2 > reach2)
                {
                    continue;
                }

                double d = Math.Sqrt(d2);
                double kappa = (d2 + (step * step) - rr) / (2 * d * step);
                if (kappa > 1)
                {
                    continue;
                }

                double alpha = kappa <= -1 ? Math.PI : Math.Acos(kappa);
                double rel = Math.Atan2(dy, dx) - heading;
                rel = Math.IEEERemainder(rel, 2 * Math.PI);
                double kc = (rel / angleStep) + count;
                int lo = Math.Max(0, (int)Math.Ceiling(kc - (alpha / angleStep) - 1e-9));
                int hi = Math.Min(n - 1, (int)Math.Floor(kc + (alpha / angleStep) + 1e-9));
                if (lo > hi)
                {
                    continue;
                }

                da[lo]++;
                da[hi + 1]--;

                // candidatos com ângulo menor que o da célula a deixam à esquerda do avanço
                int leftHi = Math.Min(hi, (int)Math.Ceiling(kc) - 1);
                if (leftHi >= lo)
                {
                    dl[lo]++;
                    dl[leftHi + 1]--;
                }
                if (pending[row + c])
                {
                    dw[lo]++;
                    dw[hi + 1]--;
                }
            }
        }

        int sa = 0, sw = 0, sl = 0;
        for (int k = 0; k < n; k++)
        {
            sa += da[k];
            sw += dw[k];
            sl += dl[k];
            area[k] = sa;
            work[k] = sw;
            left[k] = sl;
        }
    }

    /// <summary>Tira do material os pedaços soltos pequenos demais para valer uma entrada (ficam para a volta na parede).</summary>
    public void DropSlivers(bool[] uncut, int minCells)
    {
        var seen = new bool[uncut.Length];
        var stack = new Stack<int>();
        var part = new List<int>();
        for (int s = 0; s < uncut.Length; s++)
        {
            if (!uncut[s] || seen[s])
            {
                continue;
            }

            part.Clear();
            stack.Push(s);
            seen[s] = true;
            while (stack.Count > 0)
            {
                int k = stack.Pop();
                part.Add(k);
                int c = k % Columns, r = k / Columns;
                for (int dr = -1; dr <= 1; dr++)
                {
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        int nc = c + dc, nr = r + dr;
                        if (nc < 0 || nr < 0 || nc >= Columns || nr >= Rows)
                        {
                            continue;
                        }

                        int n = (nr * Columns) + nc;
                        if (uncut[n] && !seen[n])
                        {
                            seen[n] = true;
                            stack.Push(n);
                        }
                    }
                }
            }

            if (part.Count < minCells)
            {
                foreach (int k in part)
                {
                    uncut[k] = false;
                }
            }
        }
    }

    /// <summary>
    /// Caminho (centros de célula) por onde a ferramenta anda sem cortar até o primeiro ponto permitido que encosta no material.
    /// Null quando o material que sobra não se alcança por baixo.
    /// </summary>
    public List<Point2>? PathToMaterial(bool[] uncut, Point2 from, double radius)
    {
        float[] toMaterial = Distance(uncut, Columns, Rows, Cell, feature: true);
        // margem de meia célula: a célula achada sempre tira material ao chegar (garante que a ligação avança)
        double touch = radius - (0.5 * Cell);
        int start = IndexOf(from, out _, out _);
        if (start < 0)
        {
            return null;
        }

        var parent = new int[_allowed.Length];
        Array.Fill(parent, -2);
        var queue = new Queue<int>();
        queue.Enqueue(start);
        parent[start] = -1;
        int found = -1;
        while (queue.Count > 0)
        {
            int k = queue.Dequeue();
            if (k != start && toMaterial[k] <= touch)
            {
                // encosta no material: vale só chegando de uma célula livre (nunca entra de frente no material)
                if (parent[k] >= 0 && toMaterial[parent[k]] > touch)
                {
                    found = k;
                    break;
                }

                continue;
            }

            int c = k % Columns, r = k / Columns;
            for (int dr = -1; dr <= 1; dr++)
            {
                for (int dc = -1; dc <= 1; dc++)
                {
                    int nc = c + dc, nr = r + dr;
                    if ((dc == 0 && dr == 0) || nc < 0 || nr < 0 || nc >= Columns || nr >= Rows)
                    {
                        continue;
                    }

                    int n = (nr * Columns) + nc;
                    if (parent[n] != -2 || !_allowed[n])
                    {
                        continue;
                    }

                    // anda só onde não corta (longe do material) ou chega na primeira célula que encosta
                    parent[n] = k;
                    queue.Enqueue(n);
                }
            }

        }

        if (found < 0)
        {
            return null;
        }

        var cells = new List<Point2>();
        for (int k = found; k != start && k >= 0; k = parent[k])
        {
            cells.Add(new Point2(Cx(k % Columns), Cy(k / Columns)));
        }

        cells.Reverse();
        return cells;
    }

    /// <summary>Encurta o caminho de ligação em retas que ficam no limpo (sem material no alcance da ferramenta).</summary>
    public List<Point2> Simplify(bool[] uncut, Point2 from, List<Point2> cells, double radius)
    {
        var result = new List<Point2>();
        Point2 anchor = from;
        int i = 0;
        while (i < cells.Count)
        {
            int j = cells.Count - 1;
            while (j > i && !ClearLine(uncut, anchor, cells[j], radius, j == cells.Count - 1))
            {
                j--;
            }

            result.Add(cells[j]);
            anchor = cells[j];
            i = j + 1;
        }

        return result;
    }

    private bool ClearLine(bool[] uncut, Point2 a, Point2 b, double radius, bool last)
    {
        int n = Math.Max(1, (int)Math.Ceiling(a.DistanceTo(b) / (Cell * 0.5)));
        for (int i = 1; i <= n; i++)
        {
            var q = new Point2(a.X + ((b.X - a.X) * i / n), a.Y + ((b.Y - a.Y) * i / n));
            if (!IsAllowed(q) || ((!last || i < n) && HasMaterialNear(uncut, q, radius)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Vetor unitário de <paramref name="at"/> para o centro do material dentro do raio (0,0 sem material).</summary>
    public Point2 TowardMaterial(bool[] uncut, Point2 at, double radius)
    {
        double sx = 0, sy = 0;
        int n = 0;
        Visit(at, radius, (k, _) =>
        {
            if (uncut[k])
            {
                sx += Cx(k % Columns) - at.X;
                sy += Cy(k / Columns) - at.Y;
                n++;
            }
        });
        double len = Math.Sqrt((sx * sx) + (sy * sy));
        return n == 0 || len < 1e-12 ? new Point2(1, 0) : new Point2(sx / len, sy / len);
    }

    /// <summary>
    /// CAM-ADAPT-01b, borda aberta: ponto permitido onde a ferramenta fica toda no ar (longe do material) e que está mais perto do
    /// material que ainda falta. Null = nenhum.
    /// </summary>
    public Point2? OpenEntry(bool[] real, bool[] pending, double radius)
    {
        if (Array.IndexOf(pending, true) < 0)
        {
            return null;
        }

        float[] toReal = Distance(real, Columns, Rows, Cell, feature: true);
        float[] toPending = Distance(pending, Columns, Rows, Cell, feature: true);
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int k = 0; k < _allowed.Length; k++)
        {
            if (_allowed[k] && toReal[k] > radius + Cell && toPending[k] < bestDistance)
            {
                best = k;
                bestDistance = toPending[k];
            }
        }

        return best < 0 ? null : new Point2(Cx(best % Columns), Cy(best / Columns));
    }

    /// <summary>Centro para a hélice: dentro do material, com espaço para o círculo da hélice. O mais folgado. Null = não cabe.</summary>
    public Point2? HelixCenter(bool[] uncut, double helixRadius)
    {
        double need = _minDistance + helixRadius + Cell;
        int best = -1;
        float bestClear = 0;
        for (int k = 0; k < uncut.Length; k++)
        {
            if (uncut[k] && _allowed[k] && _clearance[k] >= need && _clearance[k] > bestClear)
            {
                best = k;
                bestClear = _clearance[k];
            }
        }

        return best < 0 ? null : new Point2(Cx(best % Columns), Cy(best / Columns));
    }

    /// <summary>Uma volta deslocada só é segura se todos os pontos ficam à distância certa de todas as paredes e ilhas.</summary>
    public bool LoopIsSafe(Contour loop) => loop.Sample(0.01).All(q => WallDistance(q) >= _minDistance - 0.01 && InsideRegion(q));

    /// <summary>Quantos pontos de corte (abaixo de <paramref name="aboveZ"/>) ficam perto demais da parede ou de uma ilha.</summary>
    public int UnsafeCount(IReadOnlyList<ToolMove> moves, double aboveZ)
    {
        int bad = 0;
        for (int i = 1; i < moves.Count; i++)
        {
            ToolMove m = moves[i];
            if (m.Kind == MoveKind.Rapid || m.Z >= aboveZ - 1e-9)
            {
                continue;
            }

            IEnumerable<Point2> pts = m.Kind == MoveKind.Feed
                ? [moves[i - 1].XY, m.XY]
                : Segment2.Arc(moves[i - 1].XY, m.XY, m.Center, m.Kind == MoveKind.ArcCcw).Sample(0.01);
            foreach (Point2 q in pts)
            {
                if (moves[i - 1].Z >= aboveZ - 1e-9 && q == moves[i - 1].XY)
                {
                    continue;
                }

                if (WallDistance(q) < _minDistance - 0.01 || !InsideRegion(q))
                {
                    bad++;
                }
            }
        }

        return bad;
    }

    private bool InsideRegion(Point2 q)
    {
        if (!Inside(_walls[0], q))
        {
            return false;
        }

        for (int i = 1; i < _walls.Count; i++)
        {
            if (Inside(_walls[i], q))
            {
                return false;
            }
        }

        return true;
    }

    private double WallDistance(Point2 q)
    {
        double best = double.MaxValue;
        foreach (List<Point2> poly in _walls)
        {
            for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
            {
                best = Math.Min(best, SegmentDistance(q, poly[j], poly[i]));
            }
        }

        return best;
    }

    private static double SegmentDistance(Point2 p, Point2 a, Point2 b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y, len2 = (dx * dx) + (dy * dy);
        double t = len2 < 1e-18 ? 0 : Math.Clamp((((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / len2, 0, 1);
        double ex = p.X - (a.X + (t * dx)), ey = p.Y - (a.Y + (t * dy));
        return Math.Sqrt((ex * ex) + (ey * ey));
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

    private void Visit(Point2 p, double radius, Action<int, double> each)
    {
        int c0 = Math.Max(0, (int)Math.Floor((p.X - radius - OriginX) / Cell)), c1 = Math.Min(Columns - 1, (int)Math.Floor((p.X + radius - OriginX) / Cell));
        int r0 = Math.Max(0, (int)Math.Floor((p.Y - radius - OriginY) / Cell)), r1 = Math.Min(Rows - 1, (int)Math.Floor((p.Y + radius - OriginY) / Cell));
        double r2 = radius * radius;
        for (int r = r0; r <= r1; r++)
        {
            double dy = Cy(r) - p.Y;
            for (int c = c0; c <= c1; c++)
            {
                double dx = Cx(c) - p.X;
                double d2 = (dx * dx) + (dy * dy);
                if (d2 <= r2)
                {
                    each((r * Columns) + c, d2);
                }
            }
        }
    }

    // preenche (true) ou fura (false) a área do polígono por linhas
    private static void Fill(bool[] mask, List<Point2> poly, double ox, double oy, int cols, int rows, double cell, bool value)
    {
        var xs = new List<double>();
        for (int r = 0; r < rows; r++)
        {
            double y = oy + ((r + 0.5) * cell);
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
                int c0 = Math.Max(0, (int)Math.Ceiling(((xs[t] - ox) / cell) - 0.5));
                int c1 = Math.Min(cols - 1, (int)Math.Floor(((xs[t + 1] - ox) / cell) - 0.5));
                for (int c = c0; c <= c1; c++)
                {
                    mask[(r * cols) + c] = value;
                }
            }
        }
    }

    /// <summary>
    /// Distância (mm, centro a centro) de cada célula até a célula mais perto com <c>mask == feature</c> (transformada exata de
    /// Felzenszwalb). Fora da grade conta como feature quando <paramref name="feature"/> é false (a borda fica fora do bolsão).
    /// </summary>
    private static float[] Distance(bool[] mask, int cols, int rows, double cell, bool feature)
    {
        const double inf = 1e20;
        var g = new double[cols * rows];
        for (int k = 0; k < g.Length; k++)
        {
            g[k] = mask[k] == feature ? 0 : inf;
        }

        var f = new double[Math.Max(cols, rows) + 2];
        var d = new double[f.Length];
        var v = new int[f.Length];
        var zz = new double[f.Length + 1];
        for (int c = 0; c < cols; c++)
        {
            int n = rows;
            for (int r = 0; r < n; r++)
            {
                f[r] = g[(r * cols) + c];
            }

            Edt1(f, n, d, v, zz, !feature);
            for (int r = 0; r < n; r++)
            {
                g[(r * cols) + c] = d[r];
            }
        }

        for (int r = 0; r < rows; r++)
        {
            int n = cols;
            Array.Copy(g, r * cols, f, 0, n);
            Edt1(f, n, d, v, zz, !feature);
            Array.Copy(d, 0, g, r * cols, n);
        }

        var result = new float[g.Length];
        for (int k = 0; k < g.Length; k++)
        {
            result[k] = (float)(Math.Sqrt(g[k]) * cell);
        }

        return result;
    }

    // 1D: d[q] = min_p (q−p)² + f[p]; com bordaFeature, conta uma célula feature logo antes e logo depois da linha
    private static void Edt1(double[] f, int n, double[] d, int[] v, double[] z, bool borderFeature)
    {
        int k = 0;
        v[0] = 0;
        z[0] = double.NegativeInfinity;
        z[1] = double.PositiveInfinity;
        for (int q = 1; q < n; q++)
        {
            double s = ((f[q] + ((double)q * q)) - (f[v[k]] + ((double)v[k] * v[k]))) / (2.0 * (q - v[k]));
            while (s <= z[k])
            {
                k--;
                s = ((f[q] + ((double)q * q)) - (f[v[k]] + ((double)v[k] * v[k]))) / (2.0 * (q - v[k]));
            }

            k++;
            v[k] = q;
            z[k] = s;
            z[k + 1] = double.PositiveInfinity;
        }

        k = 0;
        for (int q = 0; q < n; q++)
        {
            while (z[k + 1] < q)
            {
                k++;
            }

            double dq = q - v[k];
            d[q] = (dq * dq) + f[v[k]];
            if (borderFeature)
            {
                double before = (q + 1.0) * (q + 1.0), after = (n - q) * (double)(n - q);
                d[q] = Math.Min(d[q], Math.Min(before, after));
            }
        }
    }
}

/// <summary>Transforma a sequência de pontos do adaptativo em retas e arcos (tolerância dada), para o G-code não virar milhares de linhas.</summary>
internal static class AdaptiveFit
{
    public static IEnumerable<ToolMove> Moves(IReadOnlyList<Point2> pts, double z, double feed, double tol)
    {
        int i = 0;
        while (i < pts.Count - 1)
        {
            int lineEnd = Extend(pts, i, j => LineFits(pts, i, j, tol));
            int arcEnd = pts.Count - i >= 3 ? Extend(pts, i, j => j - i >= 2 && ArcFits(pts, i, j, tol, out _, out _)) : i;
            if (arcEnd >= i + 3 && arcEnd > lineEnd)
            {
                ArcFits(pts, i, arcEnd, tol, out Point2 center, out bool ccw);
                yield return new ToolMove(ccw ? MoveKind.ArcCcw : MoveKind.ArcCw, pts[arcEnd].X, pts[arcEnd].Y, z, center, feed);
                i = arcEnd;
            }
            else
            {
                int j = Math.Max(lineEnd, i + 1);
                yield return new ToolMove(MoveKind.Feed, pts[j].X, pts[j].Y, z, default, feed);
                i = j;
            }
        }
    }

    // maior j (> i) com fits(j), procurando por dobra e depois por busca binária
    private static int Extend(IReadOnlyList<Point2> pts, int i, Func<int, bool> fits)
    {
        int last = pts.Count - 1;
        int good = i + 1;
        if (good > last)
        {
            return last;
        }

        int stepSize = 2;
        int bad = -1;
        while (true)
        {
            int j = Math.Min(i + stepSize, last);
            if (!fits(j))
            {
                bad = j;
                break;
            }

            good = j;
            if (j == last || stepSize > 400)
            {
                return good;
            }

            stepSize *= 2;
        }

        while (bad - good > 1)
        {
            int mid = (good + bad) / 2;
            if (fits(mid))
            {
                good = mid;
            }
            else
            {
                bad = mid;
            }
        }

        return good;
    }

    private static bool LineFits(IReadOnlyList<Point2> pts, int i, int j, double tol)
    {
        Point2 a = pts[i], b = pts[j];
        double dx = b.X - a.X, dy = b.Y - a.Y, len = Math.Sqrt((dx * dx) + (dy * dy));
        if (len < 1e-9)
        {
            return j == i + 1;
        }

        for (int k = i + 1; k < j; k++)
        {
            Point2 p = pts[k];
            double t = (((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / (len * len);
            if (t < -1e-9 || t > 1 + 1e-9 || Math.Abs(((p.X - a.X) * dy) - ((p.Y - a.Y) * dx)) / len > tol)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ArcFits(IReadOnlyList<Point2> pts, int i, int j, double tol, out Point2 center, out bool ccw)
    {
        center = default;
        ccw = true;
        Point2 a = pts[i], m = pts[(i + j) / 2], b = pts[j];
        double d = 2 * ((a.X * (m.Y - b.Y)) + (m.X * (b.Y - a.Y)) + (b.X * (a.Y - m.Y)));
        if (Math.Abs(d) < 1e-12)
        {
            return false;
        }

        double a2 = (a.X * a.X) + (a.Y * a.Y), m2 = (m.X * m.X) + (m.Y * m.Y), b2 = (b.X * b.X) + (b.Y * b.Y);
        center = new Point2(((a2 * (m.Y - b.Y)) + (m2 * (b.Y - a.Y)) + (b2 * (a.Y - m.Y))) / d, ((a2 * (b.X - m.X)) + (m2 * (a.X - b.X)) + (b2 * (m.X - a.X))) / d);
        double radius = center.DistanceTo(a);
        if (radius > 500 || radius < 0.05)
        {
            return false;
        }

        ccw = ((m.X - a.X) * (b.Y - m.Y)) - ((m.Y - a.Y) * (b.X - m.X)) > 0;
        double total = 0;
        double prev = Math.Atan2(a.Y - center.Y, a.X - center.X);
        for (int k = i + 1; k <= j; k++)
        {
            Point2 p = pts[k];
            if (Math.Abs(center.DistanceTo(p) - radius) > tol)
            {
                return false;
            }

            // também o meio de cada trecho fica no arco (cordas curtas)
            var mid = new Point2((p.X + pts[k - 1].X) / 2, (p.Y + pts[k - 1].Y) / 2);
            if (Math.Abs(center.DistanceTo(mid) - radius) > tol)
            {
                return false;
            }

            double ang = Math.Atan2(p.Y - center.Y, p.X - center.X);
            double delta = ang - prev;
            while (delta > Math.PI)
            {
                delta -= 2 * Math.PI;
            }

            while (delta < -Math.PI)
            {
                delta += 2 * Math.PI;
            }

            if ((ccw && delta <= 0) || (!ccw && delta >= 0))
            {
                return false;
            }

            total += Math.Abs(delta);
            prev = ang;
        }

        return total < (2 * Math.PI) - 0.05;
    }
}
