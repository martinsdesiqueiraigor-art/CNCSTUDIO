using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CncStudio.Core;
using CncStudio.Core.Geometry;
using CncStudio.Core.Statistics;

namespace CncStudio.App.Views;

/// <summary>
/// Painel de estatísticas: tempo estimado, distâncias, tabela por ferramenta e limites.
/// As tabelas são montadas em código (Grid simples), sem pacote de DataGrid.
/// </summary>
public partial class StatisticsPanel : UserControl
{
    // Cinza semitransparente: funciona no tema claro e no escuro
    private static readonly IBrush HeaderBrush = new SolidColorBrush(Color.FromArgb(48, 128, 128, 128));
    private static readonly IBrush HoverBrush = new SolidColorBrush(Color.FromArgb(70, 128, 128, 128));

    public StatisticsPanel()
    {
        InitializeComponent();
        Show(null);
    }

    /// <summary>Diâmetro de cada ferramenta (T → mm), só para exibir na tabela.</summary>
    public Func<int, double> DiameterOf { get; set; } = _ => 10;

    /// <summary>O usuário clicou numa ferramenta: ir para a linha informada no editor.</summary>
    public event EventHandler<int>? LineRequested;

    /// <summary>Mostra as estatísticas da análise (null = limpa).</summary>
    public void Show(ProgramAnalysis? analysis)
    {
        ProgramStatistics stats = analysis?.Statistics ?? ProgramStatistics.Empty;

        TotalTimeText.Text = DurationFormat.Format(stats.TotalTime);
        CuttingTimeText.Text = DurationFormat.Format(stats.CuttingTime);
        RapidTimeText.Text = DurationFormat.Format(stats.RapidTime);
        DwellTimeText.Text = DurationFormat.Format(stats.DwellTime);
        CuttingDistanceText.Text = $"{Distance(stats.CuttingDistance)} percorridos";
        RapidDistanceText.Text = $"{Distance(stats.RapidDistance)} percorridos";
        CountsText.Text = $"{stats.MoveCount:N0} / {stats.ToolChanges}";

        string notes = $"Rápido considerado: {stats.RapidRate:N0} mm/min (Máquina → Ferramentas e zeros de peça → Programa). " +
                       "Estimativa sem aceleração: em programas com muitos movimentos curtos o tempo real é maior.";
        if (stats.MovesWithoutFeed > 0)
        {
            notes += $" Atenção: {stats.MovesWithoutFeed:N0} movimento(s) de corte sem avanço F não entraram no tempo.";
        }

        NotesText.Text = notes;

        FillTools(stats);
        FillBounds(analysis);
    }

    // ───────────────────────── Tabela por ferramenta ─────────────────────────

    private void FillTools(ProgramStatistics stats)
    {
        string[] headers = ["Ferramenta", "Ø (mm)", "1ª linha", "Tempo", "Corte", "Rápido", "Espera", "Avanço (mm/min)", "Z mín. corte", "Distância corte", "Distância rápido"];
        PrepareGrid(ToolsGrid, headers.Length, stats.Tools.Count);
        AddHeader(ToolsGrid, headers);

        int row = 1;
        foreach (ToolStatistics tool in stats.Tools)
        {
            string feed = tool.MaxFeed <= 0
                ? "—"
                : tool.MinFeed == tool.MaxFeed ? $"{tool.MaxFeed:0.##}" : $"{tool.MinFeed:0.##} – {tool.MaxFeed:0.##}";
            string[] cells =
            [
                tool.Tool == 0 ? "(sem T)" : $"T{tool.Tool}",
                tool.Tool == 0 ? "—" : $"{DiameterOf(tool.Tool):0.###}",
                tool.FirstLine.ToString(),
                DurationFormat.Format(tool.TotalTime),
                DurationFormat.Format(tool.CuttingTime),
                DurationFormat.Format(tool.RapidTime),
                DurationFormat.Format(tool.DwellTime),
                feed,
                tool.MinZ is { } z ? $"{z:0.###}" : "—",
                Distance(tool.CuttingDistance),
                Distance(tool.RapidDistance),
            ];

            int line = tool.FirstLine;
            AddRow(ToolsGrid, row++, cells, () => LineRequested?.Invoke(this, line));
        }
    }

