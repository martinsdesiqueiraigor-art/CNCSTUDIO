using CncStudio.Core.Geometry;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Simulation;

/// <summary>
/// Percorre a trajetória removendo material do <see cref="HeightMap"/> com o perfil de cada
/// ferramenta (topo reto, esférica, toroidal ou broca; ver <see cref="ToolProfile"/>). Permite avançar por distância (animação suave) ou por linha (passo a passo).
///
/// Arcos são convertidos em pontos só quando o movimento começa a ser executado,
/// com tolerância proporcional à célula da grade (não adianta ser mais fino que isso).
///
/// Não é thread-safe: a UI deve chamar sempre a partir da mesma thread de simulação.
/// </summary>
public sealed class MaterialSimulator : IMaterialSimulation
{
    private readonly HeightMap _map;
    private readonly IReadOnlyList<Move> _moves;
    private readonly Func<int, ToolProfile> _profileOf;
    private ToolProfile _profile = ToolProfile.Flat(10);
    private readonly double _arcTolerance;
    private double _toolRadius;
    private int _tool;

    // Pontos do movimento atual (começando no ponto inicial) e posição dentro deles
    private readonly List<Point3> _polyline = [];
    private int _moveIndex;
    private int _pieceIndex;
    private double _distanceInPiece;

    /// <summary>Simulação com o mesmo diâmetro para todas as ferramentas.</summary>
    public MaterialSimulator(HeightMap map, IReadOnlyList<Move> moves, double toolDiameter)
        : this(map, moves, _ => toolDiameter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(toolDiameter, 0);
    }

    /// <summary>Simulação com diâmetro por ferramenta (número T → diâmetro em mm), todas de topo reto.</summary>
    public MaterialSimulator(HeightMap map, IReadOnlyList<Move> moves, Func<int, double> diameterOfTool)
        : this(map, moves, tool => ToolProfile.Flat(diameterOfTool(tool)))
    {
    }

    /// <summary>Simulação com o perfil de cada ferramenta (número T → perfil).</summary>
    public MaterialSimulator(HeightMap map, IReadOnlyList<Move> moves, Func<int, ToolProfile> profileOfTool)
    {
        _map = map;
        _moves = moves;
        _profileOf = profileOfTool;
        _arcTolerance = Math.Max(0.001, map.CellSize * 0.25);
        Reset();
    }

    /// <summary>Diâmetro da ferramenta do movimento atual.</summary>
    public double CurrentToolDiameter => _toolRadius * 2;

    /// <summary>Perfil da ferramenta do movimento atual.</summary>
    public ToolProfile CurrentToolProfile => _profile;

    /// <summary>Número da ferramenta do movimento atual (0 = não definida).</summary>
    public int CurrentTool => _moves.Count == 0 ? 0 : _moves[Math.Min(_moveIndex, _moves.Count - 1)].Tool;

    public HeightMap Map => _map;

    /// <summary>Posição atual da ponta da ferramenta.</summary>
    public Point3 ToolPosition { get; private set; }

    /// <summary>Índice do movimento em execução (igual a Moves.Count quando terminou).</summary>
    public int CurrentMoveIndex => _moveIndex;

    public bool IsFinished => _moveIndex >= _moves.Count;

    /// <summary>Linha do arquivo em execução (0 se não há movimentos).</summary>
    public int CurrentLineNumber =>
        _moves.Count == 0 ? 0 : _moves[Math.Min(_moveIndex, _moves.Count - 1)].LineNumber;

    /// <summary>Volta ao início: bruto inteiro e ferramenta no primeiro ponto.</summary>
    public void Reset()
    {
        _map.Reset();
        _moveIndex = 0;
        Travelled = 0;
        _rapidCollisions.Clear();
        ToolPosition = _moves.Count > 0 ? _moves[0].Start : Point3.Origin;
        LoadCurrentMove();
    }

    /// <summary>Avança a ferramenta "distance" mm ao longo da trajetória, removendo material.</summary>
    public void Advance(double distance)
    {
        while (distance > 0 && !IsFinished)
        {
            distance -= AdvanceWithinPiece(distance);
        }
    }

    /// <summary>Executa até o fim do movimento (linha) atual. Usado no botão "passo".</summary>
    public void AdvanceToNextLine()
    {
        int moveIndex = _moveIndex;
        while (!IsFinished && _moveIndex == moveIndex)
        {
            AdvanceWithinPiece(double.PositiveInfinity);
        }
    }

    /// <summary>
    /// Leva a simulação ao início do movimento "moveIndex" (0 = nada executado, Count = tudo),
    /// com o material removido até ali. Para voltar atrás recomeça do bruto inteiro.
    /// Usado pela régua de reprodução e pela navegação por passes.
    /// </summary>
    public void RunToMove(int moveIndex)
    {
        moveIndex = Math.Clamp(moveIndex, 0, _moves.Count);
        bool midMove = _pieceIndex > 0 || _distanceInPiece > 0;
        if (moveIndex < _moveIndex || (moveIndex == _moveIndex && midMove))
        {
            Reset();
        }

        while (_moveIndex < moveIndex)
        {
            AdvanceWithinPiece(double.PositiveInfinity);
        }
    }

    private readonly List<int> _rapidCollisions = [];

    /// <summary>Movimentos G00 que removeram material até agora (colisões), em ordem.</summary>
    public IReadOnlyList<int> RapidCollisions => _rapidCollisions;

