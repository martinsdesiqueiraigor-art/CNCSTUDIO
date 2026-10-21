using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using CncStudio.Core.Dialects;
using CncStudio.Core.Editing;

namespace CncStudio.Web;

/// <summary>
/// Comparação de dois programas na web e no celular: o do editor (esquerda) contra um arquivo do aparelho
/// ou da biblioteca (direita). Uma coluna só, boa para tela estreita: linha removida em vermelho, incluída
/// em verde, alterada com as duas versões. Mostra só as diferenças (com contexto) ou tudo, vai de uma
/// diferença à outra e, ao tocar numa linha, leva o editor até ela. A comparação em si é do Core
/// (<see cref="ProgramDiff"/>, <see cref="DiffNavigation"/>).
/// </summary>
public sealed class ComparePanel : UserControl
{
    private const int Context = 2;

    private string _leftText = "";
    private string[] _leftLines = [];
    private string[] _rightLines = [];
    private string? _rightText;
    private Dialect _dialect = Dialect.Fanuc;
    private List<DiffRow> _rows = [];
    private IReadOnlyList<DiffEntry> _entries = [];
    private List<int> _blockEntry = [];     // posição (na lista mostrada) onde começa cada bloco
    private int _block = -1;

    private readonly TextBlock _leftTitle = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _rightTitle = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _summary = new() { FontSize = 12, Opacity = 0.85, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _counter = new() { VerticalAlignment = VerticalAlignment.Center, FontSize = 13, MinWidth = 70, TextAlignment = TextAlignment.Center };
    private readonly ToggleButton _ignoreSpaces = Chip("Ignorar espaços", true);
    private readonly ToggleButton _ignoreCase = Chip("Ignorar maiúsculas", false);
    private readonly ToggleButton _ignoreNumbers = Chip("Ignorar N", false);
    private readonly ToggleButton _onlyDiffs = Chip("Só diferenças", true);
    private readonly Button _previous = Action("◀", 44);
    private readonly Button _next = Action("▶", 44);
    private readonly NativeFileButton _deviceNative;
    private readonly ListBox _list = new() { Background = Brushes.Transparent, Padding = new Thickness(0) };
    private readonly TextBlock _empty = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(16), HorizontalAlignment = HorizontalAlignment.Center };

