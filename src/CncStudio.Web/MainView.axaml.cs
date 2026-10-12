using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using CncStudio.App.Highlighting;
using CncStudio.Core;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Statistics;

namespace CncStudio.Web;

/// <summary>
/// Tela única da versão web: editor básico + backplot 2D.
/// PC (tela larga): editor e gráfico lado a lado. Celular: um de cada vez, com botões para alternar.
/// </summary>
public partial class MainView : UserControl
{
    /// <summary>Abaixo desta largura (px) a tela vira o modo celular.</summary>
    private const double NarrowWidth = 760;

    /// <summary>Exemplo aberto na primeira vez.</summary>
    private const string FirstExample = "REBAIXO_CANAL.nc";

    private readonly GCodeColorizer _colorizer = new();
    private readonly LineMotionRenderer _lineMotion = new();
    private readonly DispatcherTimer _analysisTimer;

    private string _fileName = "sem título.nc";
    private bool _loading;
    private bool? _narrow;
    private ProgramAnalysis? _analysis;

    public MainView()
    {
        InitializeComponent();

        Editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_lineMotion);
        Editor.Options.HighlightCurrentLine = true;
        Editor.TextChanged += (_, _) => ScheduleAnalysis();
        Editor.TextArea.Caret.PositionChanged += (_, _) => OnCaretMoved();

        // No navegador não há threads: a análise roda na thread da tela, um instante depois de parar de digitar
        _analysisTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _analysisTimer.Tick += (_, _) =>
        {
            _analysisTimer.Stop();
            Analyze(keepView: true);
        };

        OpenButton.Click += async (_, _) => await OpenFromDeviceAsync();
        FitButton.Click += (_, _) => Backplot.Fit();
        DialectCombo.SelectionChanged += (_, _) =>
        {
            if (!_loading)
            {
                Analyze(keepView: true);
            }
        };
        Backplot.LineRequested += (_, line) => GoToLine(line);

        CodePageButton.Click += (_, _) => ShowPage(graph: false);
        GraphPageButton.Click += (_, _) => ShowPage(graph: true);

