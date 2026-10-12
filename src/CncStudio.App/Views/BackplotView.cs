using Avalonia;
using Avalonia.Media;
using CncStudio.Core.Geometry;
using CncStudio.Core.Trajectory;

namespace CncStudio.App.Views;

/// <summary>
/// Backplot 2D: desenha a trajetória projetada em XY, XZ ou YZ.
/// G00 tracejado em laranja, corte contínuo em azul, linha do cursor em amarelo.
///
/// As figuras chegam prontas (calculadas fora da thread de UI); aqui só se monta
/// a geometria uma vez por mudança de dados/plano, e o zoom muda apenas a transformação.
/// </summary>
public sealed class BackplotView : ZoomPanControl
{
    private static readonly Pen RapidPen = new(new SolidColorBrush(Color.Parse("#E0A030")).ToImmutable(), 1, new DashStyle([4, 4], 0));
    private static readonly Pen CutPen = new(new SolidColorBrush(Color.Parse("#3CC8FF")).ToImmutable(), 1.2);
    private static readonly Pen HighlightPen = new(new SolidColorBrush(Color.Parse("#FFE84D")).ToImmutable(), 3);
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.Parse("#FFE84D")).ToImmutable();
    private static readonly Pen AxisXPen = new(new SolidColorBrush(Color.Parse("#E05050")).ToImmutable(), 1.5);
    private static readonly Pen AxisYPen = new(new SolidColorBrush(Color.Parse("#50C050")).ToImmutable(), 1.5);
    private static readonly Pen AxisZPen = new(new SolidColorBrush(Color.Parse("#5090F0")).ToImmutable(), 1.5);

    private ToolPath? _path;
    private IReadOnlyList<PathPolyline> _figures = [];
    private ViewPlane _plane = ViewPlane.XY;
    private int _highlightedLine;

    private StreamGeometry? _rapidGeometry;
    private StreamGeometry? _cutGeometry;
    private StreamGeometry? _highlightGeometry;
    private Point? _highlightStart;
    private Rect? _contentBounds;

    public ViewPlane Plane
    {
        get => _plane;
        set
        {
            if (_plane == value)
            {
                return;
            }

            _plane = value;
            RebuildGeometry();
            RebuildHighlight();
            FitToContent();
        }
    }

    /// <summary>Troca a trajetória exibida. "fit" = reenquadrar (ao abrir arquivo).</summary>
    public void SetToolPath(ToolPath path, IReadOnlyList<PathPolyline> figures, bool fit)
    {
        _path = path;
        _figures = figures;
        RebuildGeometry();
        RebuildHighlight();
        if (fit)
        {
            FitToContent();
        }
        else
        {
            InvalidateVisual();
        }
    }

    /// <summary>Destaca o movimento gerado pela linha (base 1) do editor.</summary>
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

    protected override Rect? ContentBounds => _contentBounds;

    protected override void RenderContent(DrawingContext context)
    {
        if (_rapidGeometry is null || _cutGeometry is null)
        {
            DrawLabel(context, "Sem movimentos para mostrar", new Point(12, 12), LabelBrush);
            return;
        }

        var transform = new MatrixTransform(WorldToScreen);
        DrawAxes(context);

        _rapidGeometry.Transform = transform;
        context.DrawGeometry(null, RapidPen, _rapidGeometry);

        _cutGeometry.Transform = transform;
        context.DrawGeometry(null, CutPen, _cutGeometry);

        if (_highlightGeometry is not null && _highlightStart is { } start)
        {
            _highlightGeometry.Transform = transform;
            context.DrawGeometry(null, HighlightPen, _highlightGeometry);
            context.DrawEllipse(HighlightBrush, null, ToScreen(start.X, start.Y), 4, 4);
        }

        DrawLabel(context, $"{_plane.Caption()}  ·  roda = zoom  ·  arrastar = mover  ·  duplo clique = ajustar",
            new Point(8, Bounds.Height - 20), LabelBrush, 11);
    }

    /// <summary>Pequeno indicador dos eixos na origem do programa (X vermelho, Y verde, Z azul).</summary>
    private void DrawAxes(DrawingContext context)
    {
        Point origin = ToScreen(0, 0);
        foreach ((string name, double u, double v) in _plane.AxisDirections())
        {
            // Direção na tela (v para cima = y para baixo), com 40 px de comprimento
            double length = Math.Sqrt(u * u + v * v);
            var end = new Point(origin.X + u / length * 40, origin.Y - v / length * 40);
            Pen pen = name switch { "X" => AxisXPen, "Y" => AxisYPen, _ => AxisZPen };
            context.DrawLine(pen, origin, end);
            DrawLabel(context, name, new Point(end.X + 3, end.Y - 14), pen.Brush!);
        }
    }

    private void RebuildGeometry()
    {
        if (_path is null || _figures.Count == 0)
        {
            _rapidGeometry = null;
            _cutGeometry = null;
            _contentBounds = null;
            return;
        }

        _rapidGeometry = BuildGeometry(_figures.Where(f => f.IsRapid));
        _cutGeometry = BuildGeometry(_figures.Where(f => !f.IsRapid));
        _contentBounds = _plane.ProjectBounds(_path.Bounds);
    }

    private void RebuildHighlight()
    {
        _highlightGeometry = null;
        _highlightStart = null;
        if (_path is null)
        {
            return;
        }

        int index = _path.IndexOfLine(_highlightedLine);
        if (index < 0)
        {
            return;
        }

        Move move = _path.Moves[index];
        var points = new List<Point3> { move.Start };
        move.AppendPolyline(points, PathPolylineBuilder.ScreenTolerance(_path));
        _highlightGeometry = BuildGeometry([new PathPolyline(move.IsRapid, points.ToArray())]);
        _highlightStart = _plane.Project(move.Start);
    }

    private StreamGeometry BuildGeometry(IEnumerable<PathPolyline> figures)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            foreach (PathPolyline figure in figures)
            {
                context.BeginFigure(_plane.Project(figure.Points[0]), false);
                for (int i = 1; i < figure.Points.Length; i++)
                {
                    context.LineTo(_plane.Project(figure.Points[i]));
                }

                context.EndFigure(false);
            }
        }

        return geometry;
    }
}
