using System.Globalization;
using CncStudio.Core.Dialects;
using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Execution;

/// <summary>
/// Executa os blocos do programa como o comando faria e gera os movimentos da ferramenta.
/// Mantém o estado modal (movimento, plano, absoluto/incremental, unidades, zero de peça,
/// ferramenta, ciclo de furação) e a posição atual.
///
/// Ainda não interpreta variáveis, expressões e saltos (Fase 1B): linhas com isso são
/// puladas com um aviso, para o resto do programa continuar sendo desenhado.
/// </summary>
public sealed partial class MachineExecutor
{
    // Palavras-chave sem efeito na trajetória (aceitas sem aviso)
    private static readonly HashSet<string> SilentKeywords =
    [
        "MSG", "CFTCP", "CFC", "CFIN", "SOFT", "BRISK", "FFWON", "FFWOF", "DIAMON", "DIAMOF", "DIAM90",
        "G642", "G641", "G601", "G602", "COMPON", "COMPOF", "COMPCAD", "SPOS", "SETMS", "WAITM", "STOPRE",
        "TRAFOOF", "CUT2DF", "CUT2D", "SUPA", "GWPSON", "GWPSOF", "TOFFON", "TOFFOF",
    ];

    // Palavras-chave de controle de fluxo e variáveis: interpretadas na Fase 1B
    private static readonly HashSet<string> FlowKeywords =
    [
        "IF", "ELSE", "ENDIF", "THEN", "WHILE", "ENDWHILE", "FOR", "ENDFOR", "TO", "REPEAT", "UNTIL",
        "LOOP", "ENDLOOP", "GOTO", "GOTOF", "GOTOB", "GOTOC", "DEF", "RET", "CASE", "DEFAULT", "PROC",
    ];

    private readonly MachineSettings _settings;
    private readonly Dialect _dialect;
    private readonly List<Move> _moves = [];
    private readonly List<ParseDiagnostic> _diagnostics = [];

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
    private bool _compensationWarned;
    private bool _feedWarned;

    // ── Posição atual (com zero de peça e TRANS aplicados) ──
    private Point3 _position;

    // ── Fim de programa ──
    private bool _ended;
    private bool _warnedAfterEnd;

    private MachineExecutor(Dialect dialect, MachineSettings settings)
    {
        _dialect = dialect;
        _settings = settings;
        _workOffset = OffsetFor(54);
        _position = settings.StartPosition + Offset;
    }

    private Point3 Offset => _workOffset + _frame;

    /// <summary>Executa o programa e devolve a trajetória (com os diagnósticos de execução).</summary>
    public static ToolPath Execute(ParseResult parse, MachineSettings? settings = null)
    {
        var executor = new MachineExecutor(parse.Dialect, settings ?? MachineSettings.Default);
        foreach (NcBlock block in parse.Blocks)
        {
            executor.ExecuteBlock(block);
        }

        executor._moves.TrimExcess();
        return new ToolPath(executor._moves, executor._diagnostics);
    }

    // ───────────────────────── Bloco ─────────────────────────

    private void ExecuteBlock(NcBlock block)
    {
        int line = block.LineNumber;

        if (_ended)
        {
            if (!_warnedAfterEnd)
            {
                Warning(line, 0, 0, "Blocos depois do fim do programa (M30/M02) nunca são executados.");
                _warnedAfterEnd = true;
            }

            return;
        }

        if (!ReviewStatements(block, out BlockKeywords keywords))
        {
            return; // linha com variáveis/saltos: fica para o interpretador (Fase 1B)
        }

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

        if (words.Feed is { } feed)
        {
            _feed = feed * _unitScale;
        }

        if (words.Tool is { } tool)
        {
            _selectedTool = tool;
            if (_activeTool == 0)
            {
                _activeTool = tool; // primeira ferramenta: considera ativa mesmo sem M06
            }
        }

        if (words.ToolChange)
        {
            _activeTool = _selectedTool;
        }

        if (words.CompensationOn && !_compensationWarned)
        {
            _compensationWarned = true;
            Warning(line, 0, 0, "Compensação de raio (G41/G42) ainda não é aplicada: o desenho mostra a trajetória programada.");
        }

        ApplyCycleModes(words);

        // Siemens: TRANS/ATRANS usam os eixos do bloco como deslocamento, não como movimento
        if (keywords.Frame is { } frameKind)
        {
            ApplyFrame(block, frameKind);
        }
        else if (!words.SuppressMotion)
        {
            ExecuteMotion(block, words, keywords);
        }

        if (keywords.CycleCall is { } cycle && !keywords.IsModalCall)
        {
            ExecuteSiemensCycle(block, cycle); // ciclo chamado sem MCALL: executa já, na posição atual
        }

        if (words.EndsProgram)
        {
            _ended = true;
        }
    }

