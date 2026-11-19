using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace CncStudio.App.Modules;

/// <summary>Operação na linha do tempo: índice no trabalho, nome curto, estado ("ok", "pending", "bad") e tempo estimado.</summary>
public sealed record CamTimelineItem(int Index, string Title, string Kind, TimeSpan? Time);

/// <summary>
/// Linha do tempo das operações ligadas (UI-05), embaixo da vista do Trabalho fresa e torno: uma faixa por operação, na ordem do
/// programa, com largura pelo tempo estimado (sem tempo = largura mínima e tracejado), cor do estado e texto. Clicar seleciona a
/// operação. Só tela: o tempo vem de <see cref="CamOpTimes"/>.
/// </summary>
public sealed class CamTimeline : Border
{
    private readonly TextBlock _total = new() { FontSize = 11, Foreground = CamColors.MutedBrush, VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid _bars = new() { Height = 24 };

    /// <summary>Clique numa faixa: índice da operação no trabalho.</summary>
    public Action<int>? Pick { get; set; }

    public CamTimeline()
    {
        Background = CamColors.PanelBrush;
        BorderBrush = CamColors.BorderBrush;
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(8);
        Padding = new Thickness(10, 6);
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 5) };
        var title = new TextBlock { Text = "Linha do tempo", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = CamColors.TextBrush, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(title, Dock.Left);
        head.Children.Add(title);
        var legend = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        legend.Children.Add(Key(CamColors.Ok, "Pronta"));
        legend.Children.Add(Key(CamColors.Pending, "Incompleta"));
        legend.Children.Add(Key(CamColors.Error, "Erro"));
        DockPanel.SetDock(legend, Dock.Right);
        head.Children.Add(legend);
        head.Children.Add(_total);
        var stack = new StackPanel();
        stack.Children.Add(head);
        stack.Children.Add(_bars);
        Child = stack;
        ToolTip.SetTip(this, "Tempo estimado pelo G-code gerado (rápido e avanço; sem pausas nem troca de ferramenta). Simule no Code para conferir.");
    }

    private static StackPanel Key(string color, string text)
    {
        var k = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        k.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(2), Background = Brush.Parse(color), VerticalAlignment = VerticalAlignment.Center });
        k.Children.Add(new TextBlock { Text = text, FontSize = 10.5, Foreground = CamColors.MutedBrush, VerticalAlignment = VerticalAlignment.Center });
        return k;
    }

    /// <summary>Redesenha com as operações ligadas; <paramref name="selected"/> = índice da operação escolhida na árvore.</summary>
    public void Show(IReadOnlyList<CamTimelineItem> items, int? selected)
    {
        _bars.Children.Clear();
        _bars.ColumnDefinitions.Clear();
        if (items.Count == 0)
        {
            _total.Text = "Nenhuma operação ligada.";
            return;
        }

        double known = items.Where(i => i.Time is not null).Sum(i => i.Time!.Value.TotalSeconds);
        int missing = items.Count(i => i.Time is null);
        double floor = Math.Max(known / items.Count * 0.35, 1);
        _total.Text = (known > 0 ? "Total " + CamOpTimes.Text(TimeSpan.FromSeconds(known)).TrimStart('≈', ' ') + " estimado" : "Sem tempo estimado")
            + (missing > 0 ? $" · {missing} sem tempo" : "");
        for (int k = 0; k < items.Count; k++)
        {
            CamTimelineItem it = items[k];
            double weight = Math.Max(it.Time?.TotalSeconds ?? 0, floor);
            _bars.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(weight, GridUnitType.Star)) { MinWidth = 28 });
            (string border, string bg, string fg) = it.Kind switch
            {
                "ok" => ("#2F6B43", "#1E3324", "#A8E0B8"),
                "bad" => ("#7A3030", "#3A1C1C", "#FFB4AE"),
                _ => ("#7A6327", CamColors.PendingBg, CamColors.PendingText),
            };
            bool on = it.Index == selected;
            var bar = new Button
            {
                Content = new TextBlock { Text = it.Title, FontSize = 11, Foreground = Brush.Parse(fg), TextTrimming = TextTrimming.CharacterEllipsis },
                HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left, VerticalContentAlignment = VerticalAlignment.Center,
                Padding = new Thickness(7, 0), MinHeight = 0, Margin = new Thickness(k == 0 ? 0 : 2, 0, 0, 0),
                Background = Brush.Parse(bg), BorderBrush = on ? CamColors.SelectBrush : Brush.Parse(border),
                BorderThickness = new Thickness(on ? 2 : 1), CornerRadius = new CornerRadius(4),
            };
            string tip = $"{it.Title}\n{CamOpTimes.Text(it.Time)}{(it.Time is null ? "" : " (estimado)")}";
            ToolTip.SetTip(bar, tip);
            Avalonia.Automation.AutomationProperties.SetName(bar, it.Title + ", " + CamOpTimes.Text(it.Time));
            int index = it.Index;
            bar.Click += (_, _) => Pick?.Invoke(index);
            Grid.SetColumn(bar, k);
            _bars.Children.Add(bar);
        }
    }
}
