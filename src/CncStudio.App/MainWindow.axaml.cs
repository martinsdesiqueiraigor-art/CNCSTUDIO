using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using CncStudio.App.Dialogs;
using CncStudio.App.Highlighting;
using CncStudio.Core;
using CncStudio.Core.Dialects;
using CncStudio.Core.Editing;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;
using CncStudio.App.Views;

namespace CncStudio.App;

/// <summary>
/// Janela principal. Code-behind explícito (sem framework MVVM no protótipo):
/// cuida de arquivos, editor, análise em segundo plano e painéis.
/// </summary>
public partial class MainWindow : Window
{
    // Limite de itens no painel de diagnósticos (arquivos enormes podem gerar milhares)
    private const int MaxDiagnosticsShown = 2000;

    private readonly GCodeColorizer _colorizer = new();
    private readonly DispatcherTimer _analysisTimer;
    private CancellationTokenSource? _analysisCancellation;
    private int _analysisVersion;

    private string? _currentFilePath;
    private string _displayName = "sem título";
    private bool _isDirty;
    private bool _isLoading;
    private bool _suppressDialectEvent;
    private bool _fitOnNextAnalysis = true;

    // Tamanho da fonte do editor
    private const double DefaultFontSize = 14;
    private const double MinFontSize = 8;
    private const double MaxFontSize = 40;

    public MainWindow()
    {
        InitializeComponent();

        Editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        Editor.TextChanged += OnEditorTextChanged;
        Editor.TextArea.Caret.PositionChanged += (_, _) => OnCaretMoved();

        // Reanalisa 500 ms depois da última tecla
        _analysisTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _analysisTimer.Tick += (_, _) =>
        {
            _analysisTimer.Stop();
            StartAnalysis();
        };

        OpenButton.Click += async (_, _) => await OpenFileAsync();
        SaveButton.Click += async (_, _) => await SaveFileAsync(saveAs: false);
        SaveAsButton.Click += async (_, _) => await SaveFileAsync(saveAs: true);

        UndoButton.Click += (_, _) => Editor.Undo();
        RedoButton.Click += (_, _) => Editor.Redo();
        FindButton.Click += (_, _) => OpenSearch(replace: false);
        ReplaceButton.Click += (_, _) => OpenSearch(replace: true);
        GoToLineButton.Click += async (_, _) => await AskGoToLineAsync();
        FontSmallerButton.Click += (_, _) => ChangeFontSize(-1);
        FontLargerButton.Click += (_, _) => ChangeFontSize(+1);
        RenumberButton.Click += async (_, _) => await RenumberAsync();
        UppercaseButton.Click += (_, _) => ApplyTextTool(text => NcTextTools.ToUpperOutsideComments(text, CurrentDialect));

        // Ctrl + roda do mouse no editor = zoom da fonte
        Editor.AddHandler(PointerWheelChangedEvent, OnEditorWheel, RoutingStrategies.Tunnel);
        SetFontSize(DefaultFontSize);
        FanucSampleButton.Click += (_, _) => LoadSample("EXEMPLO.nc");
        SiemensSampleButton.Click += (_, _) => LoadSample("EXEMPLO.mpf");
        DialectCombo.SelectionChanged += OnDialectComboChanged;
        DiagnosticsList.SelectionChanged += OnDiagnosticSelected;

        BackplotXY.IsCheckedChanged += (_, _) => { if (BackplotXY.IsChecked == true) Backplot.Plane = ViewPlane.XY; };
        BackplotXZ.IsCheckedChanged += (_, _) => { if (BackplotXZ.IsChecked == true) Backplot.Plane = ViewPlane.XZ; };
        BackplotYZ.IsCheckedChanged += (_, _) => { if (BackplotYZ.IsChecked == true) Backplot.Plane = ViewPlane.YZ; };
        BackplotIso.IsCheckedChanged += (_, _) => { if (BackplotIso.IsChecked == true) Backplot.Plane = ViewPlane.Iso; };
        BackplotFitButton.Click += (_, _) => Backplot.FitToContent();

        // Atalhos capturados antes do editor (rota "tunnel")
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        LoadSample("EXEMPLO.nc");
    }

