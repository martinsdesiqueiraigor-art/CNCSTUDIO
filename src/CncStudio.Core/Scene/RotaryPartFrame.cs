using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;

namespace CncStudio.Core.Scene;

/// <summary>
/// Onde ficam as linhas de giro da mesa para mostrar o percurso "na peça": cada ponto do percurso (coordenadas da máquina)
/// volta pelo giro de A/B e cai onde o corte acontece na peça. Pivôs em coordenadas do programa (relativos ao zero da peça).
/// A gira em torno de X (pivô Y, Z); B gira em torno de Y (pivô X, Z). Só mesa; C e cabeçote não entram.
/// </summary>
public sealed record RotaryPartFrame(double AY, double AZ, bool AReversed, double BX, double BZ, bool BReversed)
{
    /// <summary>Monta a partir do perfil de máquina: pega A e B quando estão na mesa; null se não há nenhum (nada a desfazer).</summary>
    public static RotaryPartFrame? From(Cam.MachineProfile? machine)
    {
        Cam.RotaryAxisConfig? a = machine?.Axes.FirstOrDefault(x => x is { Letter: "A", Carrier: Cam.RotaryCarrier.Table });
        Cam.RotaryAxisConfig? b = machine?.Axes.FirstOrDefault(x => x is { Letter: "B", Carrier: Cam.RotaryCarrier.Table });
        return a is null && b is null
            ? null
            : new RotaryPartFrame(a?.PivotY ?? 0, a?.PivotZ ?? 0, a?.Reversed ?? false, b?.PivotX ?? 0, b?.PivotZ ?? 0, b?.Reversed ?? false);
    }

    /// <summary>Leva o ponto da máquina (com o zero de peça somado) para a peça parada, desfazendo A e B.</summary>
    public Point3 ToPart(Point3 machine, Point3 offset, RotaryAngles angles)
    {
        if (!angles.IsRotated)
        {
            return machine;
        }

        double x = machine.X - offset.X, y = machine.Y - offset.Y, z = machine.Z - offset.Z;
        if (Math.Abs(angles.A) > 1e-9)
        {
            double phi = -Math.PI / 180 * (AReversed ? -angles.A : angles.A);
            double dy = y - AY, dz = z - AZ;
            (y, z) = (AY + dy * Math.Cos(phi) - dz * Math.Sin(phi), AZ + dy * Math.Sin(phi) + dz * Math.Cos(phi));
        }

        if (Math.Abs(angles.B) > 1e-9)
        {
            double phi = -Math.PI / 180 * (BReversed ? -angles.B : angles.B);
            double dx = x - BX, dz = z - BZ;
            (x, z) = (BX + dx * Math.Cos(phi) + dz * Math.Sin(phi), BZ - dx * Math.Sin(phi) + dz * Math.Cos(phi));
        }

        return new Point3(x + offset.X, y + offset.Y, z + offset.Z);
    }
}
