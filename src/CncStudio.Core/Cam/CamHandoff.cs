using CncStudio.Core.Geometry;

namespace CncStudio.Core.Cam;

/// <summary>Forma do bruto no pacote do CAM para o Code (CAM-13), na mesma convenção do bruto do setup do Code.</summary>
public enum HandoffStockKind { Prism, Round, Tube, Step }

/// <summary>
/// Bruto do pacote, já no referencial do programa (o mesmo zero que deslocou as operações). Mesma convenção do bruto do setup:
/// <list type="bullet">
/// <item>Prisma: (X, Y) é o canto de menor X e menor Y; Width × Depth; Z é o fundo e Height a altura.</item>
/// <item>Redondo e tubo em pé (eixo Z): (X, Y) é o centro, Z o fundo, Height o comprimento.</item>
/// <item>Deitado (eixo X ou Y): o campo do eixo é o começo do cilindro, Height o comprimento, os outros dois campos são o centro.</item>
/// <item>STEP: só o caminho e o resumo (caixa e volume nas coordenadas do STEP); não é convertido em bloco e não é desenhado no simulador.</item>
/// </list>
/// Sem material: o CAM não guarda material (nada suposto).
/// </summary>
public sealed record HandoffStock(HandoffStockKind Kind)
{
    public StockAxis Axis { get; init; } = StockAxis.Z;

    public double X { get; init; }

    public double Y { get; init; }

    public double Z { get; init; }

    public double Width { get; init; }

    public double Depth { get; init; }

    public double Height { get; init; }

    public double Diameter { get; init; }

    public double InnerDiameter { get; init; }

    /// <summary>STEP: caminho do arquivo como foi escolhido no CAM.</summary>
    public string? StepPath { get; init; }

    /// <summary>STEP: caixa e volume lidos na importação (coordenadas do STEP).</summary>
    public StepStockSummary? StepSummary { get; init; }

    /// <summary>STEP: o simulador não desenha este bruto (só avisa, decisão do Igor de 08/11, opção a).</summary>
    public bool NotDrawn => Kind == HandoffStockKind.Step;

    /// <summary>
    /// O bruto do trabalho no referencial do programa: o sólido de <see cref="CamJobStock.Check"/> (referencial interno) menos a posição
    /// do zero (<see cref="CamJobZero.Position"/>), a mesma conta que desloca as operações. Arquivo antigo ("como antes") e bloco
    /// absoluto: posição (0, 0, 0), o bruto de hoje. Null se o bruto não está definido ou está incompleto, ou se o zero está incompleto.
    /// </summary>
    public static HandoffStock? InProgram(CamJob job)
    {
        if (CamJobStock.Check(job).Solid is not { } s || CamJobZero.Position(job) is not { } zero)
        {
            return null;
        }

        if (s.Kind == StockSolidKind.Step)
        {
            // STEP: não converte em bloco; leva caminho e resumo como estão no trabalho
            return new HandoffStock(HandoffStockKind.Step) { StepPath = job.StockShape!.StepPath, StepSummary = job.StockShape.StepSummary };
        }

        double minX = s.MinX - zero.X, minY = s.MinY - zero.Y, minZ = s.MinZ - zero.Z;
        if (s.Kind == StockSolidKind.Box)
        {
            return new HandoffStock(HandoffStockKind.Prism) { X = minX, Y = minY, Z = minZ, Width = s.SizeX, Depth = s.SizeY, Height = s.SizeZ };
        }

        double cx = minX + s.SizeX / 2, cy = minY + s.SizeY / 2, cz = minZ + s.SizeZ / 2;
        (double x, double y, double z) = s.Axis switch
        {
            StockAxis.X => (minX, cy, cz),
            StockAxis.Y => (cx, minY, cz),
            _ => (cx, cy, minZ),
        };
        return new HandoffStock(s.Kind == StockSolidKind.Tube ? HandoffStockKind.Tube : HandoffStockKind.Round)
        {
            Axis = s.Axis, X = x, Y = y, Z = z, Height = s.Length, Diameter = s.Diameter, InnerDiameter = s.Kind == StockSolidKind.Tube ? s.InnerDiameter : 0,
        };
    }

    /// <summary>Caixa que envolve o bruto no referencial do programa (STEP: null, não é convertido).</summary>
    public Bounds3? Extent()
    {
        double r = Diameter / 2;
        (double x0, double x1, double y0, double y1, double z0, double z1)? b = Kind switch
        {
            HandoffStockKind.Step => null,
            HandoffStockKind.Prism => (X, X + Width, Y, Y + Depth, Z, Z + Height),
            _ when Axis == StockAxis.X => (X, X + Height, Y - r, Y + r, Z - r, Z + r),
            _ when Axis == StockAxis.Y => (X - r, X + r, Y, Y + Height, Z - r, Z + r),
            _ => (X - r, X + r, Y - r, Y + r, Z, Z + Height),
        };
        return b is { } c ? Bounds3.Empty.Include(new Point3(c.x0, c.y0, c.z0)).Include(new Point3(c.x1, c.y1, c.z1)) : null;
    }
}
