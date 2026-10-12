using System.Globalization;
using System.Text;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution.Expressions;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Execution;

/// <summary>
/// Interpretador: executa os blocos na ordem em que o comando executaria, seguindo
/// saltos (GOTO), laços (WHILE, FOR, REPEAT, LOOP), condições (IF/ELSE), variáveis,
/// subprogramas (M98/M99, G65, L, chamada por nome) e repetição de trechos (REPEAT rótulo).
/// </summary>
public sealed partial class MachineExecutor
{
    private readonly VariableStore _variables = new();
    private readonly List<ProgramMessage> _messages = [];
    private readonly List<Frame> _frames = [];
    private readonly Dictionary<string, LoadedProgram?> _externalPrograms = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<(LoadedProgram Program, int Index)> _activeLoops = [];
    private LoadedProgram _main = null!;
    private long _executedBlocks;
    private int _mainEndIndex = -1;

    // Pedidos feitos pelo bloco em execução
    private int? _jump;
    private CallRequest? _call;
    private ReturnRequest? _return;
    private int _currentLine;

    /// <summary>Pedido de chamada de subprograma.</summary>
    private sealed record CallRequest(string Name, int Repeats, IReadOnlyDictionary<int, double>? Locals);

    /// <summary>Pedido de retorno (M99/M17/RET). TargetNumber = M99 P (volta para o N indicado).</summary>
    private sealed record ReturnRequest(int? TargetNumber);

    /// <summary>Um nível de execução: programa principal, subprograma ou trecho repetido.</summary>
    private sealed class Frame
    {
        public required LoadedProgram Program { get; init; }
        public int Pc { get; set; }
        public required int Start { get; init; }
        public required int End { get; init; }           // exclusivo
        public required int RegionStart { get; init; }   // limites do programa (para saltos)
        public required int RegionEnd { get; init; }
        public int RepeatsLeft { get; set; } = 1;
        public bool PopLocals { get; init; }
        public int CallerLine { get; init; }              // > 0: movimentos usam a linha da chamada
        public bool IsSection { get; init; }              // REPEAT rótulo (Siemens)
    }

    // ───────────────────────── Execução ─────────────────────────

    private void Run(ParseResult parse)
    {
        _main = LoadedProgram.Build("principal", parse.Blocks, _dialect, isExternal: false, Report);
        int end = _main.RegionEnd(0);
        _frames.Add(new Frame { Program = _main, Pc = 0, Start = 0, End = end, RegionStart = 0, RegionEnd = end });

        while (_frames.Count > 0 && !_ended)
        {
            Frame frame = _frames[^1];

            if (frame.Pc >= frame.End || frame.Pc < frame.Start)
            {
                FinishFrame(frame);
                continue;
            }

            if (++_executedBlocks > _settings.MaxExecutedBlocks || _moves.Count > _settings.MaxMoves)
            {
                int line = frame.CallerLine > 0 ? frame.CallerLine : frame.Program.Blocks[frame.Pc].LineNumber;
                Error(line, 0, 0, string.Create(CultureInfo.InvariantCulture,
                    $"Execução interrompida depois de {_executedBlocks - 1:N0} blocos: provável laço infinito (última linha executada: {line})."));
                break;
            }

            int currentIndex = frame.Pc;
            Step(frame);

            if (_ended)
            {
                if (frame.Program == _main && _frames.Count == 1)
                {
                    _mainEndIndex = currentIndex;
                }

                break;
            }

            if (_return is { } returnRequest)
            {
                ReturnFromFrame(frame, returnRequest);
                continue;
            }

            frame.Pc = _jump ?? currentIndex + 1;

            // Salto para fora de um trecho repetido: encerra o trecho e continua no destino
            if (frame.IsSection && _jump is { } target && (target < frame.Start || target >= frame.End))
            {
                _frames.RemoveAt(_frames.Count - 1);
                _frames[^1].Pc = target;
            }

            if (_call is { } call)
            {
                PushCall(frame, call);
            }
        }

        WarnUnreachableCode();
    }

