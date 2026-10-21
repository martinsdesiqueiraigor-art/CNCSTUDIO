using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CncStudio.App.Views;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Trajectory;

namespace CncStudio.App.Graphics;

/// <summary>
/// Área gráfica única (como no CIMCO): sólido iluminado, trajetória por cima com cores por
/// movimento (G00 vermelho tracejado, G01 verde, G02 azul-claro, G03 azul-escuro), ferramenta
/// com suporte (ou inserto, no torno), medição e clique para ir à linha.
///
/// O que já foi executado fica colorido; o que falta, apagado. A geometria só é refeita
/// quando dados, vista ou posição mudam; o zoom muda apenas a transformação.
/// </summary>
public sealed class GraphicsView : ZoomPanControl
{
    private static Pen MakePen(string color, double width, IDashStyle? dash = null) =>
        new(new SolidColorBrush(Color.Parse(color)).ToImmutable(), width, dash);

    private static readonly Pen[] MotionPens =
    [
        MakePen("#FF4D4D", 1, new DashStyle([4, 4], 0)),
        MakePen("#3DDC6A", 1.3),
        MakePen("#7FD8FF", 1.5),
        MakePen("#3B6BFF", 1.5),
    ];

    private static readonly Pen GhostPen = MakePen("#4A505A", 1);
    private static readonly Pen PassPen = new(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)).ToImmutable(), 6,
        lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private static readonly Pen HighlightPen = MakePen("#FFE84D", 3);
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.Parse("#FFE84D")).ToImmutable();
    private static readonly Pen StockPen = MakePen("#6A727C", 1);
    private static readonly Pen AxisLinePen = MakePen("#7A828C", 1, new DashStyle([8, 4, 2, 4], 0));
    private static readonly Pen MeasurePen = new(Brushes.White, 1.2, new DashStyle([5, 3], 0));
    private static readonly IBrush TextBackground = new SolidColorBrush(Color.FromArgb(210, 24, 26, 31)).ToImmutable();
    private static readonly Pen AxisXPen = MakePen("#E05050", 1.5);
    private static readonly Pen AxisYPen = MakePen("#50C050", 1.5);
    private static readonly Pen AxisZPen = MakePen("#5090F0", 1.5);

    // Ferramenta: corpo dourado, suporte cinza, inserto do torno amarelo
    private static readonly Pen ToolOutline = MakePen("#5A4010", 1);
    private static readonly Pen HolderOutline = MakePen("#3A3F46", 1);
    private static readonly IBrush InsertBrush = new SolidColorBrush(Color.Parse("#F2C230")).ToImmutable();
    private static readonly IBrush LatheHolderBrush = new SolidColorBrush(Color.Parse("#4A6FA5")).ToImmutable();

    // Perfil do torno no plano ZX
    private static readonly IBrush ProfileStockBrush = new SolidColorBrush(Color.Parse("#B89B6E")).ToImmutable();
    private static readonly IBrush ProfileMachinedBrush = new SolidColorBrush(Color.Parse("#8FA3B8")).ToImmutable();
    private static readonly IBrush ProfileMirrorBrush = new SolidColorBrush(Color.FromArgb(70, 143, 163, 184)).ToImmutable();

    private readonly record struct MeasurePoint(Point View, Point3? Actual);

    private ToolPath? _path;
    private IReadOnlyList<PathPolyline> _figures = [];
    private GraphicsViewKind _view = GraphicsViewKind.Iso;
    private GraphicsFrame? _frame;
    private WriteableBitmap? _bitmap;
    private int _highlightedLine;
    private int? _limit;
    private (int Start, int End)? _currentPass;
    private readonly List<MeasurePoint> _measure = [];
    private bool _measureMode;

    private readonly StreamGeometry?[] _fullGeometry = new StreamGeometry?[4];
    private readonly StreamGeometry?[] _doneGeometry = new StreamGeometry?[4];
    private StreamGeometry? _passGeometry;
    private StreamGeometry? _highlightGeometry;
    private Point? _highlightStart;
    private Rect? _pathBounds;

    /// <summary>Clicou num movimento (fora do modo de medição): índice na trajetória.</summary>
    public event EventHandler<int>? MoveClicked;

    /// <summary>A medição mudou (texto em <see cref="MeasureText"/>).</summary>
    public event EventHandler? MeasureChanged;

    public string MeasureText { get; private set; } = "";

    public bool ShowSolid { get; set; } = true;
    public bool ShowPath { get; set; } = true;

    /// <summary>Desenhar os avanços rápidos (G00, tracejados) junto da trajetória.</summary>
    public bool ShowRapids { get; set; } = true;

    /// <summary>Torno 3D por CPU com 1/4 removido: a legenda da vista segue a opção "Meia seção".</summary>
    public bool HalfSection { get; set; } = true;
    public bool ShowTool { get; set; } = true;

    public GraphicsViewKind View
    {
        get => _view;
        set
        {
            if (_view == value)
            {
                return;
            }

            _view = value;
            _measure.Clear();
            MeasureText = _measureMode ? BuildMeasureText() : "";
            RebuildAll();
            FitToContent();
        }
    }

    public bool MeasureMode
    {
        get => _measureMode;
        set
        {
            _measureMode = value;
            _measure.Clear();
            MeasureText = value ? BuildMeasureText() : "";
            InvalidateVisual();
        }
    }

    /// <summary>Nova trajetória (figuras já prontas).</summary>
    public void SetToolPath(ToolPath? path, IReadOnlyList<PathPolyline> figures)
    {
        _path = path;
        _figures = figures;
        _limit = null;
        _currentPass = null;
        _frame = null;
        _measure.Clear();
        RebuildAll();
        InvalidateVisual();
    }

    /// <summary>Quantos movimentos já foram executados (null = tudo colorido) e o passe atual.</summary>
    public void SetProgress(int? limit, (int Start, int End)? currentPass)
    {
        if (_limit == limit && _currentPass == currentPass)
        {
            return;
        }

        _limit = limit;
        _currentPass = currentPass;
        RebuildProgress();
        RebuildHighlight();
        InvalidateVisual();
    }

    public void SetHighlightedLine(int lineNumber)
    {
        if (_highlightedLine == lineNumber)
        {
            return;
        }

        _highlightedLine = lineNumber;
        RebuildHighlight();
        InvalidateVisual();
    }

    /// <summary>Mostra um quadro da simulação (sólido e ferramenta). "fit" reenquadra.</summary>
    public void ShowFrame(GraphicsFrame frame, bool fit)
    {
        _frame = frame;
        if (frame.Image is { } image)
        {
            CopyToBitmap(ref _bitmap, image.Pixels, image.Width, image.Height);
        }

        if (fit)
        {
            FitToContent();
        }
        else
        {
            InvalidateVisual();
        }
    }

    public void Redraw() => InvalidateVisual();

    protected override Rect? ContentBounds
    {
        get
        {
            Rect? bounds = _pathBounds;
            if (_frame is { } f && !f.StockBounds.IsEmpty)
            {
                Rect stock = _view.ProjectBounds(LatheAware(f.StockBounds));
                bounds = bounds is { } b ? b.Union(stock) : stock;
            }

            return bounds;
        }
    }

    /// <summary>No torno, o bruto gira: a caixa vai de −raio a +raio em X e Y (3D) ou 0 a raio (ZX).</summary>
    private Bounds3 LatheAware(Bounds3 b)
    {
        if (!_view.IsLathe())
        {
            return b;
        }

        double r = Math.Max(Math.Abs(b.Max.X), Math.Abs(b.Min.X));
        return _view == GraphicsViewKind.Lathe3D
            ? Bounds3.Empty.Include(new Point3(-r, -r, b.Min.Z)).Include(new Point3(r, r, b.Max.Z))
            : Bounds3.Empty.Include(new Point3(-r, 0, b.Min.Z)).Include(new Point3(r, 0, b.Max.Z));
    }

    // ───────────────────────── Desenho ─────────────────────────

    protected override void RenderContent(DrawingContext context)
    {
        if (_path is null || _figures.Count == 0)
        {
            DrawLabel(context, "Sem movimentos para mostrar", new Point(12, 12), LabelBrush);
            return;
        }

        var transform = new MatrixTransform(WorldToScreen);

        // 1. Sólido (imagem 3D) ou perfil do torno no plano ZX
        if (ShowSolid && _frame is { } f)
        {
            if (f.Image is { } image && _bitmap is not null && f.View == _view)
            {
                Rect dest = WorldRect(image.MinU, image.MaxV - image.HeightMm, image.MinU + image.WidthMm, image.MaxV);
                context.DrawImage(_bitmap, new Rect(0, 0, image.Width, image.Height), dest);
            }
            else if (_view == GraphicsViewKind.LatheZX && f.Profile is { } profile)
            {
                DrawProfile(context, profile);
            }
        }
        else if (_frame is { } stockFrame && !stockFrame.StockBounds.IsEmpty)
        {
            DrawStockOutline(context, stockFrame.StockBounds);
        }

        if (_view.IsLathe())
        {
            Point a = ToScreenPoint(_view.Project(new Point3(0, 0, _frame?.StockBounds.Min.Z - 5 ?? -50)));
            Point b = ToScreenPoint(_view.Project(new Point3(0, 0, (_frame?.StockBounds.Max.Z ?? 0) + 10)));
            context.DrawLine(AxisLinePen, a, b); // eixo de rotação
        }

        DrawAxes(context);

        // 2. Trajetória: apagada + parte executada colorida (ou toda colorida)
        if (ShowPath)
        {
            bool stepping = _limit.HasValue;
            for (int i = ShowRapids ? 0 : 1; i < 4; i++)
            {
                if (_fullGeometry[i] is { } full)
                {
                    full.Transform = transform;
                    context.DrawGeometry(null, stepping ? GhostPen : MotionPens[i], full);
                }
            }

            if (stepping)
            {
                if (_passGeometry is not null)
                {
                    _passGeometry.Transform = transform;
                    context.DrawGeometry(null, PassPen, _passGeometry);
                }

                for (int i = ShowRapids ? 0 : 1; i < 4; i++)
                {
                    if (_doneGeometry[i] is { } done)
                    {
                        done.Transform = transform;
                        context.DrawGeometry(null, MotionPens[i], done);
                    }
                }
            }

            if (_highlightGeometry is not null && _highlightStart is { } start)
            {
                _highlightGeometry.Transform = transform;
                context.DrawGeometry(null, HighlightPen, _highlightGeometry);
                context.DrawEllipse(HighlightBrush, null, ToScreen(start.X, start.Y), 4, 4);
            }
        }

        // 3. Ferramenta
        if (ShowTool && _frame is { } tf)
        {
            if (_view.IsLathe())
            {
                DrawLatheTool(context, tf);
            }
            else
            {
                DrawMillTool(context, tf);
            }
        }

        if (_measureMode)
        {
            DrawMeasure(context);
        }

        DrawLabel(context, $"{_view.Caption(HalfSection)}  ·  roda = zoom  ·  arrastar = mover  ·  duplo clique = ajustar  ·  clique = ir à linha",
            new Point(8, Bounds.Height - 20), LabelBrush, 11);
    }

    private Point ToScreenPoint(Point world) => ToScreen(world.X, world.Y);

    private Rect WorldRect(double u0, double v0, double u1, double v1) => new(ToScreen(u0, v1), ToScreen(u1, v0));

    private void DrawAxes(DrawingContext context)
    {
        Point origin = ToScreen(0, 0);
        foreach ((string name, double u, double v) in _view.Axes())
        {
            double length = Math.Sqrt(u * u + v * v);
            var end = new Point(origin.X + u / length * 40, origin.Y - v / length * 40);
            Pen pen = name switch { "X" => AxisXPen, "Y" => AxisYPen, _ => AxisZPen };
            context.DrawLine(pen, origin, end);
            DrawLabel(context, name, new Point(end.X + 3, end.Y - 14), pen.Brush!);
        }
    }

    /// <summary>Contorno do bruto quando o sólido está escondido.</summary>
    private void DrawStockOutline(DrawingContext context, Bounds3 b)
    {
        if (_view.IsLathe())
        {
            double r = Math.Max(Math.Abs(b.Max.X), Math.Abs(b.Min.X));
            Point p0 = ToScreenPoint(_view.Project(new Point3(r, 0, b.Min.Z)));
            Point p1 = ToScreenPoint(_view.Project(new Point3(r, 0, b.Max.Z)));
            Point p2 = ToScreenPoint(_view.Project(new Point3(-r, 0, b.Max.Z)));
            Point p3 = ToScreenPoint(_view.Project(new Point3(-r, 0, b.Min.Z)));
            DrawPolygon(context, null, StockPen, [p0, p1, p2, p3]);
            return;
        }

        Point3[] c = ViewCornersOf(b);
        int[,] edges = { { 0, 1 }, { 1, 2 }, { 2, 3 }, { 3, 0 }, { 4, 5 }, { 5, 6 }, { 6, 7 }, { 7, 4 }, { 0, 4 }, { 1, 5 }, { 2, 6 }, { 3, 7 } };
        for (int i = 0; i < edges.GetLength(0); i++)
        {
            context.DrawLine(StockPen, ToScreenPoint(_view.Project(c[edges[i, 0]])), ToScreenPoint(_view.Project(c[edges[i, 1]])));
        }
    }

    private static Point3[] ViewCornersOf(Bounds3 s) =>
    [
        new(s.Min.X, s.Min.Y, s.Min.Z), new(s.Max.X, s.Min.Y, s.Min.Z), new(s.Max.X, s.Max.Y, s.Min.Z), new(s.Min.X, s.Max.Y, s.Min.Z),
        new(s.Min.X, s.Min.Y, s.Max.Z), new(s.Max.X, s.Min.Y, s.Max.Z), new(s.Max.X, s.Max.Y, s.Max.Z), new(s.Min.X, s.Max.Y, s.Max.Z),
    ];

    /// <summary>Torno no plano ZX: perfil da peça (metade de cima cheia, de baixo espelhada e clara).</summary>
    private void DrawProfile(DrawingContext context, TurningProfile p)
    {
        for (int i = 0; i < p.Outer.Length; i++)
        {
            float outer = p.Outer[i], inner = p.Inner[i];
            if (outer <= inner)
            {
                continue;
            }

            double z0 = p.MinZ + i * p.SliceLength, z1 = z0 + p.SliceLength;
            IBrush brush = outer < p.InitialOuter - 1e-4 ? ProfileMachinedBrush : ProfileStockBrush;
            context.FillRectangle(brush, WorldRect(z0, inner, z1, outer));
            context.FillRectangle(ProfileMirrorBrush, WorldRect(z0, -outer, z1, -inner));
        }
    }

    /// <summary>Fresa com suporte: cilindros sombreados ao longo de Z, na projeção da vista.</summary>
    private void DrawMillTool(DrawingContext context, GraphicsFrame f)
    {
        Point3 tip = f.ToolPosition;
        double radius = Math.Max(0.3, f.ToolDiameter / 2);
        double length = Math.Max(20, f.ToolDiameter * 3);
        double holderRadius = Math.Max(radius * 1.6, radius + 5);
        double holderLength = Math.Max(15, f.ToolDiameter * 1.5);

        Point3 toolTop = new(tip.X, tip.Y, tip.Z + length);
        Point3 holderTop = new(tip.X, tip.Y, tip.Z + length + holderLength);

        DrawCylinder(context, toolTop, holderTop, holderRadius * 0.85, holderRadius, Color.Parse("#7D858F"), Color.Parse("#C9CED4"), HolderOutline);
        DrawCylinder(context, tip, toolTop, radius, radius, Color.Parse("#B8860B"), Color.Parse("#FFD36B"), ToolOutline);
    }

    /// <summary>Cilindro (ou tronco de cone) entre dois pontos no eixo Z, com sombreado lateral.</summary>
    private void DrawCylinder(DrawingContext context, Point3 bottom, Point3 top, double bottomRadius, double topRadius, Color dark, Color light, Pen outline)
    {
        const int Steps = 28;
        var points = new List<Point>(Steps * 2);
        var topRing = new List<Point>(Steps);
        for (int k = 0; k < Steps; k++)
        {
            double a = 2 * Math.PI * k / Steps;
            points.Add(ToScreenPoint(_view.Project(new Point3(bottom.X + bottomRadius * Math.Cos(a), bottom.Y + bottomRadius * Math.Sin(a), bottom.Z))));
            Point t = ToScreenPoint(_view.Project(new Point3(top.X + topRadius * Math.Cos(a), top.Y + topRadius * Math.Sin(a), top.Z)));
            points.Add(t);
            topRing.Add(t);
        }

        List<Point> hull = ConvexHull(points);
        if (hull.Count < 3)
        {
            return;
        }

        // Degradê atravessando o corpo (escuro-claro-escuro) dá a ideia de cilindro
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative),
            EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(dark, 0),
                new GradientStop(light, 0.35),
                new GradientStop(dark, 1),
            },
        };
        DrawPolygon(context, brush, outline, hull);

        if (_view != GraphicsViewKind.Top)
        {
            DrawPolygon(context, new SolidColorBrush(light), outline, ConvexHull(topRing)); // tampa de cima
        }
    }

    /// <summary>Torno: inserto rômbico 80° na ponta e o porta-ferramenta; broca no eixo.</summary>
    private void DrawLatheTool(DrawingContext context, GraphicsFrame f)
    {
        Point3 tip = f.ToolPosition;
        Point Screen(double x, double z) => ToScreenPoint(_view.Project(new Point3(x, 0, z)));

        if (Math.Abs(tip.X) < 1e-3)
        {
            // Broca/ferramenta no centro: cilindro ao longo de Z
            double r = Math.Max(0.5, f.ToolDiameter / 2);
            DrawPolygon(context, InsertBrush, ToolOutline,
                [Screen(r, tip.Z), Screen(r, tip.Z + 40), Screen(-r, tip.Z + 40), Screen(-r, tip.Z)]);
            return;
        }

        // Inserto: ponta embaixo/à esquerda, lados a 5° e 85° (losango de 80°), lado 8 mm
        double sign = tip.X >= 0 ? 1 : -1;
        const double Side = 8;
        double a1 = 5 * Math.PI / 180, a2 = 85 * Math.PI / 180;
        (double dz1, double dx1) = (Math.Cos(a1) * Side, Math.Sin(a1) * Side);
        (double dz2, double dx2) = (Math.Cos(a2) * Side, Math.Sin(a2) * Side);
        Point p0 = Screen(tip.X, tip.Z);
        Point p1 = Screen(tip.X + sign * dx1, tip.Z + dz1);
        Point p2 = Screen(tip.X + sign * (dx1 + dx2), tip.Z + dz1 + dz2);
        Point p3 = Screen(tip.X + sign * dx2, tip.Z + dz2);

        // Porta-ferramenta: barra saindo do inserto para longe da peça
        double hx = tip.X + sign * (dx1 + dx2) / 2, hz = tip.Z + (dz1 + dz2) / 2;
        DrawPolygon(context, LatheHolderBrush, HolderOutline,
            [Screen(hx - sign * 2, hz - 6), Screen(hx - sign * 2, hz + 6), Screen(hx + sign * 30, hz + 6), Screen(hx + sign * 30, hz - 6)]);
        DrawPolygon(context, InsertBrush, ToolOutline, [p0, p1, p2, p3]);
    }

    private static void DrawPolygon(DrawingContext context, IBrush? brush, Pen? pen, IReadOnlyList<Point> points)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext g = geometry.Open())
        {
            g.BeginFigure(points[0], true);
            for (int i = 1; i < points.Count; i++)
            {
                g.LineTo(points[i]);
            }

            g.EndFigure(true);
        }

        context.DrawGeometry(brush, pen, geometry);
    }

    /// <summary>Envoltória convexa (monotone chain).</summary>
    private static List<Point> ConvexHull(List<Point> points)
    {
        var sorted = points.Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
        if (sorted.Count < 3)
        {
            return sorted;
        }

        static double Cross(Point o, Point a, Point b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        var hull = new List<Point>();
        foreach (Point p in sorted)
        {
            while (hull.Count >= 2 && Cross(hull[^2], hull[^1], p) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }

        int lower = hull.Count + 1;
        for (int i = sorted.Count - 2; i >= 0; i--)
        {
            Point p = sorted[i];
            while (hull.Count >= lower && Cross(hull[^2], hull[^1], p) <= 0) hull.RemoveAt(hull.Count - 1);
            hull.Add(p);
        }

        hull.RemoveAt(hull.Count - 1);
        return hull;
    }

    // ───────────────────────── Medição e clique ─────────────────────────

    protected override void OnClick(Point screen)
    {
        if (_path is null || _path.Moves.Count == 0)
        {
            return;
        }

        Point world = ToWorld(screen);
        GraphicsViewKind view = _view;
        if (_measureMode)
        {
            if (_measure.Count >= 2)
            {
                _measure.Clear();
            }

            Point3? snapped = PathHitTest.NearestPoint(_path.Moves, point => view.ProjectTuple(point), world.X, world.Y, PixelsToWorld(10));
            _measure.Add(new MeasurePoint(snapped is { } p ? view.Project(p) : world, snapped));
            MeasureText = BuildMeasureText();
            MeasureChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            return;
        }

        int index = PathHitTest.NearestMove(_path.Moves, point => view.ProjectTuple(point), world.X, world.Y,
            PixelsToWorld(6), PathPolylineBuilder.ScreenTolerance(_path));
        if (index >= 0)
        {
            MoveClicked?.Invoke(this, index);
        }
    }

    private string BuildMeasureText()
    {
        bool lathe = _path?.Machine == MachineKind.Lathe;
        string Describe(Point3 p) => lathe ? $"Ø{p.X * 2:0.###} Z{p.Z:0.###}" : $"X{p.X:0.###} Y{p.Y:0.###} Z{p.Z:0.###}";

        if (_measure.Count == 0)
        {
            return "Medir: clique no 1º ponto (encaixa nos pontos da trajetória). Esc sai.";
        }

        MeasurePoint a = _measure[0];
        if (_measure.Count == 1)
        {
            return a.Actual is { } p1 ? $"Ponto 1: {Describe(p1)} — clique no 2º ponto." : "Ponto 1 marcado — clique no 2º ponto.";
        }

        MeasurePoint b = _measure[1];
        if (a.Actual is { } pa && b.Actual is { } pb)
        {
            double dx = pb.X - pa.X, dy = pb.Y - pa.Y, dz = pb.Z - pa.Z;
            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            return lathe
                ? $"Distância {distance:0.###} · ΔZ {dz:0.###} · ΔØ {2 * dx:0.###}   ({Describe(pa)} → {Describe(pb)})"
                : $"Distância {distance:0.###} · ΔX {dx:0.###} · ΔY {dy:0.###} · ΔZ {dz:0.###}";
        }

        if (_view is GraphicsViewKind.Iso or GraphicsViewKind.Lathe3D)
        {
            return "Nesta vista, clique perto dos pontos da trajetória para medir em 3D.";
        }

        (string Name, double U, double V)[] axes = _view.Axes();
        string uName = axes.FirstOrDefault(x => Math.Abs(x.V) < 1e-6).Name ?? "u";
        string vName = axes.FirstOrDefault(x => Math.Abs(x.U) < 1e-6).Name ?? "v";
        double du = b.View.X - a.View.X, dv = b.View.Y - a.View.Y;
        string vText = lathe && vName == "X" ? $"ΔØ {2 * dv:0.###}" : $"Δ{vName} {dv:0.###}";
        return $"Distância no plano {Math.Sqrt(du * du + dv * dv):0.###} · Δ{uName} {du:0.###} · {vText}";
    }

    private void DrawMeasure(DrawingContext context)
    {
        Point? previous = null;
        foreach (MeasurePoint point in _measure)
        {
            Point screen = ToScreen(point.View.X, point.View.Y);
            if (previous is { } p)
            {
                context.DrawLine(MeasurePen, p, screen);
            }

            context.DrawEllipse(null, MeasurePen, screen, 5, 5);
            previous = screen;
        }

        var text = new FormattedText(MeasureText, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 12, Brushes.White);
        context.DrawRectangle(TextBackground, null, new Rect(6, 6, text.Width + 12, text.Height + 8), 4, 4);
        context.DrawText(text, new Point(12, 10));
    }

    // ───────────────────────── Geometria da trajetória ─────────────────────────

    private void RebuildAll()
    {
        for (int i = 0; i < 4; i++)
        {
            var motion = (MotionMode)i;
            _fullGeometry[i] = _figures.Count == 0 ? null : BuildGeometry(_figures.Where(f => f.Motion == motion).Select(f => (f, f.Points.Length)));
        }

        // Enquadra a região de corte (rápidos de troca de ferramenta, ex.: X100 Z100, ficam de fora)
        Bounds3? area = _path is null || _figures.Count == 0 ? null : _path.CuttingBounds.IsEmpty ? _path.Bounds : _path.CuttingBounds;
        _pathBounds = area is { } a ? _view.ProjectBounds(_view.IsLathe() ? LatheAware(a) : a) : null;
        RebuildProgress();
        RebuildHighlight();
    }

    private void RebuildProgress()
    {
        Array.Clear(_doneGeometry);
        _passGeometry = null;
        if (_path is null || _limit is not { } limit)
        {
            return;
        }

        var done = Enumerable.Range(0, 4).Select(_ => new List<(PathPolyline, int)>()).ToArray();
        foreach (PathPolyline figure in _figures)
        {
            if (figure.FirstMove >= limit)
            {
                break;
            }

            int count = figure.PointCountThrough(Math.Min(limit - 1, figure.LastMove));
            if (count >= 2)
            {
                done[(int)figure.Motion].Add((figure, count));
            }
        }

        for (int i = 0; i < 4; i++)
        {
            _doneGeometry[i] = done[i].Count == 0 ? null : BuildGeometry(done[i]);
        }

        if (_currentPass is { } pass && pass.End > pass.Start)
        {
            double tolerance = PathPolylineBuilder.ScreenTolerance(_path);
            int end = Math.Min(pass.End, limit);
            _passGeometry = BuildGeometry(Enumerable.Range(pass.Start, Math.Max(0, end - pass.Start))
                .Select(i => PathPolylineBuilder.Single(_path.Moves[i], i, tolerance))
                .Select(f => (f, f.Points.Length)));
        }
    }

    private void RebuildHighlight()
    {
        _highlightGeometry = null;
        _highlightStart = null;
        if (_path is null || _limit.HasValue)
        {
            return; // durante a execução, o destaque é o passe atual
        }

        IReadOnlyList<int> indices = _path.MovesForLine(_highlightedLine);
        if (indices.Count == 0)
        {
            return;
        }

        double tolerance = PathPolylineBuilder.ScreenTolerance(_path);
        _highlightGeometry = BuildGeometry(indices.Select(i => PathPolylineBuilder.Single(_path.Moves[i], i, tolerance)).Select(f => (f, f.Points.Length)));
        _highlightStart = _view.Project(_path.Moves[indices[0]].Start);
    }

    private StreamGeometry BuildGeometry(IEnumerable<(PathPolyline Figure, int Count)> figures)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            foreach ((PathPolyline figure, int count) in figures)
            {
                context.BeginFigure(_view.Project(figure.Points[0]), false);
                for (int i = 1; i < count; i++)
                {
                    context.LineTo(_view.Project(figure.Points[i]));
                }

                context.EndFigure(false);
            }
        }

        return geometry;
    }

    /// <summary>Copia pixels BGRA para o bitmap (recria se o tamanho mudou).</summary>
    private static unsafe void CopyToBitmap(ref WriteableBitmap? bitmap, uint[] pixels, int width, int height)
    {
        if (bitmap is null || bitmap.PixelSize.Width != width || bitmap.PixelSize.Height != height)
        {
            bitmap?.Dispose();
            bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        }

        using ILockedFramebuffer buffer = bitmap.Lock();
        int stride = buffer.RowBytes / 4;
        var destination = new Span<uint>((void*)buffer.Address, stride * height);
        for (int row = 0; row < height; row++)
        {
            pixels.AsSpan(row * width, width).CopyTo(destination.Slice(row * stride, width));
        }
    }
}
