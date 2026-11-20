using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CncStudio.App.Modules;

/// <summary>
/// UI-PRO-P1a: abas do painel de propriedades da operação (Ferramenta, Corte, Alturas, Estratégia). Cada aba tem o próprio painel;
/// só aparecem as abas que têm campo para a operação. Uma aba com campo vazio ou inválido ganha a marca "●" (o motivo vai na dica),
/// para o campo não ficar escondido em outra aba.
/// </summary>
internal sealed class OpTabs
{
    private readonly Dictionary<string, Button> _buttons = [];
    private readonly Dictionary<string, TextBlock> _marks = [];
    private readonly Dictionary<string, StackPanel> _panels = [];
    private readonly Dictionary<string, string> _titles = [];
    private readonly Action<string> _onSelect;

    /// <summary>Barra das abas mais os painéis, para entrar no painel de propriedades.</summary>
    public Control Root { get; }

    /// <summary>Chave da aba aberta.</summary>
    public string Selected { get; private set; }

    /// <param name="tabs">Abas desta operação, na ordem da tela.</param>
    /// <param name="selected">Aba que estava aberta; se esta operação não a tem, abre a primeira.</param>
    /// <param name="onSelect">Avisado quando o usuário troca de aba (a tela lembra a escolha ao trocar de operação).</param>
    public OpTabs(IReadOnlyList<(string Key, string Title)> tabs, string? selected, Action<string> onSelect)
    {
        _onSelect = onSelect;
        Selected = tabs.Any(t => t.Key == selected) ? selected! : tabs[0].Key;
        var bar = new Grid { ColumnSpacing = 0 };
        var root = new StackPanel { Spacing = 8 };
        var content = new StackPanel { Spacing = 6 };
        for (int c = 0; c < tabs.Count; c++)
        {
            (string key, string title) = tabs[c];
            bar.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var label = new TextBlock { Text = title, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            // a marca fica no canto de cima, por cima do texto: o rótulo não perde largura (a aba tem ~66 px)
            var mark = new TextBlock { Text = "●", FontSize = 8, Foreground = CamColors.PendingTextBrush, IsVisible = false, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 1, 0) };
            var inner = new Grid();
            inner.Children.Add(label);
            inner.Children.Add(mark);
            var button = new Button
            {
                Content = inner, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = Brushes.Transparent, CornerRadius = new CornerRadius(0), Padding = new Thickness(1, 6), MinHeight = 0,
                BorderThickness = new Thickness(0, 0, 0, 2), Tag = key,
            };
            Avalonia.Automation.AutomationProperties.SetName(button, "Aba " + title);
            button.Click += (_, _) => Select(key);
            Grid.SetColumn(button, c);
            bar.Children.Add(button);
            _buttons[key] = button;
            _marks[key] = mark;
            _titles[key] = title;
            var panel = new StackPanel { Spacing = 6 };
            _panels[key] = panel;
            content.Children.Add(panel);
        }

        root.Children.Add(new Border { Child = bar, BorderBrush = CamColors.BorderBrush, BorderThickness = new Thickness(0, 0, 0, 1) });
        root.Children.Add(content);
        Root = root;
        Paint();
    }

    /// <summary>Painel onde entram os campos da aba <paramref name="key"/>.</summary>
    public StackPanel Panel(string key) => _panels[key];

    /// <summary>Abre a aba (sem chamar de volta a tela quando já está aberta).</summary>
    public void Select(string key)
    {
        if (!_panels.ContainsKey(key) || key == Selected)
        {
            return;
        }

        Selected = key;
        Paint();
        _onSelect(key);
    }

    /// <summary>Liga ou desliga a marca da aba: <paramref name="why"/> não nulo = tem campo faltando (vai na dica).</summary>
    public void Mark(string key, string? why)
    {
        if (!_marks.TryGetValue(key, out TextBlock? mark))
        {
            return;
        }

        mark.IsVisible = why is not null;
        ToolTip.SetTip(_buttons[key], why);
        Avalonia.Automation.AutomationProperties.SetName(_buttons[key], "Aba " + _titles[key] + (why is null ? "" : ", " + why));
    }

    private void Paint()
    {
        foreach ((string key, Button b) in _buttons)
        {
            bool on = key == Selected;
            b.BorderBrush = on ? CamColors.AccentBrush : Brushes.Transparent;
            b.Foreground = on ? CamColors.TextBrush : CamColors.MutedBrush;
            b.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            _panels[key].IsVisible = on;
        }
    }
}
