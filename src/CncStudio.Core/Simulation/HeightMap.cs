using CncStudio.Core.Geometry;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Simulation;

/// <summary>
/// Representação 2.5D do material: uma grade regular no plano XY em que cada
/// célula guarda a altura (Z) do topo do material naquele ponto.
/// </summary>
public sealed class HeightMap
{
    private readonly float[] _heights;
    private readonly ushort[] _cutBy;
    private readonly int[] _tileVersion;
    private readonly int[] _haloVersion;

    /// <summary>
    /// Lado (em células) dos blocos usados para saber o que mudou: quem desenha guarda a versão de
    /// cada bloco e refaz só os blocos alterados (ver <see cref="TileVersion"/>).
    /// </summary>
    public const int TileSize = 32;

    /// <param name="originX">X do canto inferior esquerdo da grade (mm).</param>
    /// <param name="originY">Y do canto inferior esquerdo da grade (mm).</param>
    /// <param name="columns">Número de células em X.</param>
    /// <param name="rows">Número de células em Y.</param>
    /// <param name="cellSize">Lado de cada célula (mm).</param>
    /// <param name="topZ">Topo do bruto (altura inicial de todas as células).</param>
    /// <param name="bottomZ">Fundo do bruto (a ferramenta não remove abaixo disso).</param>
    public HeightMap(double originX, double originY, int columns, int rows, double cellSize, double topZ, double bottomZ)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(cellSize, 0);
        if (bottomZ >= topZ)
        {
            throw new ArgumentException("O fundo do bruto precisa estar abaixo do topo.", nameof(bottomZ));
        }

