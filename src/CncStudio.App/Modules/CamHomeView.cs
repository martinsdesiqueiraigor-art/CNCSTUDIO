using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CncStudio.App.Modules;

/// <summary>
/// Tela inicial do CAM (UI-06): trabalho de fresa, trabalho de torno, abrir trabalho, abrir desenho e os recentes (do arquivo
/// <see cref="CamRecent"/>), mais atalhos para os perfis de máquina e o banco de ferramentas. Só tela: cada botão chama o que já
/// existe no Trabalho fresa ou torno.
/// </summary>
public sealed class CamHomeView : UserControl
{
    private readonly StackPanel _recentRows = new();
    private readonly TextBlock _machine = new() { FontSize = 12, Foreground = CamColors.MutedBrush, TextWrapping = TextWrapping.Wrap };

    public Action? MillJob { get; set; }

    public Action? LatheJob { get; set; }

    public Action? OpenJob { get; set; }

    public Action? OpenDrawing { get; set; }

    public Action<CamRecentItem>? OpenRecent { get; set; }

    public Action? OpenProfiles { get; set; }

    public Action? OpenBank { get; set; }

    /// <summary>Nome do perfil de máquina ativo ("" = nenhum).</summary>
    public Func<string>? ActiveMachine { get; set; }

    public CamHomeView()
    {
        Background = CamColors.BgBrush;
        var root = new DockPanel { Margin = new Thickness(36, 28, 36, 20) };

        var head = new StackPanel { Spacing = 4, Margin = new Thickness(0, 0, 0, 20) };
        head.Children.Add(new TextBlock { Text = "Começar", FontSize = 22, FontWeight = FontWeight.SemiBold, Foreground = CamColors.TextBrush });
        head.Children.Add(new TextBlock { Text = "Escolha o trabalho ou continue de onde parou.", FontSize = 13, Foreground = CamColors.MutedBrush });
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        var cards = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = 14, Margin = new Thickness(0, 0, 0, 22) };
        AddCard(cards, 0, "Trabalho de fresa", "Peça STEP ou DXF, operações 2D e furos", ("M4 20h16M7 20v-5h10v5", "M12 3v8M10 11h4l-1 3h-2z"), CamColors.Accent, () => MillJob?.Invoke());
        AddCard(cards, 1, "Trabalho de torno", "Perfil, bruto, fixação e Z0", ("M3 4h5v16H3z", "M8 9h13v6H8z"), CamColors.Accent, () => LatheJob?.Invoke());
        AddCard(cards, 2, "Abrir trabalho", "Arquivo .casillas-job (fresa ou torno)", ("M3 7h6l2 2h10v10H3z", "M3 12h18"), CamColors.Select, () => OpenJob?.Invoke());
        AddCard(cards, 3, "Abrir desenho", "DXF da peça num trabalho de fresa", ("M6 3h8l4 4v14H6zM14 3v4h4", "M9 12l3 3 3-3M12 15V9"), CamColors.Select, () => OpenDrawing?.Invoke());
        DockPanel.SetDock(cards, Dock.Top);
        root.Children.Add(cards);

        var side = new StackPanel { Width = 300, Spacing = 14, Margin = new Thickness(14, 0, 0, 0) };
        side.Children.Add(Box("Máquina", _machine, ("Perfis de máquina", () => OpenProfiles?.Invoke())));
        side.Children.Add(Box("Banco de ferramentas", new TextBlock { Text = "Fresa e torno, compartilhado com o Code.", FontSize = 12, Foreground = CamColors.MutedBrush, TextWrapping = TextWrapping.Wrap }, ("Abrir o banco", () => OpenBank?.Invoke())));
        DockPanel.SetDock(side, Dock.Right);
        root.Children.Add(side);

