using System.Globalization;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution.Expressions;
using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Execution;

/// <summary>
/// Executa o programa como o comando faria e gera os movimentos da ferramenta.
/// Mantém o estado modal (movimento, plano, absoluto/incremental, unidades, zero de peça,
/// ferramenta, ciclo de furação), a posição atual e as variáveis, e segue saltos, laços
/// e subprogramas (ver MachineExecutor.Interpreter.cs).
/// </summary>
public sealed partial class MachineExecutor
{
    // Palavras-chave sem efeito na trajetória (aceitas sem aviso)
    private static readonly HashSet<string> SilentKeywords =
    [
        "CFTCP", "CFC", "CFIN", "SOFT", "BRISK", "FFWON", "FFWOF", "DIAMON", "DIAMOF", "DIAM90",
        "G642", "G641", "G601", "G602", "COMPON", "COMPOF", "COMPCAD", "SPOS", "SETMS", "WAITM", "STOPRE",
        "TRAFOOF", "CUT2DF", "CUT2D", "SUPA", "GWPSON", "GWPSOF", "TOFFON", "TOFFOF", "PROC", "SAVE",
        "DISPLOF", "DISPLON", "SBLOF", "SBLON", "ACC", "OVR", "FGROUP", "CPRECON", "CPRECOF", "TO",
    ];

    private readonly MachineSettings _settings;
    private readonly Dialect _dialect;
    private List<Move> _moves = [];
    private readonly List<MoveTag> _tags = [];
    private readonly List<ParseDiagnostic> _diagnostics = [];
    private readonly HashSet<(int Line, int Column, string Message)> _reported = [];

    // ── Estado modal ──
    private MotionMode _motion = MotionMode.Rapid;
    private WorkPlane _plane = WorkPlane.XY;
    private bool _incremental;
    private double _unitScale = 1;             // 25,4 em polegadas
    private Point3 _workOffset;                // G54–G59
    private Point3 _frame;                     // TRANS/ATRANS (Siemens)
    private double _feed;
    private int _selectedTool;
    private int _activeTool;
    private double _spindleSpeed;                       // última rotação S (rpm), para G4 S no Siemens
    private readonly List<ProgramDwell> _dwells = [];
    private sbyte _compensationSide;          // 0 = G40, +1 = G41, −1 = G42
    private double _compensationRadius;
    private readonly HashSet<int> _toolsWithoutDiameter = [];
    private bool _feedWarned;

    // ── Posição atual (com zero de peça e TRANS aplicados) ──
    private Point3 _position;

    // ── Fim de programa ──
    private bool _ended;

    private MachineExecutor(Dialect dialect, MachineSettings settings, MachineKind machine)
    {
        _dialect = dialect;
        _settings = settings;
        _machine = machine;
        _lathe = machine == MachineKind.Lathe;
        if (_lathe)
        {
            // Torno: plano ZX, X em diâmetro e avanço por rotação (G99 / G95) por padrão
            _plane = WorkPlane.ZX;
            _diameterMode = settings.LatheDiameterProgramming;
            _feedPerRevolution = true;
        }

        _workOffset = OffsetFor(54);
        _position = settings.StartPosition + Offset;
        _variables.SystemVariables = ReadSystemVariable;
    }

    private Point3 Offset => _workOffset + _frame;

    /// <summary>Posição atual em coordenadas do programa (sem zero de peça/TRANS).</summary>
    private Point3 ProgramPosition => _position - Offset;

    /// <summary>Executa o programa e devolve a trajetória (com os diagnósticos de execução).</summary>
    public static ToolPath Execute(ParseResult parse, MachineSettings? settings = null)
    {
        settings ??= MachineSettings.Default;
        MachineKind machine = settings.Machine ?? MachineDetector.Detect(parse);
        var executor = new MachineExecutor(parse.Dialect, settings, machine);
        executor.Run(parse);
        executor.PostProcess();
        executor._moves.TrimExcess();
        return new ToolPath(executor._moves, executor._diagnostics, executor._messages, executor._dwells, machine);
    }

    // ───────────────────────── Bloco ─────────────────────────