    // ───────────────────────── Limites ─────────────────────────

    private void FillBounds(ProgramAnalysis? analysis)
    {
        string[] headers = ["", "X mín.", "X máx.", "Y mín.", "Y máx.", "Z mín.", "Z máx."];
        PrepareGrid(BoundsGrid, headers.Length, 2);
        AddHeader(BoundsGrid, headers);

        if (analysis is null)
        {
            return;
        }

        AddRow(BoundsGrid, 1, BoundsCells("Corte", analysis.ToolPath.CuttingBounds), null);
        AddRow(BoundsGrid, 2, BoundsCells("Todos (com G00)", analysis.ToolPath.Bounds), null);
    }

    private static string[] BoundsCells(string label, Bounds3 b) => b.IsEmpty
        ? [label, "—", "—", "—", "—", "—", "—"]
        : [label, N(b.Min.X), N(b.Max.X), N(b.Min.Y), N(b.Max.Y), N(b.Min.Z), N(b.Max.Z)];

    // ───────────────────────── Montagem das tabelas ─────────────────────────

    private static void PrepareGrid(Grid grid, int columns, int rows)
    {
        grid.Children.Clear();
        grid.ColumnDefinitions.Clear();
        grid.RowDefinitions.Clear();
        for (int c = 0; c < columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        }

        for (int r = 0; r <= rows; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        }
    }

    private static void AddHeader(Grid grid, string[] headers)
    {
        var background = new Border { Background = HeaderBrush };
        Grid.SetColumnSpan(background, headers.Length);
        grid.Children.Add(background);

        for (int c = 0; c < headers.Length; c++)
        {
            grid.Children.Add(Cell(headers[c], 0, c, bold: true));
        }
    }

    /// <summary>Linha da tabela; com "onClick", a linha fica clicável (com destaque ao passar o mouse).</summary>
    private static void AddRow(Grid grid, int row, string[] cells, Action? onClick)
    {
        var background = new Border { Background = Brushes.Transparent };
        Grid.SetRow(background, row);
        Grid.SetColumnSpan(background, cells.Length);
        grid.Children.Add(background);

        var texts = new List<TextBlock>();
        for (int c = 0; c < cells.Length; c++)
        {
            TextBlock text = Cell(cells[c], row, c, bold: c == 0);
            texts.Add(text);
            grid.Children.Add(text);
        }

        if (onClick is null)
        {
            return;
        }

        // O fundo fica atrás dos textos: os eventos dos textos também acionam a linha
        void Enter(object? s, PointerEventArgs e) => background.Background = HoverBrush;
        void Leave(object? s, PointerEventArgs e) => background.Background = Brushes.Transparent;
        void Click(object? s, PointerPressedEventArgs e) => onClick();

        foreach (Control control in texts.Cast<Control>().Prepend(background))
        {
            control.Cursor = new Cursor(StandardCursorType.Hand);
            control.PointerEntered += Enter;
            control.PointerExited += Leave;
            control.PointerPressed += Click;
        }

        ToolTip.SetTip(background, "Clique para ir à linha no editor");
    }

    private static TextBlock Cell(string text, int row, int column, bool bold)
    {
        var block = new TextBlock
        {
            Text = text,
            Margin = new Thickness(8, 4, 14, 4),
            FontSize = 12,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            HorizontalAlignment = column == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetRow(block, row);
        Grid.SetColumn(block, column);
        return block;
    }

    private static string Distance(double mm) => mm >= 10_000 ? $"{mm / 1000:N2} m" : $"{mm:N1} mm";

    private static string N(double value) => value.ToString("0.###");
}
