using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace CncStudio.App.Views;

/// <summary>
/// Base para as vistas 2D: guarda a transformação mundo → tela e trata zoom
/// (roda do mouse, centrado no cursor), arrasto (pan) e duplo clique (ajustar).
///
/// Coordenadas do mundo (u, v) em mm, com v para cima; na tela, Y cresce para baixo.
/// </summary>
public abstract class ZoomPanControl : Control
{
    protected static readonly IBrush BackgroundBrush = new SolidColorBrush(Color.Parse("#181A1F")).ToImmutable();
    protected static readonly IBrush LabelBrush = new SolidColorBrush(Color.Parse("#9DA5B4")).ToImmutable();

    private double _scale = 1;
    private double _offsetX;
    private double _offsetY;
    private bool _fitPending = true;

    private bool _dragging;
    private Point _dragStart;
    private double _dragOffsetX;
    private double _dragOffsetY;

    protected ZoomPanControl()
    {
        ClipToBounds = true;
        DoubleTapped += (_, _) => FitToContent();
    }

    /// <summary>Pixels por milímetro.</summary>
    protected double Scale => _scale;

    /// <summary>Matriz mundo → tela (para Geometry.Transform).</summary>
    protected Matrix WorldToScreen => new(_scale, 0, 0, -_scale, _offsetX, _offsetY);

    /// <summary>Área ocupada pelo conteúdo, em coordenadas do mundo. Null = nada para mostrar.</summary>
    protected abstract Rect? ContentBounds { get; }

    /// <summary>Desenha o conteúdo (o fundo já foi pintado).</summary>
    protected abstract void RenderContent(DrawingContext context);

    /// <summary>Enquadra o conteúdo inteiro no próximo desenho.</summary>
    public void FitToContent()
    {
        _fitPending = true;
        InvalidateVisual();
    }

    protected Point ToScreen(double u, double v) => new(u * _scale + _offsetX, -v * _scale + _offsetY);

    protected Point ToWorld(Point screen) => new((screen.X - _offsetX) / _scale, -(screen.Y - _offsetY) / _scale);

    public sealed override void Render(DrawingContext context)
    {
        // Fundo pintado também torna a área inteira clicável
        context.FillRectangle(BackgroundBrush, new Rect(Bounds.Size));
        if (_fitPending)
        {
            ApplyFit();
        }

        RenderContent(context);
    }

    private void ApplyFit()
    {
        Rect? content = ContentBounds;
        Size size = Bounds.Size;
        if (content is not { } bounds || size.Width < 1 || size.Height < 1)
        {
            return;
        }

        _fitPending = false;
        double width = Math.Max(bounds.Width, 1e-3);
        double height = Math.Max(bounds.Height, 1e-3);
        _scale = Math.Min(size.Width / width, size.Height / height) * 0.9;

        // Centro do conteúdo no centro da área
        double centerU = bounds.X + bounds.Width / 2;
        double centerV = bounds.Y + bounds.Height / 2;
        _offsetX = size.Width / 2 - centerU * _scale;
        _offsetY = size.Height / 2 + centerV * _scale;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        double factor = e.Delta.Y > 0 ? 1.2 : 1 / 1.2;
        Point p = e.GetPosition(this);

        // Mantém o ponto sob o cursor parado
        _offsetX = p.X - (p.X - _offsetX) * factor;
        _offsetY = p.Y - (p.Y - _offsetY) * factor;
        _scale *= factor;
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _dragging = true;
        _dragStart = e.GetPosition(this);
        _dragOffsetX = _offsetX;
        _dragOffsetY = _offsetY;
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging)
        {
            return;
        }

        Point p = e.GetPosition(this);
        _offsetX = _dragOffsetX + (p.X - _dragStart.X);
        _offsetY = _dragOffsetY + (p.Y - _dragStart.Y);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    /// <summary>Texto simples (rótulos de eixo, legendas).</summary>
    protected static void DrawLabel(DrawingContext context, string text, Point position, IBrush brush, double size = 12)
    {
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            Typeface.Default,
            size,
            brush);
        context.DrawText(formatted, position);
    }
}
