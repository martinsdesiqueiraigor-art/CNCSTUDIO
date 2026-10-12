using CncStudio.Core.Dialects;
using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Execution;

/// <summary>
/// Parte do executor para torno: X em diâmetro, U/W, avanço por rotação, velocidade de corte
/// constante (G96), rosca (G32/G33) e os ciclos G90/G92/G94, G70/G71/G72, G74/G75 e G76 do Fanuc.
///
/// Coordenadas internas: X guarda o RAIO (distância ao eixo de rotação) e Z o comprimento.
/// A conversão de diâmetro → raio acontece na leitura do X (ver ResolveX / XFactor).
/// </summary>
public sealed partial class MachineExecutor
{
    private readonly MachineKind _machine;
    private readonly bool _lathe;

    // ── Estado do torno ──
    private bool _diameterMode;                // X em diâmetro (DIAMON)
    private bool _feedPerRevolution;           // G95/G99
    private bool _constantSurfaceSpeed;        // G96: S = velocidade de corte (m/min)
    private double _maxSpindleSpeed;           // G50 S / LIMS= / G26 S (0 = usa o da máquina)
    private bool _threading;                   // G32/G33 modal
    private double _threadLead;                // Siemens G33 K
    private bool _rpmWarned;

    // ── Ciclos simples G90/G92/G94 (modais) ──
    private int _simpleCycle;                  // 0 = nenhum
    private double _simpleX;                   // último destino do ciclo (programa, raio)
    private double _simpleZ;
    private double _simpleTaper;

    // ── Parâmetros do 1º bloco de G71/G72/G76 (modais, como no comando) ──
    private double _roughDepth = 1;            // Δd (raio)
    private double _roughRetract = 0.5;        // e
    private int _threadFinishPasses = 1;       // m
    private double _threadMinDepth;            // Δdmin
    private double _threadAllowance;           // d
    private double _peckRetract = 0.5;         // e do G74/G75

    // ── Leitura do perfil (blocos P..Q) sem gerar movimentos ──
    private bool _capturing;
    private Frame? _currentFrame;

    // ───────────────────────── Códigos G ─────────────────────────

    /// <summary>Códigos G que mudam de significado no torno. Retorna true se tratou o código.</summary>
    private bool ReadLatheG(int line, AddressWord word, int code, BlockWords w)
    {
        if (_dialect == Dialect.Siemens)
        {
            switch (code)
            {
                case 33:
                    w.Motion = MotionMode.Linear;
                    w.Thread = true;
                    return true;
                case 26:
                    w.SpindleLimit = true; // G26 S = rotação máxima
                    return true;
                case 25:
                    return true; // rotação mínima: sem efeito no desenho
                default:
                    return false;
            }
        }

        switch (code)
        {
            case 32:
                w.Motion = MotionMode.Linear;
                w.Thread = true;
                return true;
            case 70 or 71 or 72 or 74 or 75 or 76:
                w.LatheCycle = code;
                return true;
            case 73:
                w.SuppressMotion = true;
                Warning(line, word, "Ciclo G73 (repetição de padrão) ainda não é suportado.");
                return true;
            case 90 or 92 or 94:
                w.SimpleCycle = code;
                return true;
            case 98:
                w.FeedPerRevolution = false;
                return true;
            case 99:
                w.FeedPerRevolution = true;
                return true;
            case 50:
                w.SpindleLimit = true;   // G50 S2000 = rotação máxima
                w.CoordinateSet = true;  // G50 X.. Z.. = define a posição atual
                w.SuppressMotion = true;
                return true;
            case >= 80 and <= 89:
                w.SuppressMotion = true;
                Warning(line, word, $"Ciclo de furação G{code} no torno não é suportado e foi ignorado.");
                return true;
            default:
                return false;
        }
    }