    /// <summary>
    /// Executa um bloco já com as expressões avaliadas (palavras de endereço com valores numéricos).
    /// Controle de fluxo e atribuições já foram tratados pelo interpretador.
    /// </summary>
    private void ExecuteBlock(NcBlock block, BlockKeywords keywords)
    {
        int line = block.LineNumber;
        var words = new BlockWords();
        foreach (AddressWord word in block.Words)
        {
            ReadWord(block, word, words);
        }

        // Unidades primeiro: valores do próprio bloco já usam a nova unidade
        if (words.Units is { } scale)
        {
            _unitScale = scale;
        }

        if (words.Plane is { } plane)
        {
            _plane = plane;
        }

        if (words.Incremental is { } incremental)
        {
            _incremental = incremental;
        }

        if (words.WorkOffsetCode is { } offsetCode)
        {
            ChangeWorkOffset(offsetCode);
        }

        if (words.FeedPerRevolution is { } perRevolution)
        {
            _feedPerRevolution = perRevolution;
        }

        if (words.ConstantSurfaceSpeed is { } css)
        {
            _constantSurfaceSpeed = css;
        }

        // Siemens "G4 F2": F é o tempo de espera, não o avanço
        bool siemensDwell = words.Dwell && _dialect == Dialect.Siemens;
        if (words.Feed is { } feed && !siemensDwell)
        {
            _feed = feed * _unitScale;
        }

        if (words.Spindle is { } spindle && !siemensDwell)
        {
            if (words.SpindleLimit)
            {
                _maxSpindleSpeed = Math.Abs(spindle); // G50 S (Fanuc torno) / G26 S (Siemens)
            }
            else
            {
                _spindleSpeed = Math.Abs(spindle);
            }
        }

        if (words.Tool is { } tool)
        {
            _selectedTool = tool;
            if (_activeTool == 0 || _lathe) // no torno, T troca a ferramenta na hora (sem M06)
            {
                _activeTool = tool; // primeira ferramenta: considera ativa mesmo sem M06
            }
        }

        if (words.ToolChange)
        {
            _activeTool = _selectedTool;
        }

        if (words.CompensationSide is { } side)
        {
            SetCompensation(line, side);
        }

        ApplyCycleModes(words);

        if (_lathe)
        {
            ApplyLatheModes(block, words);
        }

        // Siemens: TRANS/ATRANS usam os eixos do bloco como deslocamento, não como movimento
        if (keywords.Frame is { } frameKind)
        {
            ApplyFrame(block, frameKind);
        }
        else if (words.LatheCycle is { } latheCycle)
        {
            ExecuteLatheCycle(block, words, latheCycle);
        }
        else if (!words.SuppressMotion)
        {
            int before = _moves.Count;
            ExecuteMotion(block, words, keywords);

            // ,R / ,C valem para o canto no fim do movimento deste bloco
            if (_moves.Count > before && (keywords.CornerRadius > 0 || keywords.CornerChamfer > 0))
            {
                MoveTag tag = _tags[^1];
                tag.CornerRadius = keywords.CornerRadius ?? 0;
                tag.CornerChamfer = keywords.CornerChamfer ?? 0;
                _tags[^1] = tag;
            }
        }

        if (keywords.CycleCall is { } cycle && !keywords.IsModalCall)
        {
            ExecuteSiemensCycle(block, cycle); // ciclo chamado sem MCALL: executa já, na posição atual
        }

        if (words.Dwell)
        {
            ExecuteDwell(block, words);
        }

        HandleProgramFlowWords(block, words);
    }

    // ───────────────────────── Tempo de espera ─────────────────────────

    /// <summary>
    /// G04: Fanuc "G04 X1.5" (segundos) ou "G04 P1500" (milissegundos);
    /// Siemens "G4 F2" (segundos) ou "G4 S50" (rotações do fuso, convertidas pela última rotação S).
    /// </summary>
    private void ExecuteDwell(NcBlock block, BlockWords w)
    {
        int line = block.LineNumber;
        double? seconds = null;

        if (_dialect == Dialect.Siemens)
        {
            if (w.Feed is { } f)
            {
                seconds = f;
            }
            else if (w.Spindle is { } revolutions)
            {
                seconds = RevolutionsToSeconds(line, revolutions);
            }
        }
        else if (w.X is { } x)
        {
            // X segue a regra de números sem ponto (X1 = 0,001 s no modo de milésimos)
            bool thousandths = !x.HasDecimalPoint && _settings.FanucIntegers == FanucIntegerMode.Thousandths;
            seconds = thousandths ? x.Value / 1000 : x.Value;
        }
        else if (w.P is { } p)
        {
            seconds = p.Value / 1000; // P é sempre em milissegundos
        }

        if (seconds is not { } value)
        {
            Warning(line, 0, 0, "Tempo de espera (G04) sem duração.");
            return;
        }

        AddDwell(line, value);
    }