    /// <summary>Resultado da última análise concluída (null antes da primeira).</summary>
    public ProgramAnalysis? CurrentAnalysis { get; private set; }

    private Dialect CurrentDialect => DialectCombo.SelectedIndex == 1 ? Dialect.Siemens : Dialect.Fanuc;

    // ───────────────────────── Arquivos ─────────────────────────

    private async Task OpenFileAsync()
    {
        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir programa CNC",
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Programas CNC")
                {
                    Patterns = ["*.nc", "*.mpf", "*.spf", "*.cnc", "*.tap", "*.iso", "*.txt"],
                },
                FilePickerFileTypes.All,
            ],
        });

        string? path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        if (path is null)
        {
            return;
        }

        try
        {
            string text = await File.ReadAllTextAsync(path);
            LoadText(text, path, Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            AnalysisStatus.Text = $"Não foi possível abrir o arquivo: {ex.Message}";
        }
    }

    private async Task SaveFileAsync(bool saveAs)
    {
        string? path = saveAs ? null : _currentFilePath;
        if (path is null)
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Salvar programa CNC",
                SuggestedFileName = CurrentDialect == Dialect.Siemens ? "PROGRAMA.mpf" : "PROGRAMA.nc",
                ShowOverwritePrompt = true,
            });
            path = file?.TryGetLocalPath();
            if (path is null)
            {
                return;
            }
        }

        try
        {
            await File.WriteAllTextAsync(path, Editor.Text);
            _currentFilePath = path;
            _displayName = Path.GetFileName(path);
            _isDirty = false;
            FileStatus.Text = path;
            UpdateTitle();
        }
        catch (Exception ex)
        {
            AnalysisStatus.Text = $"Não foi possível salvar: {ex.Message}";
        }
    }

    /// <summary>Abre um exemplo da pasta "samples". Salvar depois pede um local (não sobrescreve o exemplo).</summary>
    private void LoadSample(string fileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "samples", fileName);
        if (!File.Exists(path))
        {
            AnalysisStatus.Text = $"Exemplo não encontrado: {path}";
            return;
        }

        LoadText(File.ReadAllText(path), null, fileName);
        FileStatus.Text = $"{fileName} (exemplo)";
    }

    private void LoadText(string text, string? path, string displayName)
    {
        _isLoading = true;
        Editor.Document = new TextDocument(text);
        _isLoading = false;

        _currentFilePath = path;
        _displayName = displayName;
        _isDirty = false;
        _fitOnNextAnalysis = true;
        FileStatus.Text = path ?? displayName;
        UpdateTitle();
        UpdateCaretStatus();

        // Detecção automática pela extensão; o usuário pode trocar na combo
        SetDialect(DialectDetector.FromFileName(displayName));
    }

    // ───────────────────────── Dialeto ─────────────────────────

    private void SetDialect(Dialect dialect)
    {
        _suppressDialectEvent = true;
        DialectCombo.SelectedIndex = dialect == Dialect.Siemens ? 1 : 0;
        _suppressDialectEvent = false;
        ApplyDialect();
    }

    private void OnDialectComboChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_suppressDialectEvent)
        {
            ApplyDialect();
        }
    }

    private void ApplyDialect()
    {
        _colorizer.Dialect = CurrentDialect;
        Editor.TextArea.TextView.Redraw();
        StartAnalysis();
    }

    // ───────────────────────── Análise em segundo plano ─────────────────────────

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        if (!_isDirty)
        {
            _isDirty = true;
            UpdateTitle();
        }

        _analysisTimer.Stop();
        _analysisTimer.Start();
    }

    /// <summary>
    /// Dispara parse + trajetória numa thread de fundo. Se o texto mudar antes de
    /// terminar, a análise antiga é cancelada e o resultado dela é descartado.
    /// </summary>
    private async void StartAnalysis()
    {
        _analysisTimer.Stop();
        _analysisCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        _analysisCancellation = cancellation;
        int version = ++_analysisVersion;

        string text = Editor.Text;
        Dialect dialect = CurrentDialect;
        AnalysisStatus.Text = "Analisando…";

        try
        {
            // Parse, trajetória e figuras do backplot: tudo fora da thread de UI
            (ProgramAnalysis analysis, List<PathPolyline> figures) = await Task.Run(() =>
            {
                ProgramAnalysis result = ProgramAnalyzer.Analyze(text, dialect, cancellation.Token);
                List<PathPolyline> pathPolylines = PathPolylineBuilder.Build(
                    result.ToolPath.Moves, PathPolylineBuilder.ScreenTolerance(result.ToolPath));
                return (result, pathPolylines);
            }, cancellation.Token);

            if (version == _analysisVersion)
            {
                ApplyAnalysis(analysis, figures);
            }
        }
        catch (OperationCanceledException)
        {
            // Substituída por uma análise mais nova
        }
        catch (Exception ex)
        {
            if (version == _analysisVersion)
            {
                AnalysisStatus.Text = $"Erro interno na análise: {ex.Message}";
            }
        }
    }

    private void ApplyAnalysis(ProgramAnalysis analysis, List<PathPolyline> figures)
    {
        CurrentAnalysis = analysis;
        Backplot.SetToolPath(analysis.ToolPath, figures, _fitOnNextAnalysis);
        Simulation.SetToolPath(analysis.ToolPath);
        Backplot.SetHighlightedLine(Editor.TextArea.Caret.Line);
        _fitOnNextAnalysis = false;

        var items = analysis.Diagnostics
            .Take(MaxDiagnosticsShown)
            .Select(d => new DiagnosticItem(d))
            .ToList();
        DiagnosticsList.ItemsSource = items;

        string extra = analysis.Diagnostics.Count > MaxDiagnosticsShown
            ? $" (mostrando {MaxDiagnosticsShown})"
            : "";
        AnalysisStatus.Text =
            $"{analysis.BlockCount} blocos · {analysis.ToolPath.Moves.Count} movimentos · " +
            $"{analysis.ErrorCount} erros · {analysis.WarningCount} avisos{extra} · " +
            $"análise em {analysis.Elapsed.TotalMilliseconds:0} ms";
    }

    // ───────────────────────── Navegação ─────────────────────────

    private void OnDiagnosticSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (DiagnosticsList.SelectedItem is DiagnosticItem item)
        {
            GoToLine(item.Diagnostic.LineNumber, item.Diagnostic.Column, item.Diagnostic.Length);
        }
    }

    /// <summary>Leva o cursor à linha (base 1) e seleciona o trecho indicado, se houver.</summary>
    private void GoToLine(int lineNumber, int column = 0, int length = 0)
    {
        TextDocument document = Editor.Document;
        if (document.LineCount == 0)
        {
            return;
        }

        lineNumber = Math.Clamp(lineNumber, 1, document.LineCount);
        DocumentLine line = document.GetLineByNumber(lineNumber);
        column = Math.Clamp(column, 0, line.Length);
        length = Math.Clamp(length, 0, line.Length - column);

        Editor.Select(line.Offset + column, length);
        Editor.ScrollToLine(lineNumber);
        Editor.TextArea.Focus();
    }

    // ───────────────────────── Atalhos e status ─────────────────────────

    private async void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.F3)
        {
            // Próxima ocorrência da busca
            e.Handled = true;
            if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                Editor.SearchPanel.FindPrevious();
            }
            else
            {
                Editor.SearchPanel.FindNext();
            }

            return;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        switch (e.Key)
        {
            case Key.O:
                e.Handled = true;
                await OpenFileAsync();
                break;
            case Key.S:
                e.Handled = true;
                await SaveFileAsync(saveAs: shift);
                break;
            case Key.F:
                e.Handled = true;
                OpenSearch(replace: false);
                break;
            case Key.H:
                e.Handled = true;
                OpenSearch(replace: true);
                break;
            case Key.G:
                e.Handled = true;
                await AskGoToLineAsync();
                break;
            case Key.OemPlus or Key.Add:
                e.Handled = true;
                ChangeFontSize(+1);
                break;
            case Key.OemMinus or Key.Subtract:
                e.Handled = true;
                ChangeFontSize(-1);
                break;
            case Key.D0 or Key.NumPad0:
                e.Handled = true;
                SetFontSize(DefaultFontSize);
                break;
        }
    }

    // ───────────────────────── Ferramentas de edição ─────────────────────────

    /// <summary>Abre a barra de busca do editor (com o texto selecionado já preenchido).</summary>
    private void OpenSearch(bool replace)
    {
        var search = Editor.SearchPanel;
        string selected = Editor.SelectedText;
        if (!string.IsNullOrEmpty(selected) && !selected.Contains('\n'))
        {
            search.SearchPattern = selected;
        }

        search.IsReplaceMode = replace;
        search.Open();
    }

    private async Task AskGoToLineAsync()
    {
        var dialog = new GoToLineDialog(Editor.TextArea.Caret.Line, Editor.Document.LineCount);
        int? line = await dialog.ShowDialog<int?>(this);
        if (line is int target)
        {
            GoToLine(target);
        }
    }

    private async Task RenumberAsync()
    {
        RenumberOptions? options = await new RenumberDialog().ShowDialog<RenumberOptions?>(this);
        if (options is not null)
        {
            ApplyTextTool(text => NcTextTools.RenumberBlocks(text, CurrentDialect, options));
        }
    }

    /// <summary>
    /// Aplica uma transformação no texto inteiro como UMA operação de edição
    /// (Ctrl+Z desfaz tudo de uma vez) e mantém o cursor na mesma linha.
    /// </summary>
    private void ApplyTextTool(Func<string, string> transform)
    {
        string original = Editor.Text;
        string changed = transform(original);
        if (changed == original)
        {
            return;
        }

        int line = Editor.TextArea.Caret.Line;
        Editor.Document.Replace(0, Editor.Document.TextLength, changed);
        GoToLine(Math.Min(line, Editor.Document.LineCount));
    }

    private void OnEditorWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            ChangeFontSize(e.Delta.Y > 0 ? +1 : -1);
            e.Handled = true;
        }
    }

    private void ChangeFontSize(int direction) => SetFontSize(Editor.FontSize + direction);

    private void SetFontSize(double size)
    {
        Editor.FontSize = Math.Clamp(size, MinFontSize, MaxFontSize);
        FontSizeText.Text = $"{Editor.FontSize:0} pt";
    }

    private void UpdateTitle() =>
        Title = $"CncStudio — {_displayName}{(_isDirty ? " •" : "")}";

    private void OnCaretMoved()
    {
        UpdateCaretStatus();
        Backplot.SetHighlightedLine(Editor.TextArea.Caret.Line);
    }

    private void UpdateCaretStatus()
    {
        var caret = Editor.TextArea.Caret;
        CaretStatus.Text = $"Ln {caret.Line}, Col {caret.Column}";
    }

    /// <summary>Item do painel de diagnósticos (o ListBox mostra o ToString).</summary>
    private sealed record DiagnosticItem(ParseDiagnostic Diagnostic)
    {
        public override string ToString()
        {
            string kind = Diagnostic.Severity == DiagnosticSeverity.Error ? "ERRO " : "AVISO";
            return $"{kind}  Linha {Diagnostic.LineNumber}: {Diagnostic.Message}";
        }
    }
}