    /// <summary>Avanço por minuto/rotação e velocidade de corte constante (fresa e torno).</summary>
    private bool ReadFeedModeG(int code, BlockWords w)
    {
        bool latheFanuc = _lathe && _dialect == Dialect.Fanuc;
        switch (code)
        {
            case 94 when !latheFanuc:
                w.FeedPerRevolution = false;
                return true;
            case 95 when !latheFanuc:
                w.FeedPerRevolution = true;
                return true;
            case 96:
                w.ConstantSurfaceSpeed = true;
                return true;
            case 97:
                w.ConstantSurfaceSpeed = false;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Modos do torno que valem a partir deste bloco.</summary>
    private void ApplyLatheModes(NcBlock block, BlockWords w)
    {
        if (w.Motion.HasValue)
        {
            _simpleCycle = 0;            // G00–G03/G32 cancelam G90/G92/G94
            _threading = w.Thread;
        }

        if (w.SimpleCycle is { } cycle)
        {
            _simpleCycle = cycle;
            _simpleTaper = 0;
        }

        if (w.CoordinateSet && (w.X.HasValue || w.Z.HasValue))
        {
            // G50 X Z: a posição atual passa a valer X/Z no programa
            Point3 program = ProgramPosition;
            double x = Length(w.X) is { } gx ? gx / XFactor : program.X;
            double z = Length(w.Z) ?? program.Z;
            _workOffset = _position - _frame - new Point3(x, program.Y, z);
        }

        if (_threading && _dialect == Dialect.Siemens && Length(w.K) is { } lead)
        {
            _threadLead = lead; // G33 Z.. K.. (passo)
        }
    }

    // ───────────────────────── Avanço e rotação ─────────────────────────

    /// <summary>
    /// Rotação do fuso (rpm) durante o movimento. Com G96 a rotação depende do diâmetro:
    /// usa o raio médio do movimento — para movimentos lineares em X isso dá exatamente o
    /// mesmo tempo que integrar ao longo do caminho (enquanto não bate no limite de rotação).
    /// </summary>
    private double SpindleRpm(Move move, int line)
    {
        double rpm = _spindleSpeed;
        if (_constantSurfaceSpeed)
        {
            double max = _maxSpindleSpeed > 0 ? _maxSpindleSpeed : _settings.MaxSpindleSpeed;
            double radius = Math.Abs((move.Start.X + move.End.X) / 2 - Offset.X);
            double diameter = 2 * radius;
            rpm = diameter < 1e-6 ? max : Math.Min(max, _spindleSpeed * 1000 / (Math.PI * diameter));
            if (_spindleSpeed <= 0)
            {
                rpm = 0;
            }
        }
        else if (_maxSpindleSpeed > 0)
        {
            rpm = Math.Min(rpm, _maxSpindleSpeed);
        }

        if (rpm <= 0 && !_rpmWarned)
        {
            _rpmWarned = true;
            Warning(line, 0, 0, "Avanço por rotação (G95/G99) sem rotação do fuso (S): o tempo desses movimentos não é calculado.");
        }

        return Math.Max(0, rpm);
    }

    /// <summary>Movimento de rosca: avanço = passo por rotação, independente de G94/G98.</summary>
    private void AddThreadLine(int line, Point3 target, double lead)
    {
        bool savedMode = _feedPerRevolution;
        double savedFeed = _feed;
        _feedPerRevolution = true;
        _feed = lead;
        AddLine(line, target, MotionMode.Linear);
        _feedPerRevolution = savedMode;
        _feed = savedFeed;
    }

    /// <summary>Ponto em coordenadas da máquina a partir de raio e Z do programa.</summary>
    private Point3 LathePoint(double radius, double z) => new Point3(radius, ProgramPosition.Y, z) + Offset;

    // ───────────────────────── G90 / G92 / G94 ─────────────────────────

    /// <summary>
    /// Ciclos simples do torno Fanuc, a partir do ponto inicial A (posição atual):
    /// G90 (torneamento): rápido em X, corte em Z, corte em X de volta, rápido em Z de volta.
    /// G94 (faceamento): rápido em Z, corte em X, corte em Z de volta, rápido em X de volta.
    /// G92 (rosca): como o G90, com o corte em Z no passo F e a saída em rápido.
    /// R = conicidade (diferença de raio no G90/G92, de Z no G94). Repetem a cada X/Z novo.
    /// </summary>
    private void ExecuteSimpleCycle(NcBlock block, BlockWords w)
    {
        int line = block.LineNumber;
        Point3 start = ProgramPosition;
        Point3 target = TargetPosition(w) - Offset;

        // Endereço ausente: vale o do ciclo anterior (só X muda numa sequência de passes)
        double x = w.X.HasValue || w.U.HasValue ? target.X : _simpleX;
        double z = w.Z.HasValue || w.W.HasValue ? target.Z : _simpleZ;
        if (Length(w.R) is { } taper)
        {
            _simpleTaper = taper; // G90/G92: diferença de raio; G94: diferença em Z
        }

        _simpleX = x;
        _simpleZ = z;

        switch (_simpleCycle)
        {
            case 90:
                AddLine(line, LathePoint(x + _simpleTaper, start.Z), MotionMode.Rapid);
                AddLine(line, LathePoint(x, z), MotionMode.Linear);
                AddLine(line, LathePoint(start.X, z), MotionMode.Linear);
                AddLine(line, LathePoint(start.X, start.Z), MotionMode.Rapid);
                break;
            case 92:
                AddLine(line, LathePoint(x + _simpleTaper, start.Z), MotionMode.Rapid);
                AddThreadLine(line, LathePoint(x, z), _feed);
                AddLine(line, LathePoint(start.X, z), MotionMode.Rapid);
                AddLine(line, LathePoint(start.X, start.Z), MotionMode.Rapid);
                break;
            case 94:
                AddLine(line, LathePoint(start.X, z + _simpleTaper), MotionMode.Rapid);
                AddLine(line, LathePoint(x, z), MotionMode.Linear);
                AddLine(line, LathePoint(x, start.Z), MotionMode.Linear);
                AddLine(line, LathePoint(start.X, start.Z), MotionMode.Rapid);
                break;
        }
    }

    // ───────────────────────── G70 / G71 / G72 / G76 ─────────────────────────

    private void ExecuteLatheCycle(NcBlock block, BlockWords w, int code)
    {
        int line = block.LineNumber;
        if (_capturing)
        {
            Error(line, 0, 0, $"G{code} dentro do perfil de outro ciclo não é permitido.");
            return;
        }

        switch (code)
        {
            case 71 or 72:
                ExecuteFanucRoughing(line, w, code);
                break;
            case 70:
                ExecuteFanucFinishing(line, w);
                break;
            case 76:
                ExecuteFanucThreading(line, w);
                break;
            case 74 or 75:
                ExecuteFanucGrooving(line, w, code);
                break;
        }
    }

    /// <summary>
    /// G74 (furação/canal de face, bicadas em Z) e G75 (canal externo/interno, bicadas em X),
    /// formato de dois blocos: G74 R(e) — G74 X(U) Z(W) P(Δi) Q(Δk) R(Δd) F..
    /// G74: bicadas de Δk em Z; entre colunas, desloca Δi em X. G75: bicadas de Δi em X;
    /// entre canais, desloca Δk em Z. Depois de cada bicada recua "e" (quebra-cavaco);
    /// no fundo volta ao início do eixo de bicada. P e Q sem ponto decimal são em mícrons.
    /// Δd (alívio no fundo) não é desenhado.
    /// </summary>
    private void ExecuteFanucGrooving(int line, BlockWords w, int code)
    {
        bool hasTarget = w.X.HasValue || w.U.HasValue || w.Z.HasValue || w.W.HasValue;
        if (!hasTarget)
        {
            if (Length(w.R) is { } retract)
            {
                _peckRetract = Math.Abs(retract);
            }

            return;
        }

        Point3 start = ProgramPosition;
        Point3 target = TargetPosition(w) - Offset;
        double x1 = w.X.HasValue || w.U.HasValue ? target.X : start.X;
        double z1 = w.Z.HasValue || w.W.HasValue ? target.Z : start.Z;
        double xStep = Math.Abs(Micron(w.P) ?? 0); // Δi (raio)
        double zStep = Math.Abs(Micron(w.Q) ?? 0); // Δk

        bool face = code == 74;
        double peckFrom = face ? start.Z : start.X;
        double peckTo = face ? z1 : x1;
        double peckStep = face ? zStep : xStep;
        double shiftFrom = face ? start.X : start.Z;
        double shiftTo = face ? x1 : z1;
        double shiftStep = face ? xStep : zStep;

        if (Math.Abs(shiftTo - shiftFrom) > 1e-9 && shiftStep <= 0)
        {
            Error(line, 0, 0, $"G{code}: falta o deslocamento entre {(face ? "colunas (P)" : "canais (Q)")}.");
            return;
        }

        Point3 Point(double peck, double shift) => face ? LathePoint(shift, peck) : LathePoint(peck, shift);

        foreach (double position in Steps(shiftFrom, shiftTo, shiftStep))
        {
            AddLine(line, Point(peckFrom, position), MotionMode.Rapid);

            double direction = Math.Sign(peckTo - peckFrom);
            double reached = peckFrom;
            for (int guard = 0; Math.Abs(peckTo - reached) > 1e-9 && guard < 100_000; guard++)
            {
                double next = peckStep > 0 && Math.Abs(peckTo - reached) > peckStep ? reached + direction * peckStep : peckTo;
                AddLine(line, Point(next, position), MotionMode.Linear);
                reached = next;
                if (Math.Abs(peckTo - reached) > 1e-9)
                {
                    AddLine(line, Point(reached - direction * _peckRetract, position), MotionMode.Rapid); // quebra-cavaco
                }
            }

            AddLine(line, Point(peckFrom, position), MotionMode.Rapid);
        }

        AddLine(line, LathePoint(start.X, start.Z), MotionMode.Rapid);
    }

    /// <summary>Posições de "from" até "to" (inclusive) com passo "step"; a última é sempre "to".</summary>
    private static IEnumerable<double> Steps(double from, double to, double step)
    {
        yield return from;
        if (Math.Abs(to - from) < 1e-9)
        {
            yield break;
        }

        double direction = Math.Sign(to - from);
        double position = from;
        for (int guard = 0; guard < 100_000; guard++)
        {
            position = Math.Abs(to - position) > step + 1e-9 ? position + direction * step : to;
            yield return position;
            if (position == to)
            {
                yield break;
            }
        }
    }

    /// <summary>
    /// G71 (desbaste longitudinal) / G72 (desbaste de face), formato de dois blocos:
    /// G71 U(Δd) R(e) — G71 P(ns) Q(nf) U(Δu) W(Δw) F..
    /// G72 W(Δd) R(e) — G72 P(ns) Q(nf) U(Δu) W(Δw) F..
    /// O perfil (blocos N ns a N nf) é lido sem executar; depois do ciclo, a execução continua
    /// no bloco seguinte ao N nf (como no comando).
    /// </summary>
    private void ExecuteFanucRoughing(int line, BlockWords w, int code)
    {
        bool longitudinal = code == 71;
        if (w.P is null || w.Q is null)
        {
            // 1º bloco: profundidade de corte e recuo
            if (Length(longitudinal ? w.U : w.W) is { } depth)
            {
                _roughDepth = Math.Abs(depth);
            }

            if (Length(w.R) is { } retract)
            {
                _roughRetract = Math.Abs(retract);
            }

            return;
        }

        if (CaptureFanucContour(line, w, code) is not { } contour)
        {
            return;
        }

        double shiftX = (Length(w.U) ?? 0) / XFactor; // Δu é em diâmetro
        double shiftZ = Length(w.W) ?? 0;
        var shift = new Point3(shiftX, 0, shiftZ);

        RoughContour(line, contour.Profile, longitudinal, shift, _roughDepth, _roughRetract, _feed, contourPass: true, $"G{code}");
        _jump = contour.EndIndex + 1;
    }

    /// <summary>G70 P(ns) Q(nf): acabamento seguindo o perfil, com F/S do perfil; volta ao ponto inicial.</summary>
    private void ExecuteFanucFinishing(int line, BlockWords w)
    {
        if (w.P is null || w.Q is null)
        {
            Error(line, 0, 0, "G70 sem P (bloco inicial do perfil) e Q (bloco final).");
            return;
        }

        Point3 start = _position;
        if (CaptureFanucContour(line, w, 70) is not { } contour)
        {
            return;
        }

        for (int i = 0; i < contour.Approach.Count; i++)
        {
            AppendCaptured(contour.Approach[i], contour.ApproachTags[i]);
        }

        for (int i = 0; i < contour.Profile.Count; i++)
        {
            AppendCaptured(contour.Profile[i], contour.ProfileTags[i]);
        }

        AddLine(line, start, MotionMode.Rapid);
    }

    /// <summary>Acrescenta um movimento lido do perfil (o avanço por rotação é convertido aqui).</summary>
    private void AppendCaptured(Move move, MoveTag tag)
    {
        if (move.Start != _position)
        {
            move = move with { Start = _position }; // reta: garante continuidade
        }

        AddMove(move);
        _tags[^1] = tag;
        _position = move.End;
    }

    /// <summary>
    /// G76 (rosca em vários passes), formato de dois blocos:
    /// G76 P(m)(r)(a) Q(Δdmin) R(d) — G76 X(U) Z(W) R(i) P(k) Q(Δd) F(passo).
    /// Profundidade de cada passe: Δd·√n (corte de área constante), até k − d; depois o
    /// acabamento em k, repetido m vezes. P e Q sem ponto decimal são em mícrons.
    /// </summary>
    private void ExecuteFanucThreading(int line, BlockWords w)
    {
        bool hasTarget = w.X.HasValue || w.U.HasValue || w.Z.HasValue || w.W.HasValue;
        if (!hasTarget)
        {
            if (w.P is { } p)
            {
                _threadFinishPasses = Math.Max(1, (int)p.Value / 10000);
            }

            if (Micron(w.Q) is { } minDepth)
            {
                _threadMinDepth = Math.Abs(minDepth);
            }

            if (Length(w.R) is { } allowance)
            {
                _threadAllowance = Math.Abs(allowance);
            }

            return;
        }

        if (Micron(w.P) is not { } height || Micron(w.Q) is not { } firstDepth || height <= 0 || firstDepth <= 0)
        {
            Error(line, 0, 0, "G76 sem altura do filete (P) ou profundidade do 1º passe (Q).");
            return;
        }

        Point3 start = ProgramPosition;
        Point3 target = TargetPosition(w) - Offset;
        double taper = (Length(w.R) ?? 0) / XFactor;   // raio no início − raio no fim
        double lead = _feed;
        double side = Math.Sign(start.X - target.X);    // +1 = rosca externa (A acima da raiz)
        if (side == 0)
        {
            Error(line, 0, 0, "G76: o ponto inicial está no diâmetro da raiz da rosca.");
            return;
        }

        foreach (double depth in ThreadDepths(height, firstDepth, _threadMinDepth, _threadAllowance, _threadFinishPasses))
        {
            double offset = side * (height - depth); // distância da raiz
            AddLine(line, LathePoint(target.X + taper + offset, start.Z), MotionMode.Rapid);
            AddThreadLine(line, LathePoint(target.X + offset, target.Z), lead);
            AddLine(line, LathePoint(start.X, target.Z), MotionMode.Rapid);
            AddLine(line, LathePoint(start.X, start.Z), MotionMode.Rapid);
        }
    }

    /// <summary>Profundidades (a partir da crista) dos passes do G76.</summary>
    internal static List<double> ThreadDepths(double height, double firstDepth, double minIncrement, double allowance, int finishPasses)
    {
        var depths = new List<double>();
        double roughLimit = Math.Max(0, height - allowance);
        double previous = 0;
        for (int n = 1; n < 10_000; n++)
        {
            double depth = firstDepth * Math.Sqrt(n);
            if (depth - previous < minIncrement)
            {
                depth = previous + minIncrement;
            }

            if (depth >= roughLimit - 1e-9)
            {
                break;
            }

            depths.Add(depth);
            previous = depth;
        }

        if (roughLimit > previous + 1e-9 && allowance > 0)
        {
            depths.Add(roughLimit);
        }

        for (int i = 0; i < Math.Max(1, finishPasses); i++)
        {
            depths.Add(height);
        }

        return depths;
    }

    /// <summary>P/Q do G76: sem ponto decimal = mícrons (menor incremento); com ponto = mm.</summary>
    private double? Micron(AddressWord? word)
    {
        if (word is not { } w)
        {
            return null;
        }

        if (w.HasDecimalPoint)
        {
            return w.Value * _unitScale;
        }

        return _unitScale > 1 ? w.Value * 0.0001 * 25.4 : w.Value / 1000;
    }

    // ───────────────────────── Leitura do perfil ─────────────────────────

    /// <summary>Perfil lido: aproximação (do ponto inicial até o 1º ponto) e o contorno em si.</summary>
    private sealed record CapturedContour(
        List<Move> Approach, List<MoveTag> ApproachTags,
        List<Move> Profile, List<MoveTag> ProfileTags,
        int EndIndex);

    private CapturedContour? CaptureFanucContour(int line, BlockWords w, int code)
    {
        int first = (int)w.P!.Value.Value;
        int last = (int)w.Q!.Value.Value;
        Frame frame = _currentFrame!;
        LoadedProgram program = frame.Program;

        int? start = program.FindBlockNumber(first, frame.Pc, "GOTO", frame.RegionStart, frame.RegionEnd);
        if (start is not { } s)
        {
            Error(line, 0, 0, $"G{code}: bloco N{first} (início do perfil, P) não encontrado.");
            return null;
        }

        int? end = program.FindBlockNumber(last, s - 1, "GOTOF", frame.RegionStart, frame.RegionEnd);
        if (end is not { } e)
        {
            Error(line, 0, 0, $"G{code}: bloco N{last} (fim do perfil, Q) não encontrado depois do N{first}.");
            return null;
        }

        return CaptureBlocks(line, program, s, e, frame.RegionStart, frame.RegionEnd, frame.CallerLine, $"G{code}");
    }

    /// <summary>
    /// Executa os blocos [start, end] só para obter a geometria (sem gerar movimentos de verdade)
    /// e devolve tudo como estava: posição, modos, avanço. O 1º movimento que sai do ponto
    /// inicial até o começo do perfil é separado como "aproximação".
    /// </summary>
    private CapturedContour? CaptureBlocks(int line, LoadedProgram program, int start, int end,
        int regionStart, int regionEnd, int callerLine, string cycleName)
    {
        // Guarda o estado
        var savedMoves = _moves;
        var savedTags = new List<MoveTag>(_tags);
        int savedTagCount = _tags.Count;
        Point3 savedPosition = _position;
        MotionMode savedMotion = _motion;
        double savedFeed = _feed;
        WorkPlane savedPlane = _plane;
        bool savedIncremental = _incremental;
        bool savedThreading = _threading;
        int savedSimple = _simpleCycle;
        int savedActive = _activeTool, savedSelected = _selectedTool;
        double savedSpindle = _spindleSpeed;
        bool savedCss = _constantSurfaceSpeed, savedPerRev = _feedPerRevolution;
        Frame? savedFrame = _currentFrame;
        int? savedJump = _jump;
        int savedLine = _currentLine;
        bool savedEnded = _ended;

        _moves = [];
        _tags.Clear();
        _capturing = true;
        bool flowIgnored = false;

        try
        {
            var frame = new Frame
            {
                Program = program,
                Start = start,
                End = end + 1,
                RegionStart = regionStart,
                RegionEnd = regionEnd,
                CallerLine = callerLine,
            };

            for (int i = start; i <= end; i++)
            {
                frame.Pc = i;
                Step(frame);
                if (_jump is not null || _call is not null)
                {
                    flowIgnored = true;
                }

                if (_return is not null)
                {
                    break; // fim do subprograma do contorno (M17/RET)
                }
            }
        }
        finally
        {
            _capturing = false;
        }

        List<Move> captured = _moves;
        var capturedTags = new List<MoveTag>(_tags);

        // Restaura
        _moves = savedMoves;
        _tags.Clear();
        _tags.AddRange(savedTags.Take(savedTagCount));
        _position = savedPosition;
        _motion = savedMotion;
        _feed = savedFeed;
        _plane = savedPlane;
        _incremental = savedIncremental;
        _threading = savedThreading;
        _simpleCycle = savedSimple;
        _activeTool = savedActive;
        _selectedTool = savedSelected;
        _spindleSpeed = savedSpindle;
        _constantSurfaceSpeed = savedCss;
        _feedPerRevolution = savedPerRev;
        _currentFrame = savedFrame;
        _jump = savedJump;
        _call = null;
        _return = null;
        _currentLine = savedLine;
        _ended = savedEnded;

        if (flowIgnored)
        {
            Warning(line, 0, 0, $"{cycleName}: saltos e chamadas dentro do perfil foram ignorados.");
        }

        // A aproximação são os movimentos iniciais que só mudam o eixo de profundidade
        // (G00 X.. no G71); o resto é o perfil
        int approach = captured.Count > 0 ? 1 : 0;
        if (captured.Count < 2)
        {
            Error(line, 0, 0, $"{cycleName}: o perfil precisa de pelo menos dois movimentos.");
            return null;
        }

        return new CapturedContour(
            captured.GetRange(0, approach), capturedTags.GetRange(0, approach),
            captured.GetRange(approach, captured.Count - approach), capturedTags.GetRange(approach, captured.Count - approach),
            end);
    }

    // ───────────────────────── Desbaste ─────────────────────────

    /// <summary>
    /// Desbaste em passes paralelos a um eixo, entre o ponto inicial (posição atual = canto do bruto)
    /// e o perfil deslocado do sobremetal. depthIsX = true: passes ao longo de Z com profundidade em X
    /// (G71, CYCLE95 longitudinal); false: passes ao longo de X com profundidade em Z (G72, face).
    ///
    /// O cálculo é feito num sistema normalizado (a = eixo do passe, d = profundidade), em que o
    /// material fica do lado de d maior e o perfil começa no ponto mais fundo e avança em +a.
    /// Assim o mesmo código serve para externo/interno e para longitudinal/face.
    /// </summary>
    private void RoughContour(int line, List<Move> profile, bool depthIsX, Point3 shift,
        double depthStep, double retract, double feed, bool contourPass, string cycleName,
        double normalizedDepthAllowance = 0, double normalizedFeedAllowance = 0)
    {
        if (depthStep <= 0)
        {
            Error(line, 0, 0, $"{cycleName}: profundidade de corte (Δd) deve ser maior que zero.");
            return;
        }

        Point3 a = _position;
        double D(Point3 p) => depthIsX ? p.X : p.Z;
        double A(Point3 p) => depthIsX ? p.Z : p.X;

        // Lado do material: o ponto inicial fica fora do perfil
        double sd = Math.Sign(D(a) - D(profile[0].Start));
        if (sd == 0)
        {
            sd = Math.Sign(D(a) - D(profile[^1].End));
        }

        if (sd == 0)
        {
            Error(line, 0, 0, $"{cycleName}: o ponto inicial está na mesma altura do perfil; não há material para desbastar.");
            return;
        }

        // O perfil deve começar no ponto mais fundo (mais longe do material)
        if (sd * D(profile[^1].End) < sd * D(profile[0].Start))
        {
            profile = profile.AsEnumerable().Reverse().Select(ReverseMove).ToList();
        }

        double sa = Math.Sign(A(profile[^1].End) - A(profile[0].Start));
        if (sa == 0)
        {
            Error(line, 0, 0, $"{cycleName}: o perfil não avança na direção do desbaste.");
            return;
        }

        // Sobremetal: deslocamento real (G71/G72) ou normalizado (CYCLE95: sempre para o lado do material)
        if (normalizedDepthAllowance != 0 || normalizedFeedAllowance != 0)
        {
            double depthShift = sd * normalizedDepthAllowance;
            double feedShift = -sa * normalizedFeedAllowance;
            shift = depthIsX ? new Point3(depthShift, 0, feedShift) : new Point3(feedShift, 0, depthShift);
        }

        if (shift != Point3.Origin)
        {
            profile = profile.Select(m => TranslateMove(m, shift)).ToList();
        }

        // Perfil em pontos (normalizado)
        var points = new List<Point3> { profile[0].Start };
        foreach (Move move in profile)
        {
            move.AppendPolyline(points, 0.01);
        }

        var normalized = points.Select(p => (A: sa * A(p), D: sd * D(p))).ToList();
        if (!AdvancesAlongCut(normalized))
        {
            Warning(line, 0, 0, $"{cycleName}: o perfil volta para trás na direção do corte; o desbaste usa a parte que avança (o comando daria alarme).");
        }

        double a0 = sa * A(a);
        double d0 = sd * D(a);
        double deepest = normalized.Min(p => p.D);
        if (d0 <= deepest + 1e-9)
        {
            Error(line, 0, 0, $"{cycleName}: o ponto inicial está dentro do perfil.");
            return;
        }

        Point3 Real(double an, double dn) => depthIsX
            ? new Point3(sd * dn, a.Y, sa * an)
            : new Point3(sa * an, a.Y, sd * dn);

        double savedFeed = _feed;
        _feed = feed;
        bool savedThreading = _threading;
        _threading = false;

        foreach (RoughingStep step in PlanRoughing(normalized, a0, d0, depthStep, retract))
        {
            AddLine(line, Real(step.A, step.D), step.Rapid ? MotionMode.Rapid : MotionMode.Linear);
        }

        if (contourPass)
        {
            // Passe final seguindo o perfil (deixa só o sobremetal)
            Point3 first = profile[0].Start;
            AddLine(line, depthIsX ? new Point3(first.X, a.Y, _position.Z) : new Point3(_position.X, a.Y, first.Z), MotionMode.Rapid);
            AddLine(line, first, MotionMode.Rapid);
            foreach (Move move in profile)
            {
                AddMove(move with { Feed = feed, LineNumber = line, Tool = _activeTool });
                _position = move.End;
            }
        }

        // Volta ao ponto inicial: primeiro sai do material (profundidade), depois o outro eixo
        AddLine(line, Real(sa * A(_position), d0), MotionMode.Rapid);
        AddLine(line, a, MotionMode.Rapid);

        _feed = savedFeed;
        _threading = savedThreading;
    }

    internal readonly record struct RoughingStep(double A, double D, bool Rapid);

    /// <summary>
    /// Passes de desbaste no sistema normalizado: o perfil começa no ponto mais fundo e
    /// avança em +a; o material fica acima (d maior), até o nível d0 do ponto inicial.
    ///
    /// Em cada nível, o corte vai por todos os trechos em que o perfil está abaixo do nível.
    /// Sem rebaixo há um trecho só (tipo I): rápido até o nível, corte até o perfil, recuo a 45°
    /// e volta. Com rebaixo (tipo II) há mais trechos: depois de cada um a ferramenta recua,
    /// passa por cima do ressalto e mergulha no rebaixo seguinte com avanço de corte.
    /// </summary>
    internal static List<RoughingStep> PlanRoughing(IReadOnlyList<(double A, double D)> profile,
        double a0, double d0, double depthStep, double retract)
    {
        var steps = new List<RoughingStep>();
        double deepest = profile.Min(p => p.D);
        double previousLevel = d0;
        double level = d0 - depthStep;

        for (int guard = 0; level > deepest + 1e-6 && guard < 100_000; guard++)
        {
            List<(double Start, double End)> spans = SpansBelow(profile, level, a0);
            double current = a0; // a ferramenta está em a0, acima do nível anterior
            bool atStart = true;

            // Altura segura sobre um ressalto: o primeiro nível já desbastado acima dele
            // (abaixo desse nível, em cima do ressalto, ainda há material para o passe de contorno)
            double SafeAbove(double height)
            {
                double v = level;
                while (v <= height + 1e-9 && v < d0 - 1e-9)
                {
                    v += depthStep;
                }

                return Math.Min(v, d0) + retract;
            }

            foreach ((double start, double end) in spans)
            {
                if (atStart && start <= a0 + 1e-6)
                {
                    steps.Add(new RoughingStep(a0, level, true)); // entra pelo lado livre (face)
                }
                else
                {
                    // Sobe acima do ressalto, atravessa e mergulha no rebaixo com avanço de corte
                    double clear = SafeAbove(MaxBetween(profile, current, start));
                    steps.Add(new RoughingStep(current, clear, true));
                    steps.Add(new RoughingStep(start, clear, true));
                    double approach = Math.Min(clear, previousLevel + retract);
                    if (approach < clear - 1e-9)
                    {
                        steps.Add(new RoughingStep(start, approach, true));
                    }

                    steps.Add(new RoughingStep(start, level, false));
                }

                steps.Add(new RoughingStep(end, level, false));
                current = Math.Max(start, end - retract);
                steps.Add(new RoughingStep(current, level + retract, false));
                atStart = false;
            }

            if (!atStart)
            {
                // Volta ao a0 por cima de qualquer ressalto do caminho
                double back = SafeAbove(MaxBetween(profile, a0, current));
                if (back > level + retract + 1e-9)
                {
                    steps.Add(new RoughingStep(current, back, true));
                }

                steps.Add(new RoughingStep(a0, back, true));
            }

            previousLevel = level;
            level -= depthStep;
        }

        return steps;
    }

    /// <summary>Trechos [início, fim] em "a" onde o perfil fica abaixo do nível (material a remover).</summary>
    internal static List<(double Start, double End)> SpansBelow(IReadOnlyList<(double A, double D)> profile, double level, double a0)
    {
        var spans = new List<(double, double)>();
        bool below = profile[0].D < level;
        double start = Math.Min(a0, profile[0].A);

        for (int i = 1; i < profile.Count; i++)
        {
            (double a1, double d1) = profile[i - 1];
            (double a2, double d2) = profile[i];
            if (a2 < a1 - 1e-9)
            {
                continue; // trecho que volta para trás: ignorado
            }

            if (below && d2 >= level)
            {
                spans.Add((start, Cross(a1, d1, a2, d2, level)));
                below = false;
            }
            else if (!below && d2 < level)
            {
                start = Cross(a1, d1, a2, d2, level);
                below = true;
            }
        }

        if (below)
        {
            spans.Add((start, profile[^1].A));
        }

        spans.RemoveAll(s => s.Item2 - s.Item1 < 1e-6);
        return spans;
    }

    private static double Cross(double a1, double d1, double a2, double d2, double level) =>
        Math.Abs(d2 - d1) < 1e-12 ? a2 : a1 + (a2 - a1) * (level - d1) / (d2 - d1);

    /// <summary>Maior altura do perfil entre dois valores de "a" (inclui os pontos das pontas).</summary>
    private static double MaxBetween(IReadOnlyList<(double A, double D)> profile, double from, double to)
    {
        if (to < from)
        {
            (from, to) = (to, from);
        }

        double max = double.MinValue;
        for (int i = 1; i < profile.Count; i++)
        {
            (double a1, double d1) = profile[i - 1];
            (double a2, double d2) = profile[i];
            if (a2 < from || a1 > to || a2 < a1)
            {
                continue;
            }

            // Altura do segmento recortado ao intervalo [from, to]
            double lo = Math.Max(a1, from);
            double hi = Math.Min(a2, to);
            double span = a2 - a1;
            double dLo = span < 1e-12 ? Math.Max(d1, d2) : d1 + (d2 - d1) * (lo - a1) / span;
            double dHi = span < 1e-12 ? Math.Max(d1, d2) : d1 + (d2 - d1) * (hi - a1) / span;
            max = Math.Max(max, Math.Max(dLo, dHi));
        }

        return max;
    }

    /// <summary>O perfil precisa avançar sempre na direção do corte (pode subir e descer).</summary>
    private static bool AdvancesAlongCut(List<(double A, double D)> points)
    {
        for (int i = 1; i < points.Count; i++)
        {
            if (points[i].A < points[i - 1].A - 1e-6)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>O mesmo movimento percorrido ao contrário.</summary>
    internal static Move ReverseMove(Move m)
    {
        if (!m.IsArc)
        {
            return m with { Start = m.End, End = m.Start };
        }

        return m with
        {
            Start = m.End,
            End = m.Start,
            Motion = m.Motion == MotionMode.ClockwiseArc ? MotionMode.CounterClockwiseArc : MotionMode.ClockwiseArc,
            StartAngle = m.StartAngle + m.Sweep,
            Sweep = -m.Sweep,
            StartRadius = m.EndRadius,
            EndRadius = m.StartRadius,
        };
    }

    /// <summary>O mesmo movimento deslocado (sobremetal).</summary>
    internal static Move TranslateMove(Move m, Point3 shift)
    {
        Move moved = m with { Start = m.Start + shift, End = m.End + shift };
        if (!m.IsArc)
        {
            return moved;
        }

        (double du, double dv, _) = m.Plane.ToPlane(shift);
        return moved with { CenterU = m.CenterU + du, CenterV = m.CenterV + dv };
    }
}