    /// <summary>Converte rotações do fuso em segundos (precisa de um S anterior).</summary>
    private double? RevolutionsToSeconds(int line, double revolutions)
    {
        if (_spindleSpeed <= 0)
        {
            Warning(line, 0, 0, "Espera em rotações sem rotação do fuso (S) definida: tempo ignorado.");
            return null;
        }

        return Math.Abs(revolutions) / _spindleSpeed * 60;
    }

    private void AddDwell(int line, double seconds)
    {
        if (seconds > 0)
        {
            _dwells.Add(new ProgramDwell(_moves.Count, line, seconds, _activeTool));
        }
    }

    // ───────────────────────── Palavras-chave ─────────────────────────

    private enum FrameKind
    {
        Trans,
        ATrans,
    }

    /// <summary>O que as palavras-chave do bloco pedem.</summary>
    private sealed class BlockKeywords
    {
        public double? ArcRadius { get; set; }              // CR= (Siemens)
        public Statement? CycleCall { get; set; }           // CYCLE81(...), CYCLE83(...)
        public bool IsModalCall { get; set; }               // MCALL antes do ciclo
        public bool CancelModalCall { get; set; }           // MCALL sozinho
        public FrameKind? Frame { get; set; }
        public double? CornerRadius { get; set; }           // Fanuc ,R
        public double? CornerChamfer { get; set; }          // Fanuc ,C
    }

    /// <summary>Interpreta as palavras-chave de máquina (não as de fluxo, já tratadas antes).</summary>
    private BlockKeywords ReviewKeywords(int line, IEnumerable<Statement> statements)
    {
        var keywords = new BlockKeywords();
        bool hasMcall = false;

        foreach (Statement statement in statements)
        {
            if (statement.Kind != StatementKind.Keyword)
            {
                continue;
            }

            string name = statement.Name;
            if (name is "CR")
            {
                keywords.ArcRadius = statement.Value * _unitScale;
            }
            else if (name is "MCALL")
            {
                hasMcall = true;
            }
            else if (name is "DIAMON" or "DIAM90" && _lathe)
            {
                _diameterMode = true;
            }
            else if (name is "DIAMOF" && _lathe)
            {
                _diameterMode = false;
            }
            else if (name is "LIMS")
            {
                _maxSpindleSpeed = Math.Abs(statement.Value ?? 0);
            }
            else if (name.StartsWith("CYCLE", StringComparison.Ordinal))
            {
                if (name is "CYCLE81" or "CYCLE82" or "CYCLE83" || (_lathe && name is "CYCLE95" or "CYCLE97"))
                {
                    keywords.CycleCall = statement;
                }
                else
                {
                    Warning(line, statement, $"Ciclo {name} ainda não é suportado e foi ignorado.");
                }
            }
            else if (name is "TRANS")
            {
                keywords.Frame = FrameKind.Trans;
            }
            else if (name is "ATRANS")
            {
                keywords.Frame = FrameKind.ATrans;
            }
            else if (name is ",R")
            {
                keywords.CornerRadius = statement.Value * _unitScale;
            }
            else if (name is ",C")
            {
                keywords.CornerChamfer = statement.Value * _unitScale;
            }
            else if (!SilentKeywords.Contains(name))
            {
                Warning(line, statement, $"'{name}' não é suportado e foi ignorado.");
            }
        }

        if (hasMcall)
        {
            if (keywords.CycleCall is null)
            {
                keywords.CancelModalCall = true;
            }
            else
            {
                keywords.IsModalCall = true;
            }
        }

        if (keywords.CancelModalCall)
        {
            _modalCycle = null;
        }
        else if (keywords.IsModalCall)
        {
            _modalCycle = keywords.CycleCall;
        }

        return keywords;
    }

    private void ApplyFrame(NcBlock block, FrameKind kind)
    {
        double x = Length(block.Find('X')) ?? 0;
        double y = Length(block.Find('Y')) ?? 0;
        double z = Length(block.Find('Z')) ?? 0;
        var shift = new Point3(x, y, z);

        // A posição física não muda; só o sistema de coordenadas (o programa passa a ver outro valor)
        _frame = kind == FrameKind.Trans ? shift : _frame + shift;
    }

    // ───────────────────────── Palavras de endereço ─────────────────────────

