using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using CncStudio.App.Dialogs;
using CncStudio.App.Highlighting;
using CncStudio.App.Settings;
using CncStudio.Core;
using CncStudio.Core.Dialects;
using CncStudio.Core.Editing;
using CncStudio.Core.Execution;
using CncStudio.Core.Parsing;
using CncStudio.Core.Reports;
using CncStudio.Core.Setup;
using CncStudio.Core.Simulation;
using CncStudio.Core.Geometry;
using CncStudio.Core.Statistics;
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
    private readonly LineMotionRenderer _lineMotions = new();
    private AppSettings _settings = AppSettings.Load();
    private readonly DispatcherTimer _analysisTimer;
    private CancellationTokenSource? _analysisCancellation;
    private int _analysisVersion;

    private bool _isLoading;
    private bool _suppressDialectEvent;
    private bool _fitOnNextAnalysis = true;

    // Tamanho da fonte do editor
    private const double DefaultFontSize = 14;
    private const double MinFontSize = 8;
    private const double MaxFontSize = 40;

    // Construtor sem parâmetros exigido pelo carregador de XAML
    public MainWindow()
        : this([])
    {
    }

    public MainWindow(string[] args)
    {
        InitializeComponent();

        Editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        Editor.TextArea.TextView.BackgroundRenderers.Add(_lineMotions);
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
        SetupSheetButton.Click += async (_, _) => await OpenSetupSheetAsync();
        CompareButton.Click += async (_, _) =>
        {
            var window = new CompareWindow(Editor.Text, _active.DisplayName, CurrentDialect);
            window.Show(this);
            await window.ChooseRightFileAsync();
        };
        StockSettingsButton.Click += async (_, _) => await OpenStockDialogAsync();
        Graphics.StockSettingsRequested += async (_, _) => await OpenStockDialogAsync();
        MoreSamplesButton.Click += async (_, _) => await OpenFileAsync(Path.Combine(AppContext.BaseDirectory, "samples"));
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
        RemoveBlockNumbersButton.Click += (_, _) => ApplyTextTool(text =>
            NcTextTools.RenumberBlocks(text, CurrentDialect, new RenumberOptions(Mode: RenumberMode.Remove)));
        RemoveSpacesButton.Click += (_, _) => ApplyTextTool(text => NcTextTools.RemoveSpaces(text, CurrentDialect));
        InsertSpacesButton.Click += (_, _) => ApplyTextTool(text => NcTextTools.InsertSpaces(text, CurrentDialect));
        RemoveCommentsButton.Click += (_, _) => ApplyTextTool(text => NcTextTools.RemoveComments(text, CurrentDialect));

        Search.Attach(Editor);

        // Explicação do código ao parar o mouse
        CodeHelpCheck.IsChecked = _settings.ShowCodeHelp;
        CodeHelpCheck.IsCheckedChanged += (_, _) =>
        {
            _settings.ShowCodeHelp = CodeHelpCheck.IsChecked == true;
            HideCodeHelp();
        };
        _helpTimer.Tick += (_, _) => ShowCodeHelp();
        Editor.TextArea.PointerMoved += OnEditorPointerMoved;
        Editor.TextArea.PointerExited += (_, _) => HideCodeHelp();
        Editor.TextArea.KeyDown += (_, _) => HideCodeHelp();
        ShowErrorsToggle.IsCheckedChanged += (_, _) => RefreshDiagnostics();
        ShowWarningsToggle.IsCheckedChanged += (_, _) => RefreshDiagnostics();
        RefreshDiagnostics();

        // Ctrl + roda do mouse no editor = zoom da fonte
        Editor.AddHandler(PointerWheelChangedEvent, OnEditorWheel, RoutingStrategies.Tunnel);
        SetFontSize(_settings.EditorFontSize);

        // Configuração da máquina (ferramentas, zeros de peça)
        Graphics.DiameterOf = tool => _settings.DiameterOf(tool);
        // Perfil de corte: o do setup do programa ativo; senão, topo reto com o diâmetro da tabela
        Graphics.ProfileOf = tool => SetupBinding.ProfileOf(_active.Setup, tool, _settings.DiameterOf(tool));
        Graphics.ToolSettingsRequested += async (_, _) => await OpenMachineSettingsAsync();
        Statistics.DiameterOf = tool => _settings.DiameterOf(tool);
        Statistics.LineRequested += (_, line) => GoToLine(line);
        MachineSettingsButton.Click += async (_, _) => await OpenMachineSettingsAsync();
        Closing += OnWindowClosing;
        NewButton.Click += (_, _) => NewDocument();
        ThemeButton.Click += (_, _) => ApplyTheme(_settings.Theme == "Light" ? "Dark" : "Light");
        ApplyTheme(_settings.Theme);
        RecentButton.Click += (_, _) => ShowRecentMenu();
        FanucSampleButton.Click += (_, _) => LoadSample("EXEMPLO.nc");
        SiemensSampleButton.Click += (_, _) => LoadSample("EXEMPLO.mpf");
        LatheFanucSampleButton.Click += (_, _) => LoadSample(Path.Combine("torno", "EXEMPLO_TORNO.nc"));
        LatheSiemensSampleButton.Click += (_, _) => LoadSample(Path.Combine("torno", "EXEMPLO_TORNO.mpf"));

        MachineCombo.SelectedIndex = _settings.Machine switch { "Mill" => 1, "Lathe" => 2, _ => 0 };
        MachineCombo.SelectionChanged += (_, _) =>
        {
            _settings.Machine = MachineCombo.SelectedIndex switch { 1 => "Mill", 2 => "Lathe", _ => "Auto" };
            StartAnalysis();
        };
        DialectCombo.SelectionChanged += OnDialectComboChanged;
        DiagnosticsList.SelectionChanged += OnDiagnosticSelected;

        Graphics.LineReached += (_, line) => ShowExecutingLine(line); // execução: o editor acompanha
        Graphics.LineRequested += (_, line) => GoToLine(line);          // clique na trajetória

        // Atalhos capturados antes do editor (rota "tunnel")
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        string? startFile = args.FirstOrDefault(File.Exists);
        if (startFile is not null)
        {
            OpenPath(startFile);
        }
        else
        {
            LoadSample("EXEMPLO.nc");
        }
    }

    /// <summary>Resultado da última análise concluída (null antes da primeira).</summary>
    public ProgramAnalysis? CurrentAnalysis { get; private set; }

    private Dialect CurrentDialect => DialectCombo.SelectedIndex == 1 ? Dialect.Siemens : Dialect.Fanuc;

    // ───────────────────────── Arquivos ─────────────────────────

    private async Task OpenFileAsync(string? startFolder = null)
    {
        IStorageFolder? start = startFolder is not null && Directory.Exists(startFolder)
            ? await StorageProvider.TryGetFolderFromPathAsync(startFolder)
            : null;

        IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            SuggestedStartLocation = start,
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
        if (path is not null)
        {
            OpenPath(path);
        }
    }

    /// <summary>Abre o arquivo do caminho informado.</summary>
    private void OpenPath(string path)
    {
        try
        {
            string text = File.ReadAllText(path);
            LoadText(text, path, Path.GetFileName(path));
        }
        catch (Exception ex)
        {
            AnalysisStatus.Text = $"Não foi possível abrir o arquivo: {ex.Message}";
        }
    }

    private async Task SaveFileAsync(bool saveAs)
    {
        string? path = saveAs ? null : _active.FilePath;
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
            _active.FilePath = path;
            _active.DisplayName = Path.GetFileName(path);
            _active.IsDirty = false;
            _active.IsSample = false;
            AddRecent(path);
            RebuildTabs();
            _settings.SetSetup(SetupKey, _active.Setup); // "salvar como": o setup acompanha o arquivo novo

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

        LoadText(File.ReadAllText(path), null, Path.GetFileName(fileName), isSample: true);
    }

    /// <summary>
    /// Abre o texto numa aba. Se o arquivo já está aberto, só mostra a aba dele. Se a aba atual é
    /// um exemplo ou uma aba vazia sem alterações, ela é substituída (não acumula abas à toa).
    /// </summary>
    private void LoadText(string text, string? path, string displayName, bool isSample = false)
    {
        if (path is not null && _documents.Find(d => SamePath(d.FilePath, path)) is { } alreadyOpen)
        {
            SwitchTo(alreadyOpen);
            return;
        }

        var document = new OpenProgram
        {
            Text = new TextDocument(text),
            FilePath = path,
            DisplayName = displayName,
            IsSample = isSample,
            Dialect = DialectDetector.FromFileName(displayName),
        };
        document.Setup = _settings.SetupFor(document.SetupKey);

        int index = _documents.IndexOf(_active);
        if (index >= 0 && _active.IsDisposable)
        {
            _documents[index] = document;
        }
        else
        {
            _documents.Add(document);
        }

        SwitchTo(document);
        if (path is not null)
        {
            AddRecent(path);
        }
    }

    // ───────────────────────── Abas ─────────────────────────

    private readonly List<OpenProgram> _documents = [];
    private OpenProgram _active = new() { Text = new TextDocument() };
    private int _untitledCount;
    private bool _closeConfirmed;

    private static bool SamePath(string? a, string? b) =>
        a is not null && b is not null && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Mostra a aba no editor: texto (com o desfazer dela), cursor, dialeto, bruto.</summary>
    private void SwitchTo(OpenProgram document)
    {
        if (_documents.Contains(_active) && _active != document)
        {
            _active.CaretOffset = Editor.CaretOffset;
            _active.Dialect = CurrentDialect;
        }

        _active = document;
        _isLoading = true;
        Editor.Document = document.Text;
        _isLoading = false;
        Editor.CaretOffset = Math.Min(document.CaretOffset, document.Text.TextLength);
        Editor.ScrollToLine(Editor.TextArea.Caret.Line);

        _fitOnNextAnalysis = true;
        FileStatus.Text = document.FilePath ?? (document.IsSample ? $"{document.DisplayName} (exemplo)" : document.DisplayName);
        UpdateTitle();
        UpdateCaretStatus();
        RebuildTabs();
        SetDialect(document.Dialect); // reanalisa
    }

    /// <summary>Programa novo, vazio (Ctrl+N).</summary>
    private void NewDocument()
    {
        _untitledCount++;
        var document = new OpenProgram
        {
            Text = new TextDocument(),
            DisplayName = _untitledCount == 1 ? "sem título" : $"sem título {_untitledCount}",
            Dialect = CurrentDialect,
        };
        _documents.Add(document);
        SwitchTo(document);
    }

    /// <summary>Fecha a aba; se tem alterações, pergunta se quer salvar. Retorna false se cancelou.</summary>
    private async Task<bool> CloseDocumentAsync(OpenProgram document)
    {
        if (document.IsDirty)
        {
            SwitchTo(document);
            var dialog = new ConfirmDialog($"Salvar as alterações em \"{document.DisplayName}\"?",
                ["Salvar", "Não salvar", "Cancelar"], defaultButton: 0, cancelButton: 2);
            int? answer = await dialog.ShowDialog<int?>(this);
            if (answer is null or 2)
            {
                return false;
            }

            if (answer == 0)
            {
                await SaveFileAsync(saveAs: false);
                if (document.IsDirty)
                {
                    return false; // não salvou (cancelou o "salvar como")
                }
            }
        }

        int index = _documents.IndexOf(document);
        _documents.Remove(document);
        if (_documents.Count == 0)
        {
            NewDocument();
        }
        else if (document == _active)
        {
            SwitchTo(_documents[Math.Min(index, _documents.Count - 1)]);
        }
        else
        {
            RebuildTabs();
        }

        return true;
    }

    private void SwitchRelative(int step)
    {
        if (_documents.Count < 2)
        {
            return;
        }

        int index = (_documents.IndexOf(_active) + step + _documents.Count) % _documents.Count;
        SwitchTo(_documents[index]);
    }

    /// <summary>Faixa de abas acima do editor: nome (• = não salvo) e ✕ para fechar.</summary>
    private void RebuildTabs()
    {
        TabStrip.Children.Clear();
        foreach (OpenProgram document in _documents)
        {
            bool active = document == _active;
            var name = new TextBlock
            {
                Text = document.DisplayName + (document.IsDirty ? " •" : ""),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                FontSize = 12,
                FontWeight = active ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal,
            };
            var close = new Button
            {
                Content = "✕",
                FontSize = 10,
                Padding = new Avalonia.Thickness(4, 0),
                Margin = new Avalonia.Thickness(6, 0, 0, 0),
                Background = Avalonia.Media.Brushes.Transparent,
                BorderThickness = new Avalonia.Thickness(0),
            };
            ToolTip.SetTip(close, "Fechar (Ctrl+W)");
            close.Click += async (_, e) =>
            {
                e.Handled = true;
                await CloseDocumentAsync(document);
            };

            var tab = new Border
            {
                Child = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Children = { name, close } },
                Padding = new Avalonia.Thickness(10, 3, 4, 3),
                Margin = new Avalonia.Thickness(0, 0, 2, 0),
                CornerRadius = new Avalonia.CornerRadius(4, 4, 0, 0),
                Classes = { "documentTab" },
                Cursor = new Cursor(StandardCursorType.Hand),
            };
            if (active)
            {
                tab.Classes.Add("active");
            }

            ToolTip.SetTip(tab, document.FilePath ?? document.DisplayName);
            tab.PointerPressed += async (_, e) =>
            {
                if (e.GetCurrentPoint(tab).Properties.IsMiddleButtonPressed)
                {
                    await CloseDocumentAsync(document); // botão do meio fecha, como no navegador
                }
                else if (document != _active)
                {
                    SwitchTo(document);
                }
            };
            TabStrip.Children.Add(tab);
        }
    }

    /// <summary>Ao fechar a janela, avisa dos programas não salvos.</summary>
    private async void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        _settings.Save();
        List<OpenProgram> unsaved = _documents.Where(d => d.IsDirty).ToList();
        if (_closeConfirmed || unsaved.Count == 0)
        {
            return;
        }

        e.Cancel = true;
        string names = string.Join(", ", unsaved.Select(d => d.DisplayName));
        var dialog = new ConfirmDialog($"Há alterações não salvas em: {names}.\n\nSair mesmo assim?",
            ["Sair sem salvar", "Cancelar"], defaultButton: 1, cancelButton: 1);
        if (await dialog.ShowDialog<int?>(this) == 0)
        {
            _closeConfirmed = true;
            Close();
        }
    }

    // ───────────────────────── Tema ─────────────────────────

    /// <summary>Troca entre tema claro e escuro (interface e cores do editor).</summary>
    private void ApplyTheme(string theme)
    {
        bool light = theme == "Light";
        _settings.Theme = light ? "Light" : "Dark";
        if (Avalonia.Application.Current is { } app)
        {
            app.RequestedThemeVariant = light ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;
        }

        _colorizer.LightTheme = light;
        Editor.TextArea.TextView.Redraw();
        ThemeButton.Content = light ? "☾" : "☀";
        ToolTip.SetTip(ThemeButton, light ? "Mudar para o tema escuro" : "Mudar para o tema claro");
    }

    // ───────────────────────── Arquivos recentes ─────────────────────────

    private void AddRecent(string path)
    {
        string full = Path.GetFullPath(path);
        _settings.RecentFiles.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        _settings.RecentFiles.Insert(0, full);
        if (_settings.RecentFiles.Count > 12)
        {
            _settings.RecentFiles.RemoveRange(12, _settings.RecentFiles.Count - 12);
        }

        _settings.Save();
    }

    private void ShowRecentMenu()
    {
        var menu = new MenuFlyout();
        if (_settings.RecentFiles.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "(nenhum arquivo recente)", IsEnabled = false });
        }

        foreach (string path in _settings.RecentFiles)
        {
            var item = new MenuItem { Header = $"{Path.GetFileName(path)}   —   {Path.GetDirectoryName(path)}" };
            item.Click += (_, _) =>
            {
                if (File.Exists(path))
                {
                    OpenPath(path);
                }
                else
                {
                    _settings.RecentFiles.Remove(path);
                    AnalysisStatus.Text = $"Arquivo não encontrado (removido da lista): {path}";
                }
            };
            menu.Items.Add(item);
        }

        if (_settings.RecentFiles.Count > 0)
        {
            menu.Items.Add(new Separator());
            var clear = new MenuItem { Header = "Limpar a lista" };
            clear.Click += (_, _) =>
            {
                _settings.RecentFiles.Clear();
                _settings.Save();
            };
            menu.Items.Add(clear);
        }

        menu.ShowAt(RecentButton);
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

        if (!_active.IsDirty)
        {
            _active.IsDirty = true;
            UpdateTitle();
            RebuildTabs();
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
        string? folder = _active.FilePath is not null
            ? Path.GetDirectoryName(_active.FilePath)
            : Path.Combine(AppContext.BaseDirectory, "samples", "macros");
        MachineSettings machine = _active.Setup.ApplyTo(_settings.ToMachineSettings(folder));
        AnalysisStatus.Text = "Analisando…";

        try
        {
            // Parse, trajetória e figuras do backplot: tudo fora da thread de UI
            (ProgramAnalysis analysis, List<PathPolyline> figures) = await Task.Run(() =>
            {
                ProgramAnalysis result = ProgramAnalyzer.Analyze(text, dialect, machine, cancellation.Token);
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

    private List<PathPolyline> _figures = [];

    private void ApplyAnalysis(ProgramAnalysis analysis, List<PathPolyline> figures)
    {
        _figures = figures;
        Graphics.Stock = EffectiveStock(analysis);

        // Troca de fresa para torno (ou o contrário): reenquadra (a área gráfica escolhe a vista)
        bool lathe = analysis.ToolPath.Machine == MachineKind.Lathe;
        bool wasLathe = CurrentAnalysis?.ToolPath.Machine == MachineKind.Lathe;
        if (CurrentAnalysis is null || lathe != wasLathe)
        {
            _fitOnNextAnalysis = true;
        }

        CurrentAnalysis = analysis;
        Graphics.SetToolPath(analysis.ToolPath, figures, _fitOnNextAnalysis);
        Statistics.Show(analysis);
        Graphics.SetHighlightedLine(Editor.TextArea.Caret.Line);
        _lineMotions.Update(analysis.ToolPath);
        _lineMotions.ExecutingLine = 0;
        Editor.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Background);
        _fitOnNextAnalysis = false;

        RefreshDiagnostics();

        string extra = analysis.Diagnostics.Count > MaxDiagnosticsShown
            ? $" (mostrando {MaxDiagnosticsShown})"
            : "";
        string tools = analysis.ToolPath.Tools.Count == 0
            ? ""
            : " · ferramentas " + string.Join(", ", analysis.ToolPath.Tools.Select(t => $"T{t}"));
        string machineName = lathe ? "Torno" : "Fresa";
        if (_settings.Machine == "Auto")
        {
            machineName += " (automático)";
        }

        AnalysisStatus.Text =
            $"{machineName} · {analysis.BlockCount} blocos · {analysis.ToolPath.Moves.Count} movimentos{tools} · " +
            $"tempo estimado {DurationFormat.Format(analysis.Statistics.TotalTime)} · " +
            $"{analysis.ErrorCount} erros · {analysis.WarningCount} avisos{extra} · " +
            $"análise em {analysis.Elapsed.TotalMilliseconds:0} ms";
    }

    /// <summary>Lista de avisos/erros conforme o filtro, com a contagem nos botões.</summary>
    private void RefreshDiagnostics()
    {
        IReadOnlyList<ParseDiagnostic> all = CurrentAnalysis?.Diagnostics ?? [];
        int errors = all.Count(d => d.Severity == DiagnosticSeverity.Error);
        int warnings = all.Count - errors;
        ShowErrorsToggle.Content = $"⛔ Erros ({errors})";
        ShowWarningsToggle.Content = $"⚠ Avisos ({warnings})";

        bool showErrors = ShowErrorsToggle.IsChecked == true;
        bool showWarnings = ShowWarningsToggle.IsChecked == true;
        var filtered = all
            .Where(d => d.Severity == DiagnosticSeverity.Error ? showErrors : showWarnings)
            .ToList();

        DiagnosticsList.ItemsSource = filtered.Take(MaxDiagnosticsShown).Select(d => new DiagnosticItem(d)).ToList();
        DiagnosticsHint.Text = filtered.Count > MaxDiagnosticsShown
            ? $"mostrando {MaxDiagnosticsShown} de {filtered.Count}"
            : all.Count == 0 ? "nenhum problema encontrado" : "clique para ir à linha";
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

    /// <summary>Mostra a linha no editor (cursor no início) sem tirar o foco de onde está.</summary>
    /// <summary>A execução (simulação) chegou nesta linha: destaca e mostra no editor.</summary>
    private void ShowExecutingLine(int lineNumber)
    {
        _lineMotions.ExecutingLine = lineNumber;
        ShowLine(lineNumber);
        Editor.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Background);
    }

    private void ShowLine(int lineNumber)
    {
        TextDocument document = Editor.Document;
        if (lineNumber < 1 || lineNumber > document.LineCount)
        {
            return;
        }

        Editor.TextArea.Caret.Offset = document.GetLineByNumber(lineNumber).Offset;
        Editor.ScrollToLine(lineNumber);
    }

    // ───────────────────────── Folha de setup ─────────────────────────

    /// <summary>
    /// Gera a folha de setup do programa atual (HTML) e abre no navegador, de onde dá para
    /// imprimir ou salvar em PDF. Se o texto mudou desde a última análise, analisa de novo antes.
    /// </summary>
    private async Task OpenSetupSheetAsync()
    {
        string text = Editor.Text;
        Dialect dialect = CurrentDialect;
        string? filePath = _active.FilePath;
        string fileName = filePath is null ? "sem título" : Path.GetFileName(filePath);
        MachineSettings machine = _active.Setup.ApplyTo(_settings.ToMachineSettings(filePath is null ? null : Path.GetDirectoryName(filePath)));
        var tools = _settings.Tools.Where(t => t.Diameter > 0).GroupBy(t => t.Number).ToDictionary(g => g.Key, g => g.Last());
        string? stock = CurrentStockDescription;
        AnalysisStatus.Text = "Gerando a folha de setup…";

        try
        {
            ProgramAnalysis? current = CurrentAnalysis;
            bool stale = _analysisTimer.IsEnabled || current is null;
            List<PathPolyline> figures = _figures;

            string path = await Task.Run(() =>
            {
                ProgramAnalysis analysis = stale ? ProgramAnalyzer.Analyze(text, dialect, machine) : current!;
                List<PathPolyline> drawing = stale
                    ? PathPolylineBuilder.Build(analysis.ToolPath.Moves, PathPolylineBuilder.ScreenTolerance(analysis.ToolPath))
                    : figures;

                SetupSheet sheet = SetupSheetBuilder.Build(text, analysis, fileName, filePath,
                    t => tools.TryGetValue(t, out ToolEntry? e) ? (e.Diameter, e.Description) : null, stock);
                string html = SetupSheetHtml.Render(sheet, drawing);

                string folder = Path.Combine(Path.GetTempPath(), "CncStudio");
                Directory.CreateDirectory(folder);
                string output = Path.Combine(folder, $"Setup_{Path.GetFileNameWithoutExtension(fileName)}.html");
                File.WriteAllText(output, html, System.Text.Encoding.UTF8);
                return output;
            });

            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            AnalysisStatus.Text = $"Folha de setup aberta no navegador ({path}). Para PDF: Ctrl+P → Salvar como PDF.";
        }
        catch (Exception ex)
        {
            AnalysisStatus.Text = $"Não foi possível gerar a folha de setup: {ex.Message}";
        }
    }

    /// <summary>Descrição do bruto para a folha de setup (null = não informado).</summary>
    private string? CurrentStockDescription => CurrentAnalysis is { } analysis ? EffectiveStock(analysis)?.Describe() : null;

    // ───────────────────────── Bruto ─────────────────────────

    private string SetupKey => _active.SetupKey;

    /// <summary>
    /// Bruto que vale para a análise, pela regra única do Core: setup → comentário (BRUTO ...) → automático.
    /// O bruto do setup só vale se servir para o tipo de máquina da análise (bloco × barra).
    /// </summary>
    private Stock? EffectiveStock(ProgramAnalysis analysis) =>
        SetupBinding.EffectiveStock(_active.Setup, analysis.ToolPath.Machine, _active.Text.Text, analysis.ToolPath.CuttingBounds).Stock;

    private async Task OpenStockDialogAsync()
    {
        MachineKind machine = CurrentAnalysis?.ToolPath.Machine ?? MachineKind.Mill;
        Bounds3 cutting = CurrentAnalysis?.ToolPath.CuttingBounds ?? Bounds3.Empty;
        Stock? current = _active.Setup.Stock?.ToStock(machine);

        var dialog = new StockDialog(current, machine, cutting, Editor.Text);
        StockChoice? choice = await dialog.ShowDialog<StockChoice?>(this);
        if (choice is null)
        {
            return;
        }

        // O diálogo só edita bloco (fresa) e barra (torno): o resto do setup é preservado
        MachiningSetup setup = _active.Setup.Clone();
        setup.Stock = choice.Stock is { } stock
            ? SetupBinding.FromStock(stock, setup.Stock?.Material ?? "Aço")
            : null;
        _active.Setup = setup;
        _settings.SetSetup(SetupKey, setup);
        _settings.Save();
        StartAnalysis(); // redesenha backplot e refaz a simulação com o bruto novo
    }

    // ───────────────────────── Explicação do código (mouse) ─────────────────────────

    private readonly DispatcherTimer _helpTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private string? _pendingHelp;
    private string? _shownHelp;

    private void OnEditorPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_settings.ShowCodeHelp)
        {
            return;
        }

        string? help = HelpAt(e.GetPosition(Editor));
        if (help == _shownHelp && help is not null)
        {
            return; // continua sobre o mesmo código
        }

        HideCodeHelp();
        _pendingHelp = help;
        if (help is not null)
        {
            _helpTimer.Start();
        }
    }

    /// <summary>Explicação do código sob o ponto (coordenadas do editor), ou null.</summary>
    private string? HelpAt(Avalonia.Point point)
    {
        TextViewPosition? position = Editor.GetPositionFromPoint(point);
        if (position is not { } p || p.Line < 1 || p.Line > Editor.Document.LineCount)
        {
            return null;
        }

        DocumentLine line = Editor.Document.GetLineByNumber(p.Line);
        string text = Editor.Document.GetText(line.Offset, line.Length);
        int column = p.Column - 1;
        if (column < 0 || column >= text.Length)
        {
            return null;
        }

        MachineKind machine = CurrentAnalysis?.ToolPath.Machine ?? MachineKind.Mill;
        return GCodeHelp.Describe(text, column, CurrentDialect, machine);
    }

    private void ShowCodeHelp()
    {
        _helpTimer.Stop();
        if (_pendingHelp is null)
        {
            return;
        }

        _shownHelp = _pendingHelp;
        ToolTip.SetTip(Editor, new TextBlock { Text = _shownHelp, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 520 });
        ToolTip.SetIsOpen(Editor, true);
    }

    private void HideCodeHelp()
    {
        _helpTimer.Stop();
        _pendingHelp = null;
        if (_shownHelp is not null)
        {
            _shownHelp = null;
            ToolTip.SetIsOpen(Editor, false);
            ToolTip.SetTip(Editor, null);
        }
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
                Search.FindPrevious();
            }
            else
            {
                Search.FindNext();
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
            case Key.N:
                e.Handled = true;
                NewDocument();
                break;
            case Key.W:
                e.Handled = true;
                await CloseDocumentAsync(_active);
                break;
            case Key.Tab:
                e.Handled = true;
                SwitchRelative(shift ? -1 : +1);
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
        Search.Open(replace);
    }

    /// <summary>Edita ferramentas, zeros de peça e opções; ao salvar, reanalisa e refaz a simulação.</summary>
    private async Task OpenMachineSettingsAsync()
    {
        IReadOnlyList<int> toolsInProgram = CurrentAnalysis?.ToolPath.Tools ?? [];
        var dialog = new MachineSettingsDialog(_settings, toolsInProgram);
        AppSettings? edited = await dialog.ShowDialog<AppSettings?>(this);
        if (edited is null)
        {
            return;
        }

        edited.EditorFontSize = _settings.EditorFontSize;
        _settings = edited;
        _settings.Save();
        StartAnalysis(); // zeros e números sem ponto mudam a trajetória; a simulação é refeita junto
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
        _settings.EditorFontSize = Editor.FontSize;
        FontSizeText.Text = $"{Editor.FontSize:0} pt";
    }

    private void UpdateTitle() =>
        Title = $"Casillas Code — {_active.DisplayName}{(_active.IsDirty ? " •" : "")}";

    private void OnCaretMoved()
    {
        UpdateCaretStatus();
        Graphics.SetHighlightedLine(Editor.TextArea.Caret.Line);
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