    // ───────────────────────── Palavras-chave e expressões ─────────────────────────

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
    }

    /// <summary>
    /// Analisa palavras-chave, expressões e rótulos. Retorna false se o bloco deve ser pulado
    /// (variáveis, expressões ou controle de fluxo — Fase 1B).
    /// </summary>
    private bool ReviewStatements(NcBlock block, out BlockKeywords keywords)
    {
        keywords = new BlockKeywords();
        int line = block.LineNumber;
        bool hasMcall = false;

        foreach (Statement statement in block.Statements)
        {
            switch (statement.Kind)
            {
                case StatementKind.Label:
                    break; // rótulos só importam para saltos (Fase 1B)

                case StatementKind.Expression:
                    if (statement.Text.StartsWith('T') || statement.Text.StartsWith('t'))
                    {
                        Warning(line, statement, "Ferramenta por nome (T=\"...\") ainda não é suportada; a ferramenta não muda.");
                        break;
                    }

                    Warning(line, statement, "Variáveis e expressões serão interpretadas na Fase 1B: esta linha foi pulada.");
                    return false;

                case StatementKind.Keyword:
                    string name = statement.Name;
                    if (IsFlowKeyword(name))
                    {
                        Warning(line, statement, $"'{name}' (controle de fluxo) será interpretado na Fase 1B: esta linha foi pulada.");
                        return false;
                    }

                    if (name is "CR")
                    {
                        keywords.ArcRadius = statement.Value * _unitScale;
                    }
                    else if (name is "MCALL")
                    {
                        hasMcall = true;
                    }
                    else if (name.StartsWith("CYCLE", StringComparison.Ordinal))
                    {
                        if (name is "CYCLE81" or "CYCLE82" or "CYCLE83")
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
                    else if (name is ",R" or ",C")
                    {
                        Warning(line, statement, "Arredondamento/chanfro de canto (,R / ,C) será aplicado na Fase 1B: o canto aparece vivo.");
                    }
                    else if (!SilentKeywords.Contains(name))
                    {
                        Warning(line, statement, $"'{name}' não é suportado e foi ignorado.");
                    }

                    break;
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

        return true;
    }

    private static bool IsFlowKeyword(string name) =>
        FlowKeywords.Contains(name)
        || (name.StartsWith("GOTO", StringComparison.Ordinal))
        || (name.Length > 2 && name.StartsWith("DO", StringComparison.Ordinal) && char.IsAsciiDigit(name[2]))
        || (name.Length > 3 && name.StartsWith("END", StringComparison.Ordinal) && char.IsAsciiDigit(name[3]));

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
        public int? Tool { get; set; }
        public bool ToolChange { get; set; }
        public bool CompensationOn { get; set; }
        public bool SuppressMotion { get; set; }     // G04, G53, G28...: eixos do bloco não são destino
        public bool EndsProgram { get; set; }

        // Ciclos de furação Fanuc
        public DrillCycle? Cycle { get; set; }
        public bool CycleCancel { get; set; }
        public bool? ReturnToR { get; set; }         // G99 (true) / G98 (false)

        public AddressWord? X, Y, Z, I, J, K, R, Q, P;
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
            case 'P': Set(line, word, ref w.P); break;
            case 'F':
                w.Feed = word.Value;
                break;
            case 'T':
                w.Tool = (int)word.Value;
                break;
            case 'S':
            case 'D':
            case 'H':
            case 'E':
                break; // rotação, corretores: sem efeito no desenho
            case 'L':
                if (_dialect == Dialect.Siemens)
                {
                    Warning(line, word, "Chamada de subprograma (L) será interpretada na Fase 1B: ignorada.");
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
                return;
            case 17: w.Plane = WorkPlane.XY; return;
            case 18: w.Plane = WorkPlane.ZX; return;
            case 19: w.Plane = WorkPlane.YZ; return;
            case 20 when !siemens: w.Units = 25.4; return;
            case 21 when !siemens: w.Units = 1; return;
            case 70 when siemens: w.Units = 25.4; return;
            case 71 when siemens: w.Units = 1; return;
            case 40: return;
            case 41 or 42: w.CompensationOn = true; return;
            case 53:
                w.SuppressMotion = true; // coordenada de máquina: posição real desconhecida, não desenhada
                return;
            case >= 54 and <= 59: w.WorkOffsetCode = code; return;
            case 500 when siemens: w.WorkOffsetCode = 0; return;
            case 80: w.CycleCancel = true; return;
            case 81 or 82 when !siemens: w.Cycle = DrillCycle.Drill; return;
            case 83 when !siemens: w.Cycle = DrillCycle.Peck; return;
            case 90: w.Incremental = false; return;
            case 91: w.Incremental = true; return;
            case 98 when !siemens: w.ReturnToR = false; return;
            case 99 when !siemens: w.ReturnToR = true; return;
            case 9 or 43 or 44 or 49 or 60 or 61 or 64 or 94 or 95 or 96 or 97:
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
            case 98:
                w.SuppressMotion = true;
                Warning(line, word, "Chamada de subprograma (M98) será interpretada na Fase 1B: ignorada.");
                return;
            case 99 or 17:
                Warning(line, word, $"M{code} (retorno de subprograma) será interpretado na Fase 1B.");
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

        bool hasAxis = w.X.HasValue || w.Y.HasValue || w.Z.HasValue;

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
        double x = Resolve(w.X, program.X);
        double y = Resolve(w.Y, program.Y);
        double z = Resolve(w.Z, program.Z);
        return new Point3(x, y, z) + Offset;
    }

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

        _moves.Add(new Move(_position, target, motion, line, _feed, _activeTool));
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
        _moves.Add(new Move(_position, target, _motion, line, _feed, _activeTool)
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
        if (_feed <= 0 && !_feedWarned)
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
        _diagnostics.Add(new ParseDiagnostic(line, word.Column, word.Length, DiagnosticSeverity.Warning, message));

    private void Warning(int line, Statement statement, string message) =>
        _diagnostics.Add(new ParseDiagnostic(line, statement.Column, statement.Length, DiagnosticSeverity.Warning, message));

    private void Warning(int line, int column, int length, string message) =>
        _diagnostics.Add(new ParseDiagnostic(line, column, length, DiagnosticSeverity.Warning, message));

    private void Error(int line, AddressWord word, string message) =>
        _diagnostics.Add(new ParseDiagnostic(line, word.Column, word.Length, DiagnosticSeverity.Error, message));

    private void Error(int line, int column, int length, string message) =>
        _diagnostics.Add(new ParseDiagnostic(line, column, length, DiagnosticSeverity.Error, message));
}
