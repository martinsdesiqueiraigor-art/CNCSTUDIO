using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace CncStudio.App.Modules;

/// <summary>
/// Item da barra de pendências: "pending" (âmbar) ou "bad" (vermelho), texto e para onde o clique leva. <see cref="Blocking"/> falso
/// = aviso: tira o "Pronto para gerar", mas não desliga os botões (ex.: fixação não definida).
/// </summary>
public sealed record JobBarItem(string Kind, string Text, Action Go, bool Blocking = true);

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

    private bool _compact;

    /// <summary>
    /// Uma linha só (Trabalho fresa, UX-04c): resumo à esquerda, itens clicáveis numa faixa que rola, botões à direita. Os avisos
    /// (fixação e colisão não verificadas, simular antes) vão para a dica do resumo e dos botões. O torno continua no modo normal.
    /// </summary>
    public bool Compact
    {
        get => _compact;
        set
        {
            if (value && !_compact)
            {
                _compact = true;
                BuildCompact();
                Draw();
            }
        }
    }

    private const string NotVerified = "Fixação e colisões: o app não verifica. Simule e rode a primeira vez no ar.";

    private void BuildCompact()
    {
        Margin = new Thickness(8, 0, 8, 4);
        Padding = new Thickness(10, 4);
        CornerRadius = new CornerRadius(8);
        _summary.TextWrapping = TextWrapping.NoWrap;
        _summary.VerticalAlignment = VerticalAlignment.Center;
        foreach (Control c in new Control[] { _summary, _items, _chips, _reason, SendButton, GenerateButton })
        {
            (c.Parent as Panel)?.Children.Remove(c);
        }

        SendButton.Padding = GenerateButton.Padding = new Thickness(12, 5);
        ToolTip.SetShowOnDisabled(SendButton, true);
        ToolTip.SetShowOnDisabled(GenerateButton, true);
        var strip = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = _items,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(SendButton);
        buttons.Children.Add(GenerateButton);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 10 };
        grid.Children.Add(_summary);
        Grid.SetColumn(strip, 1);
        grid.Children.Add(strip);
        Grid.SetColumn(buttons, 2);
        grid.Children.Add(buttons);
        Child = grid;
    }

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
        int blocking = items.Count(x => x.Blocking);
        string worst = ok ? "ok" : items.Any(x => x.Kind == "bad") ? "bad" : "pending";
        string title = ok
            ? "Pronto para gerar."
            : blocking > 0
                ? $"Não dá para gerar o G-code: {blocking} {(blocking == 1 ? "item" : "itens")}."
                : $"G-code gerado, com {items.Count} {(items.Count == 1 ? "aviso" : "avisos")}.";
        _summary.Inlines!.Clear();
        if (_compact)
        {
            DrawCompact(items, ok, blocking, worst);
            return;
        }

        _summary.Inlines.Add(new Run(title) { FontWeight = FontWeight.Bold, Foreground = UiKit.Status(worst).Fg });
        _summary.Inlines.Add(new Run(ok
            ? $" {_state.Ready} {(_state.Ready == 1 ? "operação em dia" : "operações em dia")}, nenhuma pendência. Primeira execução no ar."
            : blocking > 0 ? " Clique para ir ao item." : " Confira antes de rodar; clique para ir ao item."));

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

        // só o que trava desliga os botões; aviso (Blocking falso) deixa gerar e enviar
        bool can = blocking == 0 && _state.GCode;
        SendButton.IsEnabled = GenerateButton.IsEnabled = can;
        _reason.Text = can
            ? "Simule no Code antes de rodar na máquina."
            : blocking == 0 ? "Sem G-code para enviar: gere o programa primeiro." : "Botões desligados: resolva os itens à esquerda.";
    }

    private void DrawCompact(IReadOnlyList<JobBarItem> items, bool ok, int blocking, string worst)
    {
        int ready = _state.Ready;
        string ops = $"{ready} {(ready == 1 ? "operação" : "operações")}";
        string title = ok
            ? "Pronto para gerar · " + ops
            : blocking > 0
                ? $"Não dá para gerar: {blocking} {(blocking == 1 ? "item" : "itens")}"
                : $"G-code gerado · {items.Count} {(items.Count == 1 ? "aviso" : "avisos")}";
        _summary.Inlines!.Add(new Run(title) { FontWeight = FontWeight.Bold, Foreground = UiKit.Status(worst).Fg });
        string state = (_state.Trajectory ? "Trajetória ✓" : "Trajetória: não gerada") + " · " + (_state.GCode ? "G-code ✓" : "G-code: não gerado") + " · Simulado: não verificado";
        ToolTip.SetTip(_summary, state + "\n" + NotVerified + (ok ? "" : "\nClique nos itens ao lado para ir a cada um."));

        _items.Children.Clear();
        _items.IsVisible = !ok;
        foreach (JobBarItem item in items)
        {
            _items.Children.Add(ItemButton(item, compact: true));
        }

        bool can = blocking == 0 && _state.GCode;
        SendButton.IsEnabled = GenerateButton.IsEnabled = can;
        string reason = can
            ? "Simule no Code antes de rodar na máquina."
            : blocking == 0 ? "Sem G-code para enviar: gere o programa primeiro." : "Botões desligados: resolva os itens à esquerda.";
        ToolTip.SetTip(SendButton, reason + "\n" + NotVerified);
        ToolTip.SetTip(GenerateButton, reason);
    }

    /// <summary>Item clicável com a mesma redação e as mesmas cores do Modelo 3D ("pendente: …", "✕ inválido: …").</summary>
    public static Button ItemButton(JobBarItem item, bool compact = false)
    {
        (IBrush fg, IBrush bg) = UiKit.Status(item.Kind);
        string text = (item.Kind == "bad" ? "✕ inválido: " : "pendente: ") + item.Text;
        var go = new Button
        {
            Content = new TextBlock
            {
                Text = text, FontSize = 12, Foreground = fg, MaxWidth = compact ? 320 : 520,
                TextWrapping = compact ? TextWrapping.NoWrap : TextWrapping.Wrap, TextTrimming = compact ? TextTrimming.CharacterEllipsis : TextTrimming.None,
            },
            Background = bg,
            Padding = new Thickness(8, 3),
            Margin = new Thickness(0, 2, 6, 2),
            CornerRadius = new CornerRadius(8),
            MinHeight = 26,
            Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
        };
        ToolTip.SetTip(go, compact ? text + "\nIr ao item" : "Ir ao item");
        go.Click += (_, _) => item.Go();
        return go;
    }
}
