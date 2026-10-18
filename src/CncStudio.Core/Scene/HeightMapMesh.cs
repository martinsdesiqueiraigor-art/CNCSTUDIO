using CncStudio.Core.Geometry;
using CncStudio.Core.Rendering;
using CncStudio.Core.Simulation;

namespace CncStudio.Core.Scene;

/// <summary>
/// Malha do material da fresa a partir do <see cref="HeightMap"/>, dividida nos mesmos blocos do
/// mapa (<see cref="HeightMap.TileSize"/>). <see cref="Update"/> refaz só os blocos que mudaram,
/// então a simulação não precisa reenviar a grade inteira à placa de vídeo a cada passo.
///
/// Cada bloco tem: o topo de cada célula, as paredes com o vizinho de +X e de +Y (quando as
/// alturas diferem), as laterais externas das células da borda e o fundo. Juntos, os blocos
/// formam um sólido fechado. O topo é por célula (sem juntar trechos) para não abrir frestas
/// entre triângulos vizinhos na placa de vídeo.
/// </summary>
public sealed class HeightMapMesh
{
    private readonly SolidMesh?[] _tiles;
    private readonly int[] _builtVersion;
    private ScenePalette _palette;

    public HeightMapMesh(HeightMap map, ScenePalette? palette = null)
    {
        Map = map;
        _palette = palette ?? ScenePalette.Realistic;
        _tiles = new SolidMesh?[map.TileColumns * map.TileRows];
        _builtVersion = new int[_tiles.Length];
        Array.Fill(_builtVersion, -1);
    }

    public HeightMap Map { get; }

    /// <summary>Cores em uso. Trocar refaz todos os blocos no próximo <see cref="Update"/>.</summary>
    public ScenePalette Palette
    {
        get => _palette;
        set
        {
            if (!ReferenceEquals(value, _palette))
            {
                _palette = value;
                Array.Fill(_builtVersion, -1);
            }
        }
    }

    public int TileColumns => Map.TileColumns;
    public int TileRows => Map.TileRows;

    /// <summary>Malha do bloco de índice tileRow × TileColumns + tileColumn (vazia antes do primeiro Update).</summary>
    public SolidMesh Tile(int index) => _tiles[index] ?? Empty;

    public int TileCount => _tiles.Length;

    public int TriangleCount => _tiles.Sum(t => t?.TriangleCount ?? 0);

    private static readonly SolidMesh Empty = SolidMesh.Create(1);

    /// <summary>
    /// Refaz os blocos que mudaram desde a última chamada e devolve os índices deles. Um bloco
    /// depende também do primeiro vizinho em +X e em +Y (as paredes entre eles são dele).
    /// </summary>
    public IReadOnlyList<int> Update()
    {
        var changed = new List<int>();
        int tc = TileColumns, tr = TileRows;
        for (int row = 0; row < tr; row++)
        {
            for (int column = 0; column < tc; column++)
            {
                int index = row * tc + column;
                int version = Map.TileVersion(column, row);
                if (column + 1 < tc)
                {
                    version = Math.Max(version, Map.TileVersion(column + 1, row));
                }

                if (row + 1 < tr)
                {
                    version = Math.Max(version, Map.TileVersion(column, row + 1));
                }

                if (_builtVersion[index] == version && _tiles[index] is not null)
                {
                    continue;
                }

                _tiles[index] = BuildTile(Map, _palette, column, row);
                _builtVersion[index] = version;
                changed.Add(index);
            }
        }

        return changed;
    }

    /// <summary>Todos os blocos numa malha só (para testes e para exportar).</summary>
    public SolidMesh Merged()
    {
        Update();
        var mesh = SolidMesh.Create(TriangleCount / 2 + 16);
        foreach (SolidMesh? tile in _tiles)
        {
            if (tile is not null)
            {
                mesh.Append(tile);
            }
        }

        return mesh;
    }

    /// <summary>
    /// Normal suavizada de uma parede entre a célula (column, row) e a vizinha em +X (alongX) ou +Y:
    /// direção oposta ao gradiente da altura nas células em volta. Assim um furo redondo, que na grade
    /// vira degraus, aparece liso. Se a conta discordar do lado da parede, fica a normal do eixo.
    /// </summary>
    private static Point3 WallShade(HeightMap map, int column, int row, bool alongX, Point3 axis)
    {
        float H(int c, int r) => map[Math.Clamp(c, 0, map.Columns - 1), Math.Clamp(r, 0, map.Rows - 1)];
        double across = 0, side = 0;
        if (alongX)
        {
            for (int dr = -1; dr <= 1; dr++)
            {
                across += (dr == 0 ? 2 : 1) * (H(column + 1, row + dr) - H(column, row + dr));
            }

            side = (H(column, row + 1) - H(column, row - 1) + H(column + 1, row + 1) - H(column + 1, row - 1)) / 2;
            var n = new Point3(-across, -side, 0);
            return n.X * axis.X > 1e-9 ? n : axis;
        }

        for (int dc = -1; dc <= 1; dc++)
        {
            across += (dc == 0 ? 2 : 1) * (H(column + dc, row + 1) - H(column + dc, row));
        }

        side = (H(column + 1, row) - H(column - 1, row) + H(column + 1, row + 1) - H(column - 1, row + 1)) / 2;
        var m = new Point3(-side, -across, 0);
        return m.Y * axis.Y > 1e-9 ? m : axis;
    }

