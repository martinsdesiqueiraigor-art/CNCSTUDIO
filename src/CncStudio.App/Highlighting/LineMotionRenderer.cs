using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;
using CncStudio.Core.Trajectory;

namespace CncStudio.App.Highlighting;

/// <summary>
/// Fundo das linhas do editor conforme o movimento que a linha gera (como no CIMCO):
/// uma faixa colorida à esquerda e um tom leve no fundo — G00 vermelho, G01 verde,
/// G02 azul-claro, G03 azul-escuro. A linha em execução na simulação ganha destaque amarelo.
/// </summary>
public sealed class LineMotionRenderer : IBackgroundRenderer
{
    private static readonly Color[] MotionColors =
    [
        Color.Parse("#FF4D4D"),
        Color.Parse("#3DDC6A"),
        Color.Parse("#7FD8FF"),
        Color.Parse("#3B6BFF"),
    ];

    private static readonly IBrush[] BarBrushes = MotionColors.Select(c => (IBrush)new SolidColorBrush(c).ToImmutable()).ToArray();
    private static readonly IBrush[] TintBrushes = MotionColors.Select(c => (IBrush)new SolidColorBrush(Color.FromArgb(26, c.R, c.G, c.B)).ToImmutable()).ToArray();
    private static readonly IBrush ExecutingBrush = new SolidColorBrush(Color.FromArgb(80, 255, 232, 77)).ToImmutable();
    private static readonly IBrush ExecutingBar = new SolidColorBrush(Color.Parse("#FFE84D")).ToImmutable();

    private Dictionary<int, MotionMode> _motions = [];

    /// <summary>Linha em execução na simulação (0 = nenhuma).</summary>
    public int ExecutingLine { get; set; }

    /// <summary>Mostrar as cores por movimento.</summary>
    public bool Enabled { get; set; } = true;

    public KnownLayer Layer => KnownLayer.Background;

    /// <summary>Movimento de cada linha (o primeiro movimento que ela gera).</summary>
    public void Update(ToolPath path)
    {
        var motions = new Dictionary<int, MotionMode>();
        foreach (Move move in path.Moves)
        {
            motions.TryAdd(move.LineNumber, move.Motion);
        }

        _motions = motions;
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!textView.VisualLinesValid)
        {
            return;
        }

        double width = textView.Bounds.Width;
        foreach (VisualLine line in textView.VisualLines)
        {
            int number = line.FirstDocumentLine.LineNumber;
            double top = line.VisualTop - textView.VerticalOffset;
            var row = new Rect(0, top, width, line.Height);

            if (number == ExecutingLine)
            {
                drawingContext.FillRectangle(ExecutingBrush, row);
                drawingContext.FillRectangle(ExecutingBar, new Rect(0, top, 4, line.Height));
                continue;
            }

            if (Enabled && _motions.TryGetValue(number, out MotionMode motion))
            {
                int index = (int)motion;
                drawingContext.FillRectangle(TintBrushes[index], row);
                drawingContext.FillRectangle(BarBrushes[index], new Rect(0, top, 3, line.Height));
            }
        }
    }
}
