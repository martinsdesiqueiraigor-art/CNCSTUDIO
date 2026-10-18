using CncStudio.Core.Rendering;
using CncStudio.Core.Simulation;

namespace CncStudio.Core.Scene;

/// <summary>
/// Malha do material da fresa a partir do <see cref="HeightMap"/>, dividida nos mesmos blocos do
/// mapa (<see cref="HeightMap.TileSize"/>). <see cref="Update"/> refaz só os blocos que mudaram,
/// então a simulação não precisa reenviar a grade inteira à placa de vídeo a cada passo.
///
/// O contorno é liso: furos redondos e paredes retas saem como na peça real, e não em degraus de
/// célula (ver <see cref="HeightMapContour"/>). Cada bloco tem o topo, as paredes, as laterais
/// externas da borda da grade, o fundo e as arestas vivas; juntos, os blocos formam um sólido
/// fechado. Um bloco enxerga até <see cref="HeightMap.HaloCells"/> células além da própria borda.
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
    /// também muda quando algo muda na faixa de células do vizinho que ele enxerga
    /// (<see cref="HeightMap.HaloVersion"/>).
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
                int version = Math.Max(Map.TileVersion(column, row), Map.HaloVersion(column, row));
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

    /// <summary>Malha de um bloco, do zero (a atualização parcial tem de dar exatamente isto).</summary>
    public static SolidMesh BuildTile(HeightMap map, ScenePalette palette, int tileColumn, int tileRow) =>
        new HeightMapContour(map, palette).BuildTile(tileColumn, tileRow);
}