    /// <summary>O que as palavras de endereço do bloco pedem.</summary>
    private sealed class BlockWords
    {
        public MotionMode? Motion { get; set; }
        public WorkPlane? Plane { get; set; }
        public bool? Incremental { get; set; }
        public double? Units { get; set; }
        public int? WorkOffsetCode { get; set; }
        public double? Feed { get; set; }
        public double? Spindle { get; set; }         // S (rotação; no Siemens G4, rotações de espera)
        public bool Dwell { get; set; }              // G04
        public bool CycleDwell { get; set; }         // G82: espera no fundo do furo
        public bool? FeedPerRevolution { get; set; } // G95/G99 (true) — G94/G98 (false)
        public bool? ConstantSurfaceSpeed { get; set; } // G96 (true) — G97 (false)
        public bool SpindleLimit { get; set; }       // G50 S (Fanuc torno) / G26 S (Siemens): S é a rotação máxima
        public bool CoordinateSet { get; set; }      // G50 X Z (Fanuc torno): define a posição atual
        public bool Thread { get; set; }             // G32 (Fanuc) / G33 (Siemens): rosca, avanço = passo
        public int? SimpleCycle { get; set; }        // G90/G92/G94 do torno Fanuc
        public int? LatheCycle { get; set; }         // G70/G71/G72/G76 do torno Fanuc
        public int? Tool { get; set; }
        public bool ToolChange { get; set; }
        public sbyte? CompensationSide { get; set; }   // G40 = 0, G41 = +1, G42 = −1
        public bool SuppressMotion { get; set; }     // G04, G53, G28...: eixos do bloco não são destino
        public bool EndsProgram { get; set; }
        public bool CallSubprogram { get; set; }     // M98
        public bool ReturnFromSubprogram { get; set; } // M99 / M17
        public int? SiemensSubprogram { get; set; }  // L123

        // Ciclos de furação Fanuc
        public DrillCycle? Cycle { get; set; }
        public bool CycleCancel { get; set; }
        public bool? ReturnToR { get; set; }         // G99 (true) / G98 (false)

        public AddressWord? X, Y, Z, I, J, K, R, Q, P, U, W;
        public int Repeat = 1;                        // K/L nos ciclos Fanuc
    }

    private void ReadWord(NcBlock block, AddressWord word, BlockWords w)
    {
        int line = block.LineNumber;
        switch (word.Letter)
        {
            case 'G':
                ReadG(line, word, w);
                break;
            case 'M':
                ReadM(line, word, w);
                break;
            case 'X': Set(line, word, ref w.X); break;
            case 'Y': Set(line, word, ref w.Y); break;
            case 'Z': Set(line, word, ref w.Z); break;
            case 'I': Set(line, word, ref w.I); break;
            case 'J': Set(line, word, ref w.J); break;
            case 'K':
                // No Fanuc, K em ciclo de furação é número de repetições; no plano G18/G19 é centro
                Set(line, word, ref w.K);
                break;
            case 'R': Set(line, word, ref w.R); break;
            case 'Q': Set(line, word, ref w.Q); break;
            case 'P':
                // Siemens: P é o número de repetições de uma chamada de subprograma
                Set(line, word, ref w.P);
                break;
            case 'F':
                w.Feed = word.Value;
                break;
            case 'T':
                // Torno Fanuc: T0101 = ferramenta 01, corretor 01
                w.Tool = _lathe && _dialect == Dialect.Fanuc && word.Value >= 100 ? (int)word.Value / 100 : (int)word.Value;
                break;
            case 'S':
                w.Spindle = word.Value;
                break;
            case 'U' when _lathe && _dialect == Dialect.Fanuc:
                Set(line, word, ref w.U); // incremental de X (em diâmetro)
                break;
            case 'W' when _lathe && _dialect == Dialect.Fanuc:
                Set(line, word, ref w.W); // incremental de Z
                break;
            case 'D':
            case 'H':
            case 'E':
                break; // rotação, corretores: sem efeito no desenho
            case 'L':
                if (_dialect == Dialect.Siemens)
                {
                    w.SiemensSubprogram = (int)word.Value; // L123 = chamada do subprograma L123
                }
                else
                {
                    w.Repeat = Math.Max(1, (int)word.Value); // L = repetições (ciclos, M98)
                }

                break;
            case 'A':
            case 'B':
            case 'C':
            case 'U':
            case 'V':
            case 'W':
                Warning(line, word, $"Eixo {word.Letter} não é simulado (apenas X, Y e Z).");
                break;
            default:
                Warning(line, word, $"Endereço '{word.Letter}' ignorado.");
                break;
        }
    }

