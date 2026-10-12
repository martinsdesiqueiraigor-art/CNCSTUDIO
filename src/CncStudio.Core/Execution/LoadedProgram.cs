using CncStudio.Core.Dialects;
using CncStudio.Core.Parsing;

namespace CncStudio.Core.Execution;

/// <summary>
/// Um programa pronto para ser interpretado: os blocos e os índices para saltos
/// (números N, rótulos, inícios de programa) e os pares das estruturas
/// (WHILE/END, IF/ELSE/ENDIF, FOR/ENDFOR, REPEAT/UNTIL, LOOP/ENDLOOP).
/// </summary>
internal sealed class LoadedProgram
{
    private readonly Dictionary<int, List<int>> _numbers = [];
    private readonly Dictionary<string, List<int>> _labels = new(StringComparer.OrdinalIgnoreCase);

    private LoadedProgram(string name, IReadOnlyList<NcBlock> blocks, bool isExternal)
    {
        Name = name;
        Blocks = blocks;
        IsExternal = isExternal;
        Executed = new bool[blocks.Count];
    }

    public string Name { get; }
    public IReadOnlyList<NcBlock> Blocks { get; }

    /// <summary>Veio de outro arquivo (subprograma externo).</summary>
    public bool IsExternal { get; }

    /// <summary>Blocos que chegaram a ser executados (para achar código morto).</summary>
    public bool[] Executed { get; }

