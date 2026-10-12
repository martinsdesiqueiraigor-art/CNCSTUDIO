using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Rendering;
using CncStudio.Core.Trajectory;

namespace CncStudio.Web;

/// <summary>Vista da fresa no backplot web (no torno é sempre o plano ZX).</summary>
public enum MillView
{
    /// <summary>Isométrica: mostra as profundidades (2,5D).</summary>
    Iso,

    /// <summary>Topo (plano XY).</summary>
    Top,

    /// <summary>Frente (plano XZ).</summary>
    Front,
}

/// <summary>
/// Backplot leve para o navegador (só linhas, sem sólido):
///   torno → plano ZX (Z para a direita, X/raio para cima), com a linha de centro;
///   fresa → isométrica 2,5D, topo ou frente.
/// Cores dos movimentos: G00 vermelho tracejado, G01 verde, G02 azul claro, G03 azul escuro.
/// Na simulação, o caminho inteiro fica apagado e o já executado aparece aceso, com a ferramenta.
/// Toque/clique: vai à linha do programa. Arrastar: move. Roda ou pinça com dois dedos: zoom.
/// Duplo toque: ajusta à peça.
/// </summary>
public sealed class BackplotView : Control
{
    /// <summary>Movimentos por bloco da simulação (blocos completos ficam guardados, prontos para desenhar).</summary>
    private const int BlockSize = 256;

    // Mesmas cores do desktop
    private static readonly Color[] MotionColors =
    [
        Color.Parse("#FF4D4D"), // G00
        Color.Parse("#3DDC6A"), // G01
        Color.Parse("#7FD8FF"), // G02
        Color.Parse("#3D6BFF"), // G03
    ];

    private static readonly IBrush[] BrightBrushes = MotionColors.Select(c => (IBrush)new SolidColorBrush(c)).ToArray();
    private static readonly IBrush[] DimBrushes = MotionColors.Select(c => (IBrush)new SolidColorBrush(c, 0.22)).ToArray();
    private static readonly DashStyle RapidDash = new([5, 4], 0);