    private void ReadG(int line, AddressWord word, BlockWords w)
    {
        double value = word.Value;
        bool integer = value == Math.Floor(value);
        int code = (int)value;

        if (!integer)
        {
            Warning(line, word, $"G{Format(value)} não é suportado e foi ignorado.");
            return;
        }

        bool siemens = _dialect == Dialect.Siemens;
        if ((_lathe && ReadLatheG(line, word, code, w)) || ReadFeedModeG(code, w))
        {
            return;
        }

        switch (code)
        {
            case >= 0 and <= 3:
                if (w.Motion.HasValue)
                {
                    Error(line, word, "Mais de um código de movimento (G00–G03) no mesmo bloco.");
                }

                w.Motion = (MotionMode)code;
                w.CycleCancel = true; // G00–G03 cancelam ciclo de furação
                return;
            case 4:
                w.SuppressMotion = true; // tempo de espera: X/P são tempo
                w.Dwell = true;
                return;
            case 17: w.Plane = WorkPlane.XY; return;
            case 18: w.Plane = WorkPlane.ZX; return;
            case 19: w.Plane = WorkPlane.YZ; return;
            case 20 when !siemens: w.Units = 25.4; return;
            case 21 when !siemens: w.Units = 1; return;
            case 70 when siemens: w.Units = 25.4; return;
            case 71 when siemens: w.Units = 1; return;
            case 40: w.CompensationSide = 0; return;
            case 41: w.CompensationSide = 1; return;
            case 42: w.CompensationSide = -1; return;
            case 53:
                w.SuppressMotion = true; // coordenada de máquina: posição real desconhecida, não desenhada
                return;
            case >= 54 and <= 59: w.WorkOffsetCode = code; return;
            case 500 when siemens: w.WorkOffsetCode = 0; return;
            case 80: w.CycleCancel = true; return;
            case 81 when !siemens: w.Cycle = DrillCycle.Drill; return;
            case 82 when !siemens:
                w.Cycle = DrillCycle.Drill;
                w.CycleDwell = true;
                return;
            case 83 when !siemens: w.Cycle = DrillCycle.Peck; return;
            case 90: w.Incremental = false; return;
            case 91: w.Incremental = true; return;
            case 98 when !siemens: w.ReturnToR = false; return;
            case 99 when !siemens: w.ReturnToR = true; return;
            case 9 or 43 or 44 or 49 or 60 or 61 or 64:
                return; // sem efeito na trajetória desenhada
            case 28 or 30:
                w.SuppressMotion = true;
                Warning(line, word, $"G{code} (retorno à referência) não é desenhado: a posição da máquina não é conhecida.");
                return;
            case 73 or 74 or 76 or 84 or 85 or 86 or 87 or 88 or 89 when !siemens:
                w.CycleCancel = true;
                Warning(line, word, $"Ciclo G{code} ainda não é suportado (só G81, G82 e G83).");
                return;
        }

        if (siemens && code is 60 or 64 or 641 or 642 or 601 or 602 or 450 or 451)
        {
            return;
        }

        if (!siemens && code is 10 or 92 or 52 or 68 or 69 or 15 or 16 or 50 or 51)
        {
            w.SuppressMotion = code is 10 or 92 or 52;
        }

        Warning(line, word, $"G{code} não é suportado e foi ignorado.");
    }

    private void ReadM(int line, AddressWord word, BlockWords w)
    {
        int code = (int)word.Value;
        switch (code)
        {
            case 0 or 1 or 3 or 4 or 5 or 7 or 8 or 9 or 19:
                return;
            case 6:
                w.ToolChange = true;
                return;
            case 2 or 30:
                w.EndsProgram = true;
                return;
            case 98 when _dialect == Dialect.Fanuc:
                w.CallSubprogram = true; // P = programa (eixos do bloco ainda movem antes da chamada)
                return;
            case 99 when _dialect == Dialect.Fanuc:
            case 17:
                w.ReturnFromSubprogram = true;
                return;
        }

        Warning(line, word, $"M{code} não é suportado e foi ignorado.");
    }

    private void Set(int line, AddressWord word, ref AddressWord? target)
    {
        if (target.HasValue)
        {
            Error(line, word, $"Endereço {word.Letter} repetido no mesmo bloco.");
        }

        target = word;
    }

    // ───────────────────────── Movimento ─────────────────────────

