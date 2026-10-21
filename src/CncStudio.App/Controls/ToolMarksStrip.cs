using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CncStudio.Core.Scene;
using CncStudio.Core.Trajectory;

namespace CncStudio.App.Controls;

/// <summary>
/// Faixa fina em cima da barra de progresso com uma marca em cada troca de ferramenta (na cor da
/// ferramenta). Tocar numa marca avisa por <see cref="MarkClicked"/>, que leva a simulação até ela.
/// As marcas ficam alinhadas com o controle deslizante logo abaixo (mesma margem das pontas).
/// </summary>
public sealed class ToolMarksStrip : Control
{
    /// <summary>Folga das pontas: o controle deslizante reserva metade do botão de cada lado.</summary>
    private const double EdgePadding = 10;

    private IReadOnlyList<ToolChange> _marks = [];
    private Func<ToolChange, double> _positionOf = _ => 0;
    private int _hover = -1;

    public ToolMarksStrip()
    {
        Height = 14;
        Cursor = new Cursor(StandardCursorType.Arrow);
    }

    /// <summary>Uma marca foi tocada (o movimento dela está em <see cref="ToolChange.MoveIndex"/>).</summary>
    public event EventHandler<ToolChange>? MarkClicked;

    /// <summary>Raio de toque em volta de cada marca (px): maior no celular, onde o dedo é largo.</summary>
    public double HitRadius { get; set; } = 9;

    /// <summary>
    /// Define as marcas. "positionOf" diz onde cada uma fica na barra, de 0 (início) a 1 (fim): quem usa
    /// a barra por movimentos (desktop) ou por passos (web) escolhe a conta.
    /// </summary>
    public void SetMarks(IReadOnlyList<ToolChange> marks, Func<ToolChange, double> positionOf)
    {
        _marks = marks;
        _positionOf = positionOf;
        _hover = -1;
        ToolTip.SetTip(this, null);
        InvalidateVisual();
    }

    private double XOf(ToolChange mark) =>
        EdgePadding + Math.Max(0, Bounds.Width - 2 * EdgePadding) * Math.Clamp(_positionOf(mark), 0, 1);

    private int NearestMark(double x)
    {
        int best = -1;
        double bestDistance = HitRadius;
        for (int i = 0; i < _marks.Count; i++)
        {
            double distance = Math.Abs(XOf(_marks[i]) - x);
            if (distance <= bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }

        return best;
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        for (int i = 0; i < _marks.Count; i++)
        {
            ToolChange mark = _marks[i];
            uint rgb = ScenePalette.ToolColors[(mark.Tool - 1) % ScenePalette.ToolColors.Count];
            var brush = new SolidColorBrush(Color.FromUInt32(0xFF000000 | rgb));
            double x = XOf(mark);
            double top = i == _hover ? 1 : 3;

            // Triângulo apontando para baixo (para o ponto da barra) com a haste fina
            var triangle = new StreamGeometry();
            using (StreamGeometryContext g = triangle.Open())
            {
                g.BeginFigure(new Point(x - 4.5, top), isFilled: true);
                g.LineTo(new Point(x + 4.5, top));
                g.LineTo(new Point(x, top + 8));
                g.EndFigure(isClosed: true);
            }

            context.DrawGeometry(brush, null, triangle);
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        int hover = NearestMark(e.GetPosition(this).X);
        if (hover == _hover)
        {
            return;
        }

        _hover = hover;
        Cursor = new Cursor(hover >= 0 ? StandardCursorType.Hand : StandardCursorType.Arrow);
        ToolTip.SetTip(this, hover >= 0 ? $"T{_marks[hover].Tool}: ir à troca de ferramenta (linha {_marks[hover].LineNumber})" : null);
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_hover >= 0)
        {
            _hover = -1;
            InvalidateVisual();
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        int index = NearestMark(e.GetPosition(this).X);
        if (index >= 0)
        {
            e.Handled = true;
            MarkClicked?.Invoke(this, _marks[index]);
        }
    }
}