        var recent = new DockPanel();
        var recentHead = new TextBlock { Text = "Recentes", FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = CamColors.TextBrush, Margin = new Thickness(14, 10) };
        DockPanel.SetDock(recentHead, Dock.Top);
        recent.Children.Add(recentHead);
        recent.Children.Add(new ScrollViewer { Content = _recentRows, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        root.Children.Add(new Border { Background = CamColors.PanelBrush, BorderBrush = CamColors.BorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = recent });

        Content = root;
    }

    /// <summary>Refaz a lista de recentes e a máquina ativa (ao mostrar a tela).</summary>
    public void Refresh()
    {
        string machine = ActiveMachine?.Invoke() ?? "";
        _machine.Text = machine.Length > 0 ? "Ativa: " + machine : "Nenhum perfil ativo. Sem perfil, o CAM gera em 3 eixos.";
        _recentRows.Children.Clear();
        IReadOnlyList<CamRecentItem> items = CamRecent.Load();
        if (items.Count == 0)
        {
            _recentRows.Children.Add(new TextBlock
            {
                Text = "Nenhum trabalho ainda. Os trabalhos que você abrir ou salvar no CAM aparecem aqui.",
                FontSize = 12, Foreground = CamColors.MutedBrush, Margin = new Thickness(14, 4, 14, 14), TextWrapping = TextWrapping.Wrap,
            });
            return;
        }

        foreach (CamRecentItem item in items)
        {
            _recentRows.Children.Add(RecentRow(item));
        }
    }

    private Button RecentRow(CamRecentItem item)
    {
        bool exists = File.Exists(item.Path);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,80,100,150"), ColumnSpacing = 12 };
        var thumb = new Border
        {
            Width = 40, Height = 32, CornerRadius = new CornerRadius(4), Background = CamColors.CardBrush,
            Child = UiKit.Icon2(item.Lathe ? "torno-externo" : "bolsao", CamColors.Text, CamColors.Accent, 20),
        };
        grid.Children.Add(thumb);
        var names = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = item.Name, FontWeight = FontWeight.SemiBold, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = CamColors.TextBrush });
        names.Children.Add(new TextBlock { Text = item.Path, FontSize = 11, Foreground = CamColors.MutedBrush, TextTrimming = TextTrimming.PathSegmentEllipsis });
        Grid.SetColumn(names, 1);
        grid.Children.Add(names);
        AddCell(grid, 2, item.Lathe ? "Torno" : "Fresa", CamColors.TextBrush);
        AddCell(grid, 3, CamRecent.WhenText(item.When), CamColors.MutedBrush);
        var state = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        state.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = exists ? Brush.Parse(CamColors.Ok) : CamColors.PendingBrush });
        state.Children.Add(new TextBlock { Text = exists ? "Abrir" : "Arquivo não encontrado", FontSize = 12, Foreground = CamColors.MutedBrush });
        Grid.SetColumn(state, 4);
        grid.Children.Add(state);

        var row = new Button
        {
            Content = grid, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = Brushes.Transparent, BorderBrush = CamColors.BorderBrush, BorderThickness = new Thickness(0, 1, 0, 0),
            CornerRadius = new CornerRadius(0), Padding = new Thickness(14, 8), IsEnabled = exists,
        };
        ToolTip.SetTip(row, exists ? "Abrir " + item.Path : "O arquivo não está mais neste caminho: " + item.Path);
        Avalonia.Automation.AutomationProperties.SetName(row, "Abrir " + item.Name);
        row.Click += (_, _) => OpenRecent?.Invoke(item);
        return row;
    }

    private static void AddCell(Grid grid, int column, string text, IBrush fg)
    {
        var t = new TextBlock { Text = text, FontSize = 12, Foreground = fg, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(t, column);
        grid.Children.Add(t);
    }

    private static void AddCard(Grid grid, int column, string title, string sub, (string Base, string Accent) icon, string accent, Action click)
    {
        var canvas = new Canvas { Width = 24, Height = 24 };
        canvas.Children.Add(new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(icon.Base), Stroke = CamColors.TextBrush, StrokeThickness = 1.6, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round });
        canvas.Children.Add(new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(icon.Accent), Stroke = Brush.Parse(accent), StrokeThickness = 1.6, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round });
        var iconBox = new Border
        {
            Width = 40, Height = 40, CornerRadius = new CornerRadius(8), HorizontalAlignment = HorizontalAlignment.Left,
            Background = accent == CamColors.Accent ? CamColors.AccentBgBrush : CamColors.SelectBgBrush,
            Child = new Viewbox { Width = 24, Height = 24, Child = canvas },
        };
        var stack = new StackPanel { Spacing = 8 };
        stack.Children.Add(iconBox);
        stack.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeight.SemiBold, Foreground = CamColors.TextBrush });
        stack.Children.Add(new TextBlock { Text = sub, FontSize = 11, Foreground = CamColors.MutedBrush, TextWrapping = TextWrapping.Wrap });
        var card = new Button
        {
            Content = stack, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top, Height = 130, Padding = new Thickness(16),
            Background = CamColors.PanelBrush, BorderBrush = CamColors.BorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8),
        };
        Avalonia.Automation.AutomationProperties.SetName(card, title);
        card.Click += (_, _) => click();
        Grid.SetColumn(card, column);
        grid.Children.Add(card);
    }

    private static Border Box(string title, Control body, (string Text, Action Click) link)
    {
        var stack = new StackPanel { Spacing = 6, Margin = new Thickness(14, 10, 14, 12) };
        stack.Children.Add(new TextBlock { Text = title, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = CamColors.TextBrush });
        stack.Children.Add(body);
        var go = new Button { Content = link.Text, Padding = new Thickness(10, 4), FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
        go.Click += (_, _) => link.Click();
        stack.Children.Add(go);
        return new Border { Background = CamColors.PanelBrush, BorderBrush = CamColors.BorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Child = stack };
    }
}
