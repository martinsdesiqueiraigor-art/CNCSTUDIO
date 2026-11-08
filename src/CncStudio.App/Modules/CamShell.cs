using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CncStudio.Core.Cam;

namespace CncStudio.App.Modules;

/// <summary>
/// Peças comuns da casca do CAM (UX-04c fresa, UX-04e torno): abas Preparação/Operações/Código NC, faixa de ícones de 2 cores,
/// linha da árvore (nome, estado em 2ª linha, coluna fixa do olho), selo ✓/!, olho e as divisórias arrastáveis. Só tela.
/// </summary>
public static class CamShell
{
    /// <summary>Abas Preparação, Operações e Código NC; <paramref name="pick"/> recebe 0, 1 ou 2.</summary>
    public static StackPanel BuildTabs(Button[] tabs, Action<int> pick)
    {
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Bottom };
        string[] names = ["Preparação", "Operações", "Código NC"];
        for (int k = 0; k < names.Length; k++)
        {
            int index = k;
            var tab = new Button
            {
                Content = names[k], Padding = new Thickness(16, 7), FontSize = 13, CornerRadius = new CornerRadius(8, 8, 0, 0),
                BorderThickness = new Thickness(0, 0, 0, 2), Background = Brushes.Transparent,
            };
            tab.Click += (_, _) => pick(index);
            tabs[k] = tab;
            strip.Children.Add(tab);
        }

