using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CncStudio.Core.Cam;

namespace CncStudio.App.Modules;

/// <summary>Prévia 2D do CAM: contornos do desenho (o escolhido em destaque) e o percurso de corte da operação.</summary>
public sealed class CamPreview : Control
{
    private static readonly IPen ContourPen = new Pen(new SolidColorBrush(Color.Parse("#8FA3B5")), 1.2);
    private static readonly IPen SelectedPen = new Pen(new SolidColorBrush(Color.Parse("#4D8DFF")), 2.4);
    private static readonly IPen PathPen = new Pen(new SolidColorBrush(Color.Parse("#FFB347")), 1.4);
    private static readonly IPen AxisPen = new Pen(new SolidColorBrush(Color.Parse("#2E4452")), 1);

    private IReadOnlyList<Contour> _contours = [];
    private int _selected = -1;
    private IReadOnlyList<List<Point2>> _path = [];

    // Mapeamento da última pintura (para converter clique em ponto do desenho)
    private double _minX, _minY, _scale, _ox, _oy;
    private bool _mapped;

    /// <summary>Clique na vista: índice do contorno escolhido (-1 = clicou fora de todos).</summary>
    public event EventHandler<int>? ContourPicked;

    public CamPreview()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!_mapped || _contours.Count == 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Point at = e.GetPosition(this);
        var world = new Point2(_minX + ((at.X - _ox) / _scale), _minY + ((Bounds.Height - _oy - at.Y) / _scale));
        ContourPicked?.Invoke(this, ContourPick.At(_contours, world, 8 / _scale));
    }

    public void Set(IReadOnlyList<Contour> contours, int selected, IReadOnlyList<List<Point2>> path)
    {
        _contours = contours;
        _selected = selected;
        _path = path;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        _mapped = false;
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size)); // recebe o clique em toda a área
        double minX = 0, minY = 0, maxX = 0, maxY = 0;
        bool any = false;
        void Include(Point2 p)
        {
            if (!any)
            {
                (minX, maxX, minY, maxY) = (p.X, p.X, p.Y, p.Y);
                any = true;
                return;
            }

            minX = Math.Min(minX, p.X);
            maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y);
            maxY = Math.Max(maxY, p.Y);
        }

        var contourPoints = _contours.Select(c => c.Sample(0.02)).ToList();
        foreach (Point2 p in contourPoints.SelectMany(l => l).Concat(_path.SelectMany(l => l)))
        {
            Include(p);
        }

        if (!any)
        {
            return;
        }

        Include(new Point2(0, 0));
        double w = Math.Max(maxX - minX, 1e-6), h = Math.Max(maxY - minY, 1e-6);
        const double margin = 28;
        double scale = Math.Min((Bounds.Width - (2 * margin)) / w, (Bounds.Height - (2 * margin)) / h);
        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
        {
            return;
        }

        double ox = (Bounds.Width - (w * scale)) / 2, oy = (Bounds.Height - (h * scale)) / 2;
        (_minX, _minY, _scale, _ox, _oy, _mapped) = (minX, minY, scale, ox, oy, true);
        Point Map(Point2 p) => new(ox + ((p.X - minX) * scale), Bounds.Height - oy - ((p.Y - minY) * scale));

        // Eixos X e Y no zero da peça
        Point origin = Map(new Point2(0, 0));
        context.DrawLine(AxisPen, new Point(0, origin.Y), new Point(Bounds.Width, origin.Y));
        context.DrawLine(AxisPen, new Point(origin.X, 0), new Point(origin.X, Bounds.Height));

        for (int i = 0; i < contourPoints.Count; i++)
        {
            Draw(context, contourPoints[i].Select(Map).ToList(), i == _selected ? SelectedPen : ContourPen, closed: true);
        }

        foreach (List<Point2> line in _path)
        {
            Draw(context, line.Select(Map).ToList(), PathPen, closed: false);
        }
    }

    private static void Draw(DrawingContext context, List<Point> points, IPen pen, bool closed)
    {
        if (points.Count < 2)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (StreamGeometryContext g = geometry.Open())
        {
            g.BeginFigure(points[0], false);
            for (int i = 1; i < points.Count; i++)
            {
                g.LineTo(points[i]);
            }

            g.EndFigure(closed);
        }

        context.DrawGeometry(null, pen, geometry);
    }
}
