using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;
using CncStudio.Core.Simulation;

namespace CncStudio.Core.Cam;

/// <summary>Como uma célula da grade se compara à peça.</summary>
public enum CompareCell : byte
{
    /// <summary>Sem peça e sem material (fora dos dois): não entra na conta.</summary>
    None = 0,

    /// <summary>Altura do bruto igual à da peça, dentro da tolerância.</summary>
    Within = 1,

    /// <summary>Sobra material acima da peça.</summary>
    Excess = 2,

    /// <summary>O bruto foi cortado abaixo da peça (gouge).</summary>
    Gouge = 3,
}

/// <summary>Um trecho conexo de células de excesso ou de gouge.</summary>
public sealed record CompareRegion(CompareCell Kind, int Cells, double Volume, double MaxDepth, double MinX, double MinY, double MaxX, double MaxY);

/// <summary>Resultado da comparação peça × bruto na grade de alturas (CAM-STOCK-03a). Verificação, nunca garantia.</summary>
public sealed class CamStockCompareResult
{
    internal CamStockCompareResult(HeightMap grid, CompareCell[] cells, double tolerance, double gouge, double excess, int within, int counted,
        IReadOnlyList<CompareRegion> regions)
    {
        Columns = grid.Columns;
        Rows = grid.Rows;
        CellSize = grid.CellSize;
        OriginX = grid.OriginX;
        OriginY = grid.OriginY;
        Cells = cells;
        Tolerance = tolerance;
        GougeVolume = gouge;
        ExcessVolume = excess;
        WithinCells = within;
        CountedCells = counted;
        Regions = regions;
    }

    public int Columns { get; }

    public int Rows { get; }

    public double CellSize { get; }

    public double OriginX { get; }

    public double OriginY { get; }

    /// <summary>Uma entrada por célula (linha por linha), para a tela pintar.</summary>
    public IReadOnlyList<CompareCell> Cells { get; }

    /// <summary>Diferença de altura aceita (mm); hoje é o tamanho da célula.</summary>
    public double Tolerance { get; }

    /// <summary>Volume cortado além da peça (mm³).</summary>
    public double GougeVolume { get; }

    /// <summary>Volume que ainda sobra acima da peça (mm³).</summary>
    public double ExcessVolume { get; }

    public int WithinCells { get; }

    /// <summary>Células que entram na conta (com peça ou com material).</summary>
    public int CountedCells { get; }

    /// <summary>Porcentagem de células dentro da tolerância (100 se nada entra na conta).</summary>
    public double WithinPercent => CountedCells == 0 ? 100 : 100.0 * WithinCells / CountedCells;

    public bool HasGouge => GougeVolume > 0;

    public bool HasExcess => ExcessVolume > 0;

    /// <summary>Gouge primeiro e depois excesso, do maior volume para o menor.</summary>
    public IReadOnlyList<CompareRegion> Regions { get; }

    public CompareCell CellAt(int column, int row) => Cells[(row * Columns) + column];
}

/// <summary>Resumo por operação para o selo da árvore.</summary>
public enum CamStockVerdict
{
    /// <summary>Operação sem estado de bruto: não é verificada.</summary>
    NoState,

    /// <summary>Operação desligada.</summary>
    Disabled,

    /// <summary>Dentro da tolerância.</summary>
    Ok,

    /// <summary>Ainda sobra material; sem gouge.</summary>
    Excess,

    /// <summary>Cortou além da peça.</summary>
    Gouge,
}

public sealed record CamStockCompareSummary(int Index, CamStockVerdict Verdict, double GougeVolume, double ExcessVolume);

