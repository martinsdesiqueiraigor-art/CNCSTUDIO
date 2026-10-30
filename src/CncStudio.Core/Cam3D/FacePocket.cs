using CncStudio.Core.Cam;
using CncStudio.Core.Geometry;

namespace CncStudio.Core.Cam3D;

/// <summary>
/// Bolsão a partir de uma região plana escolhida no modelo 3D: a região é o fundo, e o contorno dela é a
/// borda do bolsão. Só serve para fundo plano voltado para cima, sem ilhas nem furos dentro (as ilhas
/// seriam cortadas); o contorno é lido da malha, com as paredes como a malha mostra.
/// </summary>
public static class FacePocket
{
    /// <summary>
    /// <paramref name="topZ"/>: topo do modelo (de onde o bolsão começa a descer). <paramref name="floorZ"/>: Z do fundo, <b>informado
    /// pelo usuário</b>; o da região lida da malha (<c>face.Center.Z</c>) é só referência. <paramref name="z"/>: alturas Z informadas
    /// (auditoria R-05); nada de Z seguro ou aproximação fixos.
    /// </summary>
    public static OperationResult Build(MeshFeatures features, MeshFace face, CamTool tool, ModelZ z, double floorZ, double topZ, double stepDown, PocketParams p)
    {
        if (face.Kind != FaceKind.Plane || face.Normal.Z < 0.99)
        {
            return new OperationResult(null, "Escolha uma região plana horizontal voltada para cima (o fundo do bolsão).");
        }

        IReadOnlyList<IReadOnlyList<Point3>> loops = features.BoundaryLoops(face);
        if (loops.Count == 0)
        {
            return new OperationResult(null, "Não achei o contorno desta região (malha aberta?).");
        }

        if (loops.Count > 1)
        {
            return new OperationResult(null, "Esta região tem ilhas ou furos dentro. Bolsão com ilhas ainda não sai a partir do 3D.");
        }

        if (!double.IsFinite(floorZ) || floorZ >= topZ - 1e-6)
        {
            return new OperationResult(null, "O fundo está no topo do modelo ou acima dele: não há o que cavar (digite o Z do fundo do rebaixo).");
        }

        if (z.Validate(topZ, topZ) is { } badZ)
        {
            return new OperationResult(null, badZ);
        }

        IReadOnlyList<Point3> ring = loops[0];
        var segments = new List<Segment2>();
        for (int i = 0; i < ring.Count; i++)
        {
            Point3 a = ring[i], b = ring[(i + 1) % ring.Count];
            segments.Add(Segment2.Line(new Point2(a.X, a.Y), new Point2(b.X, b.Y)));
        }

        var boundary = new Contour(segments);
        var levels = new CamLevels(z.SafeZ, z.ClearanceZ, topZ, floorZ, stepDown);
        return CamOperations.Pocket(boundary, tool, levels, p);
    }
}
