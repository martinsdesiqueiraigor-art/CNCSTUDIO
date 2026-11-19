using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CncStudio.Core.Panel;

namespace CncStudio.App.Graphics;

/// <summary>
/// Painel do comando sobre o gráfico (UI-C2), como o da versão web: linha atual, posição absoluta e o que falta em cada eixo,
/// ferramenta, rápido ou corte, rotação, avanço, G ativos e o zero de peça. Os dados vêm do <see cref="ControlPanel"/> do Core
/// (o mesmo da web); aqui só se desenha. A seta recolhe e abre; quem guarda a escolha é a janela.
/// </summary>
public sealed class ControlPanelOverlay : Border
{
    private static readonly FontFamily Mono = new("Cascadia Mono, Consolas, Menlo, monospace");
    private static readonly IBrush Blue = Brush.Parse("#7DB8F2");
    private static readonly IBrush Muted = Brush.Parse("#9AA3AE");
    private static readonly IBrush Text = Brush.Parse("#E6E8EB");

    private readonly TextBlock _line = new() { FontFamily = Mono, FontSize = 12, Foreground = Blue, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _toggle = new() { Width = 28, Height = 24, Padding = new Thickness(0), MinHeight = 0, HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
    private readonly StackPanel _body = new() { Spacing = 3, Margin = new Thickness(12, 8, 12, 10) };
    private readonly Grid _axes = new() { ColumnDefinitions = new ColumnDefinitions("18,*,Auto"), RowSpacing = 1 };
    private readonly TextBlock _unit = new() { FontSize = 10, Foreground = Muted, Text = "ABSOLUTA (mm)" };
    private readonly TextBlock _tool = new() { FontFamily = Mono, FontSize = 12, Foreground = Text };
    private readonly TextBlock _modal = new() { FontFamily = Mono, FontSize = 12, Foreground = Brush.Parse("#6FD38E"), TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _wcs = new() { FontFamily = Mono, FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = Blue };
    private bool _open = true;

    /// <summary>O usuário abriu ou recolheu o painel (para a janela guardar).</summary>
    public event Action<bool>? OpenChanged;

    public ControlPanelOverlay()
    {
        Width = 270;
        Background = new SolidColorBrush(Color.Parse("#14161A"), 0.94);
        BorderBrush = Brush.Parse("#2E4A66");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(10);
        ClipToBounds = true;

        var head = new DockPanel { Height = 34, Margin = new Thickness(12, 0, 6, 0) };
        DockPanel.SetDock(_toggle, Dock.Right);
        _toggle.VerticalAlignment = VerticalAlignment.Center;
        _toggle.Click += (_, _) => SetOpen(!_open, notify: true);
        head.Children.Add(_toggle);
        var title = new TextBlock { Text = "PAINEL", FontFamily = Mono, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = Blue, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        DockPanel.SetDock(title, Dock.Left);
        head.Children.Add(title);
        head.Children.Add(_line);

        var unitRow = new DockPanel();
        var toGo = new TextBlock { Text = "FALTA", FontSize = 10, Foreground = Muted };
        DockPanel.SetDock(toGo, Dock.Right);
        unitRow.Children.Add(toGo);
        unitRow.Children.Add(_unit);
        _body.Children.Add(unitRow);
        _body.Children.Add(_axes);
        _body.Children.Add(new Border { Height = 1, Background = Brush.Parse("#2C3037"), Margin = new Thickness(0, 4) });
        _body.Children.Add(_tool);
        _body.Children.Add(_modal);
        var wcsRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        wcsRow.Children.Add(new TextBlock { Text = "ZERO DE PEÇA", FontSize = 10, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center });
        wcsRow.Children.Add(_wcs);
        _body.Children.Add(wcsRow);

        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(new Border { Height = 1, Background = Brush.Parse("#2C3037") });
        stack.Children.Add(_body);
        Child = stack;
        ToolTip.SetTip(_toggle, "Recolher ou abrir o painel");
        Avalonia.Automation.AutomationProperties.SetName(_toggle, "Recolher ou abrir o painel do comando");
        SetOpen(true, notify: false);
        Show(null);
    }

    /// <summary>Abre ou recolhe (sem avisar a janela quando quem chama é ela, ao carregar a escolha guardada).</summary>
    public void SetOpen(bool open, bool notify = false)
    {
        _open = open;
        _body.IsVisible = open;
        _toggle.Content = open ? "▾" : "▸";
        if (notify)
        {
            OpenChanged?.Invoke(open);
        }
    }

    /// <summary>Mostra o estado do ponto atual da simulação (null = sem programa).</summary>
    public void Show(ControlPanelState? state)
    {
        CultureInfo c = CultureInfo.CurrentCulture;
        _axes.Children.Clear();
        _axes.RowDefinitions.Clear();
        if (state is null)
        {
            _line.Text = "";
            _tool.Text = "—";
            _modal.Text = "";
            _wcs.Text = "—";
            return;
        }

        _line.Text = state.Line > 0 ? "· linha " + state.Line.ToString(c) : "· início";
        _unit.Text = "ABSOLUTA (" + (state.Inches ? "pol" : "mm") + ")";
        for (int k = 0; k < state.Axes.Count; k++)
        {
            PanelAxis a = state.Axes[k];
            _axes.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            Add(new TextBlock { Text = a.Name, FontFamily = Mono, FontSize = 13, Foreground = Brush.Parse("#4C9BE8"), VerticalAlignment = VerticalAlignment.Center }, k, 0);
            Add(new TextBlock { Text = a.Position.ToString("0.000", c), FontFamily = Mono, FontSize = 17, Foreground = Text }, k, 1);
            Add(new TextBlock { Text = a.DistanceToGo.ToString("0.000", c), FontFamily = Mono, FontSize = 12, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center }, k, 2);
        }

        string tool = state.Tool > 0 ? "T" + state.Tool.ToString("00", c) : "T—";
        string move = state.Feed is null ? "rápido" : "corte";
        string spindle = state.Spindle > 0 ? (state.SurfaceSpeed ? $"S{state.Spindle:0} m/min" : $"S{state.Spindle:0} rpm") : "";
        string feed = state.Feed is { } f ? $"F{f.ToString("0.###", c)} {(state.Inches ? "pol" : "mm")}/min" : "";
        _tool.Text = string.Join("  ", new[] { tool, move, spindle, feed }.Where(x => x.Length > 0));
        _modal.Text = string.Join(" ", state.GCodes);
        _wcs.Text = state.WorkOffset;
    }

    private void Add(Control c, int row, int column)
    {
        Grid.SetRow(c, row);
        Grid.SetColumn(c, column);
        _axes.Children.Add(c);
    }
}