/// <summary>
/// Compara o bruto simulado (<see cref="CamStockTimeline"/>, grade de alturas) com a peça STEP: gouge onde o bruto ficou abaixo
/// da peça, excesso onde ainda sobra material acima dela (CAM-STOCK-03a). Só visual: não muda G-code nem arquivo, e nada é gravado.
/// <para>
/// Limites: a peça vale pelo <b>topo por coluna</b> (rebaixo e reentrância somem; gouge embaixo de saliência não aparece);
/// a tolerância é o tamanho da célula e vale também de lado (uma célula na vizinhança), para a parede da peça não dar falso
/// alarme. Voxel (eixo rotativo, cilindro deitado) fica de fora. O resultado é indicativo.
/// </para>
/// Usa o material do próprio <see cref="CamStockTimeline.Map"/>: chamar <see cref="After"/> o deixa no estado daquela operação,
/// como <see cref="CamStockTimeline.ShowAfter"/>. Não é thread-safe.
/// </summary>
public sealed class CamStockCompare
{
    /// <summary>Texto fixo do limite: vai junto do resultado na tela.</summary>
    public const string Disclaimer = "Verificação indicativa, nunca garantia: a peça vale pelo topo de cada coluna e a medida é a da célula.";

    public const string VoxelUnavailable = "Verificação indisponível com eixo rotativo ou cilindro deitado (voxel).";

    public const string NoPart = "Sem peça 3D no trabalho: não há o que comparar.";

    private readonly CamStockTimeline _timeline;
    private readonly float[] _part;

    private CamStockCompare(CamStockTimeline timeline, float[] part)
    {
        _timeline = timeline;
        _part = part;
    }

    /// <summary>
    /// Prepara a comparação. <paramref name="part"/> é a malha da peça já colocada (<see cref="CamJobPart.Place"/>), nas coordenadas
    /// do desenho; o zero do trabalho é descontado aqui, como o bruto faz. Null com o motivo em <paramref name="problem"/> quando
    /// não dá para comparar (sem bruto, voxel, sem peça, sem zero).
    /// </summary>
    public static CamStockCompare? Create(CamStockTimeline timeline, CamJob job, TriangleMesh? part, out string? problem)
    {
        ArgumentNullException.ThrowIfNull(timeline);
        ArgumentNullException.ThrowIfNull(job);
        problem = null;
        if (timeline.Problem is { } why)
        {
            problem = why;
            return null;
        }

        if (timeline.UsesVoxels)
        {
            problem = VoxelUnavailable;
            return null;
        }

        if (timeline.Map is not { } map)
        {
            problem = "Sem bruto calculado.";
            return null;
        }

        if (part is null || part.TriangleCount == 0)
        {
            problem = NoPart;
            return null;
        }

        if (CamJobZero.Position(job) is not { } zero)
        {
            problem = "Zero da peça incompleto: sem ele não dá para comparar a peça.";
            return null;
        }

        TriangleMesh inProgram = zero.X == 0 && zero.Y == 0 && zero.Z == 0 ? part : part.Translated(-zero.X, -zero.Y, -zero.Z);
        return new CamStockCompare(timeline, PartHeights(map, inProgram));
    }

    /// <summary>Compara o bruto depois da operação <paramref name="index"/> (−1 = bruto inteiro). Null se a operação não tem estado.</summary>
    public CamStockCompareResult? After(int index)
    {
        if (_timeline.Map is not { } map || !_timeline.ShowAfter(index))
        {
            return null;
        }

        return Compare(map, _part);
    }

    /// <summary>Compara o bruto depois da última operação (fim do programa).</summary>
    public CamStockCompareResult? Final() => After(_timeline.States.Count - 1);

    /// <summary>Selo de cada operação (depois dela), na ordem do trabalho. Deixa o material no estado da última operação.</summary>
    public IReadOnlyList<CamStockCompareSummary> Summary()
    {
        var list = new List<CamStockCompareSummary>(_timeline.States.Count);
        foreach (CamStockState s in _timeline.States)
        {
            if (s.Status == CamStockStatus.NoState)
            {
                list.Add(new CamStockCompareSummary(s.Index, CamStockVerdict.NoState, 0, 0));
                continue;
            }

            if (s.Status == CamStockStatus.Disabled)
            {
                list.Add(new CamStockCompareSummary(s.Index, CamStockVerdict.Disabled, 0, 0));
                continue;
            }

            CamStockCompareResult? r = After(s.Index);
            if (r is null)
            {
                list.Add(new CamStockCompareSummary(s.Index, CamStockVerdict.NoState, 0, 0));
                continue;
            }

            CamStockVerdict v = r.HasGouge ? CamStockVerdict.Gouge : r.HasExcess ? CamStockVerdict.Excess : CamStockVerdict.Ok;
            list.Add(new CamStockCompareSummary(s.Index, v, r.GougeVolume, r.ExcessVolume));
        }

        return list;
    }