    /// <summary>Malha de um bloco, do zero (a atualização parcial tem de dar exatamente isto).</summary>
    public static SolidMesh BuildTile(HeightMap map, ScenePalette palette, int tileColumn, int tileRow)
    {
        int c0 = tileColumn * HeightMap.TileSize, r0 = tileRow * HeightMap.TileSize;
        int c1 = Math.Min(map.Columns, c0 + HeightMap.TileSize), r1 = Math.Min(map.Rows, r0 + HeightMap.TileSize);
        int columns = map.Columns, rows = map.Rows;
        double size = map.CellSize, ox = map.OriginX, oy = map.OriginY;
        double top = map.TopZ, bottom = map.BottomZ;
        ReadOnlySpan<float> h = map.Heights;

        var mesh = SolidMesh.Create((c1 - c0) * (r1 - r0) * 2);

        // Coordenadas das linhas da grade sempre pela mesma conta: blocos vizinhos coincidem no float
        double X(int column) => ox + column * size;
        double Y(int row) => oy + row * size;

        uint ColorOf(int column, int row)
        {
            float z = map[column, row];
            return z >= top - 1e-4
                ? palette.Stock
                : palette.Machined((top - z) / Math.Max(top - bottom, 1e-6), map.CutBy(column, row));
        }

        var up = new Point3(0, 0, 1);
        for (int row = r0; row < r1; row++)
        {
            double y0 = Y(row), y1 = Y(row + 1);
            for (int column = c0; column < c1; column++)
            {
                double x0 = X(column), x1 = X(column + 1);
                float a = h[row * columns + column];
                mesh.AddQuad(new Point3(x0, y0, a), new Point3(x1, y0, a), new Point3(x1, y1, a), new Point3(x0, y1, a), up, ColorOf(column, row));

                // Parede com o vizinho de +X (a cor é a da célula mais funda, que é a parede usinada)
                if (column + 1 < columns)
                {
                    float b = h[row * columns + column + 1];
                    if (a != b)
                    {
                        float low = Math.Min(a, b), high = Math.Max(a, b);
                        uint color = a < b ? ColorOf(column, row) : ColorOf(column + 1, row);
                        var axis = new Point3(a > b ? 1 : -1, 0, 0);
                        mesh.AddQuad(new Point3(x1, y0, low), new Point3(x1, y1, low), new Point3(x1, y1, high), new Point3(x1, y0, high),
                            axis, color, WallShade(map, column, row, alongX: true, axis));
                    }
                }

                // Parede com o vizinho de +Y
                if (row + 1 < rows)
                {
                    float b = h[(row + 1) * columns + column];
                    if (a != b)
                    {
                        float low = Math.Min(a, b), high = Math.Max(a, b);
                        uint color = a < b ? ColorOf(column, row) : ColorOf(column, row + 1);
                        var axis = new Point3(0, a > b ? 1 : -1, 0);
                        mesh.AddQuad(new Point3(x0, y1, low), new Point3(x1, y1, low), new Point3(x1, y1, high), new Point3(x0, y1, high),
                            axis, color, WallShade(map, column, row, alongX: false, axis));
                    }
                }

                // Laterais externas (cor do bruto), do fundo até a altura da célula
                if (a <= bottom)
                {
                    continue;
                }

                if (column == 0)
                {
                    mesh.AddQuad(new Point3(x0, y0, bottom), new Point3(x0, y1, bottom), new Point3(x0, y1, a), new Point3(x0, y0, a),
                        new Point3(-1, 0, 0), palette.Stock);
                }

                if (column == columns - 1)
                {
                    mesh.AddQuad(new Point3(x1, y0, bottom), new Point3(x1, y1, bottom), new Point3(x1, y1, a), new Point3(x1, y0, a),
                        new Point3(1, 0, 0), palette.Stock);
                }

                if (row == 0)
                {
                    mesh.AddQuad(new Point3(x0, y0, bottom), new Point3(x1, y0, bottom), new Point3(x1, y0, a), new Point3(x0, y0, a),
                        new Point3(0, -1, 0), palette.Stock);
                }

                if (row == rows - 1)
                {
                    mesh.AddQuad(new Point3(x0, y1, bottom), new Point3(x1, y1, bottom), new Point3(x1, y1, a), new Point3(x0, y1, a),
                        new Point3(0, 1, 0), palette.Stock);
                }
            }
        }

        // Fundo do bloco
        mesh.AddQuad(new Point3(X(c0), Y(r0), bottom), new Point3(X(c1), Y(r0), bottom), new Point3(X(c1), Y(r1), bottom), new Point3(X(c0), Y(r1), bottom),
            new Point3(0, 0, -1), palette.Stock);
        return mesh;
    }
}
