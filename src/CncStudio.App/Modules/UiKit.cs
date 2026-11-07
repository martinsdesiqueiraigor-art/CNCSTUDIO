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
        // faixa de operações do Trabalho (UX-03a): peça cheia + caminho da ferramenta com seta
        ["contorno-fora"] = "M8 8h8v8H8zM4 13V6a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2H9M11 18l-2 2 2 2",
        ["contorno-dentro"] = "M3 3h18v18H3zM7 12V8a1 1 0 0 1 1-1h8a1 1 0 0 1 1 1v8a1 1 0 0 1-1 1h-4M14 15l-2 2 2 2",
        ["contorno-linha"] = "M4 6h16v14H4zM12 3.5a2.5 2.5 0 1 0 0 5 2.5 2.5 0 0 0 0-5z",
        ["texto-cilindro"] = "M5 6a7 2.5 0 1 0 14 0 7 2.5 0 1 0-14 0zM5 6v12a7 2.5 0 0 0 14 0V6M9 12h6M12 12v5",
        ["varredura"] = "M3 18c3-7 6-9 9-9s6 2 9 9M5 15l2-4 2 4 2-4 2 4 2-4 2 4",
        ["desbaste-niveis"] = "M3 20h18M5 20v-4h14v4M8 16v-4h8v4M11 12V8h2v4",
        ["rosca"] = "M6 5h12M6 9l12 2M6 13l12 2M6 17l12 2",
        ["canal"] = "M3 8h6v8h6V8h6",
        ["torneamento"] = "M3 6h18M3 18h18M8 6v12M17 6v8",
        ["perfil"] = "M3 18l5-8 4 4 4-9 5 8",
        // árvore do Trabalho (UX-04c)
        ["olho"] = "M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12zM12 9a3 3 0 1 0 0 6 3 3 0 0 0 0-6z",
        ["olho-fechado"] = "M2 12s4-7 10-7 10 7 10 7-4 7-10 7S2 12 2 12zM4 4l16 16",
        ["material"] = "M4 7l8-4 8 4-8 4zM4 12l8 4 8-4M4 17l8 4 8-4",
        ["zero"] = "M12 3v18M3 12h18M9 12a3 3 0 1 0 6 0 3 3 0 1 0-6 0",
        ["moeda"] ="M12 4a8 8 0 1 0 0 16 8 8 0 0 0 0-16zM12 7v10M9.5 9.5c0-1 1-1.5 2.5-1.5s2.5.8 2.5 1.8-1 1.4-2.5 1.7-2.5.7-2.5 1.8 1 1.7 2.5 1.7 2.5-.5 2.5-1.5",
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

    /// <summary>
    /// Ícones de 2 cores da faixa do Trabalho (UX-04c), grade 24 × 24: traço neutro (peça, máquina) e detalhe no destaque
    /// (o que a operação faz). Desenhados para o Casillas.
    /// </summary>
    private static readonly Dictionary<string, (string Base, string Accent)> Icons2 = new()
    {
        ["abrir-dxf"] = ("M6 3h8l4 4v14H6zM14 3v4h4", "M9 12l3 3 3-3M12 15V9"),
        ["abrir"] = ("M3 7h6l2 2h10v10H3z", "M3 12h18"),
        ["salvar"] = ("M5 3h12l2 2v16H5z", "M8 3v6h8V3M8 21v-7h8v7"),
        ["nova-fixacao"] = ("M3 18h18M6 18V8h4v10M14 18V8h4v10", "M10 13h4"),
        ["zero"] = ("M12 3v18M3 12h18", "M9 12a3 3 0 1 0 6 0 3 3 0 1 0-6 0"),
        ["faceamento"] = ("M4 19h16", "M4 6h16M4 10h16M4 14h16"),
        ["contorno-fora"] = ("M7 7h10v10H7z", "M4 4h16v16H4z"),
        ["contorno-dentro"] = ("M3 3h18v18H3z", "M7 7h10v10H7z"),
        ["contorno-linha"] = ("M4 4h16v16H4z", "M12 2v4M12 18v4M2 12h4M18 12h4"),
        ["bolsao"] = ("M4 4h16v16H4z", "M8.5 8.5h7v7h-7z"),
        ["chanfro"] = ("M4 20V10l6-6h10", "M4 10l6-6"),
        ["ranhura"] = ("M3 8h18v8H3z", "M7 12h10"),
        ["furo"] = ("M12 3v6M12 15v6", "M8 12a4 4 0 1 0 8 0 4 4 0 1 0-8 0"),
        ["macho"] = ("M12 3v18", "M8 7l8 2M8 11l8 2M8 15l8 2"),
        ["mandrilhar"] = ("M9 3h6v10l-3 5-3-5z", "M12 18v3"),
        ["texto-cilindro"] = ("M5 6a7 2.5 0 1 0 14 0 7 2.5 0 1 0-14 0zM5 6v12a7 2.5 0 0 0 14 0V6", "M9 12h6M12 12v5"),
    };

    /// <summary>Ícone de 2 cores (traço <paramref name="color"/>, detalhe <paramref name="accent"/>); sem o nome, cai no ícone de linha.</summary>
    public static Control Icon2(string name, string color, string accent, double size = 24)
    {
        if (!Icons2.TryGetValue(name, out (string Base, string Accent) d))
        {
            return Icon(name, color, size);
        }

        Avalonia.Controls.Shapes.Path Part(string data, string c) => new()
        {
            Data = Geometry.Parse(data),
            Stroke = new SolidColorBrush(Color.Parse(c)),
            StrokeThickness = 1.6,
            StrokeLineCap = PenLineCap.Round,
            StrokeJoin = PenLineJoin.Round,
        };

        // as duas partes na mesma grade 24 × 24 (sem esticar cada uma), escaladas juntas
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(Part(d.Base, color));
        canvas.Children.Add(Part(d.Accent, accent));
        return new Viewbox { Width = size, Height = size, Child = canvas, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    }

    /// <summary>Cores de estado ("ok", "bad", "pending", outro = neutro) legíveis no tema atual: texto e fundo da etiqueta.</summary>
    public static (IBrush Fg, IBrush Bg) Status(string kind)
    {
        bool light = Avalonia.Application.Current?.ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light;
        (string fg, string bg) = (kind, light) switch
        {
            ("ok", false) => ("#2EE6A6", "#0D2A25"),
            ("bad", false) => ("#FF6B6B", "#33181D"),
            ("pending", false) => ("#FFC247", "#2A2314"),
            (_, false) => ("#A3B1C9", "#18263D"),
            ("ok", true) => ("#0E7A55", "#DDF4EA"),
            ("bad", true) => ("#B3261E", "#FBE3E1"),
            ("pending", true) => ("#8A5A00", "#FFEFCC"),
            _ => ("#4A5668", "#E6EAF1"),
        };
        return (Brush.Parse(fg), Brush.Parse(bg));
    }

    /// <summary>Etiqueta de estado da árvore ("OK", "desligada", "erro: motivo", "não verificada"): sempre com texto, nunca só cor.</summary>
    public static Border StateTag((string Kind, string Text) st)
    {
        // "erro: motivo" mostra só "erro" (o estado inteiro, nunca cortado); o motivo vai na dica e na barra inferior
        int cut = st.Text.IndexOf(": ", StringComparison.Ordinal);
        string shown = st.Kind == "bad" && cut > 0 ? st.Text[..cut] : st.Text;
        var text = new TextBlock { Text = shown, FontSize = 11 };
        ToolTip.SetTip(text, st.Text);
        var tag = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(7, 1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = text,
        };
        return FollowTheme(tag, st.Kind, text);
    }

    /// <summary>
    /// Linha de árvore com estado: controles à esquerda (caixa, ícone), título com reticências no meio e etiqueta inteira à direita.
    /// Precisa da árvore sem rolagem horizontal e do item esticado (<see cref="StateItem"/>).
    /// </summary>
    public static DockPanel StateRow(string title, (string Kind, string Text) st, params Control[] lead)
    {
        var row = new DockPanel();
        foreach (Control c in lead)
        {
            DockPanel.SetDock(c, Dock.Left);
            c.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(c);
        }

        Border tag = StateTag(st);
        DockPanel.SetDock(tag, Dock.Right);
        tag.Margin = new Thickness(6, 0, 0, 0);
        row.Children.Add(tag);
        var text = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(text, title);
        row.Children.Add(text);
        return row;
    }

    /// <summary>Item de árvore que ocupa a largura da árvore (para a etiqueta de <see cref="StateRow"/> ficar sempre à vista).</summary>
    public static TreeViewItem StateItem(Control header, object? tag = null) =>
        new() { Header = header, Tag = tag, IsExpanded = true };

    /// <summary>Aviso em bloco com as cores de estado do tema (claro, escuro e CMYK); acompanha a troca de tema.</summary>
    public static Border StatusNote(string text, string kind = "pending")
    {
        var label = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        var note = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = label,
        };
        return FollowTheme(note, kind, label);
    }

    private static Border FollowTheme(Border border, string kind, TextBlock text)
    {
        void Apply()
        {
            (IBrush fg, IBrush bg) = Status(kind);
            border.Background = bg;
            text.Foreground = fg;
            if (border.BorderThickness != default && fg is ISolidColorBrush s)
            {
                border.BorderBrush = new SolidColorBrush(s.Color, 0.55);
            }
        }

        Apply();
        border.ActualThemeVariantChanged += (_, _) => Apply();
        return border;
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