    /// <summary>
    /// Altura do topo da peça no centro de cada célula da grade (NaN onde a peça não tem material). Cada triângulo é projetado
    /// em X e Y e vale o maior Z entre os que cobrem a célula. Triângulo vertical não conta.
    /// </summary>
    public static float[] PartHeights(HeightMap grid, TriangleMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(mesh);
        var top = new float[grid.Columns * grid.Rows];
        Array.Fill(top, float.NaN);
        for (int i = 0; i < mesh.TriangleCount; i++)
        {
            (Point3 a, Point3 b, Point3 c) = mesh.Triangle(i);
            double det = ((b.Y - c.Y) * (a.X - c.X)) + ((c.X - b.X) * (a.Y - c.Y));
            if (Math.Abs(det) < 1e-12)
            {
                continue;
            }

            int c0 = Math.Max(0, (int)Math.Floor((Math.Min(a.X, Math.Min(b.X, c.X)) - grid.OriginX) / grid.CellSize) - 1);
            int c1 = Math.Min(grid.Columns - 1, (int)Math.Ceiling((Math.Max(a.X, Math.Max(b.X, c.X)) - grid.OriginX) / grid.CellSize) + 1);
            int r0 = Math.Max(0, (int)Math.Floor((Math.Min(a.Y, Math.Min(b.Y, c.Y)) - grid.OriginY) / grid.CellSize) - 1);
            int r1 = Math.Min(grid.Rows - 1, (int)Math.Ceiling((Math.Max(a.Y, Math.Max(b.Y, c.Y)) - grid.OriginY) / grid.CellSize) + 1);
            const double eps = -1e-9;
            for (int row = r0; row <= r1; row++)
            {
                double y = grid.CellCenterY(row);
                for (int col = c0; col <= c1; col++)
                {
                    double x = grid.CellCenterX(col);
                    double l1 = (((b.Y - c.Y) * (x - c.X)) + ((c.X - b.X) * (y - c.Y))) / det;
                    double l2 = (((c.Y - a.Y) * (x - c.X)) + ((a.X - c.X) * (y - c.Y))) / det;
                    double l3 = 1 - l1 - l2;
                    if (l1 < eps || l2 < eps || l3 < eps)
                    {
                        continue;
                    }

                    float z = (float)((l1 * a.Z) + (l2 * b.Z) + (l3 * c.Z));
                    int k = (row * grid.Columns) + col;
                    if (float.IsNaN(top[k]) || z > top[k])
                    {
                        top[k] = z;
                    }
                }
            }
        }

        return top;
    }

