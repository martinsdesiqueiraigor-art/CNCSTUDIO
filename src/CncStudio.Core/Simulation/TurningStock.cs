using CncStudio.Core.Geometry;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Simulation;

/// <summary>
/// Barra do torno discretizada em fatias ao longo de Z. Cada fatia guarda o raio externo e o
/// raio do furo (0 = maciça): é o perfil de revolução da peça. Fatia com externo ≤ furo = vazia.
/// </summary>
public sealed class TurningStock
{
    private readonly float[] _outer;
    private readonly float[] _inner;
    private readonly float _initialOuter;
    private readonly float _initialInner;

    public TurningStock(double minZ, double maxZ, double radius, double boreRadius = 0, int maxSlices = 1500)
    {
        MinZ = Math.Min(minZ, maxZ);
        double length = Math.Max(Math.Abs(maxZ - minZ), 0.1);
        SliceLength = length / Math.Clamp((int)Math.Ceiling(length / 0.05), 1, maxSlices);
        Slices = (int)Math.Round(length / SliceLength);
        _initialOuter = (float)Math.Abs(radius);
        _initialInner = (float)Math.Max(0, boreRadius);
        _outer = new float[Slices];
        _inner = new float[Slices];
        Reset();
    }

    public double MinZ { get; }
    public double MaxZ => MinZ + Slices * SliceLength;
    public double SliceLength { get; }
    public int Slices { get; }
    public double Radius => _initialOuter;

    /// <summary>Muda a cada remoção de material.</summary>
    public int Version { get; private set; }

    public ReadOnlySpan<float> Outer => _outer;
    public ReadOnlySpan<float> Inner => _inner;

    /// <summary>Raio externo original (para saber o que foi usinado).</summary>
    public float InitialOuter => _initialOuter;

    public float InitialInner => _initialInner;

    public double SliceCenter(int slice) => MinZ + (slice + 0.5) * SliceLength;

    public void Reset()
    {
        Array.Fill(_outer, _initialOuter);
        Array.Fill(_inner, _initialInner);
        Version++;
    }

    /// <summary>
    /// Bruto automático para a trajetória: raio = maior raio de corte (o último passe do
    /// desbaste costuma sair no diâmetro do bruto); face em Z0 (até 0,5 mm acima, se o programa
    /// faceia de cima com pouco sobremetal); comprimento até um pouco além do corte mais fundo em Z.
    /// </summary>
    public static TurningStock CreateForToolPath(ToolPath path, double margin = 2)
    {
        Bounds3 area = path.CuttingBounds.IsEmpty ? path.Bounds : path.CuttingBounds;
        if (area.IsEmpty)
        {
            area = Bounds3.Empty.Include(Point3.Origin).Include(new Point3(10, 0, -10));
        }

        double radius = Math.Max(Math.Abs(area.Max.X), Math.Abs(area.Min.X));
        double face = area.Max.Z >= 0 ? 0 : area.Max.Z;
        return new TurningStock(Math.Min(area.Min.Z, face - 1) - margin, face, Math.Max(radius, 0.5));
    }

    /// <summary>Bruto informado pelo usuário (barra).</summary>
    public static TurningStock CreateForStock(Stock stock) => new(stock.MinZ, stock.MaxZ, stock.MaxX);

    /// <summary>
    /// Remove material com a ponta da ferramenta em (z, r), largura "halfWidth" em Z (raio da ponta).
    /// Ferramenta mais perto do externo = torneamento externo (baixa o raio externo); mais perto do
    /// furo = interno (aumenta o furo). No eixo (r ≈ 0) é furação: abre o furo com "drillRadius".
    /// Retorna true se removeu algo.
    /// </summary>
    public bool Cut(double z, double r, double halfWidth, double drillRadius)
    {
        r = Math.Abs(r);
        bool drilling = r < 1e-6;
        double width = drilling ? 0 : halfWidth;
        int first = Math.Max(0, (int)Math.Floor((z - width - MinZ) / SliceLength));
        int last = Math.Min(Slices - 1, (int)Math.Floor((z + width - MinZ) / SliceLength));
        bool changed = false;

        for (int i = first; i <= last; i++)
        {
            float outer = _outer[i], inner = _inner[i];
            if (outer <= inner)
            {
                continue; // fatia já vazia
            }

            if (drilling)
            {
                float bore = (float)Math.Min(drillRadius, outer);
                if (bore > inner + 1e-4f)
                {
                    _inner[i] = bore;
                    changed = true;
                }

                continue;
            }

            const float tolerance = 1e-4f; // ignora diferenças de arredondamento
            if (r >= outer - tolerance || r <= inner + tolerance)
            {
                continue; // fora do material
            }

            if (outer - r <= r - inner)
            {
                _outer[i] = (float)r;   // externo
            }
            else
            {
                _inner[i] = (float)r;   // interno (mandrilar)
            }

            changed = true;
        }

        if (changed)
        {
            Version++;
        }

        return changed;
    }
}
