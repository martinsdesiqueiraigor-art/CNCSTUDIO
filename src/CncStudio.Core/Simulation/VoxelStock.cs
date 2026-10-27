using CncStudio.Core.Geometry;

namespace CncStudio.Core.Simulation;

/// <summary>
/// Bruto em cubinhos (voxels) para a fresa com eixo rotativo: a grade de alturas só enxerga de cima
/// (3 eixos), então com a peça girada o corte é feito aqui, em qualquer direção. O bruto fica parado
/// (coordenadas da peça, com A/B desfeitos) e a ferramenta entra de lado ou inclinada.
/// Um bit por voxel; blocos de <see cref="TileSize"/>³ guardam uma versão para a malha refazer só o que mudou.
/// </summary>
public sealed class VoxelStock
{
    /// <summary>Voxels no lado de cada bloco da malha.</summary>
    public const int TileSize = 16;

    private ulong[] _bits;
    private readonly int[] _tileVersion;
    private readonly Func<double, double, double, bool>? _shape;
    private int _version;

    public VoxelStock(Bounds3 bounds, double voxelSize, Func<double, double, double, bool>? shape = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(voxelSize, 0);
        VoxelSize = voxelSize;
        OriginX = bounds.Min.X;
        OriginY = bounds.Min.Y;
        OriginZ = bounds.Min.Z;
        Nx = Math.Max(1, (int)Math.Ceiling((bounds.Max.X - bounds.Min.X) / voxelSize - 1e-9));
        Ny = Math.Max(1, (int)Math.Ceiling((bounds.Max.Y - bounds.Min.Y) / voxelSize - 1e-9));
        Nz = Math.Max(1, (int)Math.Ceiling((bounds.Max.Z - bounds.Min.Z) / voxelSize - 1e-9));
        _shape = shape;
        TilesX = (Nx + TileSize - 1) / TileSize;
        TilesY = (Ny + TileSize - 1) / TileSize;
        TilesZ = (Nz + TileSize - 1) / TileSize;
        _tileVersion = new int[TilesX * TilesY * TilesZ];
        long words = (((long)Nx * Ny * Nz) + 63) / 64;
        if (words > int.MaxValue / 2)
        {
            throw new ArgumentException("Grade grande demais.");
        }

        _bits = new ulong[words];
        Reset();
    }

    /// <summary>Lado do voxel que mantém no máximo "maxPerSide" voxels no lado maior e "maxTotal" no total.</summary>
    public static double ChooseVoxelSize(Bounds3 bounds, int maxPerSide = 256, long maxTotal = 12_000_000)
    {
        double sx = bounds.Max.X - bounds.Min.X, sy = bounds.Max.Y - bounds.Min.Y, sz = bounds.Max.Z - bounds.Min.Z;
        double size = Math.Max(Math.Max(sx, sy), sz) / maxPerSide;
        size = Math.Max(size, 0.05);
        while (((long)Math.Ceiling(sx / size) * (long)Math.Ceiling(sy / size) * (long)Math.Ceiling(sz / size)) > maxTotal)
        {
            size *= 1.1;
        }

        return size;
    }

    public double VoxelSize { get; }
    public double OriginX { get; }
    public double OriginY { get; }
    public double OriginZ { get; }
    public int Nx { get; }
    public int Ny { get; }
    public int Nz { get; }
    public int TilesX { get; }
    public int TilesY { get; }
    public int TilesZ { get; }
    public int TileCount => _tileVersion.Length;

    /// <summary>Muda a cada remoção de material.</summary>
    public int Version => _version;

    public Bounds3 Bounds => Bounds3.Empty
        .Include(new Point3(OriginX, OriginY, OriginZ))
        .Include(new Point3(OriginX + (Nx * VoxelSize), OriginY + (Ny * VoxelSize), OriginZ + (Nz * VoxelSize)));

    public int TileVersion(int tileIndex) => _tileVersion[tileIndex];

    private long Index(int x, int y, int z) => (((long)z * Ny) + y) * Nx + x;

    /// <summary>Tem material agora? Fora da grade, não.</summary>
    public bool IsSolid(int x, int y, int z)
    {
        if ((uint)x >= (uint)Nx || (uint)y >= (uint)Ny || (uint)z >= (uint)Nz)
        {
            return false;
        }

        long i = Index(x, y, z);
        return (_bits[i >> 6] & (1UL << (int)(i & 63))) != 0;
    }

    /// <summary>Tinha material no bruto inteiro (antes de qualquer corte)? Distingue parede cortada de face do bruto.</summary>
    public bool WasOriginallySolid(int x, int y, int z)
    {
        if ((uint)x >= (uint)Nx || (uint)y >= (uint)Ny || (uint)z >= (uint)Nz)
        {
            return false;
        }

        return _shape is null || _shape(OriginX + ((x + 0.5) * VoxelSize), OriginY + ((y + 0.5) * VoxelSize), OriginZ + ((z + 0.5) * VoxelSize));
    }

    /// <summary>Quantos voxels têm material agora.</summary>
    public long SolidCount
    {
        get
        {
            long n = 0;
            foreach (ulong w in _bits)
            {
                n += System.Numerics.BitOperations.PopCount(w);
            }

            return n;
        }
    }

    /// <summary>Volume do material que sobrou (mm³).</summary>
    public double Volume => SolidCount * VoxelSize * VoxelSize * VoxelSize;

