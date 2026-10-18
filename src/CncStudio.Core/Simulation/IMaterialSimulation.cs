using CncStudio.Core.Geometry;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Simulation;

/// <summary>
/// O que a interface precisa de uma simulação de remoção de material (fresa ou torno):
/// avançar por distância, por linha do programa ou até um movimento, e saber onde está.
/// Não é thread-safe: chamar sempre da mesma thread.
/// </summary>
public interface IMaterialSimulation
{
    /// <summary>Índice do movimento em execução (= total quando terminou).</summary>
    int CurrentMoveIndex { get; }

    bool IsFinished { get; }

    /// <summary>Ponta da ferramenta.</summary>
    Point3 ToolPosition { get; }

    /// <summary>Movimento em execução (ou o último); null sem movimentos.</summary>
    Move? CurrentMove { get; }

    int CurrentTool { get; }

    double CurrentToolDiameter { get; }

    /// <summary>Linha do programa em execução.</summary>
    int CurrentLineNumber { get; }

    /// <summary>Distância percorrida desde o início (mm).</summary>
    double Travelled { get; }

    /// <summary>Movimentos G00 que removeram material (colisões).</summary>
    IReadOnlyList<int> RapidCollisions { get; }

    /// <summary>Muda a cada remoção de material (para saber se precisa redesenhar).</summary>
    int Version { get; }

    void Reset();

    /// <summary>Avança "distance" mm ao longo da trajetória.</summary>
    void Advance(double distance);

    /// <summary>Executa a linha atual do programa inteira (todos os movimentos dela) e para na próxima.</summary>
    void AdvanceLine();

    /// <summary>Vai ao início do movimento indicado (recomeça do bruto se precisar voltar).</summary>
    void RunToMove(int moveIndex);

    void RunToEnd();

    /// <summary>
    /// Anda no máximo "maxDistance" mm em direção ao início do movimento "moveIndex" (recomeça do
    /// bruto se precisar voltar). Serve para simular em fatias sem travar a tela.
    /// Retorna true quando chegou.
    /// </summary>
    bool AdvanceToward(int moveIndex, double maxDistance);
}
