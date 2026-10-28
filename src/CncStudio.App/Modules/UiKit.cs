using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CncStudio.App.Modules;

/// <summary>Peças visuais comuns das telas novas: ícones de linha, cartões e fundo que acompanha o tema. Ícones desenhados para o Casillas.</summary>
public static class UiKit
{
    /// <summary>Caminhos de ícones (grade 24 × 24), só traço.</summary>
    private static readonly Dictionary<string, string> Icons = new()
    {
        ["lote"] = "M12 3l9 5-9 5-9-5 9-5zM3 13l9 5 9-5",
        ["relogio"] = "M12 4a8 8 0 1 0 0 16 8 8 0 0 0 0-16zM12 8v4l3 2",
        ["preparo"] = "M4 8h10M18 8h2M4 16h2M10 16h10M14 6v4M8 14v4",
        ["troca"] = "M20 12a8 8 0 0 1-14 5M4 12a8 8 0 0 1 14-5M18 3v4h-4M6 21v-4h4",
        ["caixa"] = "M4 8l8-4 8 4v8l-8 4-8-4zM4 8l8 4 8-4M12 12v8",
        ["percentual"] = "M6 18L18 6M8 6a2 2 0 1 0 0 .01M16 16a2 2 0 1 0 0 .01",
        ["ferramenta"] = "M12 3v8M9 11h6l-1 5h-4zM12 16v5",
        ["margem"] = "M3 17l6-6 4 4 8-9M15 6h6v6",
        ["imposto"] = "M7 3h8l4 4v14H7zM15 3v4h4M10 12h6M10 16h6",
        ["moeda"] = "M12 4a8 8 0 1 0 0 16 8 8 0 0 0 0-16zM12 7v10M9.5 9.5c0-1 1-1.5 2.5-1.5s2.5.8 2.5 1.8-1 1.4-2.5 1.7-2.5.7-2.5 1.8 1 1.7 2.5 1.7 2.5-.5 2.5-1.5",
    };

    /// <summary>Ícone de linha pelo nome; vazio se o nome não existe.</summary>
    public static Control Icon(string name, string color, double size = 18)
    {
        if (!Icons.TryGetValue(name, out string? data))
        {
            return new Border { Width = size, Height = size };
        }

        return new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(data),
            Stroke = new SolidColorBrush(Color.Parse(color)),
            StrokeThickness = 1.7,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    /// <summary>Define o fundo do bloco pelo recurso do tema (claro ou escuro).</summary>
    public static T Themed<T>(this T border, string resourceKey = "CardBackground")
        where T : Border
    {
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable(resourceKey));
        return border;
    }

    /// <summary>Cartão de resultado: título pequeno, ícone e valor grande, com faixa na cor do destaque.</summary>
    public static Border ResultCard(string title, string icon, string accent, TextBlock value)
    {
        value.FontSize = 26;
        value.FontWeight = FontWeight.Bold;
        value.Foreground = new SolidColorBrush(Color.Parse(accent));
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        head.Children.Add(Icon(icon, accent, 18));
        head.Children.Add(new TextBlock { Text = title, FontSize = 12, Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center });
        var stack = new StackPanel { Spacing = 4 };
        stack.Children.Add(head);
        stack.Children.Add(value);
        return new Border
        {
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10),
            BorderThickness = new Thickness(1.5),
            BorderBrush = new SolidColorBrush(Color.Parse(accent), 0.7),
            Child = stack,
        }.Themed();
    }
}