    /// <summary>Distância percorrida pela ferramenta desde o início (mm), rápidos inclusive.</summary>
    public double Travelled { get; private set; }

    /// <summary>Movimento em execução (ou o último, quando terminou); null sem movimentos.</summary>
    public Move? CurrentMove => _moves.Count == 0 ? null : _moves[Math.Min(_moveIndex, _moves.Count - 1)];

    /// <summary>
    /// Executa todos os movimentos da linha atual do programa (um ciclo inteiro, por exemplo)
    /// e para no começo da próxima linha. Usado na execução "linha a linha".
    /// </summary>
    public void AdvanceLine()
    {
        if (IsFinished)
        {
            return;
        }

        int line = _moves[_moveIndex].LineNumber;
        while (!IsFinished && _moves[_moveIndex].LineNumber == line)
        {
            AdvanceWithinPiece(double.PositiveInfinity);
        }
    }

    /// <summary>Muda a cada remoção de material.</summary>
    public int Version => _map.Version;

    /// <inheritdoc cref="IMaterialSimulation.AdvanceToward"/>
    public bool AdvanceToward(int moveIndex, double maxDistance)
    {
        moveIndex = Math.Clamp(moveIndex, 0, _moves.Count);
        if (moveIndex < _moveIndex || (moveIndex == _moveIndex && (_pieceIndex > 0 || _distanceInPiece > 0)))
        {
            Reset();
        }

        while (_moveIndex < moveIndex && maxDistance > 0)
        {
            maxDistance -= AdvanceWithinPiece(maxDistance);
        }

        return _moveIndex >= moveIndex;
    }

    /// <summary>Executa a trajetória inteira de uma vez.</summary>
    public void RunToEnd()
    {
        while (!IsFinished)
        {
            AdvanceWithinPiece(double.PositiveInfinity);
        }
    }

    /// <summary>
    /// Avança no máximo até o fim do trecho reto atual, cortando o material.
    /// Retorna a distância efetivamente percorrida.
    /// </summary>
    private double AdvanceWithinPiece(double maxDistance)
    {
        Point3 a = _polyline[_pieceIndex];
        Point3 b = _polyline[_pieceIndex + 1];
        double length = a.DistanceTo(b);
        double step = Math.Min(maxDistance, length - _distanceInPiece);
        double from = _distanceInPiece;

        _distanceInPiece += step;
        Travelled += step;
        bool pieceDone = _distanceInPiece >= length - 1e-9;
        Point3 to = pieceDone ? b : Point3.Lerp(a, b, _distanceInPiece / length);

        bool removed = CutPiece(a, b, length, from, pieceDone ? length : _distanceInPiece);
        ToolPosition = to;

        // G00 que remove material = colisão (o bruto informado torna isso confiável)
        if (removed && _moveIndex > 0 && _moves[_moveIndex].IsRapid && (_rapidCollisions.Count == 0 || _rapidCollisions[^1] != _moveIndex))
        {
            _rapidCollisions.Add(_moveIndex);
        }

        if (pieceDone)
        {
            _pieceIndex++;
            _distanceInPiece = 0;
            if (_pieceIndex >= _polyline.Count - 1)
            {
                _moveIndex++;
                LoadCurrentMove();
            }
        }

        return step;
    }

    /// <summary>Converte o movimento atual em pontos (reta = 2 pontos, arco = vários).</summary>
    private void LoadCurrentMove()
    {
        _polyline.Clear();
        _pieceIndex = 0;
        _distanceInPiece = 0;
        if (IsFinished)
        {
            return;
        }

        Move move = _moves[_moveIndex];
        _profile = _profileOf(move.Tool) ?? ToolProfile.Flat(10);
        _toolRadius = _profile.Radius;
        _tool = move.Tool;
        _polyline.Add(move.Start);
        move.AppendPolyline(_polyline, _arcTolerance);
    }

    /// <summary>
    /// Remove material ao longo do trecho reto a→b: "carimba" o perfil da ferramenta em pontos
    /// fixos do trecho, espaçados de no máximo meia célula (interpolando Z, cobre rampas e
    /// mergulhos). Só os pontos entre as distâncias "from" e "to" (desde a), para que andar o
    /// trecho de uma vez ou em fatias dê exatamente o mesmo material (a web simula em fatias).
    /// </summary>
    private bool CutPiece(Point3 a, Point3 b, double length, double from, double to)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double lengthXY = Math.Sqrt(dx * dx + dy * dy);
        int count = Math.Max(1, (int)Math.Ceiling(lengthXY / (_map.CellSize * 0.5)));
        (int first, int last) = StampRange(count, length, from, to);

        bool removed = false;
        for (int k = first; k <= last; k++)
        {
            Point3 p = Point3.Lerp(a, b, (double)k / count);
            removed |= _map.CutProfile(p.X, p.Y, _profile, p.Z, _tool);
        }

        return removed;
    }

    /// <summary>
    /// Pontos k (de 0 a count) de um trecho de comprimento "length" que caem entre as distâncias
    /// "from" e "to". Trecho de comprimento zero: todos.
    /// </summary>
    internal static (int First, int Last) StampRange(int count, double length, double from, double to)
    {
        if (length <= 1e-12)
        {
            return (0, count);
        }

        const double tolerance = 1e-9;
        int first = Math.Max(0, (int)Math.Ceiling(from / length * count - tolerance));
        int last = Math.Min(count, (int)Math.Floor(to / length * count + tolerance));
        return (first, last);
    }
}
