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
    Point3 Offset);