    public ComparePanel()
    {
        var close = Action("Fechar", 0);
        var device = Action("Do aparelho…", 0);
        var library = Action("Da biblioteca…", 0);
        close.Click += (_, _) => Closed?.Invoke(this, EventArgs.Empty);
        // Campo de arquivo nativo por cima do botão (o Android só abre o seletor com toque direto nele)
        _deviceNative = new NativeFileButton(device, "compare", multiple: false);
        _deviceNative.Picked += (_, files) => SetRight(files[0].Text, files[0].Name);
        library.Click += (_, _) => PickFromLibraryRequested?.Invoke(this, EventArgs.Empty);
        _previous.Click += (_, _) => GoToBlock(-1);
        _next.Click += (_, _) => GoToBlock(+1);
        foreach (ToggleButton chip in new[] { _ignoreSpaces, _ignoreCase, _ignoreNumbers, _onlyDiffs })
        {
            chip.IsCheckedChanged += (_, _) => Recompare();
        }

        _list.SelectionChanged += (_, _) =>
        {
            // Tocar numa linha leva o editor até ela
            if (_list.SelectedItem is Entry entry)
            {
                LineRequested?.Invoke(this, DiffNavigation.EditorLine(_rows, entry.RowIndex));
            }
        };

        var title = new TextBlock { Text = "Comparar programas", FontWeight = FontWeight.Bold, FontSize = 16, VerticalAlignment = VerticalAlignment.Center };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(12, 8, 12, 4) };
        header.Children.Add(title);
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        var pick = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(12, 0), ColumnSpacing = 6 };
        var names = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(_leftTitle);
        names.Children.Add(_rightTitle);
        pick.Children.Add(names);
        Grid.SetColumn(device, 1);
        Grid.SetColumn(library, 2);
        pick.Children.Add(device);
        pick.Children.Add(library);

        var chips = new WrapPanel { Margin = new Thickness(12, 6, 12, 0) };
        chips.Children.Add(_ignoreSpaces);
        chips.Children.Add(_ignoreCase);
        chips.Children.Add(_ignoreNumbers);
        chips.Children.Add(_onlyDiffs);

        var nav = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"), Margin = new Thickness(12, 6, 12, 6) };
        nav.Children.Add(_previous);
        Grid.SetColumn(_counter, 1);
        nav.Children.Add(_counter);
        Grid.SetColumn(_next, 2);
        nav.Children.Add(_next);
        Grid.SetColumn(_summary, 3);
        _summary.Margin = new Thickness(10, 0, 0, 0);
        _summary.VerticalAlignment = VerticalAlignment.Center;
        nav.Children.Add(_summary);

        var top = new StackPanel();
        top.Children.Add(header);
        top.Children.Add(pick);
        top.Children.Add(chips);
        top.Children.Add(nav);

        var body = new Grid();
        body.Children.Add(_list);
        body.Children.Add(_empty);
        _list.ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<Entry>((entry, _) => entry is null ? new Panel() : BuildRow(entry));

        var root = new DockPanel { Background = Brush("AppBackground") ?? Brush("BarBackground") };
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        root.Children.Add(body);
        Content = root;
    }

    /// <summary>Reposiciona o campo de arquivo nativo (chame ao mostrar ou esconder o painel).</summary>
    public void RefreshNative() => _deviceNative.Refresh();

    /// <summary>Fechar o painel.</summary>
    public event EventHandler? Closed;

    /// <summary>A pessoa quer escolher o arquivo da direita na biblioteca.</summary>
    public event EventHandler? PickFromLibraryRequested;

    /// <summary>Tocou numa linha: número (base 1) da linha do programa do editor.</summary>
    public event EventHandler<int>? LineRequested;

    /// <summary>Abre com o programa do editor à esquerda; a direita fica a escolher (ou a anterior continua).</summary>
    public void Open(string leftText, string leftName, Dialect dialect)
    {
        _leftText = leftText;
        _leftLines = SplitLines(leftText);
        _dialect = dialect;
        _leftTitle.Text = "Esquerda (editor): " + leftName;
        Recompare();
    }

    public void SetRight(string text, string name)
    {
        _rightText = text;
        _rightLines = SplitLines(text);
        _rightTitle.Text = "Direita: " + name;
        Recompare();
    }

    private static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

    private void Recompare()
    {
        _list.SelectedItem = null;
        _block = -1;
        if (_rightText is null)
        {
            _rightTitle.Text = "Direita: (escolha um arquivo)";
            _rows = [];
            _entries = [];
            _blockEntry = [];
            _list.ItemsSource = Array.Empty<Entry>();
            _summary.Text = "";
            _counter.Text = "";
            _previous.IsEnabled = _next.IsEnabled = false;
            _empty.Text = "Escolha o arquivo da direita (do aparelho ou da biblioteca) para comparar com o programa do editor.";
            _empty.IsVisible = true;
            return;
        }

        var options = new DiffOptions(_ignoreSpaces.IsChecked == true, _ignoreCase.IsChecked == true, _ignoreNumbers.IsChecked == true);
        _rows = ProgramDiff.Compare(_leftText, _rightText, options, _dialect);
        DiffSummary summary = DiffNavigation.Summarize(_rows);
        _entries = DiffNavigation.Collapse(_rows, _onlyDiffs.IsChecked == true ? Context : -1);

        var shown = new List<Entry>(_entries.Count);
        var blockStarts = DiffNavigation.BlockStarts(_rows);
        _blockEntry = [];
        foreach (DiffEntry entry in _entries)
        {
            if (blockStarts.Contains(entry.RowIndex))
            {
                _blockEntry.Add(shown.Count);
            }

            DiffRow row = _rows[entry.RowIndex];
            shown.Add(new Entry(
                entry.RowIndex,
                row.Kind,
                entry.HiddenBefore,
                row.LeftLine,
                row.RightLine,
                row.LeftLine is { } l && l <= _leftLines.Length ? _leftLines[l - 1] : "",
                row.RightLine is { } r && r <= _rightLines.Length ? _rightLines[r - 1] : ""));
        }

        _list.ItemsSource = shown;
        _summary.Text = summary.Total == 0
            ? "Os programas são iguais (com as opções marcadas)."
            : $"{summary.Blocks} {(summary.Blocks == 1 ? "diferença" : "diferenças")} · {summary.Changed} alteradas · {summary.Added} incluídas · {summary.Removed} removidas";
        _previous.IsEnabled = _next.IsEnabled = summary.Blocks > 0;
        _counter.Text = summary.Blocks > 0 ? $"– de {summary.Blocks}" : "";
        _empty.IsVisible = shown.Count == 0;
        _empty.Text = summary.Total == 0 ? "Nenhuma diferença." : "";
        if (summary.Blocks > 0)
        {
            GoToBlock(+1);
        }
    }

    private void GoToBlock(int direction)
    {
        if (_blockEntry.Count == 0)
        {
            return;
        }

        _block = (_block + direction + _blockEntry.Count) % _blockEntry.Count;
        _counter.Text = $"{_block + 1} de {_blockEntry.Count}";
        _list.ScrollIntoView(_blockEntry[_block]);
    }

    private sealed record Entry(int RowIndex, DiffKind Kind, int HiddenBefore, int? LeftLine, int? RightLine, string LeftText, string RightText);

    private static readonly FontFamily Mono = new("avares://CncStudio.Web/Assets/Fonts#DejaVu Sans Mono");

    private Control BuildRow(Entry entry)
    {
        var stack = new StackPanel();
        if (entry.HiddenBefore > 0)
        {
            stack.Children.Add(new TextBlock
            {
                Text = $"··· {entry.HiddenBefore} {(entry.HiddenBefore == 1 ? "linha igual" : "linhas iguais")} ···",
                FontSize = 11,
                Opacity = 0.55,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 4),
            });
        }

        switch (entry.Kind)
        {
            case DiffKind.Equal:
                stack.Children.Add(Line(entry.LeftLine, "", entry.LeftText, null, 0.7));
                break;
            case DiffKind.Removed:
                stack.Children.Add(Line(entry.LeftLine, "−", entry.LeftText, Color.FromArgb(70, 229, 72, 77), 1));
                break;
            case DiffKind.Added:
                stack.Children.Add(Line(entry.RightLine, "+", entry.RightText, Color.FromArgb(70, 46, 160, 67), 1));
                break;
            default: // alterada: as duas versões, uma sobre a outra
                stack.Children.Add(Line(entry.LeftLine, "−", entry.LeftText, Color.FromArgb(70, 229, 72, 77), 1));
                stack.Children.Add(Line(entry.RightLine, "+", entry.RightText, Color.FromArgb(70, 46, 160, 67), 1));
                break;
        }

        return stack;
    }

    private static Control Line(int? number, string sign, string text, Color? back, double opacity)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("44,18,*"), MinHeight = 26, Background = back is { } c ? new SolidColorBrush(c) : null };
        grid.Children.Add(new TextBlock { Text = number?.ToString() ?? "", FontFamily = Mono, FontSize = 12, Opacity = 0.55, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        var signText = new TextBlock { Text = sign, FontFamily = Mono, FontWeight = FontWeight.Bold, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(signText, 1);
        grid.Children.Add(signText);
        var body = new TextBlock { Text = text.Length == 0 ? " " : text, FontFamily = Mono, FontSize = 13, Opacity = opacity, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 2, 8, 2) };
        Grid.SetColumn(body, 2);
        grid.Children.Add(body);
        return grid;
    }

    private static ToggleButton Chip(string text, bool on) => new()
    {
        Content = text,
        IsChecked = on,
        MinHeight = 36,
        Padding = new Thickness(10, 2),
        Margin = new Thickness(0, 0, 6, 6),
        FontSize = 12,
    };

    private static Button Action(string text, double minWidth) => new()
    {
        Content = text,
        MinHeight = 44,
        MinWidth = minWidth,
        Padding = new Thickness(12, 4),
        HorizontalContentAlignment = HorizontalAlignment.Center,
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    private IBrush? Brush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out object? value) && value is IBrush brush ? brush
        : Application.Current?.TryFindResource(key, Application.Current.ActualThemeVariant, out object? app) == true && app is IBrush b ? b
        : null;
}