    private void ExecuteMotion(NcBlock block, BlockWords w, BlockKeywords keywords)
    {
        if (w.Motion is { } motion)
        {
            _motion = motion;
        }

        bool hasAxis = w.X.HasValue || w.Y.HasValue || w.Z.HasValue || w.U.HasValue || w.W.HasValue;

        // Torno Fanuc: G90/G92/G94 ativos repetem o ciclo a cada novo X/Z
        if (_simpleCycle != 0 && hasAxis)
        {
            ExecuteSimpleCycle(block, w);
            return;
        }

        // Ciclo de furação Fanuc ativo
        if (_drillCycle != DrillCycle.None && (hasAxis || w.R.HasValue || w.Cycle.HasValue))
        {
            ExecuteFanucDrill(block, w);
            return;
        }

        bool isArc = _motion is MotionMode.ClockwiseArc or MotionMode.CounterClockwiseArc;
        (char centerU, char centerV) = _plane.CenterLetters();
        bool hasCenter = GetWord(w, centerU).HasValue || GetWord(w, centerV).HasValue;
        bool hasRadius = (w.R.HasValue && _dialect == Dialect.Fanuc) || keywords.ArcRadius.HasValue;

        if (!hasAxis && !(isArc && hasCenter))
        {
            return;
        }

        Point3 target = TargetPosition(w);

        if (isArc)
        {
            if (hasCenter)
            {
                double du = Length(GetWord(w, centerU)) ?? 0;
                double dv = Length(GetWord(w, centerV)) ?? 0;
                AddArcWithCenter(block.LineNumber, target, du, dv);
            }
            else if (hasRadius)
            {
                double radius = keywords.ArcRadius ?? Length(w.R)!.Value;
                AddArcWithRadius(block.LineNumber, target, radius);
            }
            else
            {
                Error(block.LineNumber, 0, 0, $"Arco (G02/G03) sem centro ({centerU}/{centerV}) nem raio (R/CR=).");
                _position = target;
            }
        }
        else if (_threading && _motion == MotionMode.Linear)
        {
            // Passo: F no Fanuc (G32), K no Siemens (G33)
            AddThreadLine(block.LineNumber, target, _dialect == Dialect.Siemens ? _threadLead : _feed);
        }
        else
        {
            AddLine(block.LineNumber, target, _motion);
        }

        // Siemens: com MCALL ativo, cada posicionamento executa o ciclo
        if (_modalCycle is { } modal && !keywords.IsModalCall && (w.X.HasValue || w.Y.HasValue))
        {
            ExecuteSiemensCycle(block, modal);
        }
    }

    private static AddressWord? GetWord(BlockWords w, char letter) => letter switch
    {
        'I' => w.I,
        'J' => w.J,
        'K' => w.K,
        _ => null,
    };

    /// <summary>Destino do movimento em coordenadas da máquina (com zero de peça), considerando G90/G91.</summary>
    private Point3 TargetPosition(BlockWords w)
    {
        Point3 program = _position - Offset;
        double x = ResolveX(w.X, program.X);
        double y = Resolve(w.Y, program.Y);
        double z = Resolve(w.Z, program.Z);

        // Torno Fanuc: U/W são sempre incrementais (U em diâmetro)
        if (Length(w.U) is { } u)
        {
            x = program.X + u / XFactor;
        }

        if (Length(w.W) is { } dz)
        {
            z = program.Z + dz;
        }

        return new Point3(x, y, z) + Offset;
    }

    /// <summary>X do programa → raio (no torno em diâmetro, X é o dobro do raio).</summary>
    private double ResolveX(AddressWord? word, double current)
    {
        if (Length(word) is not { } value)
        {
            return current;
        }

        value /= XFactor;
        return _incremental ? current + value : value;
    }

    /// <summary>2 quando X é programado em diâmetro (torno), 1 nos outros casos.</summary>
    private double XFactor => _lathe && _diameterMode ? 2 : 1;

    private double Resolve(AddressWord? word, double current)
    {
        if (Length(word) is not { } value)
        {
            return current;
        }

        return _incremental ? current + value : value;
    }

    /// <summary>Converte o valor da palavra para mm (unidade e regra de inteiros do Fanuc).</summary>
    private double? Length(AddressWord? word)
    {
        if (word is not { } w)
        {
            return null;
        }

        double value = w.Value;
        if (_dialect == Dialect.Fanuc && !w.HasDecimalPoint && _settings.FanucIntegers == FanucIntegerMode.Thousandths)
        {
            value /= 1000;
        }

        return value * _unitScale;
    }