    /// <summary>Volta ao bruto inteiro.</summary>
    public void Reset()
    {
        Array.Clear(_bits);
        for (int z = 0; z < Nz; z++)
        {
            for (int y = 0; y < Ny; y++)
            {
                for (int x = 0; x < Nx; x++)
                {
                    if (WasOriginallySolid(x, y, z))
                    {
                        long i = Index(x, y, z);
                        _bits[i >> 6] |= 1UL << (int)(i & 63);
                    }
                }
            }
        }

        MarkAllDirty();
    }

    private void MarkAllDirty()
    {
        _version++;
        for (int i = 0; i < _tileVersion.Length; i++)
        {
            _tileVersion[i] = _version;
        }
    }

    /// <summary>Cópia do material para voltar depois.</summary>
    public ulong[] TakeSnapshot() => (ulong[])_bits.Clone();

    public void Restore(ulong[] snapshot)
    {
        Array.Copy(snapshot, _bits, _bits.Length);
        MarkAllDirty();
    }

    /// <summary>
    /// Remove o material tocado pela ferramenta: eixo de "tip" na direção "dir" (unitária, apontando para
    /// o cabo), até "length" de comprimento, com o perfil da ponta. Retorna true se tirou algo.
    /// </summary>
    public bool Carve(Point3 tip, Point3 dir, ToolProfile profile, double length)
    {
        double r = profile.Radius;
        // Caixa do cilindro: a ponta e a outra extremidade, abertas pelo raio em cada eixo
        Point3 end = new(tip.X + (dir.X * length), tip.Y + (dir.Y * length), tip.Z + (dir.Z * length));
        double ex = r * Math.Sqrt(Math.Max(0, 1 - (dir.X * dir.X)));
        double ey = r * Math.Sqrt(Math.Max(0, 1 - (dir.Y * dir.Y)));
        double ez = r * Math.Sqrt(Math.Max(0, 1 - (dir.Z * dir.Z)));
        int x0 = Math.Max(0, (int)Math.Floor((Math.Min(tip.X, end.X) - ex - OriginX) / VoxelSize));
        int x1 = Math.Min(Nx - 1, (int)Math.Floor((Math.Max(tip.X, end.X) + ex - OriginX) / VoxelSize));
        int y0 = Math.Max(0, (int)Math.Floor((Math.Min(tip.Y, end.Y) - ey - OriginY) / VoxelSize));
        int y1 = Math.Min(Ny - 1, (int)Math.Floor((Math.Max(tip.Y, end.Y) + ey - OriginY) / VoxelSize));
        int z0 = Math.Max(0, (int)Math.Floor((Math.Min(tip.Z, end.Z) - ez - OriginZ) / VoxelSize));
        int z1 = Math.Min(Nz - 1, (int)Math.Floor((Math.Max(tip.Z, end.Z) + ez - OriginZ) / VoxelSize));
        if (x0 > x1 || y0 > y1 || z0 > z1)
        {
            return false;
        }

        double r2 = r * r;
        bool flat = profile.IsFlat;
        int minX = int.MaxValue, minY = int.MaxValue, minZ = int.MaxValue, maxX = -1, maxY = -1, maxZ = -1;
        for (int z = z0; z <= z1; z++)
        {
            double pz = OriginZ + ((z + 0.5) * VoxelSize) - tip.Z;
            for (int y = y0; y <= y1; y++)
            {
                double py = OriginY + ((y + 0.5) * VoxelSize) - tip.Y;
                long row = Index(0, y, z);
                for (int x = x0; x <= x1; x++)
                {
                    long i = row + x;
                    ulong mask = 1UL << (int)(i & 63);
                    if ((_bits[i >> 6] & mask) == 0)
                    {
                        continue;
                    }

                    double px = OriginX + ((x + 0.5) * VoxelSize) - tip.X;
                    double t = (px * dir.X) + (py * dir.Y) + (pz * dir.Z);
                    if (t < 0 || t > length)
                    {
                        continue;
                    }

                    double d2 = (px * px) + (py * py) + (pz * pz) - (t * t);
                    if (d2 > r2 || (!flat && t < profile.HeightAt(Math.Sqrt(d2))))
                    {
                        continue;
                    }

                    _bits[i >> 6] &= ~mask;
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                    minZ = Math.Min(minZ, z);
                    maxZ = Math.Max(maxZ, z);
                }
            }
        }

        if (maxX < 0)
        {
            return false;
        }

        _version++;
        // Blocos tocados e os vizinhos (a face na borda do bloco depende do voxel do lado)
        int tx0 = Math.Max(0, (minX - 1) / TileSize), tx1 = Math.Min(TilesX - 1, (maxX + 1) / TileSize);
        int ty0 = Math.Max(0, (minY - 1) / TileSize), ty1 = Math.Min(TilesY - 1, (maxY + 1) / TileSize);
        int tz0 = Math.Max(0, (minZ - 1) / TileSize), tz1 = Math.Min(TilesZ - 1, (maxZ + 1) / TileSize);
        for (int tz = tz0; tz <= tz1; tz++)
        {
            for (int ty = ty0; ty <= ty1; ty++)
            {
                for (int tx = tx0; tx <= tx1; tx++)
                {
                    _tileVersion[(((tz * TilesY) + ty) * TilesX) + tx] = _version;
                }
            }
        }

        return true;
    }
}
