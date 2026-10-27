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

    /// <summary>
    /// Matriz 4x4 (coluna por coluna, para a placa de vídeo) que leva a peça parada à posição girada da máquina: o contrário de
    /// <see cref="ToPart"/>. Com <paramref name="angles"/> zerados é a identidade. <paramref name="offset"/> é o zero de peça somado.
    /// </summary>
    public double[] ToMachineMatrix(Point3 offset, RotaryAngles angles)
    {
        double[] m = Identity();
        if (!angles.IsRotated)
        {
            return m;
        }

        // máquina = A(B(peça)): primeiro B, depois A
        if (Math.Abs(angles.B) > 1e-9)
        {
            m = Multiply(RotationY(Math.PI / 180 * (BReversed ? -angles.B : angles.B), BX, BZ), m);
        }

        if (Math.Abs(angles.A) > 1e-9)
        {
            m = Multiply(RotationX(Math.PI / 180 * (AReversed ? -angles.A : angles.A), AY, AZ), m);
        }

        // Pivôs são do programa; a cena já tem o zero de peça somado
        return Multiply(Translation(offset.X, offset.Y, offset.Z), Multiply(m, Translation(-offset.X, -offset.Y, -offset.Z)));
    }

    public static double[] Identity() => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1];

    private static double[] Translation(double x, double y, double z) => [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, x, y, z, 1];

    // Rotação de ângulo "a" em torno de uma reta paralela a X que passa por (y0, z0)
    private static double[] RotationX(double a, double y0, double z0)
    {
        double c = Math.Cos(a), s = Math.Sin(a);
        double ty = y0 - (c * y0 - s * z0), tz = z0 - (s * y0 + c * z0);
        return [1, 0, 0, 0, 0, c, s, 0, 0, -s, c, 0, 0, ty, tz, 1];
    }

    // Rotação em torno de uma reta paralela a Y que passa por (x0, z0)
    private static double[] RotationY(double a, double x0, double z0)
    {
        double c = Math.Cos(a), s = Math.Sin(a);
        double tx = x0 - (c * x0 + s * z0), tz = z0 - (-s * x0 + c * z0);
        return [c, 0, -s, 0, 0, 1, 0, 0, s, 0, c, 0, tx, 0, tz, 1];
    }

    // Produto a * b de matrizes 4x4 em ordem de colunas
    private static double[] Multiply(double[] a, double[] b)
    {
        var r = new double[16];
        for (int col = 0; col < 4; col++)
        {
            for (int row = 0; row < 4; row++)
            {
                double sum = 0;
                for (int k = 0; k < 4; k++)
                {
                    sum += a[(k * 4) + row] * b[(col * 4) + k];
                }

                r[(col * 4) + row] = sum;
            }
        }

        return r;
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
