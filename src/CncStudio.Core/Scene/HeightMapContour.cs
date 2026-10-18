using CncStudio.Core.Geometry;
using CncStudio.Core.Rendering;
using CncStudio.Core.Simulation;

namespace CncStudio.Core.Scene;

/// <summary>
/// Malha do material com contorno liso. A grade de alturas guarda uma altura por célula; desenhar
/// cada célula como uma caixa deixa furos redondos e paredes inclinadas em degraus. Aqui a superfície
/// é montada sobre a grade "dual" (quadrados entre os centros de 4 células vizinhas): em cada
/// quadrado, para cada altura que aparece nos 4 cantos, o trecho "pelo menos tão alto" é um polígono
/// (marching squares) e a borda dele vira uma parede vertical até o nível de baixo.
///
/// Onde duas células vizinhas têm alturas diferentes, o ponto da parede na aresta entre elas não fica
/// no meio: sai de um campo suavizado (a indicação "tem material nesta altura" com um filtro 1-2-1 em
/// 3 × 3 células), então um círculo cortado na grade volta a ser um círculo, e uma parede reta continua
/// exatamente no meio. O ponto depende só do par de células, igual nos dois quadrados que dividem a
/// aresta; por isso a malha não abre frestas. As normais das paredes seguem o gradiente do mesmo campo.
///
/// Cada quadrado depende das células a até 2 de distância (ver <see cref="HeightMap.HaloCells"/>).
/// As arestas vivas (o alto e o pé de cada parede) saem junto, em <see cref="SolidMesh.Edges"/>.
/// </summary>
internal sealed class HeightMapContour
{
    // Margem (fração da distância entre centros) que mantém o ponto da parede longe dos centros
    private const double MinCrossing = 0.15, MaxCrossing = 0.85;

    private readonly HeightMap _map;
    private readonly ScenePalette _palette;
    private readonly int _columns, _rows;
    private readonly double _size, _ox, _oy, _top, _bottom, _flatTolerance;
    private readonly int[] _ci = new int[4], _rj = new int[4];
    private readonly float[] _hh = new float[4];
    private readonly Crossing[] _cross = new Crossing[4];
    private readonly Point3[] _poly = new Point3[8];
    private readonly int[] _polyEdge = new int[8];
    private SolidMesh _mesh = SolidMesh.Create(1);

    private struct Crossing
    {
        public double X, Y, Nx, Ny;
    }

    public HeightMapContour(HeightMap map, ScenePalette palette)
    {
        _map = map;
        _palette = palette;
        _columns = map.Columns;
        _rows = map.Rows;
        _size = map.CellSize;
        _ox = map.OriginX;
        _oy = map.OriginY;
        _top = map.TopZ;
        _bottom = map.BottomZ;
        _flatTolerance = Math.Max(1e-4, map.CellSize * 0.01);
    }

    /// <summary>
    /// Malha de um bloco. Cada quadrado dual pertence ao bloco da célula do seu canto de menor X e Y;
    /// os quadrados de meia célula na borda da grade (cantos virtuais iguais à célula da borda) ficam
    /// com os blocos da primeira coluna e da primeira linha.
    /// </summary>
    public SolidMesh BuildTile(int tileColumn, int tileRow)
    {
        int c0 = tileColumn * HeightMap.TileSize, r0 = tileRow * HeightMap.TileSize;
        int c1 = Math.Min(_columns, c0 + HeightMap.TileSize), r1 = Math.Min(_rows, r0 + HeightMap.TileSize);
        int iFirst = c0 == 0 ? -1 : c0, jFirst = r0 == 0 ? -1 : r0;
        _mesh = SolidMesh.Create((c1 - iFirst) * (r1 - jFirst) * 2);
        _mesh.UseExplicitEdges();

        for (int j = jFirst; j < r1; j++)
        {
            for (int i = iFirst; i < c1; i++)
            {
                Dual(i, j);
            }
        }

        // Fundo do bloco (o mesmo retângulo que os quadrados dual do bloco cobrem)
        double xa = ClampX(Cx(iFirst)), xb = ClampX(Cx(c1)), ya = ClampY(Cy(jFirst)), yb = ClampY(Cy(r1));
        _mesh.AddQuad(new Point3(xa, ya, _bottom), new Point3(xb, ya, _bottom), new Point3(xb, yb, _bottom), new Point3(xa, yb, _bottom),
            new Point3(0, 0, -1), _palette.Stock);
        return _mesh;
    }

