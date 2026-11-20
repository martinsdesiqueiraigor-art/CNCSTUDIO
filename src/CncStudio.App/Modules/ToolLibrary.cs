using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CncStudio.App.Modules;

/// <summary>Uma ferramenta do trabalho na biblioteca: T, Ø (texto; "—" no torno) e nome.</summary>
public sealed record ToolLibraryRow(string T, string Diameter, string Name);

/// <summary>
/// Biblioteca de ferramentas (UI-PRO-P3b): painel recolhível à direita do 3D com T, Ø e nome das ferramentas que as operações do
/// trabalho usam. Só leitura: o banco e as operações não mudam. Recolhida, é uma tira estreita com a contagem; aberta, ocupa ~190 px.
/// </summary>
public sealed class ToolLibrary : Border
{
    private const double OpenWidth = 190;
    private static bool _open; // vale para fresa e torno enquanto o app está aberto

    private readonly StackPanel _rows = new() { Spacing = 2 };
    private readonly TextBlock _count = new() { FontSize = 11, Foreground = CamColors.MutedBrush, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
    private readonly TextBlock _arrow = new() { FontSize = 12, Foreground = CamColors.MutedBrush, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
    private readonly Button _toggle = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(4, 6), MinHeight = 0 };
    private readonly TextBlock _title = new() { Text = "Ferramentas", FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = CamColors.TextBrush };
    private readonly ScrollViewer _list = new() { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    private int _n;

    public ToolLibrary()
    {
        Background = CamColors.PanelBrush;
        BorderBrush = CamColors.BorderBrush;
        BorderThickness = new Thickness(1, 0, 0, 0);
        _list.Content = _rows;
        _toggle.Content = new StackPanel { Spacing = 2, Children = { _arrow, _count } };
        _toggle.Click += (_, _) => { _open = !_open; Render(); };
        Avalonia.Automation.AutomationProperties.SetName(_toggle, "Biblioteca de ferramentas");
        var head = new DockPanel { Margin = new Thickness(4, 4, 0, 4), LastChildFill = true };
        DockPanel.SetDock(_toggle, Dock.Right);
        head.Children.Add(_toggle);
        head.Children.Add(_title);
        Child = new DockPanel { Children = { head, _list } };
        DockPanel.SetDock(head, Dock.Top);
        Render();
    }

    /// <summary>Atualiza a lista (chamada a cada mudança do trabalho).</summary>
    public void Update(IReadOnlyList<ToolLibraryRow> tools)
    {
        _n = tools.Count;
        _rows.Children.Clear();
        _rows.Margin = new Thickness(8, 0, 6, 6);
        if (tools.Count == 0)
        {
            _rows.Children.Add(new TextBlock { Text = "Nenhuma ferramenta ainda: ela aparece quando uma operação tem T.", FontSize = 11, Foreground = CamColors.MutedBrush, TextWrapping = TextWrapping.Wrap });
        }

        foreach (ToolLibraryRow t in tools)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("34,44,*"), Margin = new Thickness(0, 1) };
            grid.Children.Add(new TextBlock { Text = t.T, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = CamColors.AccentTextBrush });
            var d = new TextBlock { Text = t.Diameter, FontSize = 12, Foreground = CamColors.TextBrush };
            Grid.SetColumn(d, 1);
            grid.Children.Add(d);
            var n = new TextBlock { Text = t.Name.Length > 0 ? t.Name : "sem nome", FontSize = 12, Foreground = t.Name.Length > 0 ? CamColors.TextBrush : CamColors.MutedBrush, TextTrimming = TextTrimming.CharacterEllipsis };
            Grid.SetColumn(n, 2);
            grid.Children.Add(n);
            ToolTip.SetTip(grid, $"{t.T} · {t.Diameter} · {(t.Name.Length > 0 ? t.Name : "sem nome")}");
            _rows.Children.Add(grid);
        }

        Render();
    }

    private void Render()
    {
        _count.Text = _n.ToString();
        _arrow.Text = _open ? "›" : "‹";
        ToolTip.SetTip(_toggle, (_open ? "Recolher" : "Abrir") + $" a biblioteca de ferramentas ({_n})");
        Width = _open ? OpenWidth : 30;
        _title.IsVisible = _open;
        _list.IsVisible = _open;
    }
}
