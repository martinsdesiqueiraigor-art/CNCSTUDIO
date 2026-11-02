using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace CncStudio.App.Modules;

/// <summary>Item da barra de pendências: "pending" (âmbar) ou "bad" (vermelho), texto e para onde o clique leva.</summary>
public sealed record JobBarItem(string Kind, string Text, Action Go);

/// <summary>
/// Barra inferior dos Trabalhos (fresa e torno, UX-01): resumo do que falta, itens clicáveis, estado da trajetória, do G-code e da
/// simulação (só o que o app sabe de fato) e os botões "Enviar ao Code para simular" e "Gerar G-code". Cor sempre com texto.
/// </summary>
public sealed class JobBar : Border
{
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private readonly WrapPanel _items = new();
    private readonly StackPanel _chips = new() { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly TextBlock _reason = new() { FontSize = 11, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, MaxWidth = 340, HorizontalAlignment = HorizontalAlignment.Right };
    private IReadOnlyList<JobBarItem> _last = [];
    private (int Ready, bool Trajectory, bool GCode) _state;

    public Button SendButton { get; } = new() { Content = "Enviar ao Code para simular", Padding = new Thickness(14, 7) };

    public Button GenerateButton { get; } = new() { Content = "Gerar G-code", Padding = new Thickness(14, 7), FontWeight = FontWeight.SemiBold };

    public JobBar()
    {
        Margin = new Thickness(12, 0, 12, 10);
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(12, 8);
        BorderThickness = new Thickness(1);
        this.Themed();
        this.Bind(BorderBrushProperty, this.GetResourceObservable("SubtleBorder"));

        var left = new StackPanel { Spacing = 4 };
        left.Children.Add(_summary);
        left.Children.Add(_items);
        left.Children.Add(new TextBlock { Text = "Fixação e colisões: o app não verifica. Simule e rode a primeira vez no ar.", FontSize = 11, Opacity = 0.6, TextWrapping = TextWrapping.Wrap });

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(SendButton);
        buttons.Children.Add(GenerateButton);
        var right = new StackPanel { Spacing = 6, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(_chips);
        right.Children.Add(buttons);
        right.Children.Add(_reason);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        grid.Children.Add(left);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        Child = grid;
        ActualThemeVariantChanged += (_, _) => Draw();
    }

    /// <summary>
    /// <paramref name="items"/>: o que impede de gerar (vazio = pronto). <paramref name="ready"/>: operações ligadas e geradas.
    /// <paramref name="trajectory"/> e <paramref name="gcode"/>: o que o app calculou agora. A simulação o Trabalho não vê: "não verificado".
    /// </summary>
    public void Update(IReadOnlyList<JobBarItem> items, int ready, bool trajectory, bool gcode)
    {
        _last = items;
        _state = (ready, trajectory, gcode);
        Draw();
    }

    private void Draw()
    {
        IReadOnlyList<JobBarItem> items = _last;
        bool ok = items.Count == 0;
        string worst = ok ? "ok" : items.Any(x => x.Kind == "bad") ? "bad" : "pending";
        _summary.Inlines!.Clear();
        _summary.Inlines.Add(new Run(ok ? "Pronto para gerar." : $"Não dá para gerar o G-code: {items.Count} {(items.Count == 1 ? "item" : "itens")}.")
        {
            FontWeight = FontWeight.Bold,
            Foreground = UiKit.Status(worst).Fg,
        });
        _summary.Inlines.Add(new Run(ok
            ? $" {_state.Ready} {(_state.Ready == 1 ? "operação em dia" : "operações em dia")}, nenhuma pendência. Primeira execução no ar."
            : " Clique para ir ao item."));

        _items.Children.Clear();
        _items.IsVisible = !ok;
        foreach (JobBarItem item in items)
        {
            _items.Children.Add(ItemButton(item));
        }

        _chips.Children.Clear();
        _chips.Children.Add(UiKit.StateTag(_state.Trajectory ? ("ok", "Trajetória ✓") : ("pending", "Trajetória: não gerada")));
        _chips.Children.Add(UiKit.StateTag(_state.GCode ? ("ok", "G-code ✓") : ("pending", "G-code: não gerado")));
        _chips.Children.Add(UiKit.StateTag(("pending", "Simulado: não verificado")));

        bool can = ok && _state.GCode;
        SendButton.IsEnabled = GenerateButton.IsEnabled = can;
        _reason.Text = can
            ? "Simule no Code antes de rodar na máquina."
            : ok ? "Sem G-code para enviar: gere o programa primeiro." : "Botões desligados: resolva os itens à esquerda.";
    }

    /// <summary>Item clicável com a mesma redação e as mesmas cores do Modelo 3D ("pendente: …", "✕ inválido: …").</summary>
    public static Button ItemButton(JobBarItem item)
    {
        (IBrush fg, IBrush bg) = UiKit.Status(item.Kind);
        var go = new Button
        {
            Content = new TextBlock { Text = (item.Kind == "bad" ? "✕ inválido: " : "pendente: ") + item.Text, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = fg, MaxWidth = 520 },
            Background = bg,
            Padding = new Thickness(8, 3),
            Margin = new Thickness(0, 2, 6, 2),
            CornerRadius = new CornerRadius(8),
            MinHeight = 26,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        ToolTip.SetTip(go, "Ir ao item");
        go.Click += (_, _) => item.Go();
        return go;
    }
}
