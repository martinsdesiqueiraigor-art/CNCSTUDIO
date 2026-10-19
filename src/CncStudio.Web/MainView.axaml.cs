using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using CncStudio.App.Highlighting;
using CncStudio.Core;
using CncStudio.Core.Dialects;
using CncStudio.Core.Editing;
using CncStudio.Core.Execution;
using CncStudio.Core.Parsing;
using CncStudio.Core.Statistics;
using CncStudio.Core.Trajectory;
using CncStudio.Core.Reports;
using CncStudio.Core.Setup;
using CncStudio.Core.Scene;

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
        BuildToolsMenu();

        _simTimer = new DispatcherTimer();
        _simTimer.Tick += (_, _) => SimTick();
        SimStartButton.Click += (_, _) => { StopSimulation(); SetStep(0); };
        SimBackButton.Click += (_, _) => { StopSimulation(); SetStep(Math.Min(_step, _stepEnds.Length) - 1); };
        SimStepButton.Click += (_, _) => { StopSimulation(); SetStep(_step >= _stepEnds.Length ? 1 : _step + 1); };
        SimEndButton.Click += (_, _) => { StopSimulation(); SetStep(_stepEnds.Length); };
        SimPlayButton.Click += (_, _) => TogglePlay();
        BuildSpeedFlyout();
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
        SetupButton.Click += async (_, _) => await EditMachiningSetupAsync();

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
        Library.AllowSave = EnsureFullAccessAsync;
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
        FitButton.Click += (_, _) => Fit3DOr2D();
        Wire3D();
        DialectCombo.SelectionChanged += (_, _) =>
        {
            if (!_loading)
            {
                Analyze(keepView: true);
            }
        };
        Backplot.LineRequested += (_, line) => GoToLine(line);
        // Toque fora da trajetória: esconde/mostra as informações por cima do gráfico
        Backplot.EmptyTapped += (_, _) =>
        {
            GraphInfo.IsVisible = !GraphInfo.IsVisible;
            UpdateTimeChip();
        };
        TimeChip.Click += (_, _) => OpenStats();
        // Barra de progresso: arrastar vai a qualquer passo; tocar numa marca vai à troca de ferramenta
        SimSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty && !_syncingSlider)
            {
                StopSimulation();
                SetStep((int)Math.Round(SimSlider.Value));
            }
        };
        ToolMarks.MarkClicked += (_, change) =>
        {
            StopSimulation();
            int index = Array.FindIndex(_stepEnds, end => end > change.MoveIndex);
            if (index >= 0)
            {
                SetStep(index + 1);
            }
        };
        CollapseEditorButton.Click += (_, _) => SetEditorCollapsed(!_editorCollapsed);
        StatsView.Closed += (_, _) => StatsHost.IsVisible = false;
        StatsView.SetupSheetRequested += async (_, _) => await OpenSetupSheetAsync();
        StatsView.AccountRequested += async (_, _) =>
        {
            StatsHost.IsVisible = false;
            await OpenAccountAsync();
        };
        // Espaço ocupado pelos botões (em cima) e pelas informações (embaixo) ao enquadrar a peça
        Backplot.FitPadding = new Thickness(0, 8, 0, 34);

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

        // 3D: volta como estava (só carrega o motor 3D se a pessoa deixou aberto)
        _technical = Stored("cc.technical") == "1";
        PlotMode savedMode = Stored("cc.mode") switch
        {
            "3d" => PlotMode.ThreeD,
            "2d3d" => PlotMode.TwoDPlus3D,
            "2d" => PlotMode.TwoD,
            _ => Stored("cc.view3d") == "1" ? PlotMode.TwoDPlus3D : PlotMode.TwoD, // versão anterior
        };
        if (savedMode != PlotMode.TwoD)
        {
            Dispatcher.UIThread.Post(async () => await SetModeAsync(savedMode), DispatcherPriority.Background);
        }

        // Conta: confere a situação ao abrir (e pede para entrar se não houver conta)
        Dispatcher.UIThread.Post(async () => await RefreshAccountAsync(askLogin: true), DispatcherPriority.Background);
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

        Add("Conta…", OpenAccountAsync);
        menu.Items.Add(new Separator());
        Add("Biblioteca…", OpenLibraryAsync);
        Add("Salvar", SaveAsync);
        Add("Salvar como…", SaveAsAsync);
        Add("Compartilhar / exportar", ShareCurrentAsync);
        menu.Items.Add(new Separator());
        Add("Novo programa (colar ou digitar)…", NewFromPasteAsync);
        menu.Items.Add(new Separator());
        Add("Estatísticas", () => { OpenStats(); return Task.CompletedTask; });
        Add("Folha de setup…", OpenSetupSheetAsync);

        menu.Items.Add(BuildExamplesMenu());
    }

    /// <summary>Grupos do menu Exemplos: pasta em samples/exemplos → título.</summary>
    private static readonly (string Folder, string Title)[] ExampleGroups =
    [
        ("fanuc-torno", "Fanuc · Torno"),
        ("fanuc-fresa", "Fanuc · Fresa"),
        ("siemens-torno", "Siemens · Torno"),
        ("siemens-fresa", "Siemens · Fresa"),
    ];

    /// <summary>Exemplos agrupados por comando e máquina, um por ciclo/método de repetição; os antigos em "Programas completos".</summary>
    private MenuItem BuildExamplesMenu()
    {
        var examples = new MenuItem { Header = "Exemplos" };
        List<string> names = ExampleNames().ToList();

        MenuItem Entry(string name)
        {
            // "fanuc-torno/04_G71_G70_DESBASTE.nc" → "04  G71 G70 DESBASTE"
            string file = Path.GetFileNameWithoutExtension(name.Split('/')[^1]);
            int underscore = file.IndexOf('_');
            string label = underscore is > 0 and <= 3 && file[..underscore].All(char.IsAsciiDigit)
                ? file[..underscore] + "  " + file[(underscore + 1)..].Replace('_', ' ')
                : file.Replace('_', ' ');
            var item = new MenuItem { Header = label };
            item.Click += (_, _) =>
            {
                CloseLibrary();
                LoadExample(name);
            };
            return item;
        }

        foreach ((string folder, string title) in ExampleGroups)
        {
            string prefix = "exemplos/" + folder + "/";
            var group = new MenuItem { Header = title };
            foreach (string name in names.Where(n => n.StartsWith(prefix, StringComparison.Ordinal)))
            {
                group.Items.Add(Entry(name));
            }

            if (group.Items.Count > 0)
            {
                examples.Items.Add(group);
            }
        }

        var complete = new MenuItem { Header = "Programas completos" };
        foreach (string name in names.Where(n => !n.StartsWith("exemplos/", StringComparison.Ordinal)))
        {
            complete.Items.Add(Entry(name));
        }

        examples.Items.Add(complete);
        return examples;
    }

    private void LoadExample(string name)
    {
        using Stream? stream = typeof(MainView).Assembly.GetManifestResourceStream("samples/" + name);
        if (stream is null)
        {
            return;
        }

        using var reader = new StreamReader(stream);
        LoadProgram(name.Split('/')[^1], reader.ReadToEnd());
    }

    private readonly NativeFileButton _openNative;

    /// <summary>Caminho do programa aberto na biblioteca (null = não está salvo nela).</summary>
    private string? _libraryPath;

    /// <summary>Há alterações ainda não salvas na biblioteca.</summary>
    private bool _dirty;

    private void UpdateTitle() => FileNameText.Text = (_dirty ? "• " : "") + (_libraryPath ?? _fileName);

    private void MarkSaved(string path)
    {
        string oldKey = SetupKey;
        _libraryPath = path;
        _fileName = path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path;
        if (SetupKey != oldKey && Stored(oldKey) is { Length: > 0 })
        {
            // "Salvar como": o setup acompanha o programa no endereço novo
            _machiningSetup.ProjectKey = _libraryPath ?? _fileName;
            Store(SetupKey, SetupJson.Serialize(_machiningSetup));
        }
        _dirty = false;
        UpdateTitle();
        ScheduleSave();
    }

    // ───────────────────────── Estatísticas e folha de setup ─────────────────────────

    /// <summary>Botão "Ciclo 3min 07s" no gráfico: aparece com as informações do rodapé do gráfico.</summary>
    private void UpdateTimeChip()
    {
        bool has = _analysis is { } a && a.ToolPath.Moves.Count > 0;
        // No 3D o chip ficaria embaixo da vista 3D (elemento do navegador, sempre por cima)
        TimeChip.IsVisible = has && GraphInfo.IsVisible && !_view3D;
        if (has)
        {
            TimeChip.Content = "Ciclo " + DurationFormat.Format(_analysis!.Statistics.TotalTime) + "  ›";
        }
    }

    /// <summary>Dados da folha de setup do programa aberto (bruto: setup → comentário "BRUTO ..." → não informado).</summary>
    private SetupSheet? BuildSetupSheet()
    {
        if (_analysis is not { } analysis)
        {
            return null;
        }

        string text = Editor.Text;
        SetupSheet sheet = SetupSheetBuilder.Build(text, analysis, _fileName, _libraryPath, _ => null);
        EffectiveStockResult stock = SetupBinding.EffectiveStock(_machiningSetup, sheet.Machine, text, sheet.CuttingBounds);
        if (stock.Stock is { } effective && (stock.Source == StockSource.Setup || sheet.Stock is null))
        {
            sheet = sheet with { Stock = effective.Describe(BrazilianCulture.Instance) };
        }

        return sheet;
    }

    private void OpenStats()
    {
        if (BuildSetupSheet() is not { } sheet)
        {
            return;
        }

        CloseLibrary();
        StatsView.Show(sheet, _fullAccess);
        StatsHost.IsVisible = true;
    }

    /// <summary>Folha de setup em tela cheia; depois do teste grátis, sem licença, com marca d'água.</summary>
    private async Task OpenSetupSheetAsync()
    {
        if (BuildSetupSheet() is not { } sheet || _analysis is not { } analysis)
        {
            return;
        }

        StopSimulation();
        try
        {
            List<PathPolyline> figures = PathPolylineBuilder.Build(analysis.ToolPath.Moves, PathPolylineBuilder.ScreenTolerance(analysis.ToolPath));
            string html = SetupSheetHtml.Render(sheet, figures, BrazilianCulture.Instance, printHint: false,
                watermark: _fullAccess ? null : "VERSÃO DE AVALIAÇÃO");
            string fileBase = "Setup_" + Path.GetFileNameWithoutExtension(_fileName);
            await WebInterop.ShowSetupSheetAsync(html, fileBase);
        }
        catch (Exception ex)
        {
            SetStatus("Não foi possível montar a folha de setup: " + ex.Message);
        }
    }

    // ───────────────────────── Conta e licença ─────────────────────────

    /// <summary>Última situação da conta (JSON do account.js).</summary>
    private string _accountJson = "{}";

    /// <summary>Trial ou licença ativa: salvar e exportar liberados.</summary>
    private bool _fullAccess;

    private bool _accountChecked;

    private static (bool SignedIn, string State, bool Recovery) ReadAccount(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            System.Text.Json.JsonElement root = doc.RootElement;
            bool signedIn = root.TryGetProperty("signedIn", out var a) && a.ValueKind == System.Text.Json.JsonValueKind.True;
            string state = root.TryGetProperty("state", out var b) && b.ValueKind == System.Text.Json.JsonValueKind.String ? b.GetString()! : "";
            bool recovery = root.TryGetProperty("recovery", out var c) && c.ValueKind == System.Text.Json.JsonValueKind.True;
            return (signedIn, state, recovery);
        }
        catch (System.Text.Json.JsonException)
        {
            return (false, "", false);
        }
    }

    /// <summary>
    /// Confere a conta no servidor (ou a última confirmação, sem internet) e aplica as restrições.
    /// askLogin: sem conta, mostra a tela de entrar.
    /// </summary>
    private async Task RefreshAccountAsync(bool askLogin, string reason = "")
    {
        try
        {
            string json = await WebInterop.AccountCheckAsync() ?? "{}";
            var account = ReadAccount(json);

            if (account.Recovery && await WebInterop.AccountNewPasswordAsync())
            {
                _statusNote = "Senha nova salva";
            }

            if (!account.SignedIn && askLogin && await WebInterop.AccountLoginAsync(reason) == "ok")
            {
                json = await WebInterop.AccountCheckAsync() ?? "{}";
                account = ReadAccount(json);
            }

            ApplyAccount(json);
            if (account.SignedIn && account.State is "device_limit" or "device_revoked")
            {
                await OpenAccountAsync(); // explica e oferece "Sair da conta"
            }
        }
        catch (Exception ex)
        {
            // Sem o módulo da conta (navegador antigo): o app segue funcionando sem restrição
            _fullAccess = true;
            SetStatus("Não foi possível conferir a conta: " + ex.Message);
        }
    }

    private void ApplyAccount(string json)
    {
        _accountJson = json;
        _accountChecked = true;
        var account = ReadAccount(json);
        _fullAccess = account.SignedIn && account.State is "licensed" or "trial";
        string summary;
        try
        {
            summary = WebInterop.AccountSummary(json);
        }
        catch
        {
            summary = "";
        }

        // Duas linhas curtas: cabe no celular sem empurrar os botões
        VersionText.Text = summary.Length == 0 ? AppVersion() : AppVersion() + "\n" + summary;
        if (_analysis is not null)
        {
            ShowSummary();
        }
    }

    /// <summary>Menu Arquivos › Conta: situação, aparelhos e "Sair da conta".</summary>
    private async Task OpenAccountAsync()
    {
        string? action = await WebInterop.AccountPanelAsync(_accountJson);
        switch (action)
        {
            case "signout":
                await WebInterop.AccountSignOutAsync();
                ApplyAccount("{}");
                _statusNote = "Você saiu da conta";
                await RefreshAccountAsync(askLogin: true);
                break;
            case "login":
                await RefreshAccountAsync(askLogin: true);
                break;
        }
    }

    /// <summary>
    /// Salvar e exportar: liberados no trial e com licença. Sem isso, explica e oferece entrar
    /// (ou ver a conta). Retorna true se pode seguir.
    /// </summary>
    private async Task<bool> EnsureFullAccessAsync()
    {
        if (!_accountChecked)
        {
            await RefreshAccountAsync(askLogin: false);
        }

        if (_fullAccess)
        {
            return true;
        }

        var account = ReadAccount(_accountJson);
        if (!account.SignedIn)
        {
            await RefreshAccountAsync(askLogin: true, "Salvar e exportar precisam de uma conta. Crie a sua e use tudo grátis por 14 dias.");
        }
        else
        {
            await OpenAccountAsync();
        }

        return _fullAccess;
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
        if (!await EnsureFullAccessAsync())
        {
            return;
        }

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
        if (!await EnsureFullAccessAsync())
        {
            return;
        }

        await OpenLibraryAsync();
        await Library.SaveHereAsync();
    }

    /// <summary>Compartilha o programa aberto (WhatsApp, Drive, e-mail...) ou baixa o arquivo.</summary>
    private async Task ShareCurrentAsync()
    {
        if (!await EnsureFullAccessAsync())
        {
            return;
        }

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
        if (_scene3DView is not null)
        {
            _scene3DView.LightTheme = light;
        }
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

    private CheckBox? _sectionOption;

    /// <summary>Legenda das cores (no menu Ver, não solta sobre o gráfico).</summary>
    private static Control BuildLegend()
    {
        Control Row(uint rgb, string text)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(4, 1) };
            row.Children.Add(new Border
            {
                Width = 14,
                Height = 4,
                CornerRadius = new CornerRadius(2),
                VerticalAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(Color.FromUInt32(0xFF000000 | rgb)),
            });
            row.Children.Add(new TextBlock { Text = text, FontSize = 12, Opacity = 0.85 });
            return row;
        }

        var legend = new StackPanel { Spacing = 0 };
        legend.Children.Add(new TextBlock { Text = "Legenda", FontSize = 12, FontWeight = FontWeight.SemiBold, Margin = new Thickness(4, 0, 4, 2) });
        legend.Children.Add(Row(0xFF7A1A, "Corte (avanço)"));
        legend.Children.Add(Row(0x4AA3FF, "Rápido (G00)"));
        legend.Children.Add(Row(0xE5484D, "Rápido que encosta no material"));
        legend.Children.Add(Row(ScenePalette.ToolColors[0], "Modo técnico: uma cor por ferramenta (T1, T2…)"));
        return legend;
    }

    private void BuildDisplayMenu()
    {
        CheckBox Option(string text, bool initial, Action<bool> apply)
        {
            var box = new CheckBox { Content = text, IsChecked = initial, MinHeight = 36 };
            box.IsCheckedChanged += (_, _) => apply(box.IsChecked == true);
            return box;
        }

        var panel = new StackPanel { Spacing = 2, Margin = new Thickness(4), MinWidth = 250 };
        panel.Children.Add(Option("Trajetória", true, v =>
        {
            SetPath3D(v);
        }));
        panel.Children.Add(Option("Rápidos (G00)", true, v =>
        {
            Backplot.ShowRapids = v;
            SetRapids3D(v);
        }));
        panel.Children.Add(Option("Ferramenta", true, v =>
        {
            Backplot.ShowTool = v;
            SetTool3D(v);
        }));
        _sectionOption = Option("Meia seção (torno)", true, SetQuarterSection);
        panel.Children.Add(_sectionOption);
        panel.Children.Add(Option("Modo técnico (corte na cor de cada ferramenta)", Stored("cc.technical") == "1", SetTechnical3D));
        panel.Children.Add(Option("Material transparente (3D)", false, SetTransparent3D));
        panel.Children.Add(Option("Fundo claro (3D)", Stored("cc.bg3d") == "1", SetLightBackground3D));
        panel.Children.Add(Option("Percurso à frente (na simulação)", true, v => Backplot.ShowAhead = v));
        panel.Children.Add(new Separator { Margin = new Thickness(0, 4) });
        panel.Children.Add(Option("Modo claro do app (oficina com muita luz)", Stored("cc.theme") == "light", v =>
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
        panel.Children.Add(new Separator { Margin = new Thickness(0, 4) });
        panel.Children.Add(BuildLegend());
        // A vista 3D é um elemento do navegador e fica por cima de qualquer menu: enquanto o Ver está
        // aberto, ela some (e volta ao fechar). Menu com rolagem para caber em tela baixa (celular deitado).
        var scroll = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var flyout = new Flyout { Content = scroll, Placement = PlacementMode.BottomEdgeAlignedRight };
        flyout.Opening += (_, _) => scroll.MaxHeight = Math.Max(180, Bounds.Height - 90);
        flyout.Opened += (_, _) => Scene3DSlot.IsVisible = false;
        flyout.Closed += (_, _) => Scene3DSlot.IsVisible = _view3D;
        DisplayButton.Flyout = flyout;
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
    // ───────────────────────── Funções do editor ─────────────────────────

    /// <summary>
    /// Menu "Funções": renumerar (início, incremento, todos os blocos ou só os que têm N),
    /// remover numeração e desfazer. Só listas e botões (sem digitação, que falha no Android).
    /// </summary>
    private void BuildToolsMenu()
    {
        static ComboBox Choice(int[] values, int selected) => new()
        {
            ItemsSource = values,
            SelectedItem = selected,
            MinWidth = 90,
        };

        int Read(string key, int fallback) =>
            int.TryParse(Stored(key), out int v) && v > 0 ? v : fallback;

        ComboBox start = Choice([1, 5, 10, 100, 1000], Read("cc.rn.start", 10));
        ComboBox increment = Choice([1, 2, 5, 10, 100], Read("cc.rn.step", 10));
        var all = new RadioButton { Content = "Todos os blocos (cria N onde falta)", GroupName = "rn", IsChecked = Stored("cc.rn.mode") != "existing" };
        var existing = new RadioButton { Content = "Só os blocos que já têm N", GroupName = "rn", IsChecked = Stored("cc.rn.mode") == "existing" };

        var renumber = new Button { Content = "Renumerar", Classes = { "bar", "primary" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        var remove = new Button { Content = "Remover numeração", Classes = { "bar" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };
        var undo = new Button { Content = "Desfazer", Classes = { "bar" }, HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center };

        Grid Row(string label, Control control)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 2) };
            grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
            return grid;
        }

        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(6), MinWidth = 260 };
        panel.Children.Add(new TextBlock { Text = "Renumerar blocos (N)", FontWeight = FontWeight.Bold });
        panel.Children.Add(Row("Começar em N", start));
        panel.Children.Add(Row("Incremento", increment));
        panel.Children.Add(all);
        panel.Children.Add(existing);
        panel.Children.Add(renumber);
        panel.Children.Add(new Separator { Margin = new Thickness(0, 4) });
        panel.Children.Add(remove);
        panel.Children.Add(undo);
        panel.Children.Add(new TextBlock
        {
            Text = "P/Q do G70–G73, GOTO e M99 P acompanham a nova numeração.",
            FontSize = 12,
            Opacity = 0.7,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 260,
        });

        var flyout = new Flyout { Content = panel, Placement = PlacementMode.BottomEdgeAlignedRight };
        flyout.Opened += (_, _) => undo.IsEnabled = Editor.CanUndo;
        ToolsButton.Flyout = flyout;

        renumber.Click += (_, _) =>
        {
            int first = start.SelectedItem is int s ? s : 10;
            int step = increment.SelectedItem is int i ? i : 10;
            RenumberMode mode = existing.IsChecked == true ? RenumberMode.ExistingOnly : RenumberMode.AllBlocks;
            Store("cc.rn.start", first.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Store("cc.rn.step", step.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Store("cc.rn.mode", mode == RenumberMode.ExistingOnly ? "existing" : "all");
            flyout.Hide();
            ApplyRenumber(new RenumberOptions(first, step, mode));
        };
        remove.Click += (_, _) =>
        {
            flyout.Hide();
            ApplyRenumber(new RenumberOptions(Mode: RenumberMode.Remove));
        };
        undo.Click += (_, _) =>
        {
            flyout.Hide();
            if (Editor.CanUndo)
            {
                StopSimulation();
                Editor.Undo();
                _statusNote = "Desfeito";
            }
        };
    }

    /// <summary>Aplica a renumeração (ou remoção) ao programa inteiro, como uma única ação desfazível.</summary>
    private void ApplyRenumber(RenumberOptions options)
    {
        StopSimulation();
        Dialect dialect = CurrentDialect;
        string before = Editor.Text;
        int references = NcTextTools.ReferencedBlockNumbers(before, dialect).Count;
        string after;
        try
        {
            after = NcTextTools.RenumberBlocks(before, dialect, options);
        }
        catch (ArgumentException)
        {
            return;
        }

        if (after == before)
        {
            SetStatus(options.Mode == RenumberMode.Remove ? "O programa não tem numeração para remover." : "Nada mudou.");
            return;
        }

        int line = Editor.TextArea.Caret.Line;
        Editor.Document.Replace(0, Editor.Document.TextLength, after); // uma ação só no "Desfazer"
        GoToLine(Math.Min(line, Editor.Document.LineCount));

        _statusNote = options.Mode switch
        {
            RenumberMode.Remove when references > 0 =>
                $"Numeração removida; {references} bloco(s) ficaram com N por serem destino de P/Q, GOTO ou M99 P",
            RenumberMode.Remove => "Numeração removida",
            _ when references > 0 => $"Renumerado de N{options.Start} em {options.Increment}; {references} referência(s) atualizada(s)",
            _ => $"Renumerado de N{options.Start} em {options.Increment}",
        };
        _analysisTimer.Stop();
        Analyze(keepView: true);
    }

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
        LoadMachiningSetup();
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
            _analysis = ProgramAnalyzer.Analyze(Editor.Text, dialect, _machiningSetup.ApplyTo(MachineSettings.Default));
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
            Rebuild3D();
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
        UpdateViewButtons();
        Rebuild3D();
        Editor.TextArea.TextView.Redraw();
        UpdateTimeChip();
        if (StatsHost.IsVisible)
        {
            OpenStats(); // painel aberto acompanha a edição
        }

        ShowSummary();
    }

    /// <summary>Mensagem de uma ação (ex.: renumerar) mostrada junto do próximo resumo da análise.</summary>
    private string? _statusNote;

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
        string note = _statusNote is null ? "" : _statusNote + " · ";
        _statusNote = null;
        SetStatus(note + $"{machine} · {a.BlockCount} blocos · {a.ToolPath.Moves.Count} movimentos · " +
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

        // Distância acumulada (para "percorrido / total") e marcas de troca de ferramenta
        _cumLength = new double[moves.Count + 1];
        for (int i = 0; i < moves.Count; i++)
        {
            _cumLength[i + 1] = _cumLength[i] + moves[i].Length;
        }

        int stepCount = Math.Max(1, _stepEnds.Length);
        ToolMarks.SetMarks(
            _analysis?.ToolPath.ToolChanges ?? [],
            change =>
            {
                int index = Array.FindIndex(_stepEnds, end => end > change.MoveIndex);
                return index < 0 ? 0 : index / (double)stepCount;
            });
        SyncSlider();
        ShowSimPosition();
    }

    /// <summary>Vai para o passo pedido (0 = início, Length = trajetória completa).</summary>
    private void SetStep(int step)
    {
        _step = Math.Clamp(step, 0, _stepEnds.Length);
        OnStepChanged3D();
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
        ProblemsButton.IsVisible = errors + warnings > 0 && !_editorCollapsed;
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

    // O navegador roda em modo invariante (sem "pt-BR"): vírgula decimal pela cultura invariante copiada
    private static readonly System.Globalization.CultureInfo PtCulture = MakePtCulture();

    private static System.Globalization.CultureInfo MakePtCulture()
    {
        var culture = (System.Globalization.CultureInfo)System.Globalization.CultureInfo.InvariantCulture.Clone();
        culture.NumberFormat.NumberDecimalSeparator = ",";
        return culture;
    }

    private double[] _cumLength = [0];

    private void SyncSlider()
    {
        _syncingSlider = true;
        SimSlider.Maximum = Math.Max(1, _stepEnds.Length);
        SimSlider.Value = Math.Min(_step, SimSlider.Maximum);
        _syncingSlider = false;
    }

    /// <summary>
    /// Linha de informações do ponto atual: posição, ferramenta, G, linha, passo, distância
    /// percorrida / total e avanço.
    /// </summary>
    private void ShowSimPosition()
    {
        SyncSlider();
        if (_stepEnds.Length == 0 || _analysis is not { } analysis)
        {
            SimPositionText.Text = "Sem movimentos";
            return;
        }

        var culture = PtCulture;
        var moves = analysis.ToolPath.Moves;
        bool complete = _step >= _stepEnds.Length;
        int visible = complete ? moves.Count : _step == 0 ? 0 : _stepEnds[_step - 1];
        double total = analysis.ToolPath.TotalLength;
        string distance = $"{CncStudio.Core.Trajectory.PathMarks.FormatDistance(_cumLength[visible], culture)} / {CncStudio.Core.Trajectory.PathMarks.FormatDistance(total, culture)}";
        string step = complete
            ? $"Completo · {_stepEnds.Length} passos"
            : _step > 0 && _stepPassCount[_step - 1] > 1
                ? $"Passo {_step}/{_stepEnds.Length} · passe {_stepPass[_step - 1]}/{_stepPassCount[_step - 1]}"
                : $"Passo {_step}/{_stepEnds.Length}";
        if (visible == 0)
        {
            SimPositionText.Text = $"{step} · {distance}";
        }
        else
        {
            CncStudio.Core.Trajectory.Move m = moves[visible - 1];
            string x = (analysis.ToolPath.Machine == MachineKind.Lathe ? m.End.X * 2 : m.End.X).ToString("0.###", culture);
            string pos = analysis.ToolPath.Machine == MachineKind.Lathe
                ? $"X{x} Z{m.End.Z.ToString("0.###", culture)}"
                : $"X{x} Y{m.End.Y.ToString("0.###", culture)} Z{m.End.Z.ToString("0.###", culture)}";
            string g = m.Motion switch
            {
                CncStudio.Core.Trajectory.MotionMode.Rapid => "G00",
                CncStudio.Core.Trajectory.MotionMode.Linear => "G01",
                CncStudio.Core.Trajectory.MotionMode.ClockwiseArc => "G02",
                CncStudio.Core.Trajectory.MotionMode.CounterClockwiseArc => "G03",
                _ => "",
            };
            string tool = m.Tool != 0 ? $" · T{m.Tool}" : "";
            string feed = m.IsRapid ? "" : $" · F{m.Feed.ToString("0.###", culture)}";
            SimPositionText.Text = $"{pos}{tool} · {g} · L{m.LineNumber}{feed} · {step} · {distance}";
        }

        SimPositionText.Text += Progress3D();
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

    // Velocidade num menu (menos poluição na faixa de baixo); controles criados aqui porque
    // nomes de x:Name dentro de um Flyout não são encontrados pelo código gerado.
    private Slider SpeedSlider = new() { Minimum = 1, Maximum = 50, Value = 5, Width = 150, VerticalAlignment = VerticalAlignment.Center };
    private TextBlock SpeedText = new() { Text = "5/s", VerticalAlignment = VerticalAlignment.Center, FontSize = 13, MinWidth = 40, Margin = new Thickness(4, 0, 0, 0) };
    private Button SpeedDownButton = new() { Content = "−", MinWidth = 44, MinHeight = 44 };
    private Button SpeedUpButton = new() { Content = "+", MinWidth = 44, MinHeight = 44 };

    private void BuildSpeedFlyout()
    {
        ToolTip.SetTip(SpeedSlider, "Velocidade (passos por segundo)");
        ToolTip.SetTip(SpeedDownButton, "Mais devagar");
        ToolTip.SetTip(SpeedUpButton, "Mais rápido");
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(2) };
        row.Children.Add(SpeedDownButton);
        row.Children.Add(SpeedSlider);
        row.Children.Add(SpeedUpButton);
        row.Children.Add(SpeedText);
        SpeedButton.Flyout = new Flyout { Content = row, Placement = PlacementMode.TopEdgeAlignedRight };
    }

    private bool _syncingSlider;

    private void UpdateSpeed()
    {
        int speed = (int)Math.Round(SpeedSlider.Value);
        SpeedText.Text = $"{speed}/s";
        SpeedButton.Content = $"⏱ {speed}/s ▾";
        _simTimer.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, speed));
    }

    private void SetMillView(MillView view)
    {
        IsoViewButton.IsChecked = view == MillView.Iso;
        TopViewButton.IsChecked = view == MillView.Top;
        FrontViewButton.IsChecked = view == MillView.Front;
        _millViewChoice = view;
        UpdateBackplotView();
        SetSceneView(view switch { MillView.Top => SceneView.Top, MillView.Front => SceneView.Front, _ => SceneView.Isometric });
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
        GraphHeader.Classes.Set("tight", narrow);
        PageSwitch.IsVisible = narrow && _compact != true;
        HeaderPages.IsVisible = narrow && _compact == true;
        ApplyHeaderLayout();
        Splitter.IsVisible = !narrow;
        UpdateStatusBar();

        if (narrow)
        {
            CollapseEditorButton.IsVisible = false; // no celular as abas já separam código e gráfico
            if (_editorCollapsed)
            {
                _editorCollapsed = false;
                Editor.IsVisible = true;
                FileNameText.IsVisible = true;
                ToolsButton.IsVisible = true;
                EditButton.IsVisible = _touch;
                UpdateProblemsButton();
            }

            Workspace.ColumnDefinitions = new ColumnDefinitions("*,0,0");
            ShowPage(graph: GraphPageButton.IsChecked == true);
        }
        else
        {
            Workspace.ColumnDefinitions = new ColumnDefinitions(_editorCollapsed ? "44,0,*" : "*,6,*");
            Splitter.IsVisible = !_editorCollapsed;
            CollapseEditorButton.IsVisible = true;
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
        ApplyHeaderLayout();
    }

    /// <summary>
    /// Cabeçalho: no celular em pé, marca "Casillas Code" e situação na 1ª linha e os botões na 2ª
    /// (sobra altura); deitado, uma linha só e compacta; no PC, tudo numa linha.
    /// </summary>
    private void ApplyHeaderLayout()
    {
        bool narrow = _narrow == true;
        bool compact = _compact == true;
        bool twoRows = narrow && !compact;

        Grid.SetRow(HeaderActions, twoRows ? 1 : 0);
        Grid.SetColumn(HeaderActions, twoRows ? 0 : 2);
        Grid.SetColumnSpan(HeaderActions, twoRows ? 3 : 1);
        HeaderActions.HorizontalAlignment = twoRows ? HorizontalAlignment.Right : HorizontalAlignment.Stretch;
        HeaderActions.Margin = twoRows ? new Thickness(0, 10, 0, 0) : new Thickness(0);
        BrandText.IsVisible = !(narrow && compact); // deitado no celular: só o símbolo
        HeaderGrid.Margin = compact ? new Thickness(10, 5) : new Thickness(12, 10);
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
