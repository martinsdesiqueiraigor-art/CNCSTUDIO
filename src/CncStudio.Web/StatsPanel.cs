using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Reports;
using CncStudio.Core.Statistics;

namespace CncStudio.Web;

/// <summary>
/// Estatísticas do programa (folha que sobe por cima do gráfico): tempo de ciclo dividido em corte,
/// rápido e espera; cada ferramenta com operação, código, tempo, avanço e alcance; limites; botão
/// para a folha de setup. Depois do teste grátis, sem licença, mostra só o tempo total.
/// </summary>
public sealed class StatsPanel : UserControl
{
    private readonly StackPanel _content = new() { Spacing = 8 };

    public StatsPanel()
    {
        var sheet = new Border
        {
            BorderThickness = new Thickness(0, 1, 0, 0),
            CornerRadius = new CornerRadius(16, 16, 0, 0),
            VerticalAlignment = VerticalAlignment.Bottom,
            MaxWidth = 560,
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _content,
                Padding = new Thickness(14, 8, 14, 14),
            },
        };

        sheet.Bind(Border.BackgroundProperty, this.GetResourceObservable("BarBackground"));
        sheet.Bind(Border.BorderBrushProperty, this.GetResourceObservable("SubtleBorder"));

        // Fundo escurecido: toque fora fecha
        var dim = new Border { Background = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)) };
        dim.PointerPressed += (_, _) => Closed?.Invoke(this, EventArgs.Empty);

        var root = new Grid();
        root.Children.Add(dim);
        root.Children.Add(sheet);
        Content = root;

        // A folha ocupa no máximo 88% da altura
        root.SizeChanged += (_, e) => sheet.MaxHeight = Math.Max(240, e.NewSize.Height * 0.88);
    }

    public event EventHandler? Closed;

    public event EventHandler? SetupSheetRequested;

    public event EventHandler? AccountRequested;

    /// <summary>Monta o conteúdo. full = false: teste encerrado sem licença (só o tempo total).</summary>
    public void Show(SetupSheet sheet, bool full)
    {
        _content.Children.Clear();
        ProgramStatistics stats = sheet.Statistics;
        bool lathe = sheet.Machine == MachineKind.Lathe;

        var handle = new Border
        {
            Width = 40,
            Height = 4,
            CornerRadius = new CornerRadius(2),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 4),
        };
        handle.Bind(Border.BackgroundProperty, this.GetResourceObservable("SubtleBorder"));
        _content.Children.Add(handle);

        // Tempo total e fechar
        var close = new Button { Content = "Fechar", Classes = { "bar" }, VerticalAlignment = VerticalAlignment.Center };
        close.Click += (_, _) => Closed?.Invoke(this, EventArgs.Empty);
        var head = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        head.Children.Add(close);
        head.Children.Add(new StackPanel
        {
            Children =
            {
                Soft("Tempo estimado de ciclo", 12),
                new TextBlock { Text = DurationFormat.Format(stats.TotalTime), FontSize = 30, FontWeight = FontWeight.Bold },
            },
        });
        _content.Children.Add(head);

        // Barra corte / rápido / espera
        double total = Math.Max(stats.TotalTime.TotalSeconds, 1e-9);
        var bar = new Grid { Height = 10, ClipToBounds = true };
        AddPart(bar, stats.CuttingTime.TotalSeconds / total, "Motion1");
        AddPart(bar, stats.RapidTime.TotalSeconds / total, "Motion0");
        AddPart(bar, stats.DwellTime.TotalSeconds / total, "SubtleBorder");
        _content.Children.Add(new Border { CornerRadius = new CornerRadius(5), ClipToBounds = true, Child = bar });
        _content.Children.Add(new WrapPanel
        {
            Children =
            {
                Legend("Motion1", "Corte " + DurationFormat.Format(stats.CuttingTime)),
                Legend("Motion0", "Rápido " + DurationFormat.Format(stats.RapidTime)),
                Legend("SubtleBorder", "Espera " + DurationFormat.Format(stats.DwellTime)),
            },
        });

        if (!full)
        {
            var account = new Button { Content = "Ver minha conta", Classes = { "bar" } };
            account.Click += (_, _) => AccountRequested?.Invoke(this, EventArgs.Empty);
            var limitedSheet = new Button { Content = "Folha de setup", Classes = { "bar" } };
            limitedSheet.Click += (_, _) => SetupSheetRequested?.Invoke(this, EventArgs.Empty);
            _content.Children.Add(Card(new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock { Text = "Detalhes por ferramenta", FontWeight = FontWeight.SemiBold },
                    Soft("Liberados no teste grátis e com licença. O tempo total aparece sempre; a folha de setup sai com marca d'água.", 12, wrap: true),
                    new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { account, limitedSheet } },
                },
            }));
            return;
        }

        // Ferramentas
        _content.Children.Add(Section("Por ferramenta"));
        foreach (SetupTool tool in sheet.Tools)
        {
            ToolStatistics t = tool.Statistics;
            string name = tool.Number == 0 ? "Sem ferramenta" : "T" + tool.Number + (tool.Description.Length > 0 ? " · " + tool.Description : "");
            // Nome à esquerda (encolhe com "…"), tempo à direita
            var title = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            title.Children.Add(new TextBlock
            {
                Text = name,
                FontWeight = FontWeight.Bold,
                FontSize = 15,
                TextWrapping = TextWrapping.Wrap, // quebrando, a largura pedida não passa do espaço disponível
                MaxLines = 2,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 8, 0),
            });
            var time = new TextBlock { Text = DurationFormat.Format(t.TotalTime), FontWeight = FontWeight.Bold, FontSize = 15 };
            Grid.SetColumn(time, 1);
            title.Children.Add(time);

            var card = new StackPanel { Spacing = 3, Children = { title } };
            if (tool.Code is { Length: > 0 } code)
            {
                card.Children.Add(Soft(code, 12, wrap: true));
            }

            card.Children.Add(Progress(tool.Share));

            string feed = t.MaxFeed <= 0 ? "—"
                : Math.Abs(t.MinFeed - t.MaxFeed) < 0.5 ? $"{t.MaxFeed:0} mm/min"
                : $"{t.MinFeed:0}–{t.MaxFeed:0} mm/min";
            // Torno: menor diâmetro; broca no centro (Ø 0) mostra a profundidade em Z
            bool center = lathe && tool.Reach is < 0.001;
            string reach = center ? (t.MinZ is { } z ? $"Z {z:0.###} (no centro)" : "—")
                : tool.Reach is not { } r ? "—" : lathe ? $"Ø {r:0.###} mm" : $"Z {r:0.###}";
            card.Children.Add(Pairs(
                ("Avanço", feed),
                (lathe && !center ? "Menor Ø" : "Profundidade", reach),
                ("Corte", $"{t.CuttingDistance:0} mm · {tool.Share * 100:0}% do tempo"),
                ("Entra na linha", tool.FirstLine.ToString())));
            _content.Children.Add(Card(card));
        }

        // Limites
        _content.Children.Add(Section("Limites"));
        _content.Children.Add(Card(Pairs(
            ("Corte", Limits(sheet.CuttingBounds, lathe)),
            ("Com G00", Limits(sheet.AllBounds, lathe)),
            ("Bruto", sheet.Stock ?? "não informado (ex.: comentário BRUTO D50 X 70)"),
            ("Programa", $"{sheet.LineCount} linhas · {sheet.MoveCount} movimentos · {stats.ToolChanges} troca(s)"))));

        if (stats.MovesWithoutFeed > 0)
        {
            _content.Children.Add(Soft($"{stats.MovesWithoutFeed} movimento(s) de corte sem avanço F ficaram fora do tempo.", 12, wrap: true));
        }

        _content.Children.Add(Soft("Tempo sem aceleração da máquina: programas com muitos movimentos curtos demoram mais.", 11, wrap: true));

        var setup = new Button
        {
            Content = "Folha de setup",
            Classes = { "bar", "primary" },
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
            MinHeight = 44,
        };
        setup.Click += (_, _) => SetupSheetRequested?.Invoke(this, EventArgs.Empty);
        _content.Children.Add(setup);
    }

    // ───────────── peças ─────────────

    private IBrush? Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out object? value) && value is IBrush brush ? brush
        : Application.Current?.TryFindResource(key, Application.Current.ActualThemeVariant, out object? app) == true && app is IBrush b ? b
        : null;

    private void AddPart(Grid bar, double fraction, string brush)
    {
        if (fraction <= 0.0005)
        {
            return;
        }

        bar.ColumnDefinitions.Add(new ColumnDefinition(fraction, GridUnitType.Star));
        var part = new Border { Background = Brush(brush) };
        Grid.SetColumn(part, bar.ColumnDefinitions.Count - 1);
        bar.Children.Add(part);
    }

    private Control Legend(string brush, string text) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 4,
        Margin = new Thickness(0, 0, 12, 0),
        Children =
        {
            new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(2), Background = Brush(brush), VerticalAlignment = VerticalAlignment.Center },
            Soft(text, 12),
        },
    };

    private Control Progress(double share)
    {
        var grid = new Grid { Height = 6, ColumnDefinitions = new ColumnDefinitions($"{Math.Max(share, 0.005).ToString(System.Globalization.CultureInfo.InvariantCulture)}*,{Math.Max(1 - share, 0).ToString(System.Globalization.CultureInfo.InvariantCulture)}*") };
        grid.Children.Add(new Border { Background = Brush("Motion1"), CornerRadius = new CornerRadius(3) });
        return new Border { Background = Brush("SubtleBorder"), CornerRadius = new CornerRadius(3), Child = grid, Margin = new Thickness(0, 2) };
    }

    private Control Card(Control child)
    {
        var card = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(10, 8),
            Child = child,
        };
        // Cores acompanham o tema (claro/escuro) mesmo depois de o painel ser criado
        card.Bind(Border.BackgroundProperty, this.GetResourceObservable("AppBackground"));
        card.Bind(Border.BorderBrushProperty, this.GetResourceObservable("SubtleBorder"));
        return card;
    }

    private static Control Section(string text) => new TextBlock
    {
        Text = text.ToUpperInvariant(),
        FontSize = 12,
        FontWeight = FontWeight.SemiBold,
        Opacity = 0.65,
        Margin = new Thickness(0, 6, 0, 0),
    };

    private static TextBlock Soft(string text, double size, bool wrap = false) => new()
    {
        Text = text,
        FontSize = size,
        Opacity = 0.7,
        TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
    };

    private static Control Pairs(params (string Label, string Value)[] rows)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 2, 0, 0) };
        for (int i = 0; i < rows.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new TextBlock { Text = rows[i].Label, FontSize = 12, Opacity = 0.65, Margin = new Thickness(0, 1, 12, 1) };
            var value = new TextBlock { Text = rows[i].Value, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1) };
            Grid.SetRow(label, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            grid.Children.Add(label);
            grid.Children.Add(value);
        }

        return grid;
    }

    private static string Limits(Bounds3 b, bool lathe)
    {
        if (b.IsEmpty)
        {
            return "—";
        }

        return lathe
            ? $"Ø {Math.Abs(b.Min.X) * 2:0.###} a {Math.Abs(b.Max.X) * 2:0.###} · Z {b.Min.Z:0.###} a {b.Max.Z:0.###}"
            : $"X {b.Min.X:0.###} a {b.Max.X:0.###} · Y {b.Min.Y:0.###} a {b.Max.Y:0.###} · Z {b.Min.Z:0.###} a {b.Max.Z:0.###}";
    }
}