        OriginX = originX;
        OriginY = originY;
        Columns = columns;
        Rows = rows;
        CellSize = cellSize;
        TopZ = topZ;
        BottomZ = bottomZ;
        _heights = new float[columns * rows];
        _cutBy = new ushort[columns * rows];
        TileColumns = (columns + TileSize - 1) / TileSize;
        TileRows = (rows + TileSize - 1) / TileSize;
        _tileVersion = new int[TileColumns * TileRows];
        _haloVersion = new int[TileColumns * TileRows];
        Reset();
    }

    public double OriginX { get; }
    public double OriginY { get; }
    public int Columns { get; }
    public int Rows { get; }
    public double CellSize { get; }
    public double TopZ { get; }
    public double BottomZ { get; }

    public double Width => Columns * CellSize;
    public double Height => Rows * CellSize;

    /// <summary>
    /// Incrementado sempre que alguma célula muda. A UI compara com o último
    /// valor desenhado para saber se precisa redesenhar.
    /// </summary>
    public int Version { get; private set; }

    /// <summary>Alturas em ordem linha a linha: índice = row * Columns + column (row 0 = menor Y).</summary>
    public ReadOnlySpan<float> Heights => _heights;

    public float this[int column, int row] => _heights[row * Columns + column];

    /// <summary>Número T da última ferramenta que rebaixou a célula (0 = intocada ou sem número).</summary>
    public int CutBy(int column, int row) => _cutBy[row * Columns + column];

    /// <summary>Blocos de <see cref="TileSize"/> × <see cref="TileSize"/> células em X.</summary>
    public int TileColumns { get; }

    /// <summary>Blocos em Y.</summary>
    public int TileRows { get; }

    /// <summary>Valor de <see cref="Version"/> na última mudança do bloco (tileColumn, tileRow).</summary>
    public int TileVersion(int tileColumn, int tileRow) => _tileVersion[tileRow * TileColumns + tileColumn];

    /// <summary>
    /// Quantas células da borda de um bloco influenciam a malha do bloco vizinho (o contorno liso do
    /// material olha até 3 células além da própria borda).
    /// </summary>
    public const int HaloCells = 3;

    /// <summary>
    /// Valor de <see cref="Version"/> na última mudança, feita num bloco vizinho, em células que a malha
    /// deste bloco enxerga (as de uma faixa de <see cref="HaloCells"/> na borda do vizinho).
    /// </summary>
    public int HaloVersion(int tileColumn, int tileRow) => _haloVersion[tileRow * TileColumns + tileColumn];

    // Marca o bloco da célula como alterado na versão que está para ser publicada; se a célula está
    // perto da borda do bloco, marca também os vizinhos que enxergam essa borda
    private void Touch(int column, int row)
    {
        int tc = column / TileSize, tr = row / TileSize;
        _tileVersion[tr * TileColumns + tc] = Version + 1;
        int lc = column - tc * TileSize, lr = row - tr * TileSize;
        if (lc >= HaloCells && lc < TileSize - HaloCells && lr >= HaloCells && lr < TileSize - HaloCells)
        {
            return;
        }

        int dcMin = lc < HaloCells ? -1 : 0, dcMax = lc >= TileSize - HaloCells ? 1 : 0;
        int drMin = lr < HaloCells ? -1 : 0, drMax = lr >= TileSize - HaloCells ? 1 : 0;
        for (int dr = drMin; dr <= drMax; dr++)
        {
            for (int dc = dcMin; dc <= dcMax; dc++)
            {
                int nc = tc + dc, nr = tr + dr;
                if ((dc != 0 || dr != 0) && nc >= 0 && nr >= 0 && nc < TileColumns && nr < TileRows)
                {
                    _haloVersion[nr * TileColumns + nc] = Version + 1;
                }
            }
        }
    }

    public double CellCenterX(int column) => OriginX + (column + 0.5) * CellSize;
    public double CellCenterY(int row) => OriginY + (row + 0.5) * CellSize;

    /// <summary>Altura na célula que contém o ponto (x, y). Fora da grade retorna TopZ.</summary>
    public float GetHeightAt(double x, double y)
    {
        int column = (int)Math.Floor((x - OriginX) / CellSize);
        int row = (int)Math.Floor((y - OriginY) / CellSize);
        if (column < 0 || row < 0 || column >= Columns || row >= Rows)
        {
            return (float)TopZ;
        }

        return _heights[row * Columns + column];
    }

    /// <summary>Volta todo o material para o topo do bruto.</summary>
    public void Reset()
    {
        Array.Fill(_heights, (float)TopZ);
        Array.Clear(_cutBy);
        Version++;
        Array.Fill(_tileVersion, Version);
        Array.Fill(_haloVersion, Version);
    }

    /// <summary>
    /// Remove material com uma fresa de topo reto: toda célula cujo centro está
    /// dentro do círculo (x, y, radius) é rebaixada até z (se estiver acima).
    /// Retorna true se removeu algum material.
    /// </summary>
    /// <param name="tool">Número T de quem corta (para colorir o corte por ferramenta).</param>
    public bool CutDisc(double x, double y, double radius, double z, int tool = 0)
    {
        if (z >= TopZ)
        {
            return false; // ferramenta acima do bruto: nada a remover
        }

        float cutZ = (float)Math.Max(z, BottomZ);
        double r2 = radius * radius;

        int rowMin = Math.Max(0, (int)Math.Ceiling((y - radius - OriginY) / CellSize - 0.5));
        int rowMax = Math.Min(Rows - 1, (int)Math.Floor((y + radius - OriginY) / CellSize - 0.5));
        bool changed = false;

        for (int row = rowMin; row <= rowMax; row++)
        {
            double dy = CellCenterY(row) - y;
            double h2 = r2 - dy * dy;
            if (h2 < 0)
            {
                continue;
            }

            // Meia largura da corda do círculo nesta linha da grade
            double half = Math.Sqrt(h2);
            int columnMin = Math.Max(0, (int)Math.Ceiling((x - half - OriginX) / CellSize - 0.5));
            int columnMax = Math.Min(Columns - 1, (int)Math.Floor((x + half - OriginX) / CellSize - 0.5));
            int rowStart = row * Columns;

            for (int column = columnMin; column <= columnMax; column++)
            {
                int index = rowStart + column;
                if (_heights[index] > cutZ)
                {
                    _heights[index] = cutZ;
                    _cutBy[index] = (ushort)Math.Clamp(tool, 0, ushort.MaxValue);
                    Touch(column, row);
                    changed = true;
                }
            }
        }

        if (changed)
        {
            Version++;
        }

        return changed;
    }

    /// <summary>
    /// Remove material com a ferramenta de perfil dado, com a ponta em (x, y, z): cada célula a
    /// uma distância d do eixo é rebaixada até z + altura do perfil em d (se estiver acima).
    /// Topo reto usa <see cref="CutDisc"/> (mesmo resultado, mais rápido).
    /// </summary>
    public bool CutProfile(double x, double y, ToolProfile profile, double z, int tool = 0)
    {
        if (profile.IsFlat)
        {
            return CutDisc(x, y, profile.Radius, z, tool);
        }

        if (z >= TopZ)
        {
            return false;
        }

        double radius = profile.Radius;
        double r2 = radius * radius;
        int rowMin = Math.Max(0, (int)Math.Ceiling((y - radius - OriginY) / CellSize - 0.5));
        int rowMax = Math.Min(Rows - 1, (int)Math.Floor((y + radius - OriginY) / CellSize - 0.5));
        bool changed = false;

        for (int row = rowMin; row <= rowMax; row++)
        {
            double dy = CellCenterY(row) - y;
            double h2 = r2 - dy * dy;
            if (h2 < 0)
            {
                continue;
            }

            double half = Math.Sqrt(h2);
            int columnMin = Math.Max(0, (int)Math.Ceiling((x - half - OriginX) / CellSize - 0.5));
            int columnMax = Math.Min(Columns - 1, (int)Math.Floor((x + half - OriginX) / CellSize - 0.5));
            int rowStart = row * Columns;

            for (int column = columnMin; column <= columnMax; column++)
            {
                double dx = CellCenterX(column) - x;
                double surface = z + profile.HeightAt(Math.Sqrt(dx * dx + dy * dy));
                if (surface >= TopZ)
                {
                    continue;
                }

                float cutZ = (float)Math.Max(surface, BottomZ);
                int index = rowStart + column;
                if (_heights[index] > cutZ)
                {
                    _heights[index] = cutZ;
                    _cutBy[index] = (ushort)Math.Clamp(tool, 0, ushort.MaxValue);
                    Touch(column, row);
                    changed = true;
                }
            }
        }

        if (changed)
        {
            Version++;
        }

        return changed;
    }

    /// <summary>
    /// Bruto informado pelo usuário (bloco da fresa). A resolução é escolhida para que o
    /// maior lado tenha no máximo "maxCellsPerSide" células.
    /// </summary>
    public static HeightMap CreateForStock(Stock stock, int maxCellsPerSide = 400)
    {
        double width = Math.Max(stock.MaxX - stock.MinX, 0.1);
        double height = Math.Max(stock.MaxY - stock.MinY, 0.1);
        double cellSize = Math.Max(width, height) / maxCellsPerSide;
        int columns = Math.Max(1, (int)Math.Ceiling(width / cellSize));
        int rows = Math.Max(1, (int)Math.Ceiling(height / cellSize));
        return new HeightMap(stock.MinX, stock.MinY, columns, rows, cellSize, stock.MaxZ, stock.MinZ);
    }

    /// <summary>
    /// Cria um bruto que cobre os movimentos de corte da trajetória, com folga
    /// do raio da ferramenta + margem. Topo em Z0 e fundo abaixo do corte mais profundo.
    /// A resolução é escolhida para que o maior lado tenha no máximo "maxCellsPerSide" células.
    /// </summary>
    public static HeightMap CreateForToolPath(ToolPath path, double toolDiameter, double margin = 5, int maxCellsPerSide = 400)
    {
        Bounds3 area = path.CuttingBounds.IsEmpty ? path.Bounds : path.CuttingBounds;
        if (area.IsEmpty)
        {
            area = Bounds3.Empty.Include(Point3.Origin);
        }

        double extra = toolDiameter / 2 + margin;
        double minX = area.Min.X - extra;
        double minY = area.Min.Y - extra;
        double width = area.SizeX + 2 * extra;
        double height = area.SizeY + 2 * extra;

        double cellSize = Math.Max(width, height) / maxCellsPerSide;
        int columns = Math.Max(1, (int)Math.Ceiling(width / cellSize));
        int rows = Math.Max(1, (int)Math.Ceiling(height / cellSize));

        const double topZ = 0;
        double bottomZ = Math.Min(area.Min.Z, topZ) - 5;

        return new HeightMap(minX, minY, columns, rows, cellSize, topZ, bottomZ);
    }
}
