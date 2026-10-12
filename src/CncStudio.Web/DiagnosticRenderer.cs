using Avalonia;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.Rendering;
using CncStudio.Core.Parsing;

namespace CncStudio.Web;

/// <summary>
/// Marca no editor os erros (vermelho) e avisos (amarelo): sublinhado no trecho com problema
/// e uma faixa na margem esquerda da linha, como um alarme do comando.
/// </summary>
public sealed class DiagnosticRenderer : IBackgroundRenderer
{
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#FF5C5C"));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#E3B341"));
    private static readonly IBrush ErrorTint = new SolidColorBrush(Color.FromArgb(46, 255, 92, 92));

    private Dictionary<int, List<ParseDiagnostic>> _byLine = [];

    public KnownLayer Layer => KnownLayer.Selection; // acima do fundo colorido por movimento

    public void Update(IReadOnlyList<ParseDiagnostic> diagnostics)
    {
        _byLine = diagnostics.GroupBy(d => d.LineNumber).ToDictionary(g => g.Key, g => g.ToList());
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid || _byLine.Count == 0)
        {
            return;
        }

        double width = textView.Bounds.Width;
        foreach (VisualLine visualLine in textView.VisualLines)
        {
            int number = visualLine.FirstDocumentLine.LineNumber;
            if (!_byLine.TryGetValue(number, out List<ParseDiagnostic>? list))
            {
                continue;
            }

            double top = visualLine.VisualTop - textView.VerticalOffset;
            bool error = list.Any(d => d.Severity == DiagnosticSeverity.Error);
            if (error)
            {
                drawingContext.FillRectangle(ErrorTint, new Rect(0, top, width, visualLine.Height));
            }

            drawingContext.FillRectangle(error ? ErrorBrush : WarningBrush, new Rect(0, top, 4, visualLine.Height));

            int lineLength = visualLine.FirstDocumentLine.Length;
            foreach (ParseDiagnostic d in list)
            {
                int start = Math.Clamp(d.Column, 0, lineLength);
                int end = d.Length > 0 ? Math.Clamp(d.Column + d.Length, start, lineLength) : lineLength;
                if (end <= start)
                {
                    continue;
                }

                double x0 = visualLine.GetVisualPosition(start, VisualYPosition.TextBottom).X - textView.HorizontalOffset;
                double x1 = visualLine.GetVisualPosition(end, VisualYPosition.TextBottom).X - textView.HorizontalOffset;
                double y = top + visualLine.Height - 2;
                IBrush brush = d.Severity == DiagnosticSeverity.Error ? ErrorBrush : WarningBrush;
                DrawWave(drawingContext, brush, x0, x1, y);
            }
        }
    }

    /// <summary>Sublinhado ondulado (como corretor ortográfico).</summary>
    private static void DrawWave(DrawingContext context, IBrush brush, double x0, double x1, double y)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(x0, y), false);
            bool up = true;
            for (double x = x0 + 2; x <= x1 + 0.1; x += 2)
            {
                ctx.LineTo(new Point(x, up ? y - 2 : y));
                up = !up;
            }

            ctx.EndFigure(false);
        }

        context.DrawGeometry(null, new Pen(brush, 1.2), geometry);
    }
}