    /// <summary>Executa um bloco: fluxo e variáveis primeiro, depois a parte de máquina.</summary>
    private void Step(Frame frame)
    {
        NcBlock block = frame.Program.Blocks[frame.Pc];
        int line = frame.CallerLine > 0 ? frame.CallerLine : block.LineNumber;
        _currentLine = line;
        _jump = null;
        _call = null;
        _return = null;

        if (frame.Program == _main)
        {
            _main.Executed[frame.Pc] = true;
        }

        if (block.ProgramId is not null && block.Words.Count == 0 && block.Statements.Count == 0)
        {
            return; // linha "O1234" / "%_N_..._SPF"
        }

        var words = new List<AddressWord>(block.Words);
        var machineKeywords = new List<Statement>();
        if (!InterpretStatements(frame, block, line, words, machineKeywords))
        {
            return;
        }

        // G65: chamada de macro com argumentos (as letras do bloco viram #1–#26)
        if (_dialect == Dialect.Fanuc && words.Exists(w => w.Letter == 'G' && w.Value is 65 or 66))
        {
            MacroCall(line, words);
            return;
        }

        var effective = new NcBlock
        {
            LineNumber = line,
            BlockNumber = block.BlockNumber,
            Words = words,
            Statements = machineKeywords,
        };

        BlockKeywords keywords = ReviewKeywords(line, machineKeywords);
        ExecuteBlock(effective, keywords);
    }

    /// <summary>
    /// Trata rótulos, controle de fluxo, atribuições e expressões dos endereços.
    /// Retorna false se o bloco terminou aqui (salto, condição falsa, só atribuição...).
    /// </summary>
    private bool InterpretStatements(Frame frame, NcBlock block, int line, List<AddressWord> words, List<Statement> machineKeywords)
    {
        IReadOnlyList<Statement> statements = block.Statements;
        bool siemens = _dialect == Dialect.Siemens;

        for (int i = 0; i < statements.Count; i++)
        {
            Statement statement = statements[i];
            try
            {
                switch (statement.Kind)
                {
                    case StatementKind.Label:
                        break;

                    case StatementKind.Expression:
                        HandleExpression(statement, line, words, machineKeywords);
                        break;

                    case StatementKind.Keyword:
                        switch (statement.Name)
                        {
                            case "IF":
                                if (!HandleIf(frame, statements, ref i, line))
                                {
                                    return false;
                                }

                                break;

                            case "ELSEIF" or "ELSE" when siemens:
                                // Chegou aqui seguindo o ramo verdadeiro: pula para depois do ENDIF
                                JumpToPartner(frame, line, statement, frame.Program.EndIf, +1);
                                return false;

                            case "ENDIF" when siemens:
                                break; // fim do IF: nada a executar

                            case "REPEAT" when siemens && !statements.Skip(i + 1).Any(s => s.Kind == StatementKind.Keyword):
                                return false; // REPEAT sozinho: início de REPEAT/UNTIL

                            case "WHILE":
                                if (!Condition(statement, line))
                                {
                                    JumpToPartner(frame, line, statement, frame.Program.Pairs, +1);
                                    return false;
                                }

                                if (!siemens)
                                {
                                    i++; // pula o "DOn" que acompanha o WHILE no Fanuc
                                }

                                break;

                            case "DO" when !siemens:
                                break; // DOn sem WHILE: laço sem condição (sai com GOTO)

                            case "END" when !siemens:
                                JumpToPartner(frame, line, statement, frame.Program.Pairs, 0, reevaluate: true);
                                return false;

                            case "ENDWHILE" when siemens:
                                JumpToPartner(frame, line, statement, frame.Program.Pairs, 0);
                                return false;

                            case "FOR" when siemens:
                                if (!HandleFor(frame, statement, line))
                                {
                                    return false;
                                }

                                break;

                            case "ENDFOR" when siemens:
                                HandleEndFor(frame, statement, line);
                                return false;

                            case "UNTIL" when siemens:
                                if (!Condition(statement, line))
                                {
                                    JumpToPartner(frame, line, statement, frame.Program.Pairs, +1);
                                }

                                return false;

                            case "LOOP" when siemens:
                                break;

                            case "ENDLOOP" when siemens:
                                JumpToPartner(frame, line, statement, frame.Program.Pairs, +1);
                                return false;

                            case "GOTO" or "GOTOF" or "GOTOB" or "GOTOC":
                                Goto(frame, statements, i, line);
                                return false;

                            case "REPEAT" or "REPEATB" when siemens:
                                RepeatSection(frame, statements, i, line, words);
                                return false;

                            case "DEF" when siemens:
                                Define(statement, line);
                                break;

                            case "RET" when siemens:
                                _return = new ReturnRequest(null);
                                return false;

                            case "MSG" when siemens:
                                AddMessage(statement, line);
                                break;

                            case "THEN":
                                break; // tratado pelo IF

                            default:
                                if (siemens && TrySiemensNamedCall(statement, words))
                                {
                                    break;
                                }

                                machineKeywords.Add(statement);
                                break;
                        }

                        break;
                }
            }
            catch (ExpressionException ex)
            {
                Error(line, statement, ex.Message + " Linha ignorada.");
                return false;
            }
        }

        return true;
    }

