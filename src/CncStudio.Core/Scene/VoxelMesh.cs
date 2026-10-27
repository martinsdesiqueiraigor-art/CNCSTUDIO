using CncStudio.Core.Geometry;
using CncStudio.Core.Rendering;
using CncStudio.Core.Simulation;

namespace CncStudio.Core.Scene;

/// <summary>
/// Malha do material em voxels (<see cref="VoxelStock"/>), em blocos: só as faces expostas, juntando
/// faces vizinhas em fileiras. Face do bruto original usa a cor do bruto; parede aberta por corte, a cor de usinado.
/// </summary>
public sealed class VoxelMesh : IMaterialMesh
{
    private readonly VoxelStock _stock;
    private readonly SolidMesh?[] _tiles;
    private readonly int[] _builtVersion;
    private ScenePalette _palette;

    private static readonly SolidMesh Empty = SolidMesh.Create(1);

    public VoxelMesh(VoxelStock stock, ScenePalette? palette = null)
    {
        _stock = stock;
        _palette = palette ?? ScenePalette.Realistic;
        _tiles = new SolidMesh?[stock.TileCount];
        _builtVersion = new int[_tiles.Length];
        Array.Fill(_builtVersion, -1);
    }

    public VoxelStock Stock => _stock;

    public int TileCount => _tiles.Length;

    public SolidMesh Tile(int index) => _tiles[index] ?? Empty;

    public int TriangleCount => _tiles.Sum(t => t?.TriangleCount ?? 0);

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

    public IReadOnlyList<int> Update()
    {
        var changed = new List<int>();
        for (int i = 0; i < _tiles.Length; i++)
        {
            int version = _stock.TileVersion(i);
            if (_builtVersion[i] == version && _tiles[i] is not null)
            {
                continue;
            }

            _tiles[i] = BuildTile(_stock, _palette, i);
            _builtVersion[i] = version;
            changed.Add(i);
        }

        return changed;
    }

    /// <summary>Todos os blocos numa malha só (testes e exportar).</summary>
    public SolidMesh Merged()
    {
        Update();
        var mesh = SolidMesh.Create((TriangleCount / 2) + 16);
        foreach (SolidMesh? tile in _tiles)
        {
            if (tile is not null)
            {
                mesh.Append(tile);
            }
        }

        return mesh;
    }

    /// <summary>Malha de um bloco, do zero.</summary>
    public static SolidMesh BuildTile(VoxelStock s, ScenePalette palette, int tileIndex)
    {
        int ts = VoxelStock.TileSize;
        int tx = tileIndex % s.TilesX, ty = tileIndex / s.TilesX % s.TilesY, tz = tileIndex / (s.TilesX * s.TilesY);
        int[] lo = [tx * ts, ty * ts, tz * ts];
        int[] n = [s.Nx, s.Ny, s.Nz];
        int[] hi = [Math.Min(lo[0] + ts, n[0]), Math.Min(lo[1] + ts, n[1]), Math.Min(lo[2] + ts, n[2])];
        var mesh = SolidMesh.Create(256);
        mesh.UseExplicitEdges();
        uint stockColor = palette.Stock;
        uint cutColor = palette.ColorByTool ? palette.MachinedDeep : palette.MachinedShallow;
        double h = s.VoxelSize;
        double[] origin = [s.OriginX, s.OriginY, s.OriginZ];

        // Para cada eixo "a" (normal da face) e sinal, varre camadas; "u" é o eixo da fileira e "v" o outro
        for (int a = 0; a < 3; a++)
        {
            int u = (a + 1) % 3, v = (a + 2) % 3;
            for (int sign = -1; sign <= 1; sign += 2)
            {
                int[] c = new int[3];
                for (c[a] = lo[a]; c[a] < hi[a]; c[a]++)
                {
                    for (c[v] = lo[v]; c[v] < hi[v]; c[v]++)
                    {
                        c[u] = lo[u];
                        while (c[u] < hi[u])
                        {
                            uint? color = FaceColor(s, c, a, sign, stockColor, cutColor);
                            if (color is null)
                            {
                                c[u]++;
                                continue;
                            }

                            int start = c[u];
                            while (c[u] + 1 < hi[u] && FaceColor(s, [.. WithU(c, u, c[u] + 1)], a, sign, stockColor, cutColor) == color)
                            {
                                c[u]++;
                            }

                            int end = c[u] + 1; // fileira de start até end (exclusivo)
                            c[u] = end;
                            EmitQuad(mesh, origin, h, a, u, v, sign, c[a], start, end, c[v], color.Value);
                        }
                    }
                }
            }
        }

        return mesh;
    }

    private static int[] WithU(int[] c, int u, int value)
    {
        int[] r = (int[])c.Clone();
        r[u] = value;
        return r;
    }

    // Face do voxel c virada para o eixo a (sinal): só existe se o voxel tem material e o vizinho não
    private static uint? FaceColor(VoxelStock s, int[] c, int a, int sign, uint stockColor, uint cutColor)
    {
        if (!s.IsSolid(c[0], c[1], c[2]))
        {
            return null;
        }

        int nx = c[0], ny = c[1], nz = c[2];
        switch (a)
        {
            case 0: nx += sign; break;
            case 1: ny += sign; break;
            default: nz += sign; break;
        }

        if (s.IsSolid(nx, ny, nz))
        {
            return null;
        }

        return s.WasOriginallySolid(nx, ny, nz) ? cutColor : stockColor;
    }

    private static void EmitQuad(SolidMesh mesh, double[] origin, double h, int a, int u, int v, int sign, int layer, int uStart, int uEnd, int vIndex, uint rgb)
    {
        double plane = origin[a] + ((sign > 0 ? layer + 1 : layer) * h);
        double u0 = origin[u] + (uStart * h), u1 = origin[u] + (uEnd * h);
        double v0 = origin[v] + (vIndex * h), v1 = origin[v] + ((vIndex + 1) * h);
        Point3 P(double pu, double pv)
        {
            double[] p = new double[3];
            p[a] = plane;
            p[u] = pu;
            p[v] = pv;
            return new Point3(p[0], p[1], p[2]);
        }

        double[] nrm = new double[3];
        nrm[a] = sign;
        mesh.AddQuad(P(u0, v0), P(u1, v0), P(u1, v1), P(u0, v1), new Point3(nrm[0], nrm[1], nrm[2]), rgb);
    }
}
