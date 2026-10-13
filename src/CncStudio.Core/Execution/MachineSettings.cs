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
public sealed record MachineSettings
{
    public static MachineSettings Default { get; } = new();

    /// <summary>Limites de curso (avisos quando o programa passa deles). Null = sem verificação.</summary>
    public TravelLimits? TravelLimits { get; init; }

    /// <summary>Fresa ou torno. Null = descobrir pelo programa (<see cref="MachineDetector"/>).</summary>
    public MachineKind? Machine { get; init; }

    /// <summary>Torno: X programado em diâmetro (padrão dos tornos). False = X em raio.</summary>
    public bool LatheDiameterProgramming { get; init; } = true;

    /// <summary>
    /// Rotação máxima do fuso (rpm), usada na velocidade de corte constante (G96) quando o
    /// programa não limita com G50 S (Fanuc) ou LIMS= (Siemens).
    /// </summary>
    public double MaxSpindleSpeed { get; init; } = 4000;

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

    /// <summary>
    /// Velocidade de avanço rápido (G00) em mm/min, usada só na estimativa de tempo.
    /// O padrão é conservador; máquinas atuais costumam ter 15.000–40.000 mm/min.
    /// </summary>
    public double RapidRate { get; init; } = 10_000;

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

    /// <summary>
    /// Diâmetro de cada ferramenta (T → mm), usado na compensação G41/G42.
    /// Null ou valor ≤ 0: a compensação não é aplicada (o desenho mostra o programado).
    /// </summary>
    public Func<int, double>? ToolDiameterOf { get; init; }

    /// <summary>
    /// Diâmetro usado quando a ferramenta não está em <see cref="ToolDiameterOf"/> nem tem o
    /// diâmetro escrito no comentário da troca ("FRESA D10"). 0 = não compensa.
    /// </summary>
    public double FallbackToolDiameter { get; init; }
}