    // ───────────────────────── Condições e saltos ─────────────────────────

    private bool Condition(Statement statement, int line)
    {
        if (string.IsNullOrWhiteSpace(statement.Arguments))
        {
            throw new ExpressionException($"{statement.Name} sem condição.");
        }

        return ExpressionEvaluator.EvaluateCondition(statement.Arguments, _dialect, _variables,
            message => Warning(line, statement, message));
    }

    /// <summary>
    /// IF: com GOTO no mesmo bloco é salto condicional (os dois dialetos); Fanuc "IF [...] THEN"
    /// executa o resto do bloco só se verdadeiro; Siemens IF sem GOTO abre um bloco IF/ELSE/ENDIF.
    /// Retorna false se o bloco termina aqui.
    /// </summary>
    private bool HandleIf(Frame frame, IReadOnlyList<Statement> statements, ref int index, int line)
    {
        Statement statement = statements[index];
        bool condition = Condition(statement, line);
        Statement? next = index + 1 < statements.Count ? statements[index + 1] : null;

        if (next is { Kind: StatementKind.Keyword, Name: "GOTO" or "GOTOF" or "GOTOB" or "GOTOC" })
        {
            if (condition)
            {
                Goto(frame, statements, index + 1, line);
            }

            return false;
        }

        if (next is { Kind: StatementKind.Keyword, Name: "THEN" })
        {
            index++;
            return condition; // executa a atribuição seguinte só se verdadeiro
        }

        if (_dialect == Dialect.Siemens)
        {
            if (!condition)
            {
                JumpToNextBranch(frame, frame.Pc, line, statement);
            }

            return false;
        }

        // Fanuc "IF [...]" sem GOTO/THEN: o resto do bloco só vale se verdadeiro
        return condition;
    }

    /// <summary>Siemens IF falso: procura o próximo ramo (ELSEIF verdadeiro, ELSE ou ENDIF).</summary>
    private void JumpToNextBranch(Frame frame, int ifIndex, int line, Statement statement)
    {
        int marker = ifIndex;
        for (int guard = 0; guard < 1000; guard++)
        {
            if (!frame.Program.NextBranch.TryGetValue(marker, out int nextMarker))
            {
                Error(line, statement, "IF sem ENDIF correspondente.");
                return;
            }

            Statement first = frame.Program.Blocks[nextMarker].Statements.First(s => s.Kind == StatementKind.Keyword);
            if (first.Name == "ELSEIF")
            {
                if (Condition(first, frame.Program.Blocks[nextMarker].LineNumber))
                {
                    _jump = nextMarker + 1;
                    return;
                }

                marker = nextMarker;
                continue;
            }

            _jump = nextMarker + 1; // ELSE ou ENDIF
            return;
        }
    }

    private void JumpToPartner(Frame frame, int line, Statement statement, Dictionary<int, int> map, int offset, bool reevaluate = false)
    {
        if (!map.TryGetValue(frame.Pc, out int partner))
        {
            Error(line, statement, $"{statement.Name} sem o par correspondente (verifique a estrutura do programa).");
            return;
        }

        // Fanuc END n: volta ao WHILE (reavalia); se o par for um DO sem WHILE, volta para depois dele
        if (reevaluate && !frame.Program.Blocks[partner].Statements.Any(s => s.Name == "WHILE"))
        {
            offset = 1;
        }

        _jump = partner + offset;
    }

    /// <summary>GOTO n (Fanuc, número de bloco) ou GOTOF/GOTOB/GOTO rótulo (Siemens).</summary>
    private void Goto(Frame frame, IReadOnlyList<Statement> statements, int index, int line)
    {
        Statement statement = statements[index];
        int? target = null;

        // Destino numérico: GOTO260, GOTO 260, GOTO#10, GOTO[#1+10]
        double? number = statement.Value;
        if (number is null && !string.IsNullOrWhiteSpace(statement.Arguments))
        {
            number = ExpressionEvaluator.Evaluate(statement.Arguments, _dialect, _variables,
                message => Warning(line, statement, message));
        }

        string direction = statement.Name;
        if (number is { } n)
        {
            target = frame.Program.FindBlockNumber((int)Math.Round(n), frame.Pc, direction, frame.RegionStart, frame.RegionEnd);
            if (target is null && direction != "GOTOC")
            {
                Error(line, statement, $"Destino do salto não encontrado: N{(int)Math.Round(n)}.");
            }
        }
        else if (index + 1 < statements.Count && statements[index + 1].Kind == StatementKind.Keyword)
        {
            string label = statements[index + 1].Name;
            target = frame.Program.FindLabel(label, frame.Pc, direction, frame.RegionStart, frame.RegionEnd);
            if (target is null && direction != "GOTOC")
            {
                Error(line, statement, $"Rótulo do salto não encontrado: {label}.");
            }
        }
        else
        {
            Error(line, statement, "Salto sem destino.");
        }

        if (target is { } t)
        {
            _jump = t;
        }
    }

