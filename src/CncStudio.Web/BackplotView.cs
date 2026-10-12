using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Trajectory;

namespace CncStudio.Web;

/// <summary>
/// Backplot 2D leve para o navegador:
///   torno → plano ZX (Z para a direita, X/raio para cima), com a linha de centro;
///   fresa → vista de topo XY.
/// Cores dos movimentos: G00 vermelho tracejado, G01 verde, G02 azul claro, G03 azul escuro.
/// Toque/clique: vai à linha do programa. Arrastar: move. Roda ou pinça com dois dedos: zoom.
/// Duplo toque: ajusta à peça.
/// </summary>
public sealed class BackplotView : Control
{
    // Mesmas cores do desktop
    private static readonly IPen RapidPen = new Pen(new SolidColorBrush(Color.Parse("#FF4D4D")), 1, new DashStyle([4, 3], 0));
    private static readonly IBrush[] MotionBrushes =
    [
        new SolidColorBrush(Color.Parse("#FF4D4D")), // G00
        new SolidColorBrush(Color.Parse("#3DDC6A")), // G01
        new SolidColorBrush(Color.Parse("#7FD8FF")), // G02
        new SolidColorBrush(Color.Parse("#3D6BFF")), // G03
    ];

    private static readonly IBrush Background = new SolidColorBrush(Color.Parse("#0D1117"));
    private static readonly IBrush AxisBrush = new SolidColorBrush(Color.Parse("#6E7681"));
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.Parse("#FFD33D"));
    private static readonly IBrush TextBrush = new SolidColorBrush(Color.Parse("#8B949E"));

    private ToolPath? _path;
    private bool _lathe;
    private StreamGeometry?[] _geometries = new StreamGeometry?[4];
    private StreamGeometry? _highlight;
    private int _highlightLine;
    private double _arcTolerance = 0.01;

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

    /// <summary>Mostra uma nova trajetória. "keepView" mantém zoom e posição (edição no mesmo programa).</summary>
    public void SetPath(ToolPath? path, bool keepView)
    {
        bool latheChanged = (path?.Machine == MachineKind.Lathe) != _lathe;
        _path = path;
        _lathe = path?.Machine == MachineKind.Lathe;
        _arcTolerance = path is null ? 0.01 : Math.Max(0.002, PathPolylineBuilder.ScreenTolerance(path));
        BuildGeometries();
        BuildHighlight();
        if (!keepView || latheChanged)
        {
            _fitPending = true;
            _userMovedView = false;
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

    private (double U, double V) Project(Point3 p) => _lathe ? (p.Z, p.X) : (p.X, p.Y);

    private void BuildGeometries()
    {
        _geometries = new StreamGeometry?[4];
        if (_path is null || _path.Moves.Count == 0)
        {
            return;
        }

        var contexts = new StreamGeometryContext?[4];
        var last = new Point3?[4]; // último ponto de cada geometria (para não abrir figura nova à toa)
        var points = new List<Point3>();

        foreach (Move move in _path.Moves)
        {
            int kind = (int)move.Motion;
            if (contexts[kind] is null)
            {
                _geometries[kind] = new StreamGeometry();
                contexts[kind] = _geometries[kind]!.Open();
            }

            StreamGeometryContext ctx = contexts[kind]!;
            points.Clear();
            move.AppendPolyline(points, _arcTolerance);

            if (last[kind] is not { } previous || previous != move.Start)
            {
                if (last[kind] is not null)
                {
                    ctx.EndFigure(false);
                }

                (double u0, double v0) = Project(move.Start);
                ctx.BeginFigure(new Point(u0, v0), false);
            }

            foreach (Point3 p in points)
            {
                (double u, double v) = Project(p);
                ctx.LineTo(new Point(u, v));
            }

            last[kind] = move.End;
        }

        for (int k = 0; k < 4; k++)
        {
            if (contexts[k] is { } ctx)
            {
                ctx.EndFigure(false);
                ctx.Dispose();
            }
        }
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
        var points = new List<Point3>();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            foreach (Move move in _path.Moves)
            {
                if (move.LineNumber != _highlightLine)
                {
                    continue;
                }

                points.Clear();
                move.AppendPolyline(points, _arcTolerance);
                (double u0, double v0) = Project(move.Start);
                ctx.BeginFigure(new Point(u0, v0), false);
                foreach (Point3 p in points)
                {
                    (double u, double v) = Project(p);
                    ctx.LineTo(new Point(u, v));
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
        using (context.PushTransform(matrix))
        {
            double px = 1 / _scale;
            if (_geometries[0] is { } rapid)
            {
                context.DrawGeometry(null, new Pen(RapidPen.Brush, 1.2 * px, RapidPen.DashStyle), rapid);
            }

            for (int k = 1; k < 4; k++)
            {
                if (_geometries[k] is { } g)
                {
                    context.DrawGeometry(null, new Pen(MotionBrushes[k], 1.8 * px, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), g);
                }
            }

            if (_highlight is { } h)
            {
                context.DrawGeometry(null, new Pen(HighlightBrush, 4 * px, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), h);
            }
        }

        DrawText(context, _lathe ? "Torno · plano ZX" : "Fresa · vista de topo XY", new Point(12, 10));
    }

    private void DrawAxes(DrawingContext context, Size size)
    {
        Point origin = ToScreen(0, 0);
        var axisPen = new Pen(AxisBrush, 1);
        if (_lathe)
        {
            // Linha de centro (traço-ponto), como no desenho técnico
            context.DrawLine(new Pen(AxisBrush, 1, new DashStyle([10, 3, 2, 3], 0)), new Point(0, origin.Y), new Point(size.Width, origin.Y));
        }

        // Zero peça: pequena cruz com os nomes dos eixos
        context.DrawLine(axisPen, origin, origin + new Vector(28, 0));
        context.DrawLine(axisPen, origin, origin + new Vector(0, -28));
        DrawText(context, _lathe ? "Z" : "X", origin + new Vector(31, -8));
        DrawText(context, _lathe ? "X" : "Y", origin + new Vector(-4, -45));
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
        foreach (Point3 corner in new[] { b.Min, b.Max })
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
        const double margin = 28;
        _scale = Math.Min((size.Width - 2 * margin) / width, (size.Height - 2 * margin - 20) / height);
        _scale = Math.Clamp(_scale, 0.01, 10_000);

        double centerU = (minU + maxU) / 2;
        double centerV = (minV + maxV) / 2;
        _offset = new Vector(size.Width / 2 - centerU * _scale, (size.Height + 20) / 2 + centerV * _scale);
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