    private static readonly IBrush Background = new SolidColorBrush(Color.Parse("#0D1117"));
    private static readonly IBrush AxisBrush = new SolidColorBrush(Color.Parse("#6E7681"));
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.Parse("#FFD33D"));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#8B949E"));
    private static readonly IBrush ToolBrush = new SolidColorBrush(Color.Parse("#F0883E"));
    private static readonly IBrush ToolFill = new SolidColorBrush(Color.Parse("#F0883E"), 0.35);

    private ToolPath? _path;
    private bool _lathe;
    private MillView _millView = MillView.Iso;
    private double _arcTolerance = 0.01;

    // Cada movimento já projetado no plano da vista (mm), com o ponto inicial
    private Point[][] _polys = [];
    private StreamGeometry?[] _full = new StreamGeometry?[4];
    private readonly List<StreamGeometry?[]> _blocks = [];
    private StreamGeometry? _highlight;
    private int _highlightLine;
    private int? _visibleMoves;

    // Transformação mundo (mm) → tela: tela = (u * escala + desloc.X, −v * escala + desloc.Y)
    private double _scale = 1;
    private Vector _offset;
    private bool _fitPending = true;
    private bool _userMovedView; // depois que o usuário mexe no zoom, mudar o tamanho não reajusta mais

    // Ponteiros ativos (mouse ou dedos) para arrastar e pinçar
    private readonly Dictionary<int, Point> _pointers = [];
    private Point _pressPoint;
    private bool _moved;
    private double _pinchDistance;
    private DateTime _lastTap = DateTime.MinValue;

    public BackplotView()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    /// <summary>O usuário tocou num movimento: número da linha no editor.</summary>
    public event EventHandler<int>? LineRequested;

    public bool IsLathe => _lathe;

    /// <summary>Vista usada na fresa.</summary>
    public MillView MillView
    {
        get => _millView;
        set
        {
            if (_millView == value)
            {
                return;
            }

            _millView = value;
            if (!_lathe)
            {
                Rebuild();
                Fit();
            }
        }
    }

    /// <summary>
    /// Simulação: quantos movimentos já foram executados (null = sem simulação, tudo aceso).
    /// </summary>
    public int? VisibleMoves
    {
        get => _visibleMoves;
        set
        {
            if (_visibleMoves == value)
            {
                return;
            }

            _visibleMoves = value;
            InvalidateVisual();
        }
    }

    /// <summary>Mostra uma nova trajetória. "keepView" mantém zoom e posição (edição no mesmo programa).</summary>
    public void SetPath(ToolPath? path, bool keepView)
    {
        bool latheChanged = (path?.Machine == MachineKind.Lathe) != _lathe;
        _path = path;
        _lathe = path?.Machine == MachineKind.Lathe;
        _arcTolerance = path is null ? 0.01 : Math.Max(0.002, PathPolylineBuilder.ScreenTolerance(path));
        _visibleMoves = null;
        Rebuild();
        if (!keepView || latheChanged)
        {
            Fit();
        }

        InvalidateVisual();
    }

    /// <summary>Destaca os movimentos de uma linha do programa (linha do cursor no editor).</summary>
    public void HighlightLine(int line)
    {
        if (line == _highlightLine)
        {
            return;
        }

        _highlightLine = line;
        BuildHighlight();
        InvalidateVisual();
    }

    /// <summary>Ajusta o zoom para mostrar a peça inteira.</summary>
    public void Fit()
    {
        _fitPending = true;
        _userMovedView = false;
        InvalidateVisual();
    }

    // ───────────────────────── Geometria (em mm, montada uma vez) ─────────────────────────

    private (double U, double V) Project(Point3 p)
    {
        if (_lathe)
        {
            return (p.Z, p.X);
        }

        return _millView switch
        {
            MillView.Top => ViewCamera.Top.Project(p),
            MillView.Front => ViewCamera.Front.Project(p),
            _ => ViewCamera.Iso.Project(p),
        };
    }

    private Point ProjectPoint(Point3 p)
    {
        (double u, double v) = Project(p);
        return new Point(u, v);
    }

    private void Rebuild()
    {
        _blocks.Clear();
        if (_path is null)
        {
            _polys = [];
            _full = new StreamGeometry?[4];
            BuildHighlight();
            return;
        }

        var points = new List<Point3>();
        _polys = new Point[_path.Moves.Count][];
        for (int i = 0; i < _polys.Length; i++)
        {
            Move move = _path.Moves[i];
            points.Clear();
            points.Add(move.Start);
            move.AppendPolyline(points, _arcTolerance);
            _polys[i] = points.Select(ProjectPoint).ToArray();
        }

        _full = BuildRange(0, _polys.Length);
        BuildHighlight();
    }

    /// <summary>Geometrias (uma por tipo de movimento) dos movimentos [from, to).</summary>
    private StreamGeometry?[] BuildRange(int from, int to)
    {
        var result = new StreamGeometry?[4];
        if (_path is null || from >= to)
        {
            return result;
        }

        var contexts = new StreamGeometryContext?[4];
        var last = new Point?[4]; // último ponto de cada geometria (para não abrir figura nova à toa)

        for (int i = from; i < to; i++)
        {
            int kind = (int)_path.Moves[i].Motion;
            Point[] poly = _polys[i];
            if (contexts[kind] is null)
            {
                result[kind] = new StreamGeometry();
                contexts[kind] = result[kind]!.Open();
            }

            StreamGeometryContext ctx = contexts[kind]!;
            if (last[kind] is not { } previous || previous != poly[0])
            {
                if (last[kind] is not null)
                {
                    ctx.EndFigure(false);
                }

                ctx.BeginFigure(poly[0], false);
            }

            for (int k = 1; k < poly.Length; k++)
            {
                ctx.LineTo(poly[k]);
            }

            last[kind] = poly[^1];
        }

        for (int k = 0; k < 4; k++)
        {
            if (contexts[k] is { } ctx)
            {
                ctx.EndFigure(false);
                ctx.Dispose();
            }
        }

        return result;
    }

    private void BuildHighlight()
    {
        _highlight = null;
        if (_path is null || _highlightLine <= 0)
        {
            return;
        }

        var geometry = new StreamGeometry();
        bool any = false;
        using (StreamGeometryContext ctx = geometry.Open())
        {
            for (int i = 0; i < _polys.Length; i++)
            {
                if (_path.Moves[i].LineNumber != _highlightLine)
                {
                    continue;
                }

                Point[] poly = _polys[i];
                ctx.BeginFigure(poly[0], false);
                for (int k = 1; k < poly.Length; k++)
                {
                    ctx.LineTo(poly[k]);
                }

                ctx.EndFigure(false);
                any = true;
            }
        }

        _highlight = any ? geometry : null;
    }

    // ───────────────────────── Desenho ─────────────────────────

    public override void Render(DrawingContext context)
    {
        Rect bounds = Bounds;
        context.FillRectangle(Background, new Rect(bounds.Size));
        if (bounds.Width < 2 || bounds.Height < 2)
        {
            return;
        }

        if (_fitPending)
        {
            FitNow(bounds.Size);
        }

        if (_path is null || _path.Moves.Count == 0)
        {
            DrawText(context, "Sem trajetória para mostrar", new Point(12, 10));
            return;
        }

        DrawAxes(context, bounds.Size);

        // Mundo → tela: escala com Y invertido. A espessura da caneta é dividida pela escala
        // para continuar com o mesmo tamanho na tela em qualquer zoom.
        var matrix = new Matrix(_scale, 0, 0, -_scale, _offset.X, _offset.Y);
        double px = 1 / _scale;
        using (context.PushTransform(matrix))
        {
            if (_visibleMoves is not { } visible)
            {
                DrawSet(context, _full, BrightBrushes, px);
            }
            else
            {
                // Simulação: tudo apagado + o que já foi executado aceso
                DrawSet(context, _full, DimBrushes, px);
                visible = Math.Clamp(visible, 0, _polys.Length);
                int complete = visible / BlockSize;
                for (int b = 0; b < complete; b++)
                {
                    while (_blocks.Count <= b)
                    {
                        int start = _blocks.Count * BlockSize;
                        _blocks.Add(BuildRange(start, start + BlockSize));
                    }

                    DrawSet(context, _blocks[b], BrightBrushes, px);
                }

                DrawSet(context, BuildRange(complete * BlockSize, visible), BrightBrushes, px);
            }

            if (_highlight is { } h)
            {
                context.DrawGeometry(null, new Pen(HighlightBrush, 3 * px, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), h);
            }
        }

        if (_visibleMoves is { } executed)
        {
            DrawTool(context, executed);
        }
    }

    private static void DrawSet(DrawingContext context, StreamGeometry?[] set, IBrush[] brushes, double px)
    {
        if (set[0] is { } rapid)
        {
            context.DrawGeometry(null, new Pen(brushes[0], 0.9 * px, RapidDash), rapid);
        }

        for (int k = 1; k < 4; k++)
        {
            if (set[k] is { } g)
            {
                context.DrawGeometry(null, new Pen(brushes[k], 1.15 * px, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), g);
            }
        }
    }

    /// <summary>Ferramenta na ponta do último movimento executado.</summary>
    private void DrawTool(DrawingContext context, int executed)
    {
        if (_polys.Length == 0)
        {
            return;
        }

        Point world = executed <= 0 ? _polys[0][0] : _polys[Math.Min(executed, _polys.Length) - 1][^1];
        Point tip = ToScreen(world.X, world.Y);
        var pen = new Pen(ToolBrush, 1.5);

        if (_lathe)
        {
            // Pastilha rômbica com a ponta no ponto programado (acima da peça, lado +X)
            var insert = new StreamGeometry();
            using (StreamGeometryContext ctx = insert.Open())
            {
                ctx.BeginFigure(tip, true);
                ctx.LineTo(tip + new Vector(9, -9));
                ctx.LineTo(tip + new Vector(0, -18));
                ctx.LineTo(tip + new Vector(-9, -9));
                ctx.EndFigure(true);
            }

            context.DrawGeometry(ToolFill, pen, insert);
            return;
        }

        // Fresa: ponta + haste na direção do eixo Z da vista
        Vector up = ScreenDirection(new Point3(0, 0, 1));
        if (up.Length > 0.1)
        {
            context.DrawLine(new Pen(ToolBrush, 6) { LineCap = PenLineCap.Round }, tip, tip + up * 34);
        }

        context.DrawEllipse(ToolFill, pen, tip, 6, 6);
    }

    /// <summary>Direção na tela (unitária) de um eixo do mundo; comprimento 0 se ele aponta para o observador.</summary>
    private Vector ScreenDirection(Point3 axis)
    {
        (double u0, double v0) = Project(new Point3(0, 0, 0));
        (double u1, double v1) = Project(axis);
        var d = new Vector(u1 - u0, -(v1 - v0));
        return d.Length < 1e-6 ? default : d / d.Length;
    }

    private void DrawAxes(DrawingContext context, Size size)
    {
        Point origin = ToScreen(0, 0);
        if (_lathe)
        {
            // Linha de centro (traço-ponto), como no desenho técnico
            context.DrawLine(new Pen(AxisBrush, 1, new DashStyle([10, 3, 2, 3], 0)), new Point(0, origin.Y), new Point(size.Width, origin.Y));
            DrawAxis(context, origin, new Vector(1, 0), "Z");
            DrawAxis(context, origin, new Vector(0, -1), "X");
            return;
        }

        // Fresa: triedro XYZ no zero peça (eixos que apontam para o observador não aparecem)
        DrawAxis(context, origin, ScreenDirection(new Point3(1, 0, 0)), "X");
        DrawAxis(context, origin, ScreenDirection(new Point3(0, 1, 0)), "Y");
        DrawAxis(context, origin, ScreenDirection(new Point3(0, 0, 1)), "Z");
    }

    private static void DrawAxis(DrawingContext context, Point origin, Vector direction, string name)
    {
        if (direction.Length < 0.1)
        {
            return;
        }

        Point end = origin + direction * 28;
        context.DrawLine(new Pen(AxisBrush, 1), origin, end);
        DrawText(context, name, end + direction * 6 + new Vector(-4, -8));
    }

    private static void DrawText(DrawingContext context, string text, Point at)
    {
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface.Default, 12, TextBrush);
        context.DrawText(formatted, at);
    }

    private Point ToScreen(double u, double v) => new(u * _scale + _offset.X, -v * _scale + _offset.Y);

    private (double U, double V) ToWorld(Point p) => ((p.X - _offset.X) / _scale, -(p.Y - _offset.Y) / _scale);

    private void FitNow(Size size)
    {
        _fitPending = false;
        if (_path is null || _path.Moves.Count == 0)
        {
            _scale = 4;
            _offset = new Vector(size.Width / 2, size.Height / 2);
            return;
        }

        // Ajusta pela região de corte (os rápidos de troca de ferramenta vão longe e encolheriam a peça)
        Bounds3 b = _path.CuttingBounds.IsEmpty ? _path.Bounds : _path.CuttingBounds;
        double minU = double.MaxValue, minV = double.MaxValue, maxU = double.MinValue, maxV = double.MinValue;
        foreach (Point3 corner in ViewCamera.Corners(b))
        {
            (double u, double v) = Project(corner);
            minU = Math.Min(minU, u);
            maxU = Math.Max(maxU, u);
            minV = Math.Min(minV, v);
            maxV = Math.Max(maxV, v);
        }

        if (_lathe)
        {
            // A linha de centro sempre aparece
            minV = Math.Min(minV, 0);
        }

        double width = Math.Max(maxU - minU, 1);
        double height = Math.Max(maxV - minV, 1);
        const double margin = 30;
        _scale = Math.Min((size.Width - 2 * margin) / width, (size.Height - 2 * margin) / height);
        _scale = Math.Clamp(_scale, 0.01, 10_000);

        double centerU = (minU + maxU) / 2;
        double centerV = (minV + maxV) / 2;
        _offset = new Vector(size.Width / 2 - centerU * _scale, size.Height / 2 + centerV * _scale);
    }

    private void ZoomAt(Point screen, double factor)
    {
        double newScale = Math.Clamp(_scale * factor, 0.01, 10_000);
        factor = newScale / _scale;
        // Mantém o ponto sob o cursor/dedos parado
        _offset = new Vector(screen.X - (screen.X - _offset.X) * factor, screen.Y - (screen.Y - _offset.Y) * factor);
        _scale = newScale;
        _userMovedView = true;
        InvalidateVisual();
    }

    // ───────────────────────── Interação ─────────────────────────

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        ZoomAt(e.GetPosition(this), e.Delta.Y > 0 ? 1.2 : 1 / 1.2);
        e.Handled = true;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        Point p = e.GetPosition(this);
        _pointers[e.Pointer.Id] = p;
        e.Pointer.Capture(this);

        if (_pointers.Count == 1)
        {
            _pressPoint = p;
            _moved = false;
        }
        else if (_pointers.Count == 2)
        {
            _pinchDistance = PinchDistance();
            _moved = true; // dois dedos nunca viram "toque"
        }

        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_pointers.TryGetValue(e.Pointer.Id, out Point previous))
        {
            return;
        }

        Point p = e.GetPosition(this);
        _pointers[e.Pointer.Id] = p;

        if (_pointers.Count == 1)
        {
            if (!_moved && Distance(p, _pressPoint) < 6)
            {
                return; // tremidinha do dedo não é arrasto
            }

            _moved = true;
            _userMovedView = true;
            _offset += p - previous;
            InvalidateVisual();
        }
        else if (_pointers.Count == 2)
        {
            double distance = PinchDistance();
            if (_pinchDistance > 0 && distance > 0)
            {
                Point[] two = _pointers.Values.Take(2).ToArray();
                var center = new Point((two[0].X + two[1].X) / 2, (two[0].Y + two[1].Y) / 2);
                // Move junto com o centro dos dedos e aplica o zoom da pinça
                _offset += (p - previous) / 2;
                ZoomAt(center, distance / _pinchDistance);
            }

            _pinchDistance = distance;
        }

        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        bool wasSingle = _pointers.Count == 1;
        _pointers.Remove(e.Pointer.Id);
        e.Pointer.Capture(null);

        if (_pointers.Count < 2)
        {
            _pinchDistance = 0;
        }

        if (wasSingle && !_moved)
        {
            Tap(e.GetPosition(this));
        }

        e.Handled = true;
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _pointers.Remove(e.Pointer.Id);
    }

    private void Tap(Point p)
    {
        DateTime now = DateTime.UtcNow;
        if ((now - _lastTap).TotalMilliseconds < 350)
        {
            _lastTap = DateTime.MinValue;
            Fit();
            return;
        }

        _lastTap = now;
        if (_path is null)
        {
            return;
        }

        (double u, double v) = ToWorld(p);
        // Raio de toque generoso (dedo): 14 px na tela
        int index = PathHitTest.NearestMove(_path.Moves, Project, u, v, 14 / _scale, _arcTolerance);
        if (index >= 0)
        {
            LineRequested?.Invoke(this, _path.Moves[index].LineNumber);
        }
    }

    private double PinchDistance()
    {
        if (_pointers.Count < 2)
        {
            return 0;
        }

        Point[] two = _pointers.Values.Take(2).ToArray();
        return Distance(two[0], two[1]);
    }

    private static double Distance(Point a, Point b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        // Tela girou, coluna mudou etc.: reajusta, a menos que o usuário já tenha mexido na vista
        if (e.PreviousSize.Width < 2 || !_userMovedView)
        {
            _fitPending = true;
            InvalidateVisual();
        }
    }
}