        return strip;
    }

    /// <summary>A aba escolhida fica laranja (seleção) com traço embaixo; as outras, cinza.</summary>
    public static void StyleTabs(Button[] tabs, int tab)
    {
        for (int k = 0; k < tabs.Length; k++)
        {
            bool on = k == tab;
            tabs[k].Foreground = Brush.Parse(on ? "#ffb066" : "#9aa7b5");
            tabs[k].BorderBrush = on ? Brush.Parse("#ff8a1f") : Brushes.Transparent;
            tabs[k].FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
        }
    }

    /// <summary>
    /// Monta a faixa grande a partir dos grupos do Core: ícone de 2 cores, nome e, nos botões que ainda não existem, o selo
    /// "em breve" (apagados). Devolve cada botão com o seu item, para a tela apagar o que não serve à seleção.
    /// </summary>
    public static List<(CamPaletteItem Item, Button Button)> BuildPalette(Panel palette, IEnumerable<CamPaletteGroup> groups, Func<CamPaletteItem, Task> click)
    {
        palette.Children.Clear();
        var list = new List<(CamPaletteItem, Button)>();
        bool first = true;
        foreach (CamPaletteGroup group in groups)
        {
            if (!first)
            {
                palette.Children.Add(new Border { Width = 1, Margin = new Thickness(6, 2), Background = Brush.Parse("#1f2733") });
            }

            first = false;
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            foreach (CamPaletteItem item in group.Items)
            {
                var content = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
                content.Children.Add(UiKit.Icon2(item.Icon, "#e6edf3", "#ff8a1f", 24));
                content.Children.Add(new TextBlock
                {
                    Text = item.Name, FontSize = 11, LineHeight = 13, MaxLines = 2, TextWrapping = TextWrapping.Wrap,
                    TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
                });
                if (item.Soon)
                {
                    content.Children.Add(new TextBlock { Text = "em breve", FontSize = 10, Foreground = Brush.Parse("#9aa7b5"), HorizontalAlignment = HorizontalAlignment.Center });
                }

                // 66 px, ou mais se a palavra mais longa do nome não couber (nunca quebrar no meio da palavra: "Torneamento")
                double word = item.Name.Split(' ').Max(w =>
                {
                    var probe = new TextBlock { Text = w, FontSize = 11 };
                    probe.Measure(Size.Infinity);
                    return probe.DesiredSize.Width;
                });
                var button = new Button
                {
                    Content = content, Width = Math.Max(66, Math.Ceiling(word) + 10), Height = 76, Padding = new Thickness(2, 4), MinHeight = 0,
                    VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center,
                    Background = Brush.Parse("#18202b"), BorderBrush = Brush.Parse("#243040"),
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
                    Opacity = item.Soon ? 0.45 : 1,
                };
                ToolTip.SetShowOnDisabled(button, true);
                Avalonia.Automation.AutomationProperties.SetName(button, item.Action is not null ? item.Name : item.Soon ? item.Name + " (em breve)" : "Adicionar " + item.Name);
                CamPaletteItem picked = item;
                button.Click += async (_, _) => await click(picked);
                buttons.Children.Add(button);
                list.Add((item, button));
            }

            var box = new StackPanel { Spacing = 3 };
            box.Children.Add(buttons);
            box.Children.Add(new TextBlock { Text = group.Title.ToUpperInvariant(), FontSize = 10, Foreground = Brush.Parse("#9aa7b5"), HorizontalAlignment = HorizontalAlignment.Center });
            palette.Children.Add(box);
        }

        return list;
    }

    /// <summary>Liga ou apaga o botão da faixa; a dica diz o porquê.</summary>
    public static void SetPaletteState(CamPaletteItem item, Button button, string? why)
    {
        button.IsEnabled = why is null;
        ToolTip.SetTip(button, why is null ? item.Hint : $"{item.Hint}\nAgora não: {why}");
    }

    /// <summary>
    /// Linha da árvore em 240 px: ícone (e selo) à esquerda, nome com reticências (a dica mostra inteiro), estado em segunda linha
    /// pequena e uma coluna fixa à direita (olho nas operações), que nunca cobre o nome.
    /// </summary>
    public static Grid TreeRow(string title, (string Kind, string Text)? state, Control? lead, Control? right = null, string? tip = null)
    {
        var g = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,24"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        if (lead is not null)
        {
            lead.Margin = new Thickness(0, 0, 6, 0);
            lead.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRowSpan(lead, 2);
            g.Children.Add(lead);
        }

        var text = new TextBlock { Text = title, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        if (state is { } st)
        {
            var small = new TextBlock { Text = st.Text, FontSize = 10.5, Foreground = UiKit.Status(st.Kind).Fg, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(small, 1);
            Grid.SetRow(small, 1);
            g.Children.Add(small);
        }

        if (right is not null)
        {
            Grid.SetColumn(right, 2);
            Grid.SetRowSpan(right, 2);
            right.HorizontalAlignment = HorizontalAlignment.Center;
            g.Children.Add(right);
        }

        ToolTip.SetTip(text, tip ?? (state is { } s2 ? title + "\n" + s2.Text : title));
        return g;
    }

    /// <summary>Selo redondo da operação: "✓" pronta, "!" incompleta, "–" desligada.</summary>
    public static Border Badge(string mark, string kind)
    {
        (IBrush fg, IBrush bg) = UiKit.Status(kind);
        return new Border
        {
            Width = 17, Height = 17, CornerRadius = new CornerRadius(9), Background = bg, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = mark, FontSize = 11, FontWeight = FontWeight.Bold, Foreground = fg, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
    }

    /// <summary>Olho da operação: liga e desliga (o mesmo Enabled de antes).</summary>
    public static Button Eye(bool enabled, Action toggle)
    {
        var eye = new Button
        {
            Content = UiKit.Icon(enabled ? "olho" : "olho-fechado", enabled ? "#9aa7b5" : "#5b6675", 15),
            Padding = new Thickness(3), MinHeight = 0, MinWidth = 0, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        string eyeText = enabled ? "Desligar a operação (não entra no programa)" : "Ligar a operação";
        ToolTip.SetTip(eye, eyeText);
        Avalonia.Automation.AutomationProperties.SetName(eye, eyeText);
        eye.Click += (_, _) => toggle();
        return eye;
    }
}

/// <summary>
/// Divisórias arrastáveis árvore | centro | painel (UX-04c): árvore 200–420 (padrão 240), painel 260–520 (padrão 290), centro
/// ≥ 480. Duplo clique volta ao padrão. As larguras ficam nas preferências do app (config.json), nunca no .casillas-job.
/// </summary>
public sealed class CamPanes
{
    private const double TreeDefault = 240, TreeMin = 200, TreeMax = 420;
    private const double PropsDefault = 290, PropsMin = 260, PropsMax = 520;
    private const double CenterMin = 480, SplitterWidth = 5;

    private readonly Grid _panes;
    private readonly GridSplitter _propsSplitter;
    private readonly Func<bool> _propsVisible;

    /// <summary>Larguras escolhidas (arrastadas ou guardadas); a tela pode mostrar menos se a janela for estreita.</summary>
    private double _treeWidth = TreeDefault, _propsWidth = PropsDefault;

    /// <summary>Grade com 5 colunas: árvore, divisória, centro, divisória, painel.</summary>
    public CamPanes(Grid panes, GridSplitter treeSplitter, GridSplitter propsSplitter, Func<bool> propsVisible)
    {
        _panes = panes;
        _propsSplitter = propsSplitter;
        _propsVisible = propsVisible;
        treeSplitter.DragCompleted += (_, _) => Dragged();
        propsSplitter.DragCompleted += (_, _) => Dragged();
        treeSplitter.DoubleTapped += (_, _) => Reset();
        propsSplitter.DoubleTapped += (_, _) => Reset();
        panes.SizeChanged += (_, _) => Fit();
    }

    /// <summary>Guarda as larguras (árvore, painel) nas preferências do app; (0, 0) = padrão.</summary>
    public Action<double, double>? Save { get; set; }

    /// <summary>Larguras guardadas (0 = padrão); fora dos limites, volta ao limite.</summary>
    public void Set(double tree, double props)
    {
        _treeWidth = tree > 0 ? Math.Clamp(tree, TreeMin, TreeMax) : TreeDefault;
        _propsWidth = props > 0 ? Math.Clamp(props, PropsMin, PropsMax) : PropsDefault;
        Fit();
    }

    private void Dragged()
    {
        _treeWidth = Math.Clamp(_panes.ColumnDefinitions[0].ActualWidth, TreeMin, TreeMax);
        if (_propsVisible())
        {
            _propsWidth = Math.Clamp(_panes.ColumnDefinitions[4].ActualWidth, PropsMin, PropsMax);
        }

        Save?.Invoke(_treeWidth, _propsWidth);
        Fit();
    }

    private void Reset()
    {
        _treeWidth = TreeDefault;
        _propsWidth = PropsDefault;
        Save?.Invoke(0, 0);
        Fit();
    }

    /// <summary>
    /// Aplica as larguras sem passar da janela: o centro fica com pelo menos 480 px; se não couber, o painel e depois a árvore
    /// encolhem até o mínimo, e só então o centro cede (nada fica além da borda direita). Fora da aba Operações o painel some.
    /// </summary>
    public void Fit()
    {
        bool props = _propsVisible();
        double avail = _panes.Bounds.Width;
        double tree = _treeWidth, side = props ? _propsWidth : 0;
        double fixedParts = SplitterWidth + (props ? SplitterWidth : 0);
        if (avail > 0)
        {
            double over = tree + side + fixedParts + CenterMin - avail;
            if (over > 0 && props)
            {
                double cut = Math.Min(over, side - PropsMin);
                side -= cut;
                over -= cut;
            }

            if (over > 0)
            {
                tree -= Math.Min(over, tree - TreeMin);
            }
        }

        _panes.ColumnDefinitions[0].Width = new GridLength(tree);
        _panes.ColumnDefinitions[4].MinWidth = props ? PropsMin : 0;
        _panes.ColumnDefinitions[4].Width = new GridLength(side);
        _panes.ColumnDefinitions[3].Width = new GridLength(props ? SplitterWidth : 0);
        _propsSplitter.IsVisible = props;
        _panes.ColumnDefinitions[2].MinWidth = avail > 0 ? Math.Max(0, Math.Min(CenterMin, avail - tree - side - fixedParts)) : 0;
    }
}
