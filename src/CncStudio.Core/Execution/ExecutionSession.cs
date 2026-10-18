using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Execution;

/// <summary>Variável que mudou de valor num bloco (null = vazia).</summary>
public sealed record VariableChange(string Name, double? OldValue, double? NewValue);

/// <summary>
/// Um bloco executado.
/// <para><see cref="Line"/> é a linha no arquivo aberto (num subprograma de outro arquivo, a linha da chamada);
/// <see cref="Program"/> e <see cref="ProgramLine"/> dizem onde o bloco está de fato.</para>
/// <para><see cref="FirstMove"/> e <see cref="MoveCount"/> indexam os movimentos brutos da sessão, antes do
/// arremate de cantos (,R ,C) e da compensação G41/G42, que só são aplicados em <see cref="ExecutionSession.Finish"/>.</para>
/// </summary>
public sealed record ExecutedBlock(
    int Line,
    string Program,
    int ProgramLine,
    bool IsExternal,
    int Depth,
    int FirstMove,
    int MoveCount,
    IReadOnlyList<VariableChange> Changes);

/// <summary>Um nível da pilha de chamadas (programa principal, subprograma, macro G65 ou trecho repetido).</summary>
/// <param name="Program">Nome do programa ("principal", "O1234", "FURO"...).</param>
/// <param name="NextLine">Próxima linha deste nível, no programa dele (0 se o nível já terminou).</param>
/// <param name="CallerLine">Linha da chamada no arquivo aberto (0 no principal e em subprogramas do mesmo arquivo).</param>
/// <param name="RepeatsLeft">Repetições que faltam (M98 L, REPEAT P...), incluindo a atual.</param>
/// <param name="IsSection">Trecho repetido do Siemens (REPEAT rótulo).</param>
/// <param name="IsExternal">Subprograma lido de outro arquivo.</param>
public sealed record CallFrame(string Program, int NextLine, int CallerLine, int RepeatsLeft, bool IsSection, bool IsExternal);

/// <summary>
/// Execução do programa bloco a bloco: base do depurador de macros e das aulas.
/// Segue a mesma lógica de <see cref="MachineExecutor.Execute"/> (que é esta sessão rodada até o fim),
/// então a trajetória de <see cref="Finish"/> é idêntica à da execução direta.
/// Não é thread-safe.
/// </summary>
public sealed class ExecutionSession
{
    private readonly MachineExecutor _executor;
    private readonly List<VariableChange>? _changes;
    private ToolPath? _result;

    private ExecutionSession(ParseResult parse, MachineSettings? settings, bool trackVariables)
    {
        _executor = MachineExecutor.Start(parse, settings);
        if (trackVariables)
        {
            _changes = [];
            _executor.Variables.Changed = (name, oldValue, newValue) => _changes.Add(new VariableChange(name, oldValue, newValue));
        }
    }

    /// <summary>Começa a execução: nada executado ainda, o primeiro bloco pronto.</summary>
    public static ExecutionSession Start(ParseResult parse, MachineSettings? settings = null) => new(parse, settings, trackVariables: true);

    internal static ExecutionSession Start(ParseResult parse, MachineSettings? settings, bool trackVariables) =>
        new(parse, settings, trackVariables);

    public MachineKind Machine => _executor.Machine;

    /// <summary>Não há mais bloco a executar (fim de programa, erro que interrompe ou <see cref="Finish"/>).</summary>
    public bool IsFinished => _result is not null || !_executor.HasNext;

    /// <summary>Linha (no arquivo aberto) do próximo bloco a executar; null quando acabou.</summary>
    public int? NextLine => IsFinished ? null : _executor.NextBlock?.Line;

    /// <summary>Último bloco executado (null antes do primeiro passo).</summary>
    public ExecutedBlock? LastBlock { get; private set; }

    /// <summary>Quantos blocos já foram executados.</summary>
    public long ExecutedBlocks { get; private set; }

    /// <summary>Executa um bloco. Null quando não havia mais bloco a executar.</summary>
    public ExecutedBlock? Step()
    {
        if (IsFinished)
        {
            return null;
        }

        _changes?.Clear();
        int firstMove = _executor.RawMoveCount;
        if (!_executor.StepBlock())
        {
            return null;
        }

        ExecutedBlocks++;
        MachineExecutor.BlockPosition at = _executor.LastExecuted;
        LastBlock = new ExecutedBlock(at.Line, at.Program, at.ProgramLine, at.IsExternal, at.Depth,
            firstMove, _executor.RawMoveCount - firstMove, _changes is null ? [] : _changes.ToArray());
        return LastBlock;
    }

    /// <summary>
    /// Executa pelo menos um bloco e continua até o próximo bloco estar numa das linhas de parada
    /// (sem executá-lo) ou o programa acabar. Retorna o último bloco executado.
    /// </summary>
    public ExecutedBlock? Continue(IReadOnlySet<int> breakpoints)
    {
        ExecutedBlock? last = Step();
        while (last is not null && !IsFinished && !(NextLine is { } next && breakpoints.Contains(next)))
        {
            last = Step();
        }

        return last ?? LastBlock;
    }

    /// <summary>Executa até o próximo bloco ser o da linha dada (ou o programa acabar).</summary>
    public ExecutedBlock? RunToLine(int line) => Continue(new HashSet<int> { line });

    /// <summary>Variáveis visíveis agora (globais e locais do nível atual), sem as de sistema.</summary>
    public IReadOnlyDictionary<string, double?> Variables => _executor.Variables.Snapshot();

    /// <summary>Pilha de chamadas, do principal (primeiro) ao nível atual (último).</summary>
    public IReadOnlyList<CallFrame> CallStack => _executor.CallStack();

    /// <summary>Posição da ferramenta em coordenadas do programa.</summary>
    public Point3 Position => _executor.CurrentProgramPosition;

    /// <summary>Movimentos gerados até agora (antes do arremate de cantos e da compensação).</summary>
    public int MoveCount => _executor.RawMoveCount;

    /// <summary>Erros e avisos de execução até agora.</summary>
    public IReadOnlyList<ParseDiagnostic> Diagnostics => _executor.DiagnosticsSoFar;

    /// <summary>
    /// Executa o que falta e devolve a trajetória final, igual à de <see cref="MachineExecutor.Execute"/>.
    /// Chamadas seguintes devolvem a mesma trajetória.
    /// </summary>
    public ToolPath Finish()
    {
        if (_result is null)
        {
            _executor.Variables.Changed = null; // o resto roda sem registrar variáveis
            _result = _executor.Complete();
        }

        return _result;
    }
}