    private void AddLine(int line, Point3 target, MotionMode motion)
    {
        if (target == _position)
        {
            return;
        }

        if (motion != MotionMode.Rapid)
        {
            CheckFeed(line);
        }
        else
        {
            CheckRapidBelowZero(line, target);
        }

        AddMove(new Move(_position, target, motion, line, _feed, _activeTool));
        _position = target;
    }

    private void AddArcWithCenter(int line, Point3 target, double centerOffsetU, double centerOffsetV)
    {
        (double su, double sv, _) = _plane.ToPlane(_position);
        AddArc(line, target, su + centerOffsetU, sv + centerOffsetV);
    }

    /// <summary>Arco por raio (R no Fanuc, CR= no Siemens): R &gt; 0 até 180°, R &lt; 0 acima de 180°.</summary>
    private void AddArcWithRadius(int line, Point3 target, double radius)
    {
        (double su, double sv, _) = _plane.ToPlane(_position);
        (double eu, double ev, _) = _plane.ToPlane(target);
        double du = eu - su;
        double dv = ev - sv;
        double chord = Math.Sqrt(du * du + dv * dv);

        if (chord < 1e-9)
        {
            Error(line, 0, 0, "Arco por raio com início e fim iguais: use centro (I/J/K) para círculo completo.");
            return;
        }

        double r = Math.Abs(radius);
        if (r < chord / 2 - _settings.ArcRadiusTolerance)
        {
            Warning(line, 0, 0, string.Create(CultureInfo.InvariantCulture,
                $"Raio {r:0.###} menor que metade da distância entre os pontos ({chord / 2:0.###}): usado o mínimo possível."));
        }

        r = Math.Max(r, chord / 2);
        double h = Math.Sqrt(Math.Max(0, r * r - chord * chord / 4));

        // Normal à esquerda da corda; G03 com raio positivo tem o centro à esquerda
        double nu = -dv / chord;
        double nv = du / chord;
        bool counterClockwise = _motion == MotionMode.CounterClockwiseArc;
        double side = (counterClockwise ? 1 : -1) * (radius >= 0 ? 1 : -1);

        double cu = su + du / 2 + side * nu * h;
        double cv = sv + dv / 2 + side * nv * h;
        AddArc(line, target, cu, cv);
    }

    /// <summary>Calcula a geometria do arco no plano ativo (com hélice no eixo perpendicular).</summary>
    private void AddArc(int line, Point3 target, double centerU, double centerV)
    {
        (double su, double sv, _) = _plane.ToPlane(_position);
        (double eu, double ev, _) = _plane.ToPlane(target);
        double startRadius = Math.Sqrt(Sqr(su - centerU) + Sqr(sv - centerV));
        double endRadius = Math.Sqrt(Sqr(eu - centerU) + Sqr(ev - centerV));

        if (startRadius < 1e-9)
        {
            Error(line, 0, 0, "Arco com raio zero (o centro coincide com o ponto inicial).");
            _position = target;
            return;
        }

        double difference = Math.Abs(endRadius - startRadius);
        if (difference > _settings.ArcRadiusTolerance)
        {
            Warning(line, 0, 0, string.Create(CultureInfo.InvariantCulture,
                $"Raio inicial ({startRadius:0.###}) e final ({endRadius:0.###}) do arco diferem em {difference:0.###} mm."));
        }

        double startAngle = Math.Atan2(sv - centerV, su - centerU);
        double endAngle = Math.Atan2(ev - centerV, eu - centerU);
        double sweep = endAngle - startAngle;

        // G02 (horário) tem varredura negativa, G03 positiva; varredura ~0 vira 360°
        const double epsilon = 1e-9;
        if (_motion == MotionMode.ClockwiseArc)
        {
            if (sweep > -epsilon) sweep -= 2 * Math.PI;
        }
        else
        {
            if (sweep < epsilon) sweep += 2 * Math.PI;
        }

        CheckFeed(line);
        AddMove(new Move(_position, target, _motion, line, _feed, _activeTool)
        {
            Plane = _plane,
            CenterU = centerU,
            CenterV = centerV,
            StartRadius = startRadius,
            EndRadius = endRadius,
            StartAngle = startAngle,
            Sweep = sweep,
        });
        _position = target;
    }

