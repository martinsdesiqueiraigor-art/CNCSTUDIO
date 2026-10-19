using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Rendering;
using CncStudio.Core.Setup;
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

    /// <summary>Cores de um tema (escuro ou claro).</summary>
    private sealed record Palette(Color[] Motion, Color Background, Color Axis, Color Highlight, Color Text, Color Tool)
    {
        public IBrush[] Bright { get; } = Motion.Select(c => (IBrush)new SolidColorBrush(c)).ToArray();
        public IBrush[] Dim { get; } = Motion.Select(c => (IBrush)new SolidColorBrush(c, 0.22)).ToArray();
        public IBrush BackgroundBrush { get; } = new SolidColorBrush(Background);
        public IBrush AxisBrush { get; } = new SolidColorBrush(Axis);
        public IBrush HighlightBrush { get; } = new SolidColorBrush(Highlight);
        public IBrush TextBrush { get; } = new SolidColorBrush(Text);
        public IBrush ToolBrush { get; } = new SolidColorBrush(Tool);
    }

    // Tema escuro: mesmas cores do desktop (G00 vermelho, G01 verde, G02 azul claro, G03 azul escuro)
    private static readonly Palette DarkPalette = new(
        [Color.Parse("#FF4D4D"), Color.Parse("#3DDC6A"), Color.Parse("#7FD8FF"), Color.Parse("#3D6BFF")],
        Color.Parse("#0D1117"), Color.Parse("#6E7681"), Color.Parse("#FFD33D"), Color.Parse("#8B949E"), Color.Parse("#F0883E"));

    // Tema claro: mesmas ideias, tons mais fortes para fundo branco (bom sob luz forte na oficina)
    private static readonly Palette LightPalette = new(
        [Color.Parse("#D62828"), Color.Parse("#16A34A"), Color.Parse("#0891B2"), Color.Parse("#2547D0")],
        Color.Parse("#F6F8FA"), Color.Parse("#8C959F"), Color.Parse("#E5A000"), Color.Parse("#57606A"), Color.Parse("#E8590C"));

    private static Palette _palette = DarkPalette;
    private static readonly DashStyle RapidDash = new([5, 4], 0);

    /// <summary>Usa as cores do tema claro.</summary>
    public bool LightTheme
    {
        get => _palette == LightPalette;
        set
        {
            _palette = value ? LightPalette : DarkPalette;
            InvalidateVisual();
        }
    }

    private ToolPath? _path;
    private MachiningSetup? _machiningSetup;

    public void SetMachiningSetup(MachiningSetup? setup)
    {
        _machiningSetup = setup;
        InvalidateVisual();
    }
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

    /// <summary>Toque simples fora da trajetória (sem ser o primeiro de um duplo toque).</summary>
    public event EventHandler? EmptyTapped;

    /// <summary>Mostrar os rápidos (G00).</summary>
    public bool ShowRapids
    {
        get => _showRapids;
        set { _showRapids = value; InvalidateVisual(); }
    }

    /// <summary>Na simulação, mostrar (apagado) o percurso que ainda não foi executado.</summary>
    public bool ShowAhead
    {
        get => _showAhead;
        set { _showAhead = value; InvalidateVisual(); }
    }

    /// <summary>Desenhar a ferramenta na simulação.</summary>
    public bool ShowTool
    {
        get => _showTool;
        set { _showTool = value; InvalidateVisual(); }
    }

    /// <summary>Desenhar a trajetória (as linhas do percurso). Desligada, ficam só o setup, a ferramenta e o destaque.</summary>
    public bool ShowPath
    {
        get => _showPath;
        set { _showPath = value; InvalidateVisual(); }
    }

    private bool _showPath = true;
    private bool _showRapids = true;
    private bool _showAhead = true;
    private bool _showTool = true;

    /// <summary>Área coberta por botões/informações por cima do gráfico: o enquadramento desvia dela.</summary>
    public Thickness FitPadding { get; set; }

    private DispatcherTimer? _singleTapTimer;

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
        context.FillRectangle(_palette.BackgroundBrush, new Rect(bounds.Size));
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
            DrawText(context, "Sem trajetória para mostrar", new Point(12, 52));
            return;
        }

        DrawAxes(context, bounds.Size);

        // Mundo → tela: escala com Y invertido. A espessura da caneta é dividida pela escala
        // para continuar com o mesmo tamanho na tela em qualquer zoom.
        var matrix = new Matrix(_scale, 0, 0, -_scale, _offset.X, _offset.Y);
        double px = 1 / _scale;
        using (context.PushTransform(matrix))
        {
            DrawMachiningSetup(context, px);
            if (_showPath)
            {
                if (_visibleMoves is not { } visible)
                {
                    DrawSet(context, _full, _palette.Bright, px);
                }
                else
                {
                    // Simulação: tudo apagado + o que já foi executado aceso
                    if (_showAhead)
                    {
                        DrawSet(context, _full, _palette.Dim, px);
                    }

                    visible = Math.Clamp(visible, 0, _polys.Length);
                    int complete = visible / BlockSize;
                    for (int b = 0; b < complete; b++)
                    {
                        while (_blocks.Count <= b)
                        {
                            int start = _blocks.Count * BlockSize;
                            _blocks.Add(BuildRange(start, start + BlockSize));
                        }

                        DrawSet(context, _blocks[b], _palette.Bright, px);
                    }

                    DrawSet(context, BuildRange(complete * BlockSize, visible), _palette.Bright, px);
                }
            }

            if (_highlight is { } h)
            {
                context.DrawGeometry(null, new Pen(_palette.HighlightBrush, 3 * px, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), h);
            }
        }

        if (_visibleMoves is { } executed && _showTool)
        {
            DrawTool(context, executed);
        }
    }

    private void DrawMachiningSetup(DrawingContext context, double px)
    {
        if (_machiningSetup is not { } setup || _lathe) return;
        var stock = setup.Stock;
        // Bases das fixações atrás do bruto; os mordentes são desenhados depois dele.
        foreach (var fixture in setup.Fixtures)
            DrawFixture(context, fixture, stock, FixtureColor(fixture), px, drawBase: true);
        if (stock is null)
        {
            // Bruto não informado no setup: o backplot mostra só a trajetória e as bases
        }
        else if (stock.Kind == SetupStockKind.Prism)
            DrawBox(context, stock.X, stock.Y, stock.Z, stock.Width, stock.Depth, stock.Height,
                Color.Parse("#58A6FF"), px);
        else
        {
            // Bruto redondo e tubo: (X, Y) é o centro
            double cx = stock.X, cy = stock.Y;
            var blue = Color.Parse("#58A6FF");
            DrawSolidCylinder(context, cx, cy, stock.Z, stock.Diameter / 2,
                stock.Height, blue, px,
                stock.Kind == SetupStockKind.Tube ? stock.InnerDiameter / 2 : 0);
            DrawRing(context, cx, cy, stock.Z, stock.Diameter / 2, EdgeColor(blue), px);
            DrawRing(context, cx, cy, stock.Z + stock.Height, stock.Diameter / 2, EdgeColor(blue), px);
            if (stock.Kind == SetupStockKind.Tube)
            {
                DrawRing(context, cx, cy, stock.Z, stock.InnerDiameter / 2, blue, px);
                DrawRing(context, cx, cy, stock.Z + stock.Height, stock.InnerDiameter / 2, blue, px);
            }
            for (int i = 0; i < 4; i++)
            {
                double a = i * Math.PI / 2;
                double x = cx + stock.Diameter / 2 * Math.Cos(a);
                double y = cy + stock.Diameter / 2 * Math.Sin(a);
                context.DrawLine(new Pen(new SolidColorBrush(blue), 1.1 * px),
                    ProjectPoint(new Point3(x, y, stock.Z)),
                    ProjectPoint(new Point3(x, y, stock.Z + stock.Height)));
            }
        }
        if (stock is not null)
            foreach (var fixture in setup.Fixtures)
                DrawFixture(context, fixture, stock, FixtureColor(fixture), px, drawBase: false);
    }

    private static Color FixtureColor(SetupFixture fixture) => fixture.Kind switch
    {
        SetupFixtureKind.Vise => Color.Parse("#647789"),
        SetupFixtureKind.Clamp => Color.Parse("#8495A3"),
        _ => Color.Parse("#526779")
    };

    private void DrawFixture(DrawingContext context, SetupFixture f, SetupStock? stock, Color color, double px, bool drawBase)
    {
        if (f.Kind == SetupFixtureKind.ThreeJawChuck)
        {
            double radius = f.Width / 2;
            double cx = f.X + radius, cy = f.Y + f.Depth / 2;
            if (drawBase)
            {
                DrawSolidCylinder(context, cx, cy, f.Z, radius, f.Height, color, px);
                DrawRing(context, cx, cy, f.Z + f.Height, radius, color, px);
                return;
            }
            if (stock is null) return;
            for (int j = 0; j < 3; j++)
            {
                double angle = (j * 120 + f.Rotation) * Math.PI / 180;
                double stockRadius = stock.Kind == SetupStockKind.Prism
                    ? Math.Max(stock.Width, stock.Depth) / 2 : stock.Diameter / 2;
                double jawLength = Math.Max(f.JawThickness * 1.65, radius * 0.24);
                double jawWidth = Math.Max(f.JawThickness * 0.55, radius * 0.085);
                // Limita a posição ao raio útil da placa, mesmo quando a abertura cadastrada é maior.
                double r = Math.Clamp(Math.Max(stockRadius + jawLength * 0.30, f.JawOpening / 2),
                    0, Math.Max(0, radius - jawLength / 2));
                double x = cx + Math.Cos(angle) * r, y = cy + Math.Sin(angle) * r;
                // Castanha radial: o eixo longo aponta para o centro da placa.
                // Castanha em dois níveis: base radial e ressalto de contato.
                // Ambos giram em torno do mesmo centro; geometria apenas ilustrativa.
                double jawHeight = Math.Min(f.JawThickness, stock.Height);
                DrawBox(context, x - jawLength / 2, y - jawWidth / 2,
                    stock.Z, jawLength, jawWidth, jawHeight * 0.55,
                    color, px, angle, x, y);
                DrawBox(context, x - jawLength * 0.42, y - jawWidth * 0.40,
                    stock.Z + jawHeight * 0.55, jawLength * 0.55, jawWidth * 0.80,
                    jawHeight * 0.45, Color.Parse("#8798A6"), px, angle, x, y);
            }
            return;
        }
        if (drawBase)
        {
            DrawBox(context, f.X, f.Y, f.Z, f.Width, f.Depth, f.Height, color, px);
            return;
        }
        if (f.Kind == SetupFixtureKind.Vise)
        {
            double jaw = Math.Min(f.JawThickness, f.Depth / 4);
            double opening = Math.Min(f.JawOpening, f.Depth - 2 * jaw);
            double margin = Math.Max(0, (f.Depth - opening - 2 * jaw) / 2);
            DrawBox(context, f.X, f.Y + margin, f.Z + f.Height,
                f.Width, jaw, jaw, color, px);
            DrawBox(context, f.X, f.Y + margin + jaw + opening, f.Z + f.Height,
                f.Width, jaw, jaw, color, px);
        }
        else
        {
            DrawBox(context, f.X + f.Width * 0.2, f.Y + f.Depth * 0.2,
                f.Z + f.Height, f.Width * 0.6, f.Depth * 0.6,
                f.JawThickness, color, px);
        }
    }

    private void DrawSolidFace(DrawingContext context, Point[] points, Color color, double opacity, double px)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            g.BeginFigure(points[0], true);
            for (int i = 1; i < points.Length; i++) g.LineTo(points[i]);
            g.EndFigure(true);
        }
        context.DrawGeometry(new SolidColorBrush(color, Math.Min(1, opacity + 0.12)),
            null, geometry);
    }

    private void DrawSolidCylinder(DrawingContext context, double cx, double cy,
        double z, double radius, double height, Color color, double px, double innerRadius = 0)
    {
        const int segments = 48;
        for (int i = 0; i < segments; i++)
        {
            double a = i * 2 * Math.PI / segments, b = (i + 1) * 2 * Math.PI / segments;
            Point bottomA = ProjectPoint(new Point3(cx + radius * Math.Cos(a), cy + radius * Math.Sin(a), z));
            Point bottomB = ProjectPoint(new Point3(cx + radius * Math.Cos(b), cy + radius * Math.Sin(b), z));
            Point topB = ProjectPoint(new Point3(cx + radius * Math.Cos(b), cy + radius * Math.Sin(b), z + height));
            Point topA = ProjectPoint(new Point3(cx + radius * Math.Cos(a), cy + radius * Math.Sin(a), z + height));
            // Sem emendas verticais visíveis: as facetas vizinhas do cilindro não têm contorno.
            DrawSolidFace(context, new[] { bottomA, bottomB, topB, topA }, color, 0.78, px);
        }
        // Silhuetas das arestas: sem desenhar as emendas de cada faceta.
        DrawRing(context, cx, cy, z, radius, EdgeColor(color), px);
        DrawRing(context, cx, cy, z + height, radius, EdgeColor(color), px);
        if (innerRadius > 0)
            DrawRing(context, cx, cy, z + height, innerRadius, EdgeColor(color), px);
        if (innerRadius <= 0)
        {
            var top = new Point[segments];
            for (int i = 0; i < segments; i++)
            {
                double a = i * 2 * Math.PI / segments;
                top[i] = ProjectPoint(new Point3(cx + radius * Math.Cos(a), cy + radius * Math.Sin(a), z + height));
            }
            DrawSolidFace(context, top, color, 1.0, px);
        }
        else
        {
            for (int i = 0; i < segments; i++)
            {
                double a = i * 2 * Math.PI / segments, b = (i + 1) * 2 * Math.PI / segments;
                DrawSolidFace(context, new[] {
                    ProjectPoint(new Point3(cx + radius * Math.Cos(a), cy + radius * Math.Sin(a), z + height)),
                    ProjectPoint(new Point3(cx + radius * Math.Cos(b), cy + radius * Math.Sin(b), z + height)),
                    ProjectPoint(new Point3(cx + innerRadius * Math.Cos(b), cy + innerRadius * Math.Sin(b), z + height)),
                    ProjectPoint(new Point3(cx + innerRadius * Math.Cos(a), cy + innerRadius * Math.Sin(a), z + height))
                }, color, 0.85, px);
            }
        }
        // Contornos finais por cima das faces: legíveis nas vistas 2,5D, topo e frente.
        var edge = EdgeColor(color);
        DrawRing(context, cx, cy, z + height, radius, edge, px);
        if (innerRadius > 0)
            DrawRing(context, cx, cy, z + height, innerRadius, edge, px);
        if (_millView != MillView.Top)
        {
            var pen = new Pen(new SolidColorBrush(edge, 0.98), 1.25 * px);
            // Apenas geratrizes externas; evita o efeito de grade das facetas.
            foreach (double angle in new[] { 0.0, Math.PI })
            {
                var x = cx + radius * Math.Cos(angle);
                var y = cy + radius * Math.Sin(angle);
                context.DrawLine(pen, ProjectPoint(new Point3(x, y, z)),
                    ProjectPoint(new Point3(x, y, z + height)));
            }
        }
    }

    // Contornos claros e de alto contraste continuam legíveis em telas pequenas.
    private static Color EdgeColor(Color fill) => Color.FromRgb(
        (byte)Math.Min(255, 130 + fill.R * 0.25),
        (byte)Math.Min(255, 148 + fill.G * 0.24),
        (byte)Math.Min(255, 164 + fill.B * 0.20));

    private void DrawRing(DrawingContext context, double cx, double cy, double z,
        double radius, Color color, double px)
    {
        // Silhueta em duas passadas: separação escura e borda clara, visível no celular.
        var shadow = new Pen(new SolidColorBrush(Color.Parse("#142534")), 2.0 * px);
        var pen = new Pen(new SolidColorBrush(color), 1.25 * px);
        Point? previous = null;
        for (int i = 0; i <= 48; i++)
        {
            double a = i * Math.PI / 24;
            var current = ProjectPoint(new Point3(cx + radius * Math.Cos(a),
                cy + radius * Math.Sin(a), z));
            if (previous is { } point)
            {
                context.DrawLine(shadow, point, current);
                context.DrawLine(pen, point, current);
            }
            previous = current;
        }
    }

    private void DrawBox(DrawingContext context, double x, double y, double z,
        double width, double depth, double height, Color color, double px,
        double rotation = 0, double centreX = 0, double centreY = 0)
    {
        var points = new Point[8];
        double c = Math.Cos(rotation), s = Math.Sin(rotation);
        for (int i = 0; i < 8; i++)
        {
            double wx = x + ((i & 1) != 0 ? width : 0);
            double wy = y + ((i & 2) != 0 ? depth : 0);
            double rx = centreX + (wx - centreX) * c - (wy - centreY) * s;
            double ry = centreY + (wx - centreX) * s + (wy - centreY) * c;
            points[i] = ProjectPoint(new Point3(rx, ry, z + ((i & 4) != 0 ? height : 0)));
        }
        var shadow = new Pen(new SolidColorBrush(Color.Parse("#142534")), 2.0 * px);
        var pen = new Pen(new SolidColorBrush(EdgeColor(color)), 1.25 * px);
        DrawSolidFace(context, new[] { points[0], points[1], points[3], points[2] }, color, 0.38, px);
        DrawSolidFace(context, new[] { points[0], points[1], points[5], points[4] }, color, 0.54, px);
        DrawSolidFace(context, new[] { points[1], points[3], points[7], points[5] }, color, 0.62, px);
        DrawSolidFace(context, new[] { points[2], points[3], points[7], points[6] }, color, 0.46, px);
        DrawSolidFace(context, new[] { points[0], points[2], points[6], points[4] }, color, 0.50, px);
        DrawSolidFace(context, new[] { points[4], points[5], points[7], points[6] }, color, 0.83, px);
        foreach (var (a, b) in new (int, int)[] {
            (0,1),(0,2),(1,3),(2,3),(4,5),(4,6),(5,7),(6,7),
            (0,4),(1,5),(2,6),(3,7) })
        {
            context.DrawLine(shadow, points[a], points[b]);
            context.DrawLine(pen, points[a], points[b]);
        }
    }

    private void DrawSet(DrawingContext context, StreamGeometry?[] set, IBrush[] brushes, double px)
    {
        if (_showRapids && set[0] is { } rapid)
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

        if (!_lathe && _machiningSetup is { } setup && _path is { } path && path.Moves.Count > 0)
        {
            int moveIndex = Math.Clamp(executed - 1, 0, path.Moves.Count - 1);
            int toolNumber = path.Moves[moveIndex].Tool;
            var geometry = setup.ToolOf(toolNumber);
            if (geometry is not null)
            {
                // Representação esquemática: ponta no percurso, corpo alinhado ao eixo Z.
                // Sem remoção de material nem verificação de colisão.
                var pos = executed <= 0 ? path.Moves[0].Start : path.Moves[moveIndex].End;
                Point3 upper = new(pos.X, pos.Y, pos.Z + geometry.CuttingLength);
                // O comprimento exibido acompanha o cadastro do Setup (sem truncamento arbitrario).
                double displayHolderLength = geometry.HolderLength;
                Point3 holderEnd = new(pos.X, pos.Y, upper.Z + displayHolderLength);
                Point cuttingTop = ToScreen(Project(upper).U, Project(upper).V);
                Point holderTop = ToScreen(Project(holderEnd).U, Project(holderEnd).V);
                double radius = Math.Clamp(geometry.Diameter * _scale * 0.5, 3, 35);
                double holderRadius = Math.Clamp(geometry.HolderDiameter * _scale * 0.5, radius + 2, 50);
                var cutPen = new Pen(new SolidColorBrush(Color.Parse("#C7E8FF")), 1.4);
                var holderPen = new Pen(new SolidColorBrush(Color.Parse("#9BAFBD")), 1.4);
                context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#58A6FF")), Math.Max(3, radius * 1.65)), tip, cuttingTop);
                // Corpo principal e transição curta na ponta: silhueta do conjunto sem alterar a trajetória.
                Point shoulder = new(cuttingTop.X + (holderTop.X - cuttingTop.X) * 0.18,
                    cuttingTop.Y + (holderTop.Y - cuttingTop.Y) * 0.18);
                context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#526879")), Math.Max(5, holderRadius * 1.65)), shoulder, holderTop);
                context.DrawLine(new Pen(new SolidColorBrush(Color.Parse("#526879")), Math.Max(4, (radius + holderRadius) * 0.82)), cuttingTop, shoulder);
                context.DrawLine(cutPen, tip, cuttingTop);
                context.DrawLine(holderPen, cuttingTop, holderTop);
                context.DrawEllipse(null, cutPen, tip, radius, Math.Max(2, radius * 0.35));
                context.DrawEllipse(null, holderPen, cuttingTop, Math.Max(radius, holderRadius * 0.65), Math.Max(2, holderRadius * 0.24));
                context.DrawEllipse(null, holderPen, shoulder, holderRadius, Math.Max(2, holderRadius * 0.35));
                context.DrawEllipse(null, holderPen, holderTop, holderRadius, Math.Max(2, holderRadius * 0.35));
                if (geometry.Kind == ToolKind.Drill)
                    context.DrawLine(cutPen, tip + new Vector(-radius, -radius), tip);
                else if (geometry.Kind == ToolKind.Tap)
                    context.DrawLine(cutPen, tip + new Vector(-radius, -5), tip + new Vector(radius, 5));
                else if (geometry.Kind == ToolKind.BallEndMill)
                    context.DrawEllipse(null, cutPen, tip, radius, radius);
                return;
            }
        }
        context.DrawEllipse(_palette.ToolBrush, new Pen(_palette.BackgroundBrush, 2), tip, 5, 5);
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
            context.DrawLine(new Pen(_palette.AxisBrush, 1, new DashStyle([10, 3, 2, 3], 0)), new Point(0, origin.Y), new Point(size.Width, origin.Y));
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
        context.DrawLine(new Pen(_palette.AxisBrush, 1), origin, end);
        DrawText(context, name, end + direction * 6 + new Vector(-4, -8));
    }

    private static void DrawText(DrawingContext context, string text, Point at)
    {
        var formatted = new FormattedText(text, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Typeface.Default, 12, _palette.TextBrush);
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
        const double margin = 24;
        // Área livre = tela menos o que está coberto pelos botões e informações
        Thickness pad = FitPadding;
        double freeWidth = Math.Max(40, size.Width - pad.Left - pad.Right - 2 * margin);
        double freeHeight = Math.Max(40, size.Height - pad.Top - pad.Bottom - 2 * margin);
        _scale = Math.Min(freeWidth / width, freeHeight / height);
        _scale = Math.Clamp(_scale, 0.01, 10_000);

        double centerU = (minU + maxU) / 2;
        double centerV = (minV + maxV) / 2;
        double centerX = pad.Left + (size.Width - pad.Left - pad.Right) / 2;
        double centerY = pad.Top + (size.Height - pad.Top - pad.Bottom) / 2;
        _offset = new Vector(centerX - centerU * _scale, centerY + centerV * _scale);
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
            _singleTapTimer?.Stop(); // era duplo toque: não conta como toque simples
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
            return;
        }

        // Toque no vazio: espera um instante para saber se vem um segundo toque (duplo = ajustar)
        _singleTapTimer ??= CreateSingleTapTimer();
        _singleTapTimer.Stop();
        _singleTapTimer.Start();
    }

    private DispatcherTimer CreateSingleTapTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(360) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            EmptyTapped?.Invoke(this, EventArgs.Empty);
        };
        return timer;
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
