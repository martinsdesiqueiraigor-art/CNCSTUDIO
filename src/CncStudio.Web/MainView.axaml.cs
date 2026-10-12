using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using CncStudio.App.Highlighting;
using CncStudio.Core;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Parsing;
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

    /// <summary>Abaixo desta altura (celular deitado) tudo fica mais compacto para o gráfico caber.</summary>
    private const double CompactHeight = 520;

    /// <summary>Exemplo aberto na primeira vez.</summary>
    private const string FirstExample = "REBAIXO_CANAL.nc";

    private readonly GCodeColorizer _colorizer = new();
    private readonly LineMotionRenderer _lineMotion = new();
    private readonly DiagnosticRenderer _diagnosticMarks = new();
    private int _problemIndex = -1;
    private bool? _compact;
    private readonly DispatcherTimer _analysisTimer;
    private readonly DispatcherTimer _simTimer;

    // Ícones do botão de executar/pausar
    private static readonly Geometry PlayIcon = StreamGeometry.Parse("M0,0 L11,6 L0,12 Z");
    private static readonly Geometry PauseIcon = StreamGeometry.Parse("M0,0 H4 V12 H0 Z M7,0 H11 V12 H7 Z");

    /// <summary>Aparelho de toque: a edição usa o editor nativo do navegador.</summary>
    private readonly bool _touch;

    // Simulação linha a linha: _stepEnds[k] = quantos movimentos existem até o fim do passo k
    private int[] _stepEnds = [];
    private int _step; // passos já executados (== _stepEnds.Length → trajetória completa)

    private string _fileName = "sem título.nc";
    private bool _loading;
    private bool? _narrow;
    private ProgramAnalysis? _analysis;

    public MainView()
    {
        InitializeComponent();

        Editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_lineMotion);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_diagnosticMarks);
        ProblemsButton.Click += (_, _) => GoToNextProblem();
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

        // No celular o teclado virtual não funciona bem dentro do editor desenhado pelo Avalonia:
        // o texto fica só para leitura e o botão "Editar" abre o editor nativo do aparelho
        try
        {
            _touch = WebInterop.IsTouch();
        }
        catch
        {
            _touch = false;
        }

        Editor.IsReadOnly = _touch;
        EditButton.IsVisible = _touch;
        EditButton.Click += async (_, _) => await EditNativeAsync();

        _simTimer = new DispatcherTimer();
        _simTimer.Tick += (_, _) => SimTick();
        SimStartButton.Click += (_, _) => { StopSimulation(); SetStep(0); };
        SimBackButton.Click += (_, _) => { StopSimulation(); SetStep(Math.Min(_step, _stepEnds.Length) - 1); };
        SimStepButton.Click += (_, _) => { StopSimulation(); SetStep(_step >= _stepEnds.Length ? 1 : _step + 1); };
        SimEndButton.Click += (_, _) => { StopSimulation(); SetStep(_stepEnds.Length); };
        SimPlayButton.Click += (_, _) => TogglePlay();
        SpeedSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
            {
                UpdateSpeed();
            }
        };
        UpdateSpeed();

        IsoViewButton.Click += (_, _) => SetMillView(MillView.Iso);
        TopViewButton.Click += (_, _) => SetMillView(MillView.Top);
        FrontViewButton.Click += (_, _) => SetMillView(MillView.Front);

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
        HeaderCodeButton.Click += (_, _) => ShowPage(graph: false);
        HeaderGraphButton.Click += (_, _) => ShowPage(graph: true);

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
            var item = new MenuItem { Header = name.Replace("_", "__") }; // "_" sozinho vira tecla de atalho
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
        try
        {
            // Seletor de arquivo nativo do navegador (funciona no Android e no PC)
            string? picked = await WebInterop.PickFileAsync();
            if (picked is null)
            {
                return;
            }

            int newline = picked.IndexOf('\n');
            LoadProgram(picked[..newline], picked[(newline + 1)..]);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Não foi possível abrir o arquivo: " + ex.Message;
        }
    }

    /// <summary>Edição no celular: editor de texto nativo em tela cheia, com o cursor na linha atual.</summary>
    private async Task EditNativeAsync()
    {
        StopSimulation();
        string? edited = await WebInterop.EditTextAsync(Editor.Text, Editor.TextArea.Caret.Line, _fileName);
        if (edited is null || edited == Editor.Text)
        {
            return;
        }

        int line = Editor.TextArea.Caret.Line;
        Editor.Document.Text = edited.Replace("\r\n", "\n");
        _analysisTimer.Stop();
        Analyze(keepView: true);
        GoToLine(Math.Min(line, Editor.Document.LineCount));
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
            _diagnosticMarks.Update([]);
            UpdateProblemsButton();
            _stepEnds = [];
            _step = 0;
            return;
        }

        StopSimulation();
        Backplot.SetPath(_analysis.ToolPath, keepView);
        _lineMotion.Update(_analysis.ToolPath);
        _lineMotion.ExecutingLine = 0;
        BuildSteps();
        _diagnosticMarks.Update(_analysis.Diagnostics);
        _problemIndex = -1;
        UpdateProblemsButton();
        bool lathe = _analysis.ToolPath.Machine == MachineKind.Lathe;
        LatheLabel.IsVisible = lathe;
        MillViews.IsVisible = !lathe;
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

    // ───────────────────────── Simulação linha a linha ─────────────────────────

    /// <summary>Agrupa os movimentos por linha do programa (um passo = uma linha que gera movimento).</summary>
    private void BuildSteps()
    {
        var ends = new List<int>();
        IReadOnlyList<CncStudio.Core.Trajectory.Move> moves = _analysis?.ToolPath.Moves ?? [];
        for (int i = 0; i < moves.Count; i++)
        {
            bool lastOfLine = i == moves.Count - 1 || moves[i + 1].LineNumber != moves[i].LineNumber;
            if (lastOfLine)
            {
                ends.Add(i + 1);
            }
        }

        _stepEnds = ends.ToArray();
        _step = _stepEnds.Length;
        ShowSimPosition();
    }

    /// <summary>Vai para o passo pedido (0 = início, Length = trajetória completa).</summary>
    private void SetStep(int step)
    {
        _step = Math.Clamp(step, 0, _stepEnds.Length);
        if (_step >= _stepEnds.Length)
        {
            Backplot.VisibleMoves = null;
            _lineMotion.ExecutingLine = 0;
            Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
            ShowSummary();
            ShowSimPosition();
            return;
        }

        int visible = _step == 0 ? 0 : _stepEnds[_step - 1];
        Backplot.VisibleMoves = visible;

        if (_step > 0 && _analysis is { } a)
        {
            int line = a.ToolPath.Moves[visible - 1].LineNumber;
            _lineMotion.ExecutingLine = line;
            Editor.ScrollToLine(line);
            Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
            if (line >= 1 && line <= Editor.Document.LineCount)
            {
                StatusText.Text = $"Linha {line}: {Editor.Document.GetText(Editor.Document.GetLineByNumber(line)).Trim()}";
            }

            // Como na máquina: chegou numa linha com erro, a execução para com alarme
            if (ErrorBetween(_step == 1 ? 1 : a.ToolPath.Moves[_stepEnds[_step - 2] - 1].LineNumber + 1, line) is { } alarm)
            {
                StopSimulation();
                StatusText.Text = $"Alarme na linha {alarm.LineNumber}: {alarm.Message}";
            }
        }
        else
        {
            _lineMotion.ExecutingLine = 0;
            Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
            StatusText.Text = "Início do programa";
        }

        ShowSimPosition();
    }

    /// <summary>Primeiro erro entre duas linhas (inclusive): as linhas sem movimento também contam.</summary>
    private ParseDiagnostic? ErrorBetween(int fromLine, int toLine) =>
        _analysis?.Diagnostics.FirstOrDefault(d =>
            d.Severity == DiagnosticSeverity.Error && d.LineNumber >= fromLine && d.LineNumber <= toLine);

    // ───────────────────────── Erros e avisos ─────────────────────────

    private void UpdateProblemsButton()
    {
        int errors = _analysis?.ErrorCount ?? 0;
        int warnings = _analysis?.WarningCount ?? 0;
        ProblemsButton.IsVisible = errors + warnings > 0;
        ProblemsButton.Classes.Set("warnings", errors == 0);
        ProblemsButton.Content = errors > 0
            ? (errors == 1 ? "1 erro" : $"{errors} erros")
            : (warnings == 1 ? "1 aviso" : $"{warnings} avisos");
        Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    /// <summary>Vai para o próximo problema (erros primeiro, depois avisos) e mostra a mensagem no rodapé.</summary>
    private void GoToNextProblem()
    {
        if (_analysis is not { } a || a.Diagnostics.Count == 0)
        {
            return;
        }

        List<ParseDiagnostic> ordered = a.Diagnostics
            .OrderByDescending(d => d.Severity == DiagnosticSeverity.Error)
            .ThenBy(d => d.LineNumber)
            .ToList();
        _problemIndex = (_problemIndex + 1) % ordered.Count;
        ParseDiagnostic d = ordered[_problemIndex];

        ShowPage(graph: false);
        GoToLine(d.LineNumber);
        string kind = d.Severity == DiagnosticSeverity.Error ? "Erro" : "Aviso";
        StatusText.Text = $"{kind} {_problemIndex + 1}/{ordered.Count} · linha {d.LineNumber}: {d.Message}";
    }

    private void ShowSimPosition()
    {
        SimPositionText.Text = _stepEnds.Length == 0
            ? "Sem movimentos"
            : _step >= _stepEnds.Length
                ? $"Completo · {_stepEnds.Length} linhas com movimento"
                : $"Passo {_step} de {_stepEnds.Length}";
    }

    private void TogglePlay()
    {
        if (_simTimer.IsEnabled)
        {
            StopSimulation();
            return;
        }

        if (_stepEnds.Length == 0)
        {
            return;
        }

        if (_step >= _stepEnds.Length)
        {
            SetStep(0); // estava completo: recomeça do início
        }

        _simTimer.Start();
        SimPlayIcon.Data = PauseIcon;
    }

    private void StopSimulation()
    {
        _simTimer.Stop();
        SimPlayIcon.Data = PlayIcon;
    }

    private void SimTick()
    {
        SetStep(_step + 1);
        if (_step >= _stepEnds.Length)
        {
            StopSimulation();
        }
    }

    private void UpdateSpeed()
    {
        int speed = (int)Math.Round(SpeedSlider.Value);
        SpeedText.Text = $"{speed} linhas/s";
        _simTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, speed));
    }

    private void SetMillView(MillView view)
    {
        IsoViewButton.IsChecked = view == MillView.Iso;
        TopViewButton.IsChecked = view == MillView.Top;
        FrontViewButton.IsChecked = view == MillView.Front;
        Backplot.MillView = view;
    }

    // ───────────────────────── Layout PC / celular ─────────────────────────

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        ApplyCompact(e.NewSize.Height < CompactHeight);
        bool narrow = e.NewSize.Width < NarrowWidth;
        if (narrow == _narrow)
        {
            return;
        }

        _narrow = narrow;
        PageSwitch.IsVisible = narrow && _compact != true;
        HeaderPages.IsVisible = narrow && _compact == true;
        BrandText.IsVisible = !narrow; // no celular fica só o símbolo, para caber os botões
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

    /// <summary>
    /// Tela baixa (celular deitado): botões Código/Gráfico sobem para o cabeçalho, os botões da vista
    /// flutuam sobre o gráfico e a linha da legenda some — o gráfico fica com quase toda a altura.
    /// </summary>
    private void ApplyCompact(bool compact)
    {
        if (compact == _compact)
        {
            return;
        }

        _compact = compact;
        bool narrow = _narrow == true;
        PageSwitch.IsVisible = narrow && !compact;
        HeaderPages.IsVisible = narrow && compact;
        SimInfoRow.IsVisible = !compact;
        Grid.SetRow(GraphHeader, compact ? 1 : 0);
        GraphHeader.ZIndex = compact ? 1 : 0;
        Workspace.Margin = compact ? new Thickness(0, 2, 0, 0) : new Thickness(0, 6, 0, 0);
    }

    private void ShowPage(bool graph)
    {
        CodePageButton.IsChecked = !graph;
        GraphPageButton.IsChecked = graph;
        HeaderCodeButton.IsChecked = !graph;
        HeaderGraphButton.IsChecked = graph;
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