    /// <summary>Guarda o movimento junto com o estado da compensação.</summary>
    private void AddMove(Move move)
    {
        // Avanço por rotação (G95/G99): o movimento guarda mm/min, calculado com a rotação no ponto
        if (!move.IsRapid && _feedPerRevolution && !_capturing)
        {
            move = move with { Feed = move.Feed * SpindleRpm(move, move.LineNumber) };
        }

        _moves.Add(move);
        _tags.Add(new MoveTag { CompensationSide = _compensationSide, CompensationRadius = _compensationRadius });
    }

    private void SetCompensation(int line, sbyte side)
    {
        _compensationSide = side;
        if (side == 0 || _lathe) // torno: raio do inserto é pequeno, desenha o programado
        {
            _compensationRadius = 0;
            return;
        }

        double diameter = _settings.ToolDiameterOf?.Invoke(_activeTool) ?? 0;
        _compensationRadius = diameter / 2;
        if (diameter <= 0 && _toolsWithoutDiameter.Add(_activeTool))
        {
            Warning(line, 0, 0, $"Compensação G41/G42 sem diâmetro da ferramenta T{_activeTool}: o desenho mostra a trajetória programada.");
        }
    }

    /// <summary>Cantos ,R/,C e compensação de raio, que dependem dos movimentos seguintes.</summary>
    private void PostProcess()
    {
        if (_tags.Exists(t => t.CornerRadius > 0 || t.CornerChamfer > 0))
        {
            PathPostProcessing.ApplyCorners(_moves, _tags, (line, message) => Warning(line, 0, 0, message));
        }

        if (_tags.Exists(t => t.CompensationSide != 0 && t.CompensationRadius > 0))
        {
            _moves = PathPostProcessing.ApplyCompensation(_moves, _tags,
                (line, message) => Warning(line, 0, 0, message),
                (line, message) => Error(line, 0, 0, message));
        }
    }

    // ───────────────────────── Zero de peça ─────────────────────────

    private void ChangeWorkOffset(int code)
    {
        _workOffset = code == 0 ? Point3.Origin : OffsetFor(code);
    }

    private Point3 OffsetFor(int code) =>
        _settings.WorkOffsets.TryGetValue(code, out Point3 offset) ? offset : Point3.Origin;

    // ───────────────────────── Validação ─────────────────────────

    private void CheckFeed(int line)
    {
        if (_feed <= 0 && !_feedWarned && !_capturing) // no perfil de ciclo o avanço vem do ciclo
        {
            _feedWarned = true;
            Warning(line, 0, 0, "Movimento de corte sem avanço (F) definido.");
        }
    }

    private void CheckRapidBelowZero(int line, Point3 target)
    {
        // Heurística: G00 movendo em XY com a ferramenta abaixo do Z0 da peça
        double startZ = _position.Z - Offset.Z;
        double endZ = target.Z - Offset.Z;
        if (_lathe)
        {
            return; // no torno Z0 é a face, e G00 com Z negativo é normal (fora do diâmetro)
        }

        bool movesXY = Math.Abs(target.X - _position.X) > 1e-6 || Math.Abs(target.Y - _position.Y) > 1e-6;
        if (movesXY && startZ < -1e-6 && endZ < -1e-6)
        {
            Warning(line, 0, 0, "G00 com a ferramenta abaixo de Z0: possível colisão com a peça.");
        }
    }

    // ───────────────────────── Diagnósticos ─────────────────────────

    private static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);

    private static double Sqr(double v) => v * v;

    private void Warning(int line, AddressWord word, string message) =>
        Report(line, word.Column, word.Length, DiagnosticSeverity.Warning, message);

    private void Warning(int line, Statement statement, string message) =>
        Report(line, statement.Column, statement.Length, DiagnosticSeverity.Warning, message);

    private void Warning(int line, int column, int length, string message) =>
        Report(line, column, length, DiagnosticSeverity.Warning, message);

    private void Error(int line, AddressWord word, string message) =>
        Report(line, word.Column, word.Length, DiagnosticSeverity.Error, message);

    private void Error(int line, Statement statement, string message) =>
        Report(line, statement.Column, statement.Length, DiagnosticSeverity.Error, message);

    private void Error(int line, int column, int length, string message) =>
        Report(line, column, length, DiagnosticSeverity.Error, message);

    /// <summary>Registra o diagnóstico uma única vez (laços repetem a mesma linha milhares de vezes).</summary>
    private void Report(int line, int column, int length, DiagnosticSeverity severity, string message)
    {
        if (_reported.Add((line, column, message)))
        {
            _diagnostics.Add(new ParseDiagnostic(line, column, length, severity, message));
        }
    }
}
