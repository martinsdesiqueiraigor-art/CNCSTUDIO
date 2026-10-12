using Avalonia;
using Avalonia.Media;
using CncStudio.Core.Geometry;
using CncStudio.Core.Trajectory;

namespace CncStudio.App.Views;

/// <summary>
/// Backplot 2D: desenha a trajetória projetada no plano escolhido, com uma cor por tipo de
/// movimento (G00 vermelho tracejado, G01 verde, G02 azul-claro, G03 azul-escuro).
///
/// Modo passe a passe (<see cref="Limit"/>): só os movimentos já "executados" ficam coloridos;
/// o resto aparece apagado, o passe atual ganha um contorno claro e a ferramenta é marcada.
///
/// As figuras chegam prontas (calculadas fora da thread de UI); aqui só se monta a geometria
/// quando os dados, o plano ou o ponto da execução mudam — o zoom muda apenas a transformação.
/// </summary>
public sealed class BackplotView : ZoomPanControl
{
    private static Pen MotionPen(string color, double width, DashStyle? dash = null) =>
        new(new SolidColorBrush(Color.Parse(color)).ToImmutable(), width, dash);

    // Cores pedidas: G0 vermelho, G1 verde, G2 azul-claro, G3 azul-escuro
    private static readonly Pen RapidPen = MotionPen("#FF4D4D", 1, new DashStyle([4, 4], 0));
    private static readonly Pen LinearPen = MotionPen("#3DDC6A", 1.3);
    private static readonly Pen ClockwisePen = MotionPen("#7FD8FF", 1.5);
    private static readonly Pen CounterClockwisePen = MotionPen("#3B6BFF", 1.5);
    private static readonly Pen GhostPen = MotionPen("#3A3F47", 1);
    private static readonly Pen PassPen = new(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)).ToImmutable(), 6,
        lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private static readonly Pen HighlightPen = MotionPen("#FFE84D", 3);
    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.Parse("#FFE84D")).ToImmutable();
    private static readonly IBrush ToolBrush = new SolidColorBrush(Color.Parse("#FFB040")).ToImmutable();
    private static readonly Pen ToolPen = MotionPen("#1B1D22", 1.5);
    private static readonly Pen AxisXPen = MotionPen("#E05050", 1.5);
    private static readonly Pen AxisYPen = MotionPen("#50C050", 1.5);
    private static readonly Pen AxisZPen = MotionPen("#5090F0", 1.5);

    private static readonly MotionMode[] Motions =
        [MotionMode.Rapid, MotionMode.Linear, MotionMode.ClockwiseArc, MotionMode.CounterClockwiseArc];

    private ToolPath? _path;
    private IReadOnlyList<PathPolyline> _figures = [];
    private ViewPlane _plane = ViewPlane.XY;
    private int _highlightedLine;
    private int? _limit;
    private (int Start, int End)? _currentPass;

    private StreamGeometry?[] _fullGeometry = new StreamGeometry?[4];   // trajetória inteira, por tipo
    private StreamGeometry?[] _doneGeometry = new StreamGeometry?[4];   // até o limite (passe a passe)
    private StreamGeometry? _passGeometry;
    private StreamGeometry? _highlightGeometry;
    private Point? _highlightStart;
    private Point? _toolPoint;
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
            RebuildAll();
            FitToContent();
        }
    }

    /// <summary>
    /// Passe a passe: quantos movimentos (desde o início) já foram executados. Null = tudo
    /// colorido (modo normal). "currentPass" = faixa de movimentos [início, fim) do passe atual.
    /// </summary>
    public void SetLimit(int? limit, (int Start, int End)? currentPass)
    {
        _limit = limit;
        _currentPass = currentPass;
        RebuildProgress();
        RebuildHighlight();
        InvalidateVisual();
    }

    /// <summary>Troca a trajetória exibida. "fit" = reenquadrar (ao abrir arquivo).</summary>
    public void SetToolPath(ToolPath path, IReadOnlyList<PathPolyline> figures, bool fit)
    {
        _path = path;
        _figures = figures;
        _limit = null;
        _currentPass = null;
        RebuildAll();
        if (fit)
        {
            FitToContent();
        }
        else
        {
            InvalidateVisual();
        }
    }

    /// <summary>Destaca os movimentos gerados pela linha (base 1) do editor (fora do passe a passe).</summary>
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
        if (_contentBounds is null)
        {
            DrawLabel(context, "Sem movimentos para mostrar", new Point(12, 12), LabelBrush);
            return;
        }

        var transform = new MatrixTransform(WorldToScreen);
        DrawAxes(context);

        bool stepping = _limit.HasValue;
        for (int i = 0; i < Motions.Length; i++)
        {
            if (_fullGeometry[i] is { } full)
            {
                full.Transform = transform;
                context.DrawGeometry(null, stepping ? GhostPen : PenFor(Motions[i]), full);
            }
        }

        if (stepping)
        {
            if (_passGeometry is not null)
            {
                _passGeometry.Transform = transform;
                context.DrawGeometry(null, PassPen, _passGeometry);
            }

            for (int i = 0; i < Motions.Length; i++)
            {
                if (_doneGeometry[i] is { } done)
                {
                    done.Transform = transform;
                    context.DrawGeometry(null, PenFor(Motions[i]), done);
                }
            }
        }

        if (_highlightGeometry is not null && _highlightStart is { } start)
        {
            _highlightGeometry.Transform = transform;
            context.DrawGeometry(null, HighlightPen, _highlightGeometry);
            context.DrawEllipse(HighlightBrush, null, ToScreen(start.X, start.Y), 4, 4);
        }

        if (stepping && _toolPoint is { } tool)
        {
            context.DrawEllipse(ToolBrush, ToolPen, ToScreen(tool.X, tool.Y), 6, 6);
        }

        DrawLabel(context, $"{_plane.Caption()}  ·  roda = zoom  ·  arrastar = mover  ·  duplo clique = ajustar",
            new Point(8, Bounds.Height - 20), LabelBrush, 11);
    }

    private static Pen PenFor(MotionMode motion) => motion switch
    {
        MotionMode.Rapid => RapidPen,
        MotionMode.Linear => LinearPen,
        MotionMode.ClockwiseArc => ClockwisePen,
        _ => CounterClockwisePen,
    };

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

    // ───────────────────────── Geometria ─────────────────────────

    private void RebuildAll()
    {
        for (int i = 0; i < Motions.Length; i++)
        {
            MotionMode motion = Motions[i];
            _fullGeometry[i] = _figures.Count == 0 ? null : BuildGeometry(_figures.Where(f => f.Motion == motion).Select(f => (f, f.Points.Length)));
        }

        _contentBounds = _path is null || _figures.Count == 0 ? null : _plane.ProjectBounds(_path.Bounds);
        RebuildProgress();
        RebuildHighlight();
    }

    /// <summary>Parte já executada (passe a passe), o passe atual e a posição da ferramenta.</summary>
    private void RebuildProgress()
    {
        Array.Clear(_doneGeometry);
        _passGeometry = null;
        _toolPoint = null;
        if (_path is null || _limit is not { } limit)
        {
            return;
        }

        // Figuras estão em ordem de execução: inteiras até o limite, a última cortada no meio
        var done = Motions.Select(_ => new List<(PathPolyline, int)>()).ToArray();
        foreach (PathPolyline figure in _figures)
        {
            if (figure.FirstMove >= limit)
            {
                break;
            }

            int count = figure.PointCountThrough(Math.Min(limit - 1, figure.LastMove));
            if (count >= 2)
            {
                done[Array.IndexOf(Motions, figure.Motion)].Add((figure, count));
            }
        }

        for (int i = 0; i < Motions.Length; i++)
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

        if (_path.Moves.Count > 0)
        {
            Point3 tool = limit <= 0 ? _path.Moves[0].Start : _path.Moves[Math.Min(limit, _path.Moves.Count) - 1].End;
            _toolPoint = _plane.Project(tool);
        }
    }

    private void RebuildHighlight()
    {
        _highlightGeometry = null;
        _highlightStart = null;
        if (_path is null || _limit.HasValue)
        {
            return; // no passe a passe, o destaque é o passe atual
        }

        // Uma linha pode gerar vários movimentos (ciclos, laços): destaca todos
        IReadOnlyList<int> indices = _path.MovesForLine(_highlightedLine);
        if (indices.Count == 0)
        {
            return;
        }

        double tolerance = PathPolylineBuilder.ScreenTolerance(_path);
        _highlightGeometry = BuildGeometry(indices.Select(i => PathPolylineBuilder.Single(_path.Moves[i], i, tolerance))
            .Select(f => (f, f.Points.Length)));
        _highlightStart = _plane.Project(_path.Moves[indices[0]].Start);
    }

    /// <summary>Geometria com as figuras informadas, cada uma até "count" pontos.</summary>
    private StreamGeometry BuildGeometry(IEnumerable<(PathPolyline Figure, int Count)> figures)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open())
        {
            foreach ((PathPolyline figure, int count) in figures)
            {
                context.BeginFigure(_plane.Project(figure.Points[0]), false);
                for (int i = 1; i < count; i++)
                {
                    context.LineTo(_plane.Project(figure.Points[i]));
                }

                context.EndFigure(false);
            }
        }

        return geometry;
    }
}
