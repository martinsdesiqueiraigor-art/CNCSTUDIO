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
        ["peca"] = "M4 8l8-4 8 4v8l-8 4-8-4zM4 8l8 4 8-4M12 12v8",
        ["bruto"] = "M4 8h13v11H4zM4 8l3-4h13l-3 4M20 4v11l-3 4",
        ["fixacao"] = "M4 19h16M7 19v-9h10v9M9 10V5h6v5",
        ["ferramentas"] = "M12 3v8M9 11h6l-1 5h-4zM12 16v5",
        ["operacoes"] = "M5 6h14M5 12h14M5 18h14",
        ["contorno"] = "M7 4h10a3 3 0 0 1 3 3v10a3 3 0 0 1-3 3H7a3 3 0 0 1-3-3V7a3 3 0 0 1 3-3z",
        ["bolsao"] = "M4 4h16v16H4zM9 9h6v6H9z",
        ["faceamento"] = "M3 7h18M3 12h18M3 17h18",
        ["furo"] = "M12 4a8 8 0 1 0 0 16 8 8 0 0 0 0-16zM12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6z",
        ["texto"] = "M5 6h14M12 6v13M9 19h6",
        ["rosca"] = "M6 5h12M6 9l12 2M6 13l12 2M6 17l12 2",
        ["canal"] = "M3 8h6v8h6V8h6",
        ["torneamento"] = "M3 6h18M3 18h18M8 6v12M17 6v8",
        ["perfil"] = "M3 18l5-8 4 4 4-9 5 8",
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

    /// <summary>Linha de árvore: ícone pequeno e texto.</summary>
    public static StackPanel IconLabel(string icon, string text, string color = "#8FB4FF", double size = 15)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(Icon(icon, color, size));
        row.Children.Add(new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }
}
