namespace CncStudio.Core.Parsing;

/// <summary>Modo de movimento (grupo modal 01).</summary>
public enum MotionMode
{
    /// <summary>G00 — avanço rápido.</summary>
    Rapid = 0,

    /// <summary>G01 — interpolação linear.</summary>
    Linear = 1,

    /// <summary>G02 — arco horário.</summary>
    ClockwiseArc = 2,

    /// <summary>G03 — arco anti-horário.</summary>
    CounterClockwiseArc = 3,
}

/// <summary>Estado do spindle.</summary>
public enum SpindleState
{
    Off,
    Clockwise,
    CounterClockwise,
}

/// <summary>
/// Um bloco (linha) de programa já interpretado.
/// Os eixos guardam só o que foi escrito na linha (null = não programado);
/// movimento, avanço e spindle já trazem o valor modal efetivo naquele ponto.
/// </summary>
public sealed class Block
{
    /// <summary>Linha no arquivo (base 1).</summary>
    public required int LineNumber { get; init; }

    /// <summary>Número N do bloco, se existir.</summary>
    public int? BlockNumber { get; init; }

    /// <summary>Modo de movimento efetivo neste bloco (modal).</summary>
    public required MotionMode Motion { get; init; }

    public double? X { get; init; }
    public double? Y { get; init; }
    public double? Z { get; init; }

    /// <summary>Centro do arco relativo ao ponto inicial, em X.</summary>
    public double? I { get; init; }

    /// <summary>Centro do arco relativo ao ponto inicial, em Y.</summary>
    public double? J { get; init; }

    /// <summary>Avanço efetivo (mm/min, modal).</summary>
    public required double Feed { get; init; }

    /// <summary>Rotação efetiva (rpm, modal).</summary>
    public required double SpindleSpeed { get; init; }

    public required SpindleState Spindle { get; init; }

    /// <summary>Códigos M presentes na linha.</summary>
    public IReadOnlyList<int> MCodes { get; init; } = [];

    /// <summary>True se o bloco contém M30 ou M02.</summary>
    public bool EndsProgram { get; init; }

    public bool IsArc => Motion is MotionMode.ClockwiseArc or MotionMode.CounterClockwiseArc;

    /// <summary>
    /// True se o bloco gera deslocamento: algum eixo programado, ou um arco
    /// com I/J sem eixos (no Fanuc, isso é um círculo completo).
    /// </summary>
    public bool HasMotion =>
        X.HasValue || Y.HasValue || Z.HasValue
        || (IsArc && (I.HasValue || J.HasValue));
}
