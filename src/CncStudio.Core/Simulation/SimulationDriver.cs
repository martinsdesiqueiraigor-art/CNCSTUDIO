using System.Diagnostics;

namespace CncStudio.Core.Simulation;

/// <summary>
/// Leva a simulação até um movimento-alvo em fatias de tempo, para a tela continuar respondendo
/// (no celular, nenhuma fatia passa do orçamento dado). A tela chama <see cref="Run"/> a cada quadro.
/// </summary>
public sealed class SimulationDriver
{
    /// <summary>Orçamento padrão por fatia: folga grande abaixo dos 100 ms que travariam o toque.</summary>
    public static readonly TimeSpan DefaultBudget = TimeSpan.FromMilliseconds(12);

    // Distância andada entre uma conferência do relógio e outra (mm)
    private const double Chunk = 2;

    public SimulationDriver(IMaterialSimulation simulation)
    {
        Simulation = simulation;
        _target = simulation.CurrentMoveIndex;
    }

    public IMaterialSimulation Simulation { get; }

    /// <summary>Movimento até onde a simulação deve chegar (início dele; total = tudo).</summary>
    public int Target
    {
        get => _target;
        set
        {
            if (value != _target)
            {
                _target = value;
                AtTarget = false;
            }
        }
    }

    private int _target;

    /// <summary>True quando a simulação está no alvo (nada a fazer).</summary>
    public bool AtTarget { get; private set; }

    /// <summary>
    /// Avança em direção ao alvo até acabar o orçamento. Retorna true se andou (o material ou a
    /// ferramenta podem ter mudado).
    /// </summary>
    public bool Run(TimeSpan budget)
    {
        if (AtTarget)
        {
            return false;
        }

        var clock = Stopwatch.StartNew();
        bool reached;
        do
        {
            reached = Simulation.AdvanceToward(Target, Chunk);
        }
        while (!reached && clock.Elapsed < budget);

        AtTarget = reached;
        return true;
    }
}
