using CncStudio.Core.Geometry;

namespace CncStudio.Core.Execution;

/// <summary>Como interpretar números sem ponto decimal no Fanuc (ex.: "X10").</summary>
public enum FanucIntegerMode
{
    /// <summary>"X10" = 10 mm (modo calculadora). Padrão da maioria dos CAMs e simuladores.</summary>
    Millimeters,

    /// <summary>"X10" = 0,010 mm (menor incremento de entrada, comportamento padrão do comando).</summary>
    Thousandths,
}

/// <summary>Configuração da máquina usada na execução do programa.</summary>
public sealed class MachineSettings
{
    public static MachineSettings Default { get; } = new();

    /// <summary>Posição da ferramenta antes do primeiro movimento (coordenadas da peça).</summary>
    public Point3 StartPosition { get; init; } = Point3.Origin;

    /// <summary>
    /// Zeros de peça G54–G59 (código → deslocamento). Não informados = zero.
    /// Com todos em zero, o desenho fica nas coordenadas do programa.
    /// </summary>
    public IReadOnlyDictionary<int, Point3> WorkOffsets { get; init; } = new Dictionary<int, Point3>();

    /// <summary>Números sem ponto decimal no Fanuc (eixos, centros, R, Q).</summary>
    public FanucIntegerMode FanucIntegers { get; init; } = FanucIntegerMode.Millimeters;

    /// <summary>Diferença máxima aceita entre raio inicial e final de um arco (mm).</summary>
    public double ArcRadiusTolerance { get; init; } = 0.01;

    /// <summary>Folga de reaproximação entre bicadas na furação profunda (G83/CYCLE83), em mm.</summary>
    public double PeckClearance { get; init; } = 0.5;

    /// <summary>Limite de blocos executados (proteção contra laço infinito).</summary>
    public long MaxExecutedBlocks { get; init; } = 5_000_000;

    /// <summary>Limite de movimentos gerados (proteção de memória).</summary>
    public int MaxMoves { get; init; } = 10_000_000;

    /// <summary>Profundidade máxima de chamadas de subprograma.</summary>
    public int MaxCallDepth { get; init; } = 32;

    /// <summary>
    /// Carrega o texto de um subprograma que não está no arquivo principal
    /// (ex.: "1234" no Fanuc, "L10" ou "FURO" no Siemens). Null = não encontrado.
    /// A interface procura na pasta do programa aberto.
    /// </summary>
    public Func<string, string?>? LoadSubprogram { get; init; }
}
