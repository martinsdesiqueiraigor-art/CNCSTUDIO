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
    private int[] _stepPass = [];      // passe do ciclo neste passo (1, 2, 3…)
    private int[] _stepPassCount = []; // total de passes da linha (1 = linha comum)
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
        Editor.TextChanged += (_, _) =>
        {
            ScheduleAnalysis();
            if (!_loading && !_dirty)
            {
                _dirty = true;
                UpdateTitle();
            }
        };
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
        // Campo de arquivo nativo por cima do botão "Abrir" (o toque cai direto nele)
        _openNative = new NativeFileButton(OpenButton, "open", multiple: false);
        _openNative.Picked += (_, files) =>
        {
            StopSimulation();
            CloseLibrary();
            LoadProgram(files[0].Name, files[0].Text);
        };

        // Biblioteca de programas
        Library.CurrentProgram = () => (_fileName, Editor.Text);
        Library.OpenRequested += (_, item) =>
        {
            StopSimulation();
            CloseLibrary();
            string name = item.Path.Contains('/') ? item.Path[(item.Path.LastIndexOf('/') + 1)..] : item.Path;
            LoadProgram(name, item.Text, item.Path);
        };
        Library.Saved += (_, path) => MarkSaved(path);
        Library.Message += (_, text) => SetStatus(text);
        Library.Closed += (_, _) => CloseLibrary();

        // Botão "Instalar": aparece quando o navegador avisa que o app pode ser instalado
        var installCheck = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        installCheck.Tick += (_, _) =>
        {
            try
            {
                InstallButton.IsVisible = WebInterop.CanInstall();
            }
            catch
            {
                installCheck.Stop();
            }
        };
        installCheck.Start();
        InstallButton.Click += async (_, _) =>
        {
            try
            {
                await WebInterop.PromptInstallAsync();
            }
            catch
            {
                // navegador sem suporte: o usuário pode instalar pelo menu do navegador
            }

            InstallButton.IsVisible = false;
        };
        BuildDisplayMenu();

        // Velocidade em passos de 1 com − e +
        SpeedDownButton.Click += (_, _) => SpeedSlider.Value = Math.Max(SpeedSlider.Minimum, Math.Round(SpeedSlider.Value) - 1);
        SpeedUpButton.Click += (_, _) => SpeedSlider.Value = Math.Min(SpeedSlider.Maximum, Math.Round(SpeedSlider.Value) + 1);

        // Vibração curta em qualquer botão (opcional, menu Exibir)
        AddHandler(Button.ClickEvent, (_, _) =>
        {
            if (_vibrate)
            {
                try
                {
                    WebInterop.Vibrate(12);
                }
                catch
                {
                    // sem suporte: segue sem vibrar
                }
            }
        }, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);

        VersionText.Text = AppVersion();

        // Lembrar onde parou: salva pouco depois de cada mudança e quando o app vai para segundo plano
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveState();
        };
        try
        {
            WebInterop.OnHidden(SaveState);
        }
        catch
        {
            // navegador sem suporte: salva só pelo temporizador
        }
        FitButton.Click += (_, _) => Backplot.Fit();
        DialectCombo.SelectionChanged += (_, _) =>
        {
            if (!_loading)
            {
                Analyze(keepView: true);
            }
        };
        Backplot.LineRequested += (_, line) => GoToLine(line);
        // Toque fora da trajetória: esconde/mostra as informações por cima do gráfico
        Backplot.EmptyTapped += (_, _) => GraphInfo.IsVisible = !GraphInfo.IsVisible;
        // Espaço ocupado pelos botões (em cima) e pelas informações (embaixo) ao enquadrar a peça
        Backplot.FitPadding = new Thickness(0, 40, 0, 46);

        CodePageButton.Click += (_, _) => ShowPage(graph: false);
        GraphPageButton.Click += (_, _) => ShowPage(graph: true);
        HeaderCodeButton.Click += (_, _) => ShowPage(graph: false);
        HeaderGraphButton.Click += (_, _) => ShowPage(graph: true);

        FillExamplesMenu();
        if (Stored("cc.theme") == "light")
        {
            ApplyTheme(true);
        }

        if (!RestoreState())
        {
            LoadExample(FirstExample);
        }
    }

    private Dialect CurrentDialect => DialectCombo.SelectedIndex == 1 ? Dialect.Siemens : Dialect.Fanuc;

    // ───────────────────────── Arquivos ─────────────────────────

    private static IEnumerable<string> ExampleNames() =>
        typeof(MainView).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("samples/", StringComparison.Ordinal))
            .Select(n => n["samples/".Length..])
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

    /// <summary>Menu "Arquivos": biblioteca, salvar, salvar como, compartilhar, novo e exemplos.</summary>
    private void FillExamplesMenu()
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        ExamplesButton.Flyout = menu;
        void Add(string header, Func<Task> action)
        {
            var item = new MenuItem { Header = header };
            item.Click += async (_, _) => await action();
            menu.Items.Add(item);
        }

        Add("Biblioteca…", OpenLibraryAsync);
        Add("Salvar", SaveAsync);
        Add("Salvar como…", SaveAsAsync);
        Add("Compartilhar / exportar", ShareCurrentAsync);
        menu.Items.Add(new Separator());
        Add("Novo programa (colar ou digitar)…", NewFromPasteAsync);

        var examples = new MenuItem { Header = "Exemplos" };
        foreach (string name in ExampleNames())
        {
            var item = new MenuItem { Header = name.Replace("_", "__") }; // "_" sozinho vira tecla de atalho
            item.Click += (_, _) =>
            {
                CloseLibrary();
                LoadExample(name);
            };
            examples.Items.Add(item);
        }

        menu.Items.Add(examples);
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

    private readonly NativeFileButton _openNative;

    /// <summary>Caminho do programa aberto na biblioteca (null = não está salvo nela).</summary>
    private string? _libraryPath;

    /// <summary>Há alterações ainda não salvas na biblioteca.</summary>
    private bool _dirty;

    private void UpdateTitle() => FileNameText.Text = (_dirty ? "• " : "") + (_libraryPath ?? _fileName);

    private void MarkSaved(string path)
    {
        _libraryPath = path;
        _fileName = path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path;
        _dirty = false;
        UpdateTitle();
        ScheduleSave();
    }

    // ───────────────────────── Biblioteca ─────────────────────────

    private async Task OpenLibraryAsync()
    {
        StopSimulation();
        LibraryHost.IsVisible = true;
        string folder = _libraryPath is { } p && p.Contains('/') ? p[..p.LastIndexOf('/')] : "";
        await Library.ShowFolderAsync(folder);
        Dispatcher.UIThread.Post(() => { Library.RefreshNativeButtons(); _openNative.Refresh(); }, DispatcherPriority.Background);
    }

    private void CloseLibrary()
    {
        if (!LibraryHost.IsVisible)
        {
            return;
        }

        LibraryHost.IsVisible = false;
        Dispatcher.UIThread.Post(() => Library.RefreshNativeButtons(), DispatcherPriority.Background);
    }

    /// <summary>Salvar: se o programa já está na biblioteca, grava por cima; senão, "Salvar como".</summary>
    private async Task SaveAsync()
    {
        if (_libraryPath is null)
        {
            await SaveAsAsync();
            return;
        }

        try
        {
            await WebInterop.LibWriteAsync(_libraryPath, Editor.Text);
            MarkSaved(_libraryPath);
            SetStatus("Salvo: " + _libraryPath);
        }
        catch (Exception ex)
        {
            SetStatus("Não foi possível salvar: " + ex.Message);
        }
    }

    /// <summary>Salvar como: abre a biblioteca na pasta atual e pede o nome (avisa antes de substituir).</summary>
    private async Task SaveAsAsync()
    {
        await OpenLibraryAsync();
        await Library.SaveHereAsync();
    }

    /// <summary>Compartilha o programa aberto (WhatsApp, Drive, e-mail...) ou baixa o arquivo.</summary>
    private async Task ShareCurrentAsync()
    {
        string json = System.Text.Json.JsonSerializer.Serialize(
            new List<Dictionary<string, string>> { new() { ["name"] = _fileName, ["text"] = Editor.Text } },
            LibraryJson.Default.ListDictionaryStringString);
        try
        {
            string? result = await WebInterop.ShareFilesAsync(json, _fileName);
            if (result == "downloaded")
            {
                SetStatus("Arquivo baixado (pasta Downloads do aparelho).");
            }
        }
        catch (Exception ex)
        {
            SetStatus("Não foi possível compartilhar: " + ex.Message);
        }
    }

    /// <summary>Programa novo: abre o editor nativo vazio para colar ou digitar (funciona mesmo onde o seletor de arquivos não abre).</summary>
    private async Task NewFromPasteAsync()
    {
        StopSimulation();
        string? text = await WebInterop.EditTextAsync("", 1, "Novo programa — cole ou digite");
        if (!string.IsNullOrWhiteSpace(text))
        {
            LoadProgram("novo.nc", text.Replace("\r\n", "\n"));
        }
    }

    /// <summary>Menu "Exibir": rápidos, percurso à frente na simulação e ferramenta.</summary>
    private bool _vibrate = true;

    /// <summary>Tema claro ou escuro para o app inteiro: telas, editor e gráfico.</summary>
    private void ApplyTheme(bool light)
    {
        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = light ? Avalonia.Styling.ThemeVariant.Light : Avalonia.Styling.ThemeVariant.Dark;
        }

        _colorizer.LightTheme = light;
        Backplot.LightTheme = light;
        Editor.TextArea.TextView.Redraw();
    }
    private readonly DispatcherTimer _saveTimer;
    private bool _restoring;

    /// <summary>Versão desta publicação ("v1.0.150"); o CI grava o número na compilação.</summary>
    private static string AppVersion()
    {
        string? info = typeof(MainView).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion;
        string version = (info ?? "1.0.0").Split('+')[0];
        return version == "1.0.0" ? "dev" : "v" + version;
    }

    private static string? Stored(string key)
    {
        try
        {
            return WebInterop.StorageGet(key);
        }
        catch
        {
            return null;
        }
    }

    private static void Store(string key, string value)
    {
        try
        {
            WebInterop.StorageSet(key, value);
        }
        catch
        {
            // sem armazenamento: o app só não lembra
        }
    }

    /// <summary>Guarda o programa aberto, a linha, a tela e a vista (para voltar ao mesmo ponto).</summary>
    private void SaveState()
    {
        if (_restoring)
        {
            return;
        }

        string text = Editor.Text;
        if (text.Length > 3_000_000)
        {
            return; // programa enorme: não cabe no armazenamento do navegador
        }

        Store("cc.name", _fileName);
        Store("cc.libpath", _libraryPath ?? "");
        Store("cc.dirty", _dirty ? "1" : "0");
        Store("cc.text", text);
        Store("cc.dialect", DialectCombo.SelectedIndex.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Store("cc.line", Editor.TextArea.Caret.Line.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Store("cc.page", GraphPageButton.IsChecked == true ? "graph" : "code");
        Store("cc.view", Backplot.MillView.ToString());
        Store("cc.speed", ((int)Math.Round(SpeedSlider.Value)).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Reabre o último programa e volta à mesma linha, tela e vista. Falso se não havia nada guardado.</summary>
    private bool RestoreState()
    {
        string? text = Stored("cc.text");
        string? name = Stored("cc.name");
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(name))
        {
            return false;
        }

        _restoring = true;
        try
        {
            string? libPath = Stored("cc.libpath");
            LoadProgram(name, text, string.IsNullOrEmpty(libPath) ? null : libPath);
            if (Stored("cc.dirty") == "1")
            {
                _dirty = true;
                UpdateTitle();
            }

            if (int.TryParse(Stored("cc.dialect"), out int dialect) && dialect is 0 or 1 && dialect != DialectCombo.SelectedIndex)
            {
                DialectCombo.SelectedIndex = dialect;
            }

            if (Enum.TryParse(Stored("cc.view"), out MillView view))
            {
                SetMillView(view);
            }

            if (int.TryParse(Stored("cc.speed"), out int speed))
            {
                SpeedSlider.Value = Math.Clamp(speed, 1, 50);
            }

            if (int.TryParse(Stored("cc.line"), out int line) && line >= 1 && line <= Editor.Document.LineCount)
            {
                Editor.TextArea.Caret.Line = line;
                Editor.ScrollToLine(line);
            }

            if (Stored("cc.page") == "graph")
            {
                GraphPageButton.IsChecked = true;
                ShowPage(graph: true);
            }
        }
        finally
        {
            _restoring = false;
        }

        return true;
    }

    private void BuildDisplayMenu()
    {
        CheckBox Option(string text, bool initial, Action<bool> apply)
        {
            var box = new CheckBox { Content = text, IsChecked = initial, MinHeight = 36 };
            box.IsCheckedChanged += (_, _) => apply(box.IsChecked == true);
            return box;
        }

        var panel = new StackPanel { Spacing = 2, Margin = new Thickness(4) };
        panel.Children.Add(Option("Rápidos (G00)", true, v => Backplot.ShowRapids = v));
        panel.Children.Add(Option("Percurso à frente (na simulação)", true, v => Backplot.ShowAhead = v));
        panel.Children.Add(Option("Ferramenta", true, v => Backplot.ShowTool = v));
        panel.Children.Add(Option("Modo claro (oficina com muita luz)", Stored("cc.theme") == "light", v =>
        {
            ApplyTheme(v);
            Store("cc.theme", v ? "light" : "dark");
        }));
        _vibrate = Stored("cc.vibrate") != "0";
        panel.Children.Add(Option("Vibrar ao tocar nos botões", _vibrate, v =>
        {
            _vibrate = v;
            Store("cc.vibrate", v ? "1" : "0");
        }));
        DisplayButton.Flyout = new Flyout { Content = panel, Placement = PlacementMode.BottomEdgeAlignedRight };
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
            SetStatus("Não foi possível abrir o arquivo: " + ex.Message);
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

    private void LoadProgram(string fileName, string text, string? libraryPath = null)
    {
        _loading = true;
        _fileName = fileName;
        _libraryPath = libraryPath;
        _dirty = false;
        UpdateTitle();
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
            SetStatus("Erro ao analisar: " + ex.Message);
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
        ScheduleSave();
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
        SetStatus($"{machine} · {a.BlockCount} blocos · {a.ToolPath.Moves.Count} movimentos · " +
                          $"tempo estimado {DurationFormat.Format(a.Statistics.TotalTime)} · {problems}");
    }

    /// <summary>Mensagem no rodapé da tela e também no rodapé do gráfico (que fica por cima do desenho).</summary>
    private void SetStatus(string text)
    {
        StatusText.Text = text;
        GraphStatusText.Text = text;
    }

    /// <summary>No celular, com o gráfico na tela, o rodapé de baixo some: a informação está por cima do gráfico.</summary>
    private void UpdateStatusBar()
    {
        StatusBar.IsVisible = !(_narrow == true && GraphPageButton.IsChecked == true);
    }

    // ───────────────────────── Editor ↔ gráfico ─────────────────────────

    private void OnCaretMoved()
    {
        if (!_loading)
        {
            ScheduleSave();
        }

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
        SetStatus($"Linha {line}: {text}");
    }

    // ───────────────────────── Simulação linha a linha ─────────────────────────

    /// <summary>
    /// Monta os passos da simulação: um passo por linha do programa que gera movimento.
    /// Ciclos (G71, G72, G74, G75, G76, G81–G83, CYCLE95…) geram muitos movimentos numa linha só:
    /// esses são divididos por penetração — cada passe = rápidos de posicionamento + cortes.
    /// </summary>
    private void BuildSteps()
    {
        var ends = new List<int>();
        var passes = new List<int>();
        var counts = new List<int>();
        IReadOnlyList<CncStudio.Core.Trajectory.Move> moves = _analysis?.ToolPath.Moves ?? [];

        int lineStart = 0;
        for (int i = 0; i < moves.Count; i++)
        {
            bool lastOfLine = i == moves.Count - 1 || moves[i + 1].LineNumber != moves[i].LineNumber;
            if (!lastOfLine)
            {
                continue;
            }

            // Fronteiras de passe dentro da linha: um rápido logo depois de um corte começa outro passe
            int firstStep = ends.Count;
            for (int k = lineStart; k < i; k++)
            {
                if (!moves[k].IsRapid && moves[k + 1].IsRapid)
                {
                    ends.Add(k + 1);
                }
            }

            ends.Add(i + 1);
            int total = ends.Count - firstStep;
            for (int n = 1; n <= total; n++)
            {
                passes.Add(n);
                counts.Add(total);
            }

            lineStart = i + 1;
        }

        _stepEnds = ends.ToArray();
        _stepPass = passes.ToArray();
        _stepPassCount = counts.ToArray();
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
                string pass = _stepPassCount[_step - 1] > 1 ? $" · passe {_stepPass[_step - 1]} de {_stepPassCount[_step - 1]}" : "";
                SetStatus($"Linha {line}{pass}: {Editor.Document.GetText(Editor.Document.GetLineByNumber(line)).Trim()}");
            }

            // Como na máquina: chegou numa linha com erro, a execução para com alarme
            if (ErrorBetween(_step == 1 ? 1 : a.ToolPath.Moves[_stepEnds[_step - 2] - 1].LineNumber + 1, line) is { } alarm)
            {
                StopSimulation();
                SetStatus($"Alarme na linha {alarm.LineNumber}: {alarm.Message}");
            }
        }
        else
        {
            _lineMotion.ExecutingLine = 0;
            Editor.TextArea.TextView.InvalidateLayer(KnownLayer.Background);
            SetStatus("Início do programa");
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
        SetStatus($"{kind} {_problemIndex + 1}/{ordered.Count} · linha {d.LineNumber}: {d.Message}");
    }

    private void ShowSimPosition()
    {
        SimPositionText.Text = _stepEnds.Length == 0
            ? "Sem movimentos"
            : _step >= _stepEnds.Length
                ? $"Completo · {_stepEnds.Length} passos"
                : _step > 0 && _stepPassCount[_step - 1] > 1
                    ? $"Passo {_step} de {_stepEnds.Length} · ciclo: passe {_stepPass[_step - 1]}/{_stepPassCount[_step - 1]}"
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
        SpeedText.Text = $"{speed}/s";
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
        UpdateStatusBar();

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
        Workspace.Margin = compact ? new Thickness(0, 2, 0, 0) : new Thickness(0, 6, 0, 0);
    }

    private void ShowPage(bool graph)
    {
        CodePageButton.IsChecked = !graph;
        GraphPageButton.IsChecked = graph;
        HeaderCodeButton.IsChecked = !graph;
        HeaderGraphButton.IsChecked = graph;
        UpdateStatusBar();
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
