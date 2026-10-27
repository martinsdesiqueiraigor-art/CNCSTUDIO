using CncStudio.Core.Geometry;

namespace CncStudio.Core.Execution;

/// <summary>
/// Estado modal da máquina no momento de um movimento (o que um painel de controle mostra):
/// plano, absoluto/incremental, unidade, zero de peça, compensação, modo de avanço,
/// velocidade constante, rotação, ciclo ativo e o deslocamento (zero de peça + TRANS) já somado.
/// Guardado uma vez por mudança, não por movimento: o <c>Move.ModalId</c> aponta para a lista.
/// </summary>
public sealed record MachineModal(
    WorkPlane Plane,
    bool Incremental,
    bool Inches,
    int WorkOffsetCode,
    sbyte Compensation,
    bool FeedPerRevolution,
    bool ConstantSurfaceSpeed,
    double Spindle,
    DrillCycle Cycle,
    Point3 Offset,
    RotaryAngles Rotary = default);

/// <summary>Ângulos (graus) dos eixos rotativos A, B e C lidos do programa (fresa).</summary>
public readonly record struct RotaryAngles(double A, double B, double C)
{
    /// <summary>Ângulos intermediários entre a e b (t de 0 a 1).</summary>
    public static RotaryAngles Lerp(RotaryAngles a, RotaryAngles b, double t) =>
        new(a.A + ((b.A - a.A) * t), a.B + ((b.B - a.B) * t), a.C + ((b.C - a.C) * t));

    /// <summary>Algum eixo rotativo fora de zero: a peça (ou ferramenta) está girada em relação à posição de programação.</summary>
    public bool IsRotated => Math.Abs(A) > 1e-9 || Math.Abs(B) > 1e-9 || Math.Abs(C) > 1e-9;
}