    // ── Grade ──

    private float H(int column, int row) => _map[Math.Clamp(column, 0, _columns - 1), Math.Clamp(row, 0, _rows - 1)];

    private double Cx(int column) => _ox + (column + 0.5) * _size;

    private double Cy(int row) => _oy + (row + 0.5) * _size;

    private double ClampX(double x) => Math.Clamp(x, _ox, _ox + _columns * _size);

    private double ClampY(double y) => Math.Clamp(y, _oy, _oy + _rows * _size);

    private uint ColorOf(float z, int column, int row)
    {
        column = Math.Clamp(column, 0, _columns - 1);
        row = Math.Clamp(row, 0, _rows - 1);
        return z >= _top - 1e-4
            ? _palette.Stock
            : _palette.Machined((_top - z) / Math.Max(_top - _bottom, 1e-6), _map.CutBy(column, row));
    }

    /// <summary>Indicação "tem material pelo menos até esta altura", suavizada com um filtro 1-2-1 em 3 × 3 células.</summary>
    private double Field(int column, int row, float level)
    {
        int sum = 0;
        for (int dr = -1; dr <= 1; dr++)
        {
            int wr = dr == 0 ? 2 : 1;
            for (int dc = -1; dc <= 1; dc++)
            {
                if (H(column + dc, row + dr) >= level)
                {
                    sum += wr * (dc == 0 ? 2 : 1);
                }
            }
        }

        return sum / 16.0;
    }

    /// <summary>
    /// Ponto da parede na aresta entre duas células de alturas diferentes. Sempre chamada com o par na
    /// ordem canônica (menor coluna e linha primeiro), para os dois quadrados vizinhos acharem o mesmo ponto.
    /// Células fora da grade valem como a da borda (iguais), com a posição real do centro virtual.
    /// </summary>
    private Crossing CrossingOf(int c0, int r0, int c1, int r1)
    {
        int ca = Math.Clamp(c0, 0, _columns - 1), ra = Math.Clamp(r0, 0, _rows - 1);
        int cb = Math.Clamp(c1, 0, _columns - 1), rb = Math.Clamp(r1, 0, _rows - 1);
        float ha = H(ca, ra), hb = H(cb, rb);
        float level = Math.Max(ha, hb);
        double fa = Field(ca, ra, level), fb = Field(cb, rb, level);
        double t = fa == fb ? 0.5 : (0.5 - fa) / (fb - fa);
        t = Math.Clamp(t, MinCrossing, MaxCrossing);

        // Gradiente do campo no ponto (interpolado entre as duas células); a normal aponta para o lado baixo
        double gax = (Field(ca + 1, ra, level) - Field(ca - 1, ra, level)) * 0.5;
        double gay = (Field(ca, ra + 1, level) - Field(ca, ra - 1, level)) * 0.5;
        double gbx = (Field(cb + 1, rb, level) - Field(cb - 1, rb, level)) * 0.5;
        double gby = (Field(cb, rb + 1, level) - Field(cb, rb - 1, level)) * 0.5;
        double nx = -(gax + t * (gbx - gax)), ny = -(gay + t * (gby - gay));

        double dx = Cx(c1) - Cx(c0), dy = Cy(r1) - Cy(r0);
        double sign = ha > hb ? 1 : -1;   // de quem é mais alto para quem é mais baixo
        double len = Math.Sqrt(nx * nx + ny * ny);
        if (len < 1e-9 || nx * dx * sign + ny * dy * sign <= 0)
        {
            double dl = Math.Sqrt(dx * dx + dy * dy);
            nx = dx * sign / dl;
            ny = dy * sign / dl;
        }
        else
        {
            nx /= len;
            ny /= len;
        }

        return new Crossing { X = Cx(c0) + t * dx, Y = Cy(r0) + t * dy, Nx = nx, Ny = ny };
    }

    // ── Um quadrado dual ──