        FillExamplesMenu();
        LoadExample(FirstExample);
    }

    private Dialect CurrentDialect => DialectCombo.SelectedIndex == 1 ? Dialect.Siemens : Dialect.Fanuc;

    // ───────────────────────── Arquivos ─────────────────────────

    private static IEnumerable<string> ExampleNames() =>
        typeof(MainView).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("samples/", StringComparison.Ordinal))
            .Select(n => n["samples/".Length..])
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

    private void FillExamplesMenu()
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        ExamplesButton.Flyout = menu;
        foreach (string name in ExampleNames())
        {
            var item = new MenuItem { Header = name };
            item.Click += (_, _) => LoadExample(name);
            menu.Items.Add(item);
        }
    }

    private void LoadExample(string name)
    {
        using Stream? stream = typeof(MainView).Assembly.GetManifestResourceStream("samples/" + name);
        if (stream is null)
        {
            return;
        }

        using var reader = new StreamReader(stream);
        LoadProgram(name, reader.ReadToEnd());
    }

    private async Task OpenFromDeviceAsync()
    {
        TopLevel? top = TopLevel.GetTopLevel(this);
        if (top is null)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await top.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir programa CNC",
            AllowMultiple = false,
            // No celular, filtros por extensão escondem arquivos .nc (tipo desconhecido): deixa "todos"
            FileTypeFilter =
            [
                new FilePickerFileType("Programas CNC") { Patterns = ["*.nc", "*.mpf", "*.spf", "*.txt", "*.tap", "*.cnc", "*.iso"] },
                FilePickerFileTypes.All,
            ],
        });

        if (files.Count == 0)
        {
            return;
        }

        try
        {
            await using Stream stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            LoadProgram(files[0].Name, await reader.ReadToEndAsync());
        }
        catch (Exception ex)
        {
            StatusText.Text = "Não foi possível abrir o arquivo: " + ex.Message;
        }
    }

    private void LoadProgram(string fileName, string text)
    {
        _loading = true;
        _fileName = fileName;
        FileNameText.Text = fileName;
        DialectCombo.SelectedIndex = DialectDetector.FromFileName(fileName) == Dialect.Siemens ? 1 : 0;
        Editor.Document = new TextDocument(text);
        Editor.ScrollToHome();
        _loading = false;
        _analysisTimer.Stop();
        Analyze(keepView: false);
    }

    // ───────────────────────── Análise ─────────────────────────

    private void ScheduleAnalysis()
    {
        if (_loading)
        {
            return;
        }

        _analysisTimer.Stop();
        _analysisTimer.Start();
    }

    private void Analyze(bool keepView)
    {
        Dialect dialect = CurrentDialect;
        _colorizer.Dialect = dialect;

        try
        {
            _analysis = ProgramAnalyzer.Analyze(Editor.Text, dialect, MachineSettings.Default);
        }
        catch (Exception ex)
        {
            _analysis = null;
            StatusText.Text = "Erro ao analisar: " + ex.Message;
            Backplot.SetPath(null, keepView);
            return;
        }

        Backplot.SetPath(_analysis.ToolPath, keepView);
        _lineMotion.Update(_analysis.ToolPath);
        Editor.TextArea.TextView.Redraw();
        ShowSummary();
    }

    private void ShowSummary()
    {
        if (_analysis is not { } a)
        {
            return;
        }

        string machine = a.ToolPath.Machine == MachineKind.Lathe ? "Torno" : "Fresa";
        string problems = a.ErrorCount + a.WarningCount == 0
            ? "sem problemas"
            : $"{a.ErrorCount} erro(s), {a.WarningCount} aviso(s)";
        StatusText.Text = $"{machine} · {a.BlockCount} blocos · {a.ToolPath.Moves.Count} movimentos · " +
                          $"tempo estimado {DurationFormat.Format(a.Statistics.TotalTime)} · {problems}";
    }

    // ───────────────────────── Editor ↔ gráfico ─────────────────────────

    private void OnCaretMoved()
    {
        int line = Editor.TextArea.Caret.Line;
        Backplot.HighlightLine(line);
    }

    private void GoToLine(int line)
    {
        if (line < 1 || line > Editor.Document.LineCount)
        {
            return;
        }

        DocumentLine docLine = Editor.Document.GetLineByNumber(line);
        Editor.TextArea.Caret.Line = line;
        Editor.TextArea.Caret.Column = 1;
        Editor.ScrollToLine(line);
        _lineMotion.ExecutingLine = line;
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
        Backplot.HighlightLine(line);

        // No celular o editor está escondido: mostra a linha tocada no rodapé
        string text = Editor.Document.GetText(docLine).Trim();
        StatusText.Text = $"Linha {line}: {text}";
    }

    // ───────────────────────── Layout PC / celular ─────────────────────────

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        bool narrow = e.NewSize.Width < NarrowWidth;
        if (narrow == _narrow)
        {
            return;
        }

        _narrow = narrow;
        PageSwitch.IsVisible = narrow;
        Splitter.IsVisible = !narrow;

        if (narrow)
        {
            Workspace.ColumnDefinitions = new ColumnDefinitions("*,0,0");
            ShowPage(graph: GraphPageButton.IsChecked == true);
        }
        else
        {
            Workspace.ColumnDefinitions = new ColumnDefinitions("*,6,*");
            Grid.SetColumn(EditorHost, 0);
            Grid.SetColumn(GraphHost, 2);
            EditorHost.IsVisible = true;
            GraphHost.IsVisible = true;
        }
    }

    private void ShowPage(bool graph)
    {
        CodePageButton.IsChecked = !graph;
        GraphPageButton.IsChecked = graph;
        if (_narrow != true)
        {
            return;
        }

        // No modo celular a página visível ocupa a coluna 0 inteira
        EditorHost.IsVisible = !graph;
        GraphHost.IsVisible = graph;
        Grid.SetColumn(EditorHost, 0);
        Grid.SetColumn(GraphHost, 0);
    }
}