    // ───────────────────────── FOR (Siemens) ─────────────────────────

    /// <summary>FOR VAR=início TO fim: inicializa na primeira passagem; sai quando VAR passa do fim.</summary>
    private bool HandleFor(Frame frame, Statement statement, int line)
    {
        (string variable, string startText, string endText) = ParseFor(statement);
        var key = (frame.Program, frame.Pc);

        if (_activeLoops.Add(key))
        {
            double start = ExpressionEvaluator.Evaluate(startText, _dialect, _variables, m => Warning(line, statement, m));
            _variables.Set(variable, start);
        }

        double end = ExpressionEvaluator.Evaluate(endText, _dialect, _variables, m => Warning(line, statement, m));
        double current = _variables.Get(variable) ?? 0;
        if (current > end + 1e-9)
        {
            _activeLoops.Remove(key);
            JumpToPartner(frame, line, statement, frame.Program.Pairs, +1);
            return false;
        }

        return true;
    }

    private void HandleEndFor(Frame frame, Statement statement, int line)
    {
        if (!frame.Program.Pairs.TryGetValue(frame.Pc, out int forIndex))
        {
            Error(line, statement, "ENDFOR sem FOR correspondente.");
            return;
        }

        Statement forStatement = frame.Program.Blocks[forIndex].Statements.First(s => s.Name == "FOR");
        (string variable, _, _) = ParseFor(forStatement);
        _variables.Set(variable, (_variables.Get(variable) ?? 0) + 1);
        _jump = forIndex;
    }

    private (string Variable, string Start, string End) ParseFor(Statement statement)
    {
        string text = statement.Arguments ?? "";
        int to = IndexOfWord(text, "TO");
        int equals = text.IndexOf('=');
        if (to < 0 || equals < 0 || equals > to)
        {
            throw new ExpressionException("FOR deve ser escrito como: FOR VAR=início TO fim.");
        }

        string variable = ExpressionEvaluator.ResolveTarget(text[..equals], _dialect, _variables);
        return (variable, text[(equals + 1)..to].Trim(), text[(to + 2)..].Trim());
    }