    /// <summary>Programa → índice da linha "O1234" / "%_N_NOME_SPF".</summary>
    public Dictionary<string, int> ProgramStarts { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Pares de estrutura nos dois sentidos (abertura ↔ fechamento).</summary>
    public Dictionary<int, int> Pairs { get; } = [];

    /// <summary>Siemens: IF/ELSEIF → próximo ramo (ELSEIF, ELSE ou ENDIF).</summary>
    public Dictionary<int, int> NextBranch { get; } = [];

    /// <summary>Siemens: IF/ELSEIF/ELSE → ENDIF.</summary>
    public Dictionary<int, int> EndIf { get; } = [];

    /// <summary>Fim (exclusivo) do programa que começa em "index": o próximo início de programa.</summary>
    public int RegionEnd(int index)
    {
        for (int i = Math.Max(0, index + 1); i < Blocks.Count; i++)
        {
            if (Blocks[i].ProgramId is not null)
            {
                return i;
            }
        }

        return Blocks.Count;
    }

    /// <summary>
    /// Procura o bloco N. GOTOF: para a frente; GOTOB: para trás; GOTO/GOTOC: para a frente e,
    /// se não achar, desde o começo do programa.
    /// </summary>
    public int? FindBlockNumber(int number, int from, string direction, int regionStart, int regionEnd) =>
        _numbers.TryGetValue(number, out List<int>? indices) ? Search(indices, from, direction, regionStart, regionEnd) : null;

    public int? FindLabel(string label, int from, string direction, int regionStart, int regionEnd) =>
        _labels.TryGetValue(label, out List<int>? indices) ? Search(indices, from, direction, regionStart, regionEnd) : null;

    private static int? Search(List<int> indices, int from, string direction, int regionStart, int regionEnd)
    {
        bool InRegion(int i) => i >= regionStart && i < regionEnd;

        if (direction == "GOTOB")
        {
            for (int k = indices.Count - 1; k >= 0; k--)
            {
                if (indices[k] < from && InRegion(indices[k])) return indices[k];
            }

            return null;
        }

        foreach (int i in indices)
        {
            if (i > from && InRegion(i)) return i;
        }

        if (direction == "GOTOF")
        {
            return null;
        }

        foreach (int i in indices)
        {
            if (InRegion(i)) return i;
        }

        return null;
    }

    /// <summary>Monta os índices e confere as estruturas; problemas viram diagnósticos.</summary>
    public static LoadedProgram Build(
        string name,
        IReadOnlyList<NcBlock> blocks,
        Dialect dialect,
        bool isExternal,
        Action<int, int, int, DiagnosticSeverity, string> report)
    {
        var program = new LoadedProgram(name, blocks, isExternal);
        var fanucLoops = new Dictionary<int, Stack<int>>();          // DO id → índices abertos
        var structures = new Stack<(string Kind, int Index)>();      // Siemens
        var ifChains = new Stack<List<int>>();                       // marcadores de cada IF aberto

        void Error(NcBlock block, string message) =>
            report(block.LineNumber, 0, 0, DiagnosticSeverity.Error, message);

        for (int i = 0; i < blocks.Count; i++)
        {
            NcBlock block = blocks[i];

            if (block.ProgramId is { } id)
            {
                program.ProgramStarts.TryAdd(id, i);
            }

            if (block.BlockNumber is { } number)
            {
                Add(program._numbers, number, i);
            }

            var keywords = block.Statements.Where(s => s.Kind == StatementKind.Keyword).ToList();
            foreach (Statement label in block.Statements.Where(s => s.Kind == StatementKind.Label))
            {
                Add(program._labels, label.Name, i);
            }

            if (keywords.Count == 0)
            {
                continue;
            }

            if (dialect == Dialect.Fanuc)
            {
                // WHILE [...] DOn  /  DOn  /  ENDn
                Statement? doStatement = keywords.FirstOrDefault(s => s.Name == "DO");
                Statement? endStatement = keywords.FirstOrDefault(s => s.Name == "END");
                if (doStatement?.Value is { } doId)
                {
                    if (!fanucLoops.TryGetValue((int)doId, out Stack<int>? stack))
                    {
                        fanucLoops[(int)doId] = stack = new Stack<int>();
                    }

                    stack.Push(i);
                }
                else if (endStatement?.Value is { } endId)
                {
                    if (fanucLoops.TryGetValue((int)endId, out Stack<int>? stack) && stack.Count > 0)
                    {
                        int open = stack.Pop();
                        program.Pairs[open] = i;
                        program.Pairs[i] = open;
                    }
                    else
                    {
                        Error(block, $"END{(int)endId} sem DO{(int)endId} correspondente.");
                    }
                }

                continue;
            }

            // Siemens: a primeira palavra-chave de estrutura do bloco decide
            Statement first = keywords[0];
            bool hasGoto = keywords.Exists(s => s.Name.StartsWith("GOTO", StringComparison.Ordinal));
            switch (first.Name)
            {
                case "IF" when !hasGoto:
                    structures.Push(("IF", i));
                    ifChains.Push([i]);
                    break;

                case "ELSEIF" or "ELSE":
                    if (ifChains.Count == 0 || structures.Count == 0 || structures.Peek().Kind != "IF")
                    {
                        Error(block, $"{first.Name} sem IF correspondente.");
                        break;
                    }

                    List<int> chain = ifChains.Peek();
                    program.NextBranch[chain[^1]] = i;
                    chain.Add(i);
                    break;

                case "ENDIF":
                    if (ifChains.Count == 0 || structures.Count == 0 || structures.Peek().Kind != "IF")
                    {
                        Error(block, "ENDIF sem IF correspondente.");
                        break;
                    }

                    structures.Pop();
                    List<int> markers = ifChains.Pop();
                    program.NextBranch[markers[^1]] = i;
                    foreach (int marker in markers)
                    {
                        program.EndIf[marker] = i;
                    }

                    break;

                case "WHILE":
                    structures.Push(("WHILE", i));
                    break;
                case "FOR":
                    structures.Push(("FOR", i));
                    break;
                case "LOOP":
                    structures.Push(("LOOP", i));
                    break;
                case "REPEAT" when keywords.Count == 1:
                    structures.Push(("REPEAT", i));
                    break;

                case "ENDWHILE":
                    Close(program, structures, "WHILE", i, block, Error);
                    break;
                case "ENDFOR":
                    Close(program, structures, "FOR", i, block, Error);
                    break;
                case "ENDLOOP":
                    Close(program, structures, "LOOP", i, block, Error);
                    break;
                case "UNTIL":
                    Close(program, structures, "REPEAT", i, block, Error);
                    break;
            }
        }

        foreach ((string kind, int index) in structures)
        {
            report(blocks[index].LineNumber, 0, 0, DiagnosticSeverity.Error, $"{kind} sem o fechamento correspondente.");
        }

        foreach ((int id, Stack<int> stack) in fanucLoops)
        {
            foreach (int index in stack)
            {
                report(blocks[index].LineNumber, 0, 0, DiagnosticSeverity.Error, $"DO{id} sem END{id} correspondente.");
            }
        }

        return program;
    }

    private static void Close(
        LoadedProgram program,
        Stack<(string Kind, int Index)> structures,
        string expected,
        int index,
        NcBlock block,
        Action<NcBlock, string> error)
    {
        if (structures.Count == 0 || structures.Peek().Kind != expected)
        {
            error(block, $"Fechamento sem {expected} correspondente (estrutura cruzada ou faltando).");
            return;
        }

        int open = structures.Pop().Index;
        program.Pairs[open] = index;
        program.Pairs[index] = open;
    }

    private static void Add<TKey>(Dictionary<TKey, List<int>> map, TKey key, int index)
        where TKey : notnull
    {
        if (!map.TryGetValue(key, out List<int>? list))
        {
            map[key] = list = [];
        }

        list.Add(index);
    }
}