    private void Dual(int i, int j)
    {
        _ci[0] = i; _rj[0] = j;
        _ci[1] = i + 1; _rj[1] = j;
        _ci[2] = i + 1; _rj[2] = j + 1;
        _ci[3] = i; _rj[3] = j + 1;
        float max = float.MinValue, min = float.MaxValue;
        int maxCorner = 0;
        for (int k = 0; k < 4; k++)
        {
            float h = H(_ci[k], _rj[k]);
            _hh[k] = h;
            if (h > max)
            {
                max = h;
                maxCorner = k;
            }

            min = Math.Min(min, h);
        }

        double x0 = ClampX(Cx(i)), x1 = ClampX(Cx(i + 1)), y0 = ClampY(Cy(j)), y1 = ClampY(Cy(j + 1));
        var up = new Point3(0, 0, 1);

        if (max - min <= _flatTolerance)
        {
            // Plano (a grande maioria): um quadrado só; diferenças de menos de 1% da célula não aparecem.
            // No fundo de um bruto redondo ou tubo não há material: não desenha nada
            if (_map.HasEmptyCells && max <= _bottom)
            {
                return;
            }

            _mesh.AddQuad(new Point3(x0, y0, max), new Point3(x1, y0, max), new Point3(x1, y1, max), new Point3(x0, y1, max),
                up, ColorOf(max, _ci[maxCorner], _rj[maxCorner]));
            Borders(i, j, max, max, max, max, hasCrossing: false);
            return;
        }

        for (int e = 0; e < 4; e++)
        {
            int a = e, b = (e + 1) % 4;
            if (_hh[a] == _hh[b])
            {
                continue;
            }

            // Ordem canônica da aresta: 0→1, 1→2, 3→2, 0→3 (sempre de menor X ou Y para o maior)
            _cross[e] = e switch
            {
                0 => CrossingOf(_ci[0], _rj[0], _ci[1], _rj[1]),
                1 => CrossingOf(_ci[1], _rj[1], _ci[2], _rj[2]),
                2 => CrossingOf(_ci[3], _rj[3], _ci[2], _rj[2]),
                _ => CrossingOf(_ci[0], _rj[0], _ci[3], _rj[3]),
            };
        }

        Span<float> levels = stackalloc float[4];
        int count = 0;
        for (int k = 0; k < 4; k++)
        {
            float h = _hh[k];
            bool seen = false;
            for (int m = 0; m < count; m++)
            {
                if (levels[m] == h)
                {
                    seen = true;
                    break;
                }
            }

            if (!seen)
            {
                levels[count++] = h;
            }
        }

        levels[..count].Sort();

        Span<bool> inn = stackalloc bool[4];
        for (int li = 0; li < count; li++)
        {
            float level = levels[li];
            int cell = 0;
            for (int k = 0; k < 4; k++)
            {
                inn[k] = _hh[k] >= level;
                if (_hh[k] == level)
                {
                    cell = k;
                }
            }

            uint color = ColorOf(level, _ci[cell], _rj[cell]);
            if (li == 0)
            {
                if (_map.HasEmptyCells && level <= _bottom)
                {
                    continue;   // fora do bruto redondo (ou no furo do tubo): só as paredes sobem dali
                }

                _mesh.AddQuad(new Point3(x0, y0, level), new Point3(x1, y0, level), new Point3(x1, y1, level), new Point3(x0, y1, level), up, color);
                continue;
            }

            // Altura e cor do que fica ao pé da parede (o nível logo abaixo neste quadrado)
            float below = levels[li - 1];
            int belowCell = 0;
            for (int k = 0; k < 4; k++)
            {
                if (_hh[k] == below)
                {
                    belowCell = k;
                }
            }

            uint wallColor = _map.HasEmptyCells && below <= _bottom && _map.CutBy(Math.Clamp(_ci[belowCell], 0, _columns - 1), Math.Clamp(_rj[belowCell], 0, _rows - 1)) == 0
                ? _palette.Stock   // parede lateral do bruto redondo
                : ColorOf(below, _ci[belowCell], _rj[belowCell]);
            bool saddle = inn[0] == inn[2] && inn[1] == inn[3] && inn[0] != inn[1];
            if (saddle)
            {
                // Dois cantos em diagonal: um triângulo em cada canto de cima
                for (int k = 0; k < 4; k++)
                {
                    if (inn[k])
                    {
                        int prev = (k + 3) % 4;
                        _poly[0] = Corner(k);
                        _poly[1] = At(_cross[k], level);
                        _poly[2] = At(_cross[prev], level);
                        EmitTop(3, level, up, color);
                        EmitWall(_cross[k], _cross[prev], below, level, wallColor);
                    }
                }

                continue;
            }

            int n = 0;
            for (int k = 0; k < 4; k++)
            {
                int next = (k + 1) % 4;
                if (inn[k])
                {
                    _poly[n] = Corner(k);
                    _polyEdge[n++] = -1;
                }

                if (inn[k] != inn[next])
                {
                    _poly[n] = At(_cross[k], level);
                    _polyEdge[n++] = k;
                }
            }

            EmitTop(n, level, up, color);
            for (int v = 0; v < n; v++)
            {
                int w = (v + 1) % n;
                if (_polyEdge[v] >= 0 && _polyEdge[w] >= 0)
                {
                    EmitWall(_cross[_polyEdge[v]], _cross[_polyEdge[w]], below, level, wallColor);
                }
            }
        }

        Borders(i, j, _hh[0], _hh[1], _hh[2], _hh[3], hasCrossing: true);
    }