    private static int IndexOfWord(string text, string word)
    {
        for (int i = 0; i + word.Length <= text.Length; i++)
        {
            if (string.Compare(text, i, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0
                && (i == 0 || !char.IsAsciiLetterOrDigit(text[i - 1]))
                && (i + word.Length == text.Length || !char.IsAsciiLetterOrDigit(text[i + word.Length])))
            {
                return i;
            }
        }

        return -1;
    }

    // ───────────────────────── Variáveis ─────────────────────────

    /// <summary>
    /// Expressões do bloco: atribuição ("#1=48", "R1=R1-R3", "CONTADOR=5") ou endereço com
    /// expressão ("X#7", "X-[#9+#14]", "Z=R1", "X=IC(5)"), que vira palavra numérica.
    /// </summary>
    private void HandleExpression(Statement statement, int line, List<AddressWord> words, List<Statement> machineKeywords)
    {
        string text = statement.Text.Trim();
        void Warn(string message) => Warning(line, statement, message);

        // Fanuc ",R[#8]" / ",C[#5]": canto com expressão
        if (text.StartsWith(','))
        {
            char letter = char.ToUpperInvariant(text[1]);
            double value = ExpressionEvaluator.Evaluate(text[2..], _dialect, _variables, Warn);
            machineKeywords.Add(statement with { Kind = StatementKind.Keyword, Name = "," + letter, Value = value });
            return;
        }

        int equals = TopLevelEquals(text);

        // Fanuc: "#1=..." / "#[#2]=..."
        if (text.StartsWith('#'))
        {
            if (equals < 0)
            {
                throw new ExpressionException("Expressão solta: use #n=valor.");
            }

            Assign(text[..equals], text[(equals + 1)..], line, statement);
            return;
        }

        // Siemens: identificador "=" expressão
        if (equals > 0 && _dialect == Dialect.Siemens)
        {
            string left = text[..equals].Trim();
            string right = text[(equals + 1)..].Trim();

            if (left.Length == 1 && char.IsAsciiLetter(left[0]))
            {
                char letter = char.ToUpperInvariant(left[0]);
                if (letter == 'T' && right.StartsWith('"'))
                {
                    Warning(line, statement, "Ferramenta por nome (T=\"...\") não é suportada; a ferramenta não muda.");
                    return;
                }

                // G90/G91 do próprio bloco valem para o IC/AC desta linha
                bool incremental = words.Exists(w => w.Letter == 'G' && w.Value == 91)
                    || (_incremental && !words.Exists(w => w.Letter == 'G' && w.Value == 90));
                words.Add(new AddressWord(letter, AxisValue(letter, right, incremental, Warn), true, statement.Column, statement.Length));
                return;
            }

            Assign(left, right, line, statement);
            return;
        }

        // Fanuc: endereço com expressão "X#7", "X-[#9+#14]"
        if (char.IsAsciiLetter(text[0]))
        {
            char letter = char.ToUpperInvariant(text[0]);
            double value = ExpressionEvaluator.Evaluate(text[1..], _dialect, _variables, Warn);
            words.Add(new AddressWord(letter, value, true, statement.Column, statement.Length));
            return;
        }

        throw new ExpressionException($"Expressão não reconhecida: '{text}'.");
    }

    /// <summary>Siemens: valor de eixo, com IC(...) (incremental) e AC(...) (absoluto) por eixo.</summary>
    private double AxisValue(char letter, string text, bool incremental, Action<string> warn)
    {
        string upper = text.ToUpperInvariant();
        bool ic = upper.StartsWith("IC(", StringComparison.Ordinal);
        bool ac = upper.StartsWith("AC(", StringComparison.Ordinal);
        double value = ExpressionEvaluator.Evaluate(ic || ac ? text[2..] : text, _dialect, _variables, warn);

        if (!(ic || ac) || letter is not ('X' or 'Y' or 'Z'))
        {
            return value;
        }

        // Converte para o modo atual (G90/G91), para o resto do executor não precisar saber
        double current = letter switch
        {
            'X' => ProgramPosition.X,
            'Y' => ProgramPosition.Y,
            _ => ProgramPosition.Z,
        } / _unitScale;

        if (ic && !incremental)
        {
            return current + value;
        }

        if (ac && incremental)
        {
            return value - current;
        }

        return value;
    }

    private void Assign(string target, string expression, int line, Statement statement)
    {
        string name = ExpressionEvaluator.ResolveTarget(target, _dialect, _variables, m => Warning(line, statement, m));
        double value = ExpressionEvaluator.Evaluate(expression, _dialect, _variables, m => Warning(line, statement, m));

        if (name == "#3000")
        {
            Error(line, statement, string.Create(CultureInfo.InvariantCulture, $"Alarme do programa (#3000 = {value:0.###}): execução parada."));
            _ended = true;
            return;
        }

        if (name == "#3006")
        {
            Warning(line, statement, "Parada com mensagem (#3006): a simulação continua.");
            return;
        }

        if (name is "#3001" or "#3002" or "#3004" or "#3003")
        {
            return; // relógios e controles de modo: sem efeito no desenho
        }

        _variables.Set(name, value);
    }

    /// <summary>Siemens DEF REAL A=1, B — define variáveis (vazias valem 0).</summary>
    private void Define(Statement statement, int line)
    {
        string text = statement.Arguments ?? "";
        int space = text.IndexOf(' ');
        if (space < 0)
        {
            throw new ExpressionException("DEF sem nome de variável.");
        }

        foreach (string part in SplitTopLevel(text[(space + 1)..], ','))
        {
            string item = part.Trim();
            if (item.Length == 0)
            {
                continue;
            }

            if (item.Contains('['))
            {
                Warning(line, statement, "Variáveis em vetor (DEF ... NOME[n]) não são suportadas.");
                continue;
            }

            int equals = item.IndexOf('=');
            if (equals > 0)
            {
                Assign(item[..equals], item[(equals + 1)..], line, statement);
            }
            else
            {
                _variables.Set(item.ToUpperInvariant(), 0);
            }
        }
    }

    /// <summary>MSG("texto" &lt;&lt; R1 &lt;&lt; "mm"): monta a mensagem e registra junto do movimento atual.</summary>
    private void AddMessage(Statement statement, int line)
    {
        var text = new StringBuilder();
        foreach (string part in SplitTopLevelOperator(statement.Arguments ?? "", "<<"))
        {
            string piece = part.Trim();
            if (piece.Length == 0)
            {
                continue;
            }

            if (piece.StartsWith('"') && piece.EndsWith('"') && piece.Length >= 2)
            {
                text.Append(piece[1..^1]);
            }
            else
            {
                double value = ExpressionEvaluator.Evaluate(piece, _dialect, _variables, m => Warning(line, statement, m));
                text.Append(value.ToString("0.###", CultureInfo.InvariantCulture));
            }
        }

        _messages.Add(new ProgramMessage(_moves.Count, line, text.ToString()));
    }

    private double? ReadSystemVariable(string name)
    {
        Point3Like position = new(ProgramPosition.X, ProgramPosition.Y, ProgramPosition.Z);
        return name switch
        {
            "#5001" or "#5041" or "#5021" => position.X / _unitScale,
            "#5002" or "#5042" or "#5022" => position.Y / _unitScale,
            "#5003" or "#5043" or "#5023" => position.Z / _unitScale,
            "#4001" => (int)_motion,
            "#4003" => _incremental ? 91 : 90,
            "#4006" => _unitScale > 1 ? 20 : 21,
            "#4109" => _feed / _unitScale,
            "#4120" => _activeTool,
            "#3901" or "#3902" => 0,
            _ => null,
        };
    }

    private readonly record struct Point3Like(double X, double Y, double Z);

    // ───────────────────────── Subprogramas ─────────────────────────

    /// <summary>M98/M99/M17, L123 (Siemens) e fim de programa, a partir das palavras do bloco.</summary>
    private void HandleProgramFlowWords(NcBlock block, BlockWords words)
    {
        int line = block.LineNumber;

        if (words.CallSubprogram)
        {
            if (words.P is not { } p)
            {
                Error(line, 0, 0, "M98 sem P (número do programa).");
            }
            else
            {
                int program = (int)p.Value;
                int repeats = words.Repeat;
                if (program > 9999 && words.Repeat == 1)
                {
                    repeats = program / 10000; // formato antigo: P + repetições + número (ex.: P31234)
                    program %= 10000;
                }

                _call = new CallRequest(program.ToString(CultureInfo.InvariantCulture), repeats, null);
            }
        }

        if (words.SiemensSubprogram is { } subprogram)
        {
            int repeats = words.P is { } pWord ? Math.Max(1, (int)pWord.Value) : 1;
            _call = new CallRequest("L" + subprogram.ToString(CultureInfo.InvariantCulture), repeats, null);
        }

        if (words.ReturnFromSubprogram)
        {
            int? target = _dialect == Dialect.Fanuc && words.P is { } pTarget ? (int)pTarget.Value : null;
            _return = new ReturnRequest(target);
        }

        if (words.EndsProgram)
        {
            _ended = true;
        }
    }

    /// <summary>Fanuc G65 P.. L.. A.. B..: chama a macro com as letras como variáveis locais.</summary>
    private void MacroCall(int line, List<AddressWord> words)
    {
        if (words.Exists(w => w.Letter == 'G' && w.Value is 66))
        {
            Warning(line, 0, 0, "G66 (macro modal) ainda não é suportado: ignorado.");
            return;
        }

        int? program = null;
        int repeats = 1;
        var locals = new Dictionary<int, double>();
        foreach (AddressWord word in words)
        {
            switch (word.Letter)
            {
                case 'G':
                case 'N':
                    break;
                case 'P':
                    program = (int)word.Value;
                    break;
                case 'L':
                    repeats = Math.Max(1, (int)word.Value);
                    break;
                default:
                    if (MacroArgumentNumber(word.Letter) is { } number)
                    {
                        locals[number] = word.Value;
                    }

                    break;
            }
        }

        if (program is null)
        {
            Error(line, 0, 0, "G65 sem P (número do programa da macro).");
            return;
        }

        _call = new CallRequest(program.Value.ToString(CultureInfo.InvariantCulture), repeats, locals);
    }

    /// <summary>Tabela de argumentos do G65 (tipo I): A→#1, B→#2, C→#3, I→#4 ... Z→#26.</summary>
    private static int? MacroArgumentNumber(char letter) => letter switch
    {
        'A' => 1, 'B' => 2, 'C' => 3, 'I' => 4, 'J' => 5, 'K' => 6, 'D' => 7, 'E' => 8, 'F' => 9,
        'H' => 11, 'M' => 13, 'Q' => 17, 'R' => 18, 'S' => 19, 'T' => 20, 'U' => 21, 'V' => 22,
        'W' => 23, 'X' => 24, 'Y' => 25, 'Z' => 26,
        _ => null,
    };

    /// <summary>Siemens: palavra-chave desconhecida que é nome de subprograma ("FURO P3").</summary>
    private bool TrySiemensNamedCall(Statement statement, List<AddressWord> words)
    {
        string name = statement.Name;
        if (SilentKeywords.Contains(name) || name is "CR" or "MCALL" or "TRANS" or "ATRANS" || name.StartsWith("CYCLE", StringComparison.Ordinal))
        {
            return false;
        }

        if (FindProgram(name) is null)
        {
            return false;
        }

        int repeats = 1;
        int pIndex = words.FindIndex(w => w.Letter == 'P');
        if (pIndex >= 0)
        {
            repeats = Math.Max(1, (int)words[pIndex].Value);
            words.RemoveAt(pIndex);
        }

        _call = new CallRequest(name, repeats, null);
        return true;
    }

    /// <summary>Procura o programa no próprio arquivo ou, se não houver, pelo carregador externo.</summary>
    private (LoadedProgram Program, int Start, int End)? FindProgram(string name)
    {
        string key = name.TrimStart('O', 'o');
        if (_dialect == Dialect.Fanuc && int.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
        {
            key = number.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            key = name.ToUpperInvariant();
        }

        if (_main.ProgramStarts.TryGetValue(key, out int index))
        {
            return (_main, index + 1, _main.RegionEnd(index));
        }

        if (!_externalPrograms.TryGetValue(key, out LoadedProgram? external))
        {
            external = null;
            string? text = _settings.LoadSubprogram?.Invoke(key);
            if (text is not null)
            {
                ParseResult parse = GCodeParser.Parse(text, _dialect);
                external = LoadedProgram.Build(key, parse.Blocks, _dialect, isExternal: true, (_, _, _, _, _) => { });
            }

            _externalPrograms[key] = external;
        }

        if (external is null)
        {
            return null;
        }

        int start = external.Blocks.Count > 0 && external.Blocks[0].ProgramId is not null ? 1 : 0;
        return (external, start, external.RegionEnd(start == 1 ? 0 : -1));
    }

    private void PushCall(Frame caller, CallRequest call)
    {
        if (_frames.Count >= _settings.MaxCallDepth)
        {
            Error(_currentLine, 0, 0, "Chamadas de subprograma aninhadas demais: execução parada.");
            _ended = true;
            return;
        }

        if (FindProgram(call.Name) is not { } found)
        {
            Error(_currentLine, 0, 0, $"Subprograma {call.Name} não encontrado (nem no arquivo, nem na pasta do programa).");
            return;
        }

        if (call.Repeats <= 0)
        {
            return;
        }

        if (call.Locals is not null)
        {
            _variables.PushLocals(call.Locals);
        }

        int callerLine = found.Program.IsExternal
            ? (caller.CallerLine > 0 ? caller.CallerLine : _currentLine)
            : 0;

        _frames.Add(new Frame
        {
            Program = found.Program,
            Pc = found.Start,
            Start = found.Start,
            End = found.End,
            RegionStart = found.Start,
            RegionEnd = found.End,
            RepeatsLeft = call.Repeats,
            PopLocals = call.Locals is not null,
            CallerLine = callerLine,
        });
    }

    /// <summary>Siemens REPEAT INICIO [FIM] P=n / REPEATB ROTULO P=n.</summary>
    private void RepeatSection(Frame frame, IReadOnlyList<Statement> statements, int index, int line, List<AddressWord> words)
    {
        Statement statement = statements[index];
        var labels = statements.Skip(index + 1).Where(s => s.Kind == StatementKind.Keyword).Select(s => s.Name).ToList();
        if (labels.Count == 0)
        {
            return; // REPEAT sozinho: início de REPEAT/UNTIL
        }

        int repeats = words.Find(w => w.Letter == 'P') is { Letter: 'P' } p ? Math.Max(1, (int)p.Value) : 1;

        int? start = frame.Program.FindLabel(labels[0], frame.Pc, "GOTO", frame.RegionStart, frame.RegionEnd);
        if (start is null)
        {
            Error(line, statement, $"Rótulo {labels[0]} não encontrado.");
            return;
        }

        int end;
        if (statement.Name == "REPEATB")
        {
            end = start.Value;
        }
        else if (labels.Count > 1)
        {
            int? endLabel = frame.Program.FindLabel(labels[1], start.Value, "GOTOF", frame.RegionStart, frame.RegionEnd);
            if (endLabel is null)
            {
                Error(line, statement, $"Rótulo {labels[1]} não encontrado.");
                return;
            }

            end = endLabel.Value;
        }
        else
        {
            end = frame.Pc - 1; // do rótulo até a linha anterior ao REPEAT
        }

        if (end < start.Value)
        {
            Error(line, statement, "Trecho do REPEAT vazio ou invertido.");
            return;
        }

        // O trecho vira um quadro novo; quando ele acabar, o laço principal segue na linha após o REPEAT
        _frames.Add(new Frame
        {
            Program = frame.Program,
            Pc = start.Value,
            Start = start.Value,
            End = end + 1,
            RegionStart = frame.RegionStart,
            RegionEnd = frame.RegionEnd,
            RepeatsLeft = repeats,
            CallerLine = frame.CallerLine,
            IsSection = true,
        });
    }

    /// <summary>Fim natural de um quadro (última linha): repete, ou volta para quem chamou.</summary>
    private void FinishFrame(Frame frame)
    {
        if (frame.RepeatsLeft > 1)
        {
            frame.RepeatsLeft--;
            frame.Pc = frame.Start;
            return;
        }

        // Última linha do programa/trecho sem M99/M17: volta para quem chamou
        _frames.RemoveAt(_frames.Count - 1);
        if (frame.PopLocals)
        {
            _variables.PopLocals();
        }
    }

    /// <summary>M99/M17/RET: repete o subprograma se faltam repetições; senão volta para quem chamou.</summary>
    private void ReturnFromFrame(Frame frame, ReturnRequest request)
    {
        // Retorno dentro de um trecho repetido sai do trecho e do subprograma que o contém
        while (frame.IsSection && _frames.Count > 1)
        {
            _frames.RemoveAt(_frames.Count - 1);
            frame = _frames[^1];
        }

        if (_frames.Count == 1)
        {
            Warning(_currentLine, 0, 0, "Retorno de subprograma (M99/M17) no programa principal: a simulação termina aqui.");
            _ended = true;
            return;
        }

        if (frame.RepeatsLeft > 1)
        {
            frame.RepeatsLeft--;
            frame.Pc = frame.Start;
            return;
        }

        _frames.RemoveAt(_frames.Count - 1);
        if (frame.PopLocals)
        {
            _variables.PopLocals();
        }

        if (request.TargetNumber is { } number)
        {
            Frame caller = _frames[^1];
            int? target = caller.Program.FindBlockNumber(number, caller.Pc - 1, "GOTO", caller.RegionStart, caller.RegionEnd);
            if (target is { } t)
            {
                caller.Pc = t;
            }
            else
            {
                Error(_currentLine, 0, 0, $"M99 P{number}: bloco N{number} não encontrado no programa que chamou.");
            }
        }
    }

    // ───────────────────────── Diagnóstico de código morto ─────────────────────────

    private void WarnUnreachableCode()
    {
        if (_mainEndIndex < 0)
        {
            return;
        }

        int regionEnd = _main.RegionEnd(0);
        for (int i = _mainEndIndex + 1; i < regionEnd; i++)
        {
            NcBlock block = _main.Blocks[i];
            if (!_main.Executed[i] && (block.Words.Count > 0 || block.Statements.Any(s => s.Kind != StatementKind.Label)))
            {
                Warning(block.LineNumber, 0, 0, "Blocos depois do fim do programa (M30/M02) nunca são executados.");
                return;
            }
        }
    }

    // ───────────────────────── Utilidades de texto ─────────────────────────

    /// <summary>Primeiro "=" fora de parênteses/colchetes e que não faz parte de ==, &lt;=, &gt;= ou &lt;&gt;.</summary>
    private static int TopLevelEquals(string text)
    {
        int depth = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '(' or '[') depth++;
            else if (c is ')' or ']') depth--;
            else if (c == '=' && depth == 0)
            {
                bool partOfOperator = (i > 0 && text[i - 1] is '<' or '>' or '=' or '!')
                                      || (i + 1 < text.Length && text[i + 1] == '=');
                if (!partOfOperator)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static IEnumerable<string> SplitTopLevel(string text, char separator) =>
        SplitTopLevelOperator(text, separator.ToString());

    private static List<string> SplitTopLevelOperator(string text, string separator)
    {
        var parts = new List<string>();
        int depth = 0;
        int start = 0;
        bool inQuotes = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (!inQuotes)
            {
                if (c is '(' or '[') depth++;
                else if (c is ')' or ']') depth--;
                else if (depth == 0 && string.CompareOrdinal(text, i, separator, 0, separator.Length) == 0)
                {
                    parts.Add(text[start..i]);
                    i += separator.Length - 1;
                    start = i + 1;
                }
            }
        }

        parts.Add(text[start..]);
        return parts;
    }
}
