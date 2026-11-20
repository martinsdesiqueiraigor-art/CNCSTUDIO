using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using CncStudio.Core.Scene;

namespace CncStudio.App.Modules;

/// <summary>
/// Cubo de vistas (UI-PRO-P3b) por cima do 3D do Trabalho fresa e torno: clicar numa face leva à vista dela (Topo, Frente, Lado)
/// e o botão Iso volta à isométrica. Só chama as vistas que a cena já tem (<c>Fit(SceneView)</c>); não mexe na cena.
/// </summary>
public sealed class ViewCube : Border
{
    /// <summary>Clique numa face: a vista pedida.</summary>
    public Action<SceneView>? Pick { get; set; }

    public ViewCube()
    {
        Width = 80;
        Height = 96;
        Background = Brushes.Transparent;
        var canvas = new Canvas { Width = 80, Height = 76 };
        canvas.Children.Add(Face([new(40, 4), new(72, 20), new(40, 36), new(8, 20)], SceneView.Top, "Topo", "Vista de topo", 40, 14));
        canvas.Children.Add(Face([new(8, 20), new(40, 36), new(40, 72), new(8, 56)], SceneView.Front, "Frente", "Vista de frente", 24, 41));
        canvas.Children.Add(Face([new(40, 36), new(72, 20), new(72, 56), new(40, 72)], SceneView.Side, "Lado", "Vista lateral", 56, 41));
        foreach (Control c in Labels)
        {
            canvas.Children.Add(c);
        }

        var iso = new Button
        {
            Content = "Iso", FontSize = 11, Padding = new Thickness(10, 2), MinHeight = 0, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Background = CamColors.CardBrush, BorderBrush = CamColors.BorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
        };
        ToolTip.SetTip(iso, "Vista isométrica");
        Avalonia.Automation.AutomationProperties.SetName(iso, "Vista isométrica");
        iso.Click += (_, _) => Pick?.Invoke(SceneView.Isometric);
        Child = new StackPanel { Spacing = 4, Children = { canvas, iso } };
    }

    private readonly List<Control> _labels = [];
    private IReadOnlyList<Control> Labels => _labels;

    private Polygon Face(Point[] points, SceneView view, string text, string tip, double cx, double cy)
    {
        var fill = CamColors.CardBrush;
        var polygon = new Polygon
        {
            Points = [.. points], Fill = fill, Stroke = CamColors.BorderStrongBrush, StrokeThickness = 1, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        polygon.PointerEntered += (_, _) => polygon.Fill = CamColors.SelectHoverBrush;
        polygon.PointerExited += (_, _) => polygon.Fill = fill;
        polygon.PointerPressed += (_, _) => Pick?.Invoke(view);
        ToolTip.SetTip(polygon, tip);
        var label = new TextBlock { Text = text, FontSize = 9, Foreground = CamColors.TextBrush, IsHitTestVisible = false, Width = 40, TextAlignment = TextAlignment.Center };
        Canvas.SetLeft(label, cx - 20);
        Canvas.SetTop(label, cy - 6);
        _labels.Add(label);
        return polygon;
    }
}