    private Point3 Corner(int k) => new(ClampX(Cx(_ci[k])), ClampY(Cy(_rj[k])), 0);

    private Point3 At(Crossing c, double z) => new(ClampX(c.X), ClampY(c.Y), z);

    // Topo do polígono em _poly[0..n) à altura z (os cantos vêm com z = 0: ajustado aqui)
    private void EmitTop(int n, double z, Point3 up, uint color)
    {
        Span<Point3> pts = stackalloc Point3[8];
        for (int k = 0; k < n; k++)
        {
            pts[k] = new Point3(_poly[k].X, _poly[k].Y, z);
        }

        _mesh.AddPolygon(pts[..n], up, color);
    }

    // Parede entre dois pontos de cruzamento (o lado "dentro" fica à esquerda de p → q)
    private void EmitWall(Crossing p, Crossing q, double zLow, double zHigh, uint color)
    {
        double px = ClampX(p.X), py = ClampY(p.Y), qx = ClampX(q.X), qy = ClampY(q.Y);
        double dx = qx - px, dy = qy - py;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-9)
        {
            return;
        }

        var normal = new Point3(dy / len, -dx / len, 0);
        _mesh.AddWall(px, py, qx, qy, zLow, zHigh, normal, new Point3(p.Nx, p.Ny, 0), new Point3(q.Nx, q.Ny, 0), color);
        _mesh.AddEdge(px, py, zHigh, qx, qy, zHigh);
        _mesh.AddEdge(px, py, zLow, qx, qy, zLow);
    }

    // ── Laterais externas (cor do bruto), do fundo até a altura do topo ──

    private void Borders(int i, int j, float h0, float h1, float h2, float h3, bool hasCrossing)
    {
        double x0 = ClampX(Cx(i)), x1 = ClampX(Cx(i + 1)), y0 = ClampY(Cy(j)), y1 = ClampY(Cy(j + 1));
        double west = _ox, east = _ox + _columns * _size, south = _oy, north = _oy + _rows * _size;

        if (i == -1)
        {
            Lateral(alongX: false, west, y0, y1, h0, h3, hasCrossing && h0 != h3 ? _cross[3].Y : 0, new Point3(-1, 0, 0));
        }

        if (i == _columns - 1)
        {
            Lateral(alongX: false, east, y0, y1, h1, h2, hasCrossing && h1 != h2 ? _cross[1].Y : 0, new Point3(1, 0, 0));
        }

        if (j == -1)
        {
            Lateral(alongX: true, south, x0, x1, h0, h1, hasCrossing && h0 != h1 ? _cross[0].X : 0, new Point3(0, -1, 0));
        }

        if (j == _rows - 1)
        {
            Lateral(alongX: true, north, x0, x1, h3, h2, hasCrossing && h3 != h2 ? _cross[2].X : 0, new Point3(0, 1, 0));
        }
    }

    // Face lateral ao longo de uma borda: altura "first" até o ponto "split", "second" depois dele
    private void Lateral(bool alongX, double fixedCoord, double from, double to, float first, float second, double split, Point3 normal)
    {
        double mid = first == second ? to : Math.Clamp(split, from, to);
        Strip(alongX, fixedCoord, from, mid, first, normal);
        if (first != second)
        {
            Strip(alongX, fixedCoord, mid, to, second, normal);
        }
    }

    private void Strip(bool alongX, double fixedCoord, double from, double to, float height, Point3 normal)
    {
        if (height <= _bottom || to - from < 1e-12)
        {
            return;
        }

        Point3 P(double a, double z) => alongX ? new Point3(a, fixedCoord, z) : new Point3(fixedCoord, a, z);
        _mesh.AddQuad(P(from, _bottom), P(to, _bottom), P(to, height), P(from, height), normal, _palette.Stock);
    }
}
