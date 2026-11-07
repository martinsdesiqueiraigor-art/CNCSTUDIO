using CncStudio.Core.Cam;
using CncStudio.Core.Geometry;

namespace CncStudio.Core.Cam3D;

/// <summary>
/// Furação pelos furos verticais escolhidos no modelo. O fundo (Z da ponta da broca) é <b>informado pelo usuário</b>: a profundidade
/// que a malha mostra vale só como referência, porque não diz se o furo é cego, passante ou tem fundo cônico (auditoria R-05).
/// </summary>
public static class FaceDrilling
{
    public static OperationResult Build(IReadOnlyList<MeshFace> holes, CamTool tool, ModelZ z, double modelTopZ, double bottomZ, double peck)
    {
        if (holes.Count == 0)
        {
            return new OperationResult(null, "Escolha furos verticais.");
        }

        if (holes.Any(h => h.Kind != FaceKind.Hole || h.Axis is not { } a || Math.Abs(a.Z) < 0.99))
        {
            return new OperationResult(null, "Só furos verticais saem em furação automática.");
        }

        double smallest = holes.Min(h => h.Diameter);
        if (tool.Diameter > smallest + 0.01)
        {
            return new OperationResult(null, $"A broca ({tool.Diameter:0.##} mm) é maior que o menor furo escolhido ({smallest:0.##} mm).");
        }

        double top = holes.Max(h => h.Box.Max.Z);
        if (z.Validate(modelTopZ, top) is { } badZ)
        {
            return new OperationResult(null, badZ);
        }

        if (!double.IsFinite(bottomZ) || bottomZ >= top)
        {
            return new OperationResult(null, $"O fundo do furo (Z {bottomZ:0.##}) precisa ficar abaixo do topo dos furos (Z {top:0.##}).");
        }

        return CamOperations.Drilling(tool, z.SafeZ, holes.Select(h => new Point2(h.Center.X, h.Center.Y)), DrillCycleKind.Peck, top, bottomZ, z.ClearanceZ, peck);
    }

    /// <summary>Profundidade que a parede do furo tem na malha: só referência para o usuário (Z do fundo mais baixo).</summary>
    public static double MeshBottomZ(IReadOnlyList<MeshFace> holes) => holes.Min(h => h.Box.Min.Z);
}
