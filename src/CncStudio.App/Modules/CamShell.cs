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
            tabs[k].Foreground = on ? CamColors.AccentTextBrush : CamColors.MutedBrush;
            tabs[k].BorderBrush = on ? CamColors.AccentBrush : Brushes.Transparent;
            tabs[k].FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
        }
    }

    /// <summary>Altura dos botões da faixa (UI-02: mais baixa que a do UX-04c, para sobrar tela em 1280×720).</summary>
    public const double PaletteButtonHeight = 62;

    /// <summary>
    /// Monta a faixa a partir dos grupos do Core, na mesma ordem: ícone de 2 cores e nome. UI-02, para caber em 1280×720 sem
    /// rolar: o grupo só de arquivo (Novo, Abrir…) vira uma grade compacta 2 × 2; o botão "em breve" fica estreito, só com o ícone
    /// apagado (o nome e o "em breve" vão para a dica). Devolve cada botão com o seu item, para a tela apagar o que não serve.
    /// </summary>
    public static List<(CamPaletteItem Item, Button Button)> BuildPalette(Panel palette, IEnumerable<CamPaletteGroup> groups, Func<CamPaletteItem, Task> click, double minButtonWidth = 60, bool opsMenu = false)
    {
        palette.Children.Clear();
        var list = new List<(CamPaletteItem, Button)>();
        bool first = true;
        StackPanel? menuPanel = null; // UI-PRO-P3a: com opsMenu, os grupos de operação viram um menu "Operações" (mesmos botões, mesma lista)
        foreach (CamPaletteGroup group in groups)
        {
            bool inMenu = opsMenu && group.Id is not ("arquivo" or "fixacao") && group.Items.Any(i => i.Action is null);
            if (inMenu)
            {
                if (menuPanel is null)
                {
                    if (!first)
                    {
                        palette.Children.Add(new Border { Width = 1, Margin = new Thickness(6, 4, 6, 16), Background = CamColors.BorderBrush });
                    }

                    first = false;
                    menuPanel = new StackPanel { Spacing = 1, MinWidth = 210 };
                    palette.Children.Add(OperationsMenuBox(menuPanel));
                }

                AddMenuGroup(menuPanel, group, click, list);
                continue;
            }

            if (!first)
            {
                palette.Children.Add(new Border { Width = 1, Margin = new Thickness(6, 4, 6, 16), Background = CamColors.BorderBrush });
            }

            first = false;
            bool fileGroup = group.Items.Count > 0 && group.Items.All(i => i.Action is not null);
            Panel buttons = fileGroup
                ? new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), RowDefinitions = new RowDefinitions("*,*"), ColumnSpacing = 3, RowSpacing = 3, Height = PaletteButtonHeight }
                : new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
            int n = 0;
            foreach (CamPaletteItem item in group.Items)
            {
                Button button = fileGroup ? FileButton(item) : item.Soon ? SoonButton(item) : OpButton(item, minButtonWidth);
                if (fileGroup)
                {
                    Grid.SetColumn(button, n % 2);
                    Grid.SetRow(button, n / 2);
                }

                n++;
                ToolTip.SetShowOnDisabled(button, true);
                Avalonia.Automation.AutomationProperties.SetName(button, item.Action is not null ? item.Name : item.Soon ? item.Name + " (em breve)" : "Adicionar " + item.Name);
                CamPaletteItem picked = item;
                button.Click += async (_, _) => await click(picked);
                buttons.Children.Add(button);
                list.Add((item, button));
            }

            var box = new StackPanel { Spacing = 2 };
            box.Children.Add(buttons);
            box.Children.Add(new TextBlock { Text = group.Title.ToUpperInvariant(), FontSize = 10, Foreground = CamColors.MutedBrush, HorizontalAlignment = HorizontalAlignment.Center });
            palette.Children.Add(box);
        }

        return list;
    }

    private static Button PaletteButton(Control content, double width, double height) => new()
    {
        Content = content, Width = width, Height = height, Padding = new Thickness(2, 3), MinHeight = 0,
        VerticalContentAlignment = VerticalAlignment.Center, HorizontalContentAlignment = HorizontalAlignment.Center,
        Background = CamColors.CardBrush, BorderBrush = CamColors.BorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6),
    };

    /// <summary>Botão "Operações ▾" da faixa (UI-PRO-P3a): abre o menu com todas as operações, por grupo. Criar operação = dois cliques.</summary>
    private static StackPanel OperationsMenuBox(StackPanel menu)
    {
        var content = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(UiKit.Icon2("faceamento", CamColors.Text, CamColors.Accent, 22));
        content.Children.Add(new TextBlock { Text = "Operações ▾", FontSize = 11, LineHeight = 13, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
        Button button = PaletteButton(content, 92, PaletteButtonHeight);
        button.BorderBrush = CamColors.AccentBrush;
        var scroll = new ScrollViewer { Content = menu, MaxHeight = 520, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var flyout = new Flyout { Content = scroll, Placement = PlacementMode.BottomEdgeAlignedLeft, ShowMode = FlyoutShowMode.Standard };
        button.Flyout = flyout;
        ToolTip.SetTip(button, "Adicionar uma operação ao trabalho");
        Avalonia.Automation.AutomationProperties.SetName(button, "Operações");
        var box = new StackPanel { Spacing = 2 };
        box.Children.Add(button);
        box.Children.Add(new TextBlock { Text = "ADICIONAR", FontSize = 10, Foreground = CamColors.MutedBrush, HorizontalAlignment = HorizontalAlignment.Center });
        MenuFlyouts.AddOrUpdate(menu, flyout);
        return box;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<StackPanel, Flyout> MenuFlyouts = new();

    /// <summary>Um grupo do menu: título pequeno e uma linha por operação (ícone e nome). "Em breve" fica apagado.</summary>
    private static void AddMenuGroup(StackPanel menu, CamPaletteGroup group, Func<CamPaletteItem, Task> click, List<(CamPaletteItem, Button)> list)
    {
        menu.Children.Add(new TextBlock
        {
            Text = group.Title.ToUpperInvariant(), FontSize = 10, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.4,
            Foreground = CamColors.MutedBrush, Margin = new Thickness(6, menu.Children.Count == 0 ? 2 : 8, 0, 2),
        });
        foreach (CamPaletteItem item in group.Items)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Left };
            row.Children.Add(UiKit.Icon2(item.Icon, item.Soon ? CamColors.Muted : CamColors.Text, item.Soon ? CamColors.Muted : CamColors.Accent, 18));
            row.Children.Add(new TextBlock { Text = item.Soon ? item.Name + " · em breve" : item.Name, FontSize = 12, VerticalAlignment = VerticalAlignment.Center });
            var button = new Button
            {
                Content = row, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(5), Padding = new Thickness(8, 5), MinHeight = 0,
            };
            if (item.Soon)
            {
                button.Opacity = 0.6;
            }

            ToolTip.SetShowOnDisabled(button, true);
            Avalonia.Automation.AutomationProperties.SetName(button, item.Soon ? item.Name + " (em breve)" : "Adicionar " + item.Name);
            CamPaletteItem picked = item;
            button.Click += async (_, _) =>
            {
                if (MenuFlyouts.TryGetValue(menu, out Flyout? f))
                {
                    f.Hide();
                }

                await click(picked);
            };
            menu.Children.Add(button);
            list.Add((item, button));
        }
    }

    /// <summary>Botão de operação: ícone de 2 cores e nome (até 2 linhas, nunca quebrando no meio da palavra).</summary>
    private static Button OpButton(CamPaletteItem item, double minButtonWidth)
    {
        var content = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
        content.Children.Add(UiKit.Icon2(item.Icon, CamColors.Text, CamColors.Accent, 22));
        content.Children.Add(new TextBlock
        {
            Text = item.Name, FontSize = 11, LineHeight = 13, MaxLines = 2, TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
        });
        double word = item.Name.Split(' ').Max(w =>
        {
            // mede com a mesma fonte do botão (Inter): medir com a padrão quebrava "Faceamento" no Windows
            var probe = new TextBlock { Text = w, FontSize = 11, FontFamily = CamColors.UiFont };
            probe.Measure(Size.Infinity);
            return probe.DesiredSize.Width;
        });
        return PaletteButton(content, Math.Max(minButtonWidth, Math.Ceiling(word) + 14), PaletteButtonHeight);
    }

    /// <summary>Botão "em breve": estreito, só o ícone apagado; o nome está na dica.</summary>
    private static Button SoonButton(CamPaletteItem item)
    {
        Button b = PaletteButton(UiKit.Icon2(item.Icon, CamColors.Muted, CamColors.Muted, 20), 38, PaletteButtonHeight);
        b.Opacity = 0.6;
        return b;
    }

    /// <summary>Botão de arquivo da grade 2 × 2: ícone pequeno e nome na mesma linha.</summary>
    private static Button FileButton(CamPaletteItem item)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, HorizontalAlignment = HorizontalAlignment.Left };
        row.Children.Add(UiKit.Icon2(item.Icon, CamColors.Text, CamColors.Select, 16));
        row.Children.Add(new TextBlock { Text = item.Name, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        Button b = PaletteButton(row, 84, (PaletteButtonHeight - 3) / 2);
        b.HorizontalContentAlignment = HorizontalAlignment.Left;
        b.Padding = new Thickness(6, 0);
        return b;
    }

    /// <summary>Seções do painel de propriedades que o usuário fechou (pelo título; valem enquanto o app está aberto).</summary>
    private static readonly HashSet<string> FoldedSections = [];

    /// <summary>
    /// UI-03: título de seção do painel de propriedades que abre e fecha (▾ aberta, ▸ fechada). Fechar só esconde os campos até o
    /// próximo título (classe "camfolded"); os campos continuam lá e nada muda no trabalho. A escolha vale para todas as operações.
    /// </summary>
    public static void AddSection(Panel panel, string title)
    {
        bool folded = FoldedSections.Contains(title);
        var arrow = new TextBlock { Text = folded ? "▸" : "▾", FontSize = 11, Foreground = CamColors.MutedBrush, Width = 12, VerticalAlignment = VerticalAlignment.Center };
        var text = new TextBlock
        {
            Text = title.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeight.SemiBold, LetterSpacing = 0.4,
            Foreground = CamColors.MutedBrush, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        row.Children.Add(arrow);
        row.Children.Add(text);
        var header = new Button
        {
            Content = row, Classes = { "camsection" }, Tag = title, HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent, BorderBrush = CamColors.BorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0), CornerRadius = new CornerRadius(0), Padding = new Thickness(0, 7, 0, 3),
            Margin = new Thickness(0, 6, 0, 0), MinHeight = 0,
        };
        ToolTip.SetTip(header, folded ? "Abrir a seção" : "Fechar a seção");
        Avalonia.Automation.AutomationProperties.SetName(header, title);
        header.Click += (_, _) =>
        {
            if (!FoldedSections.Add(title))
            {
                FoldedSections.Remove(title);
            }

            Fold(panel);
        };
        panel.Children.Add(header);
        // os campos da seção entram depois deste título: dobra quando o painel terminar de montar
        Avalonia.Threading.Dispatcher.UIThread.Post(() => Fold(panel), Avalonia.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Aplica as seções fechadas: esconde (classe) os campos entre um título fechado e o próximo título. Seção com campo vazio ou
    /// inválido (tracejado do CAM-12) nunca fica fechada: a pendência tem de estar à vista.
    /// </summary>
    private static void Fold(Panel panel)
    {
        var sections = new List<(Button Header, List<Control> Fields)>();
        foreach (Control c in panel.Children)
        {
            if (c is Button { Tag: string } b && b.Classes.Contains("camsection"))
            {
                sections.Add((b, []));
            }
            else if (sections.Count > 0)
            {
                sections[^1].Fields.Add(c);
            }
        }

        foreach ((Button header, List<Control> fields) in sections)
        {
            bool missing = fields.Any(HasMissingMark);
            bool hide = FoldedSections.Contains((string)header.Tag!) && !missing;
            if (header.Content is StackPanel { Children: [TextBlock arrow, ..] })
            {
                arrow.Text = hide ? "▸" : "▾";
            }

            ToolTip.SetTip(header, missing ? "Seção com campo pendente: fica aberta" : hide ? "Abrir a seção" : "Fechar a seção");
            foreach (Control f in fields)
            {
                f.Classes.Set("camfolded", hide);
            }
        }
    }

    /// <summary>Campo com o tracejado de vazio ou inválido ligado (<see cref="DashedField"/>), em qualquer nível do controle.</summary>
    private static bool HasMissingMark(Control c) =>
        c is Avalonia.Controls.Shapes.Rectangle { StrokeDashArray.Count: > 0, IsVisible: true }
        || (c is Panel p && p.Children.Any(HasMissingMark))
        || (c is Decorator { Child: { } d } && HasMissingMark(d))
        || (c is ContentControl { Content: Control cc } && HasMissingMark(cc));

    /// <summary>Liga ou apaga o botão da faixa; a dica diz o porquê.</summary>
    public static void SetPaletteState(CamPaletteItem item, Button button, string? why)
    {
        button.IsEnabled = why is null;
        // o botão "em breve" não mostra o nome (UI-02): a dica começa por ele
        string hint = item.Soon ? $"{item.Name} · em breve\n{item.Hint}" : item.Hint;
        ToolTip.SetTip(button, why is null || item.Soon ? hint : $"{hint}\nAgora não: {why}");
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

    /// <summary>Cor do tracejado do campo vazio ou inválido (laranja da casca).</summary>
    public const string MissingStroke = CamColors.Pending;

    /// <summary>
    /// Campo com a marca de vazio ou inválido (regra do CAM-12; tracejado laranja do UX-04e, comum à fresa e ao torno desde o UX-04f).
    /// Devolve o campo para pôr na tela e a função que marca: um motivo liga o tracejado e a dica; null tira os dois. Serve a caixa de
    /// texto e a lista (o G5x do UX-04g).
    /// </summary>
    public static (Grid Field, Action<string?> Mark) DashedField(Control box)
    {
        var dash = new Avalonia.Controls.Shapes.Rectangle
        {
            Stroke = Brush.Parse(MissingStroke), StrokeThickness = 1.5, StrokeDashArray = [4, 3], RadiusX = 3, RadiusY = 3,
            IsHitTestVisible = false, IsVisible = false,
        };
        var field = new Grid();
        field.Children.Add(box);
        field.Children.Add(dash);
        void Mark(string? why)
        {
            dash.IsVisible = why is not null;
            ToolTip.SetTip(box, why);
        }

        return (field, Mark);
    }

    /// <summary>Olho da operação: liga e desliga (o mesmo Enabled de antes).</summary>
    public static Button Eye(bool enabled, Action toggle)
    {
        var eye = new Button
        {
            Content = UiKit.Icon(enabled ? "olho" : "olho-fechado", enabled ? CamColors.Muted : CamColors.Faint, 15),
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
