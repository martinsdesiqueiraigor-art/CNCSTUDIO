using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;

namespace CncStudio.Core.Trajectory;

/// <summary>Trajetória completa da ferramenta, pronta para backplot e simulação.</summary>
public sealed class ToolPath
{
    private int[]? _lineOrder; // índices dos movimentos ordenados por linha (montado sob demanda)

    public ToolPath(IReadOnlyList<Move> moves, IReadOnlyList<ParseDiagnostic> diagnostics,
        IReadOnlyList<ProgramMessage>? messages = null, IReadOnlyList<ProgramDwell>? dwells = null,
        MachineKind machine = MachineKind.Mill)
    {
        Machine = machine;
        Moves = moves;
        Diagnostics = diagnostics;
        Messages = messages ?? [];
        Dwells = dwells ?? [];

        var all = Bounds3.Empty;
        var cutting = Bounds3.Empty;
        var tools = new SortedSet<int>();
        foreach (Move move in moves)
        {
            all = move.IncludeIn(all);
            if (!move.IsRapid)
            {
                cutting = move.IncludeIn(cutting);
            }

            if (move.Tool != 0)
            {
                tools.Add(move.Tool);
            }
        }

        Bounds = all;
        CuttingBounds = cutting;
        Tools = tools.ToArray();
    }

    /// <summary>Movimentos em ordem de execução.</summary>
    public IReadOnlyList<Move> Moves { get; }

    /// <summary>Avisos e erros de execução (comandos não suportados, raio de arco inconsistente...).</summary>
    public IReadOnlyList<ParseDiagnostic> Diagnostics { get; }

    /// <summary>Envelope de todos os movimentos, inclusive rápidos.</summary>
    public Bounds3 Bounds { get; }

    /// <summary>Envelope só dos movimentos de corte (G01/G02/G03).</summary>
    public Bounds3 CuttingBounds { get; }

    /// <summary>Ferramentas usadas, em ordem crescente.</summary>
    public IReadOnlyList<int> Tools { get; }

    /// <summary>Fresa ou torno. No torno, X é o raio (Y = 0) e o desenho é no plano ZX.</summary>
    public MachineKind Machine { get; }

    /// <summary>Tempos de espera (G04, G82...), em ordem de execução.</summary>
    public IReadOnlyList<ProgramDwell> Dwells { get; }

    /// <summary>Mensagens do programa (MSG), em ordem de execução.</summary>
    public IReadOnlyList<ProgramMessage> Messages { get; }

    /// <summary>Última mensagem válida no movimento informado (null se nenhuma).</summary>
    public string? MessageAt(int moveIndex)
    {
        string? text = null;
        foreach (ProgramMessage message in Messages)
        {
            if (message.MoveIndex > moveIndex)
            {
                break;
            }

            text = message.Text;
        }

        return string.IsNullOrEmpty(text) ? null : text;
    }

    /// <summary>
    /// Índices dos movimentos gerados por uma linha do arquivo (vazio se a linha não gerou movimento).
    /// Uma linha pode gerar vários movimentos (ciclo de furação) e, dentro de laços ou
    /// subprogramas, aparecer várias vezes fora de ordem — todos são devolvidos, em ordem de execução.
    /// </summary>
    public IReadOnlyList<int> MovesForLine(int lineNumber)
    {
        int[] order = _lineOrder ??= BuildLineOrder();
        int first = LowerBound(order, lineNumber);
        int last = LowerBound(order, lineNumber + 1);
        if (first >= last)
        {
            return [];
        }

        return order[first..last];
    }

    private int[] BuildLineOrder()
    {
        var order = new int[Moves.Count];
        for (int i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        // Ordenação estável por linha: dentro da mesma linha, mantém a ordem de execução
        int[] keys = new int[order.Length];
        for (int i = 0; i < keys.Length; i++)
        {
            keys[i] = Moves[i].LineNumber;
        }

        Array.Sort(keys, order);
        for (int start = 0; start < order.Length;)
        {
            int end = start;
            while (end < order.Length && keys[end] == keys[start]) end++;
            Array.Sort(order, start, end - start);
            start = end;
        }

        return order;
    }

    /// <summary>Primeira posição em "order" cuja linha é maior ou igual à informada.</summary>
    private int LowerBound(int[] order, int lineNumber)
    {
        int low = 0;
        int high = order.Length;
        while (low < high)
        {
            int mid = (low + high) / 2;
            if (Moves[order[mid]].LineNumber < lineNumber)
            {
                low = mid + 1;
            }
            else
            {
                high = mid;
            }
        }

        return low;
    }
}