    /// <summary>
    /// Compara o material da grade com as alturas da peça. Gouge: o bruto está abaixo da peça (menos a tolerância) em toda a
    /// vizinhança 3×3 (só contam as células com peça); excesso: está acima dela (mais a tolerância) em toda a vizinhança. A tolerância é o tamanho da célula,
    /// salvo se informada.
    /// </summary>
    public static CamStockCompareResult Compare(HeightMap material, float[] partHeights, double? tolerance = null)
    {
        ArgumentNullException.ThrowIfNull(material);
        ArgumentNullException.ThrowIfNull(partHeights);
        int cols = material.Columns, rows = material.Rows;
        if (partHeights.Length != cols * rows)
        {
            throw new ArgumentException("As alturas da peça são de outra grade.", nameof(partHeights));
        }

        double tol = tolerance ?? material.CellSize;
        double bottom = material.BottomZ;
        ReadOnlySpan<float> heights = material.Heights;

        // altura efetiva dos dois lados: sem material = fundo do bruto; sem peça = fundo; peça abaixo do bruto vale o fundo
        var m = new double[cols * rows];
        var p = new double[cols * rows];
        var counted = new bool[cols * rows];
        var present = new bool[cols * rows];
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                int k = (row * cols) + col;
                bool hasMaterial = !material.IsEmptyCell(col, row);
                m[k] = hasMaterial ? Math.Max(bottom, heights[k]) : bottom;
                p[k] = float.IsNaN(partHeights[k]) ? bottom : Math.Max(bottom, partHeights[k]);
                present[k] = !float.IsNaN(partHeights[k]);
                counted[k] = (hasMaterial && m[k] > bottom + 1e-9) || !float.IsNaN(partHeights[k]);
            }
        }

        var cells = new CompareCell[cols * rows];
        double cellArea = material.CellSize * material.CellSize;
        double gouge = 0, excess = 0;
        int within = 0, total = 0;
        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                int k = (row * cols) + col;
                if (!counted[k])
                {
                    continue;
                }

                total++;
                // vizinhança 3×3: o piso mais baixo e o topo mais alto da peça; célula sem peça não baixa o piso (borda da silhueta)
                double pMin = double.MaxValue, pMax = double.MinValue;
                for (int dr = -1; dr <= 1; dr++)
                {
                    for (int dc = -1; dc <= 1; dc++)
                    {
                        int rr = row + dr, cc = col + dc;
                        if (rr < 0 || cc < 0 || rr >= rows || cc >= cols)
                        {
                            continue;
                        }

                        int nk = (rr * cols) + cc;
                        pMax = Math.Max(pMax, p[nk]);
                        if (present[nk])
                        {
                            pMin = Math.Min(pMin, p[nk]);
                        }
                    }
                }

                if (pMin == double.MaxValue)
                {
                    pMin = bottom; // sem peça por perto: nada abaixo do fundo do bruto
                }

                if (present[k] && m[k] < pMin - tol)
                {
                    cells[k] = CompareCell.Gouge;
                    gouge += (p[k] - m[k]) * cellArea;
                }
                else if (m[k] > pMax + tol)
                {
                    cells[k] = CompareCell.Excess;
                    excess += (m[k] - p[k]) * cellArea;
                }
                else
                {
                    cells[k] = CompareCell.Within;
                    within++;
                }
            }
        }

        IReadOnlyList<CompareRegion> regions = Regions(material, cells, m, p, cellArea);
        return new CamStockCompareResult(material, cells, tol, gouge, excess, within, total, regions);
    }

    private static List<CompareRegion> Regions(HeightMap grid, CompareCell[] cells, double[] m, double[] p, double cellArea)
    {
        int cols = grid.Columns, rows = grid.Rows;
        var seen = new bool[cells.Length];
        var list = new List<CompareRegion>();
        var stack = new Stack<int>();
        for (int start = 0; start < cells.Length; start++)
        {
            CompareCell kind = cells[start];
            if (seen[start] || kind is not (CompareCell.Gouge or CompareCell.Excess))
            {
                continue;
            }

            int n = 0;
            double volume = 0, depth = 0;
            int minC = cols, minR = rows, maxC = 0, maxR = 0;
            stack.Push(start);
            seen[start] = true;
            while (stack.Count > 0)
            {
                int k = stack.Pop();
                int row = k / cols, col = k % cols;
                double d = Math.Abs(m[k] - p[k]);
                n++;
                volume += d * cellArea;
                depth = Math.Max(depth, d);
                minC = Math.Min(minC, col);
                maxC = Math.Max(maxC, col);
                minR = Math.Min(minR, row);
                maxR = Math.Max(maxR, row);
                foreach ((int dr, int dc) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                {
                    int rr = row + dr, cc = col + dc;
                    if (rr < 0 || cc < 0 || rr >= rows || cc >= cols)
                    {
                        continue;
                    }

                    int nk = (rr * cols) + cc;
                    if (!seen[nk] && cells[nk] == kind)
                    {
                        seen[nk] = true;
                        stack.Push(nk);
                    }
                }
            }

            list.Add(new CompareRegion(kind, n, volume, depth,
                grid.OriginX + (minC * grid.CellSize), grid.OriginY + (minR * grid.CellSize),
                grid.OriginX + ((maxC + 1) * grid.CellSize), grid.OriginY + ((maxR + 1) * grid.CellSize)));
        }

        return [.. list.OrderBy(r => r.Kind == CompareCell.Gouge ? 0 : 1).ThenByDescending(r => r.Volume)];
    }
}
