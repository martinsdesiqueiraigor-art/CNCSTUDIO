using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Rendering;
using CncStudio.Core.Simulation;
using CncStudio.Core.Setup;
using CncStudio.Core.Scene;
using CncStudio.Core.Trajectory;

namespace CncStudio.App.Graphics;

/// <summary>
/// Área gráfica única: vistas, opções de exibição, sólido + trajetória + ferramenta, painel de
/// coordenadas e barra de reprodução (linha a linha ou contínua, com pausa).
///
/// Todo o trabalho pesado (remover material e renderizar o sólido) roda numa fila serial em
/// segundo plano; a thread da interface só recebe quadros prontos.
/// </summary>
public partial class GraphicsPanel : UserControl
{
    private const int MaxImagePixels = 1100;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly Stopwatch _clock = new();
    private TimeSpan _lastTick;

    private ToolPath? _path;
    private int[] _passStarts = [];
    private IMaterialSimulation? _simulation;
    private HeightMap? _map;
    private TurningStock? _turning;
    private Bounds3 _stockBounds = Bounds3.Empty;
    private int _generation;
    private bool _fitOnNextFrame = true;
    private bool _updatingSlider;
    private int? _deferredTarget;
    private int _lastReportedLine;
    private GraphicsFrame? _lastFrame;

    // Fila serial em segundo plano e cache da última imagem do sólido
    private Task _workQueue = Task.CompletedTask;
    private int _pendingWork;
    private (int Generation, int Version, GraphicsViewKind View, bool Section)? _imageKey;
    private SolidImage? _cachedImage;

    public GraphicsPanel()
    {
        InitializeComponent();

        Gl.Failed += (_, message) => OnGlFailed(message);
        Mode2D.Click += (_, _) => SetMode(ViewMode.TwoD);
        Mode2D3D.Click += (_, _) => SetMode(ViewMode.TwoDPlus3D);
        Mode3D.Click += (_, _) => SetMode(ViewMode.ThreeD);
        IsoView.Click += (_, _) => ChooseView(SceneView.Isometric);
        TopView.Click += (_, _) => ChooseView(SceneView.Top);
        FrontView.Click += (_, _) => ChooseView(SceneView.Front);
        SideView.Click += (_, _) => ChooseView(SceneView.Side);
        TechnicalCheck.IsCheckedChanged += (_, _) => Enqueue(null);
        TransparentCheck.IsCheckedChanged += (_, _) => Enqueue(null);

        SolidCheck.IsCheckedChanged += (_, _) => { View.ShowSolid = Gl.ShowSolid = SolidCheck.IsChecked == true; Enqueue(null); };
        // Uma só opção de trajetória para o 2D e o 3D
        PathCheck.IsCheckedChanged += (_, _) =>
        {
            View.ShowPath = Gl.ShowPath = PathCheck.IsChecked == true;
            View.Redraw();
            Gl.RequestNextFrameRendering();
        };
        RapidsCheck.IsCheckedChanged += (_, _) =>
        {
            View.ShowRapids = Gl.ShowRapids = RapidsCheck.IsChecked == true;
            View.Redraw();
            Gl.RequestNextFrameRendering();
        };
        Gl.ShowPath = PathCheck.IsChecked == true;
        LightBgCheck.IsCheckedChanged += (_, _) => ApplyGlBackground();
        ToolCheck.IsCheckedChanged += (_, _) => { View.ShowTool = Gl.ShowTool = ToolCheck.IsChecked == true; View.Redraw(); Gl.RequestNextFrameRendering(); };
        SectionCheck.IsCheckedChanged += (_, _) =>
        {
            View.HalfSection = SectionCheck.IsChecked == true;
            View.Redraw();
            Enqueue(null);
        };
        FitButton.Click += (_, _) => FitToContent();
        MeasureToggle.IsCheckedChanged += (_, _) => View.MeasureMode = MeasureToggle.IsChecked == true;
        StockButton.Click += (_, _) => StockSettingsRequested?.Invoke(this, EventArgs.Empty);
        ToolsButton.Click += (_, _) => ToolSettingsRequested?.Invoke(this, EventArgs.Empty);
        View.MoveClicked += (_, index) =>
        {
            if (_path is not null && index < _path.Moves.Count)
            {
                LineRequested?.Invoke(this, _path.Moves[index].LineNumber);
            }
        };

        PlayButton.Click += (_, _) => Play();
        PauseButton.Click += (_, _) => Pause();
        ResetButton.Click += (_, _) => { Pause(); Enqueue(s => s.Reset()); };
        EndButton.Click += (_, _) => { Pause(); Enqueue(s => s.RunToEnd()); };
        NextLineButton.Click += (_, _) => { Pause(); Enqueue(s => s.AdvanceLine()); };
        PreviousLineButton.Click += (_, _) => { Pause(); PreviousLine(); };
        NextPassButton.Click += (_, _) => { Pause(); NextPass(); };
        PreviousPassButton.Click += (_, _) => { Pause(); PreviousPass(); };
        PositionSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty && !_updatingSlider)
            {
                Pause();
                GoToMove((int)Math.Round(PositionSlider.Value));
            }
        };
        SpeedSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
            {
                UpdateSpeed();
            }
        };
        ModeCombo.SelectionChanged += (_, _) => UpdateSpeed();
        ToolMarks.MarkClicked += (_, change) =>
        {
            Pause();
            GoToMove(change.MoveIndex);
            LineRequested?.Invoke(this, change.LineNumber);
        };

        _timer.Tick += OnTick;
        UpdateSpeed();
        SyncModeChecks();
        ApplyMode();
    }

    // ───────────────────────── API para a janela principal ─────────────────────────

    /// <summary>Diâmetro de cada ferramenta (T → mm).</summary>
    public Func<int, double> DiameterOf { get; set; } = _ => 10;

    /// <summary>
    /// Perfil de corte de cada ferramenta na fresa (topo reto, esférica, toroidal, broca).
    /// Null = topo reto com <see cref="DiameterOf"/>.
    /// </summary>
    public Func<int, ToolProfile>? ProfileOf { get; set; }

    /// <summary>Bruto informado (null = automático). Vale na próxima trajetória carregada.</summary>
    public Stock? Stock { get; set; }

    public event EventHandler? ToolSettingsRequested;
    public event EventHandler? StockSettingsRequested;

    /// <summary>Clicou na trajetória: ir para a linha no editor.</summary>
    public event EventHandler<int>? LineRequested;

    /// <summary>A execução chegou a outra linha do programa: o editor acompanha.</summary>
    public event EventHandler<int>? LineReached;

    private bool IsLatheProgram => _path?.Machine == MachineKind.Lathe;

    private GraphicsViewKind ViewKind => View.View;

    public void SetHighlightedLine(int line) => View.SetHighlightedLine(line);

    public void FitToContent()
    {
        View.FitToContent();
        if (_show3D)
        {
            Gl.Fit(_viewChoice);
        }
    }

    /// <summary>Setup do programa (fixações e ferramentas desenhadas no 3D). Vale na próxima trajetória.</summary>
    public MachiningSetup? Setup
    {
        get => _setup;
        set
        {
            _setup = value;
            MachiningSetup? setup = value;
            View.LatheShapeOf = tool => LatheToolShape.Of(setup?.ToolOf(tool));
        }
    }

    private MachiningSetup? _setup;

    /// <summary>Nova trajetória: recria o bruto. Programa novo começa no início; edição mostra a peça pronta.</summary>
    public void SetToolPath(ToolPath path, IReadOnlyList<PathPolyline> figures, bool fit)
    {
        Pause();
        bool machineChanged = _path?.Machine != path.Machine;
        _path = path;
        _passStarts = PathPasses.Starts(path.Moves);
        View.SetToolPath(path, figures);

        // O que faz sentido para o tipo de máquina
        bool lathe = path.Machine == MachineKind.Lathe;
        SectionCheck.IsVisible = lathe;
        YLabel.IsVisible = !lathe;
        YBox.IsVisible = !lathe;
        int moveCount = Math.Max(1, path.Moves.Count);
        ToolMarks.SetMarks(path.ToolChanges, change => change.MoveIndex / (double)moveCount);
        if (machineChanged)
        {
            ApplyMode();
        }

        _updatingSlider = true;
        PositionSlider.Maximum = Math.Max(1, path.Moves.Count);
        PositionSlider.Value = path.Moves.Count;
        _updatingSlider = false;

        _fitOnNextFrame |= fit;
        Rebuild();
        // Programa novo (fit): começa no início, peça inteira e ferramenta no ponto de partida (o play corta).
        // Edição do mesmo programa: continua mostrando a peça pronta.
        Enqueue(fit ? (s => s.Reset()) : (s => s.RunToEnd()));
    }

    /// <summary>Recria o bruto e a simulação (ex.: diâmetros mudaram).</summary>
    public void Refresh()
    {
        Rebuild();
        Enqueue(s => s.RunToEnd());
    }

    private void Rebuild()
    {
        Pause();
        if (_path is null)
        {
            return;
        }

        Func<int, double> diameterOf = DiameterOf;
        if (IsLatheProgram)
        {
            _map = null;
            _turning = SimulationFactory.LatheStock(Stock, _path);
            _simulation = new TurningSimulator(_turning, _path.Moves, diameterOf, tool => LatheToolShape.CutOf(Setup?.ToolOf(tool)));
            _scene = SimulationScene.ForLathe(_turning, _path, Setup);
            _sceneCollisions = 0;
            _stockBounds = Bounds3.Empty.Include(new Point3(0, 0, _turning.MinZ)).Include(new Point3(_turning.Radius, 0, _turning.MaxZ));
        }
        else
        {
            _turning = null;
            Func<int, ToolProfile> profileOf = ProfileOf ?? (tool => ToolProfile.Flat(diameterOf(tool)));
            if (SimulationFactory.UsesVoxels(_path, Setup))
            {
                // Eixo rotativo com a peça girada: material em voxels (o corte vem de lado ou inclinado)
                _map = null;
                VoxelStock voxels = SimulationFactory.VoxelStockFor(Stock, _path, profileOf, Setup);
                _simulation = new VoxelSimulator(voxels, _path, profileOf);
                _scene = SimulationScene.ForVoxels(voxels, _path, profileOf, Setup);
                _stockBounds = voxels.Bounds;
            }
            else
            {
                _map = SimulationFactory.MillStock(Stock, _path, profileOf, SimulationFactory.DesktopCells);
                _simulation = new MaterialSimulator(_map, _path.Moves, profileOf, SimulationFactory.RotatedMoveSkipper(_path));
                _scene = SimulationScene.ForMill(_map, _path, profileOf, Setup);
                _stockBounds = Bounds3.Empty
                    .Include(new Point3(_map.OriginX, _map.OriginY, _map.BottomZ))
                    .Include(new Point3(_map.OriginX + _map.Width, _map.OriginY + _map.Height, _map.TopZ));
            }

            _sceneCollisions = 0;
        }

        _generation++;
        _glFitted = false;
        _rapidsShown = -1;
        _imageKey = null;
        _cachedImage = null;
    }

    // ───────────────────────── Execução ─────────────────────────

    private bool LineMode => ModeCombo.SelectedIndex == 0;

    /// <summary>Linha a linha: 0,5 a ~150 linhas/s. Contínuo: 10 a 100.000 mm/s (escala logarítmica).</summary>
    private double Speed => LineMode ? 0.5 * Math.Pow(10, SpeedSlider.Value / 40) : 10 * Math.Pow(10, SpeedSlider.Value / 25);

    private void UpdateSpeed()
    {
        SpeedLabel.Text = LineMode ? $"{Speed:0.#} linhas/s" : $"{Speed:0} mm/s";
        SpeedButton.Content = $"⏱ {SpeedLabel.Text} ▾";
        _timer.Interval = TimeSpan.FromMilliseconds(LineMode ? 20 : 30);
    }

    private void Play()
    {
        if (_simulation is null || _path is null || _path.Moves.Count == 0)
        {
            return;
        }

        if (_lastFrame is { IsFinished: true })
        {
            Enqueue(s => s.Reset()); // terminou: recomeça do bruto
        }

        _clock.Restart();
        _lastTick = TimeSpan.Zero;
        _timer.Start();
        PlayButton.IsEnabled = false;
        PauseButton.IsEnabled = true;
    }

    private void Pause()
    {
        _timer.Stop();
        PlayButton.IsEnabled = true;
        PauseButton.IsEnabled = false;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_pendingWork > 0)
        {
            return; // quadro anterior ainda sendo calculado: não acumula trabalho
        }

        TimeSpan now = _clock.Elapsed;
        double seconds = Math.Min(0.1, (now - _lastTick).TotalSeconds);
        _lastTick = now;

        if (LineMode)
        {
            // Linha a linha suave: a ferramenta percorre a linha (e os retornos dos ciclos) em 1/velocidade s
            double lines = Speed * seconds;
            IReadOnlyList<Move> moves = _path?.Moves ?? [];
            Enqueue(s => LinePacing.Advance(s, moves, lines));
            return;
        }

        double distance = Speed * seconds;
        Enqueue(s => s.Advance(distance));
    }

    private void NextPass()
    {
        int position = _lastFrame?.MoveIndex ?? 0;
        if (_path is not null && position < _path.Moves.Count && _passStarts.Length > 0)
        {
            GoToMove(PathPasses.EndOf(_passStarts, PathPasses.PassOf(_passStarts, position), _path.Moves.Count));
        }
    }

    private void PreviousPass()
    {
        int position = _lastFrame?.MoveIndex ?? 0;
        if (position > 0 && _passStarts.Length > 0)
        {
            GoToMove(_passStarts[PathPasses.PassOf(_passStarts, position - 1)]);
        }
    }

    /// <summary>Volta ao começo da linha anterior do programa (todos os movimentos dela).</summary>
    private void PreviousLine()
    {
        int position = _lastFrame?.MoveIndex ?? 0;
        if (_path is null || position <= 0)
        {
            return;
        }

        IReadOnlyList<Move> moves = _path.Moves;
        int index = Math.Min(position, moves.Count) - 1;
        int line = moves[index].LineNumber;
        while (index > 0 && moves[index - 1].LineNumber == line)
        {
            index--;
        }

        GoToMove(index);
    }

    private void GoToMove(int moveIndex)
    {
        if (_pendingWork > 0)
        {
            _deferredTarget = moveIndex;
            return;
        }

        Enqueue(s => s.RunToMove(moveIndex));
    }

    /// <summary>Executa a ação na simulação (em segundo plano, em ordem) e gera o quadro da vista atual.</summary>
    private void Enqueue(Action<IMaterialSimulation>? action)
    {
        IMaterialSimulation? simulation = _simulation;
        ToolPath? path = _path;
        if (simulation is null || path is null)
        {
            return;
        }

        GraphicsViewKind view = ViewKind;
        int generation = _generation;
        bool solid = SolidCheck.IsChecked == true;
        bool section = SectionCheck.IsChecked == true;
        HeightMap? map = _map;
        TurningStock? turning = _turning;
        SimulationScene? scene = _show3D ? _scene : null;
        ScenePalette palette = TechnicalCheck.IsChecked == true ? ScenePalette.Technical : ScenePalette.Realistic;
        bool transparent = TransparentCheck.IsChecked == true;
        Bounds3 stockBounds = _stockBounds;
        Interlocked.Increment(ref _pendingWork);

        _workQueue = _workQueue.ContinueWith(_ =>
        {
            try
            {
                action?.Invoke(simulation);
                GraphicsFrame frame = Capture(simulation, path, view, generation, solid, section, map, turning, stockBounds);
                if (scene is not null)
                {
                    frame = frame with { Scene = SceneOf(scene, simulation, section, palette, transparent) };
                }

                Dispatcher.UIThread.Post(() => ShowFrame(frame));
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => StatusText.Text = $"Erro na simulação: {ex.Message}");
            }
            finally
            {
                Interlocked.Decrement(ref _pendingWork);
            }
        }, TaskScheduler.Default);
    }

    // ───────────────────────── 3D pela placa de vídeo ─────────────────────────

    private SimulationScene? _scene;
    private int _sceneCollisions;
    private bool _glFailed;
    private bool _glFitted;

    private bool _show3D;

    private bool _appLight;

    /// <summary>Tema claro: fundo claro também no 3D.</summary>
    public bool LightTheme
    {
        get => _appLight;
        set
        {
            _appLight = value;
            ApplyGlBackground();
        }
    }

    // Fundo do 3D (degradê): claro no tema claro do app ou quando a opção "Fundo claro" está marcada
    private void ApplyGlBackground()
    {
        Gl.LightTheme = _appLight || LightBgCheck.IsChecked == true;
        Gl.RequestNextFrameRendering();
    }

    // ───────────────────────── Modo do gráfico: 2D | 2D + 3D | 3D ─────────────────────────

    private enum ViewMode { TwoD, TwoDPlus3D, ThreeD }

    private ViewMode _mode = ViewMode.TwoDPlus3D;

    // Vista pedida nos ícones (vale para a câmera do 3D e, na fresa, para o desenho 2D)
    private SceneView _viewChoice = SceneView.Isometric;

    // Desenho 2D da fresa (nunca dois 3D: com o 3D ao lado, a esquerda é topo, frente ou lado)
    private GraphicsViewKind _left2D = GraphicsViewKind.Top;

    private void SetMode(ViewMode mode)
    {
        _mode = mode == ViewMode.TwoDPlus3D && _glFailed ? ViewMode.TwoD : mode;
        if (_mode == ViewMode.TwoD && !IsLatheProgram)
        {
            _viewChoice = _left2D switch
            {
                GraphicsViewKind.Front => SceneView.Front,
                GraphicsViewKind.Side => SceneView.Side,
                GraphicsViewKind.Iso => SceneView.Isometric,
                _ => SceneView.Top,
            };
        }

        SyncModeChecks();
        ApplyMode();
        _fitOnNextFrame = true;
        _glFitted = false;
        Enqueue(null);
    }

    private void SyncModeChecks()
    {
        Mode2D.IsChecked = _mode == ViewMode.TwoD;
        Mode2D3D.IsChecked = _mode == ViewMode.TwoDPlus3D;
        Mode3D.IsChecked = _mode == ViewMode.ThreeD;
    }

    private void ChooseView(SceneView view)
    {
        _viewChoice = view;
        if (!IsLatheProgram)
        {
            if (view != SceneView.Isometric)
            {
                _left2D = view switch { SceneView.Front => GraphicsViewKind.Front, SceneView.Side => GraphicsViewKind.Side, _ => GraphicsViewKind.Top };
            }
            else if (_mode == ViewMode.TwoD)
            {
                _left2D = GraphicsViewKind.Iso; // só o 2D aberto: a isométrica é desenhada pela CPU
            }
        }

        ApplyMode();
        if (_show3D)
        {
            Gl.Fit(view);
        }

        _fitOnNextFrame = true;
        Enqueue(null);
    }

    /// <summary>Desenho da esquerda para o modo e a máquina atuais.</summary>
    private GraphicsViewKind LeftKind()
    {
        bool cpu3D = _mode == ViewMode.ThreeD && _glFailed; // sem OpenGL, o "3D" é o desenho por CPU
        if (IsLatheProgram)
        {
            return cpu3D ? GraphicsViewKind.Lathe3D : GraphicsViewKind.LatheZX;
        }

        if (cpu3D)
        {
            return GraphicsViewKind.Iso;
        }

        return _mode == ViewMode.TwoDPlus3D && _left2D == GraphicsViewKind.Iso ? GraphicsViewKind.Top : _left2D;
    }

    /// <summary>Aplica o modo: qual desenho fica à esquerda, se o 3D aparece e quais ícones de vista valem.</summary>
    private void ApplyMode()
    {
        bool show3D = _mode != ViewMode.TwoD && !_glFailed;
        bool glOnly = show3D && _mode == ViewMode.ThreeD;
        View.View = LeftKind();
        View.IsVisible = !glOnly;
        ShowGl(show3D, glOnly);

        // Torno só no 2D: o perfil ZX não tem outras vistas
        ViewIcons.IsVisible = !(IsLatheProgram && !show3D);
        IsoView.IsChecked = _viewChoice == SceneView.Isometric;
        TopView.IsChecked = _viewChoice == SceneView.Top;
        FrontView.IsChecked = _viewChoice == SceneView.Front;
        SideView.IsChecked = _viewChoice == SceneView.Side;
    }

    private void ShowGl(bool on, bool glOnly = false)
    {
        _show3D = on;
        ViewsGrid.ColumnDefinitions = new ColumnDefinitions(!on ? "*,0,0" : glOnly ? "0,0,*" : "*,6,*");
        Gl.IsVisible = on;
        ViewsSplitter.IsVisible = on && !glOnly;
        if (on && !Gl.GlReady)
        {
            // O Avalonia não avisa quando não há OpenGL (computador sem placa de vídeo): se o 3D não
            // iniciar em alguns segundos, volta ao desenho por CPU
            DispatcherTimer.RunOnce(() =>
            {
                if (Gl.IsVisible && !Gl.GlReady && !_glFailed)
                {
                    OnGlFailed("não iniciou");
                }
            }, TimeSpan.FromSeconds(3));
        }
    }

    private int _rapidsShown = -1;

    /// <summary>Botão com a lista dos G00 que encostaram no material (só muda quando a lista muda).</summary>
    private void UpdateRapidsButton(GraphicsFrame frame)
    {
        if (frame.CollisionMoves.Length == _rapidsShown || _path is not { } path)
        {
            return;
        }

        _rapidsShown = frame.CollisionMoves.Length;
        RapidsButton.IsVisible = _rapidsShown > 0;
        if (_rapidsShown == 0)
        {
            return;
        }

        RapidsButton.Content = $"⚠ {_rapidsShown} G00 no material";
        var menu = new MenuFlyout();
        foreach (int move in frame.CollisionMoves.Take(40))
        {
            int line = path.Moves[move].LineNumber;
            var item = new MenuItem { Header = $"Linha {line} (movimento {move + 1})" };
            item.Click += (_, _) =>
            {
                Pause();
                LineRequested?.Invoke(this, line);
                GoToMove(move + 1); // fim do G00: a ferramenta já dentro do material
            };
            menu.Items.Add(item);
        }

        RapidsButton.Flyout = menu;
    }

    // Sem OpenGL: volta ao desenho por CPU
    private void OnGlFailed(string message)
    {
        _glFailed = true;
        Mode2D3D.IsVisible = false;
        if (_mode == ViewMode.TwoDPlus3D)
        {
            _mode = ViewMode.TwoD;
        }

        SyncModeChecks();
        ApplyMode();
        _fitOnNextFrame = true;
        Enqueue(null);

        StatusText.Text = "3D pela placa de vídeo indisponível (" + message + "): usando o desenho por CPU.";
    }


    /// <summary>Atualiza a cena 3D com a simulação (na fila de segundo plano, dona da cena) e tira a foto.</summary>
    private SceneSnapshot SceneOf(SimulationScene scene, IMaterialSimulation simulation, bool section, ScenePalette palette, bool transparent)
    {
        scene.QuarterSection = section;
        scene.TransparentMaterial = transparent;
        scene.Palette = palette;
        scene.Refresh();
        scene.SetTool(simulation.CurrentTool, simulation.ToolPosition, simulation.CurrentMoveIndex);
        scene.SetCurrentMove(simulation.CurrentMoveIndex);
        if (simulation.RapidCollisions.Count != _sceneCollisions)
        {
            _sceneCollisions = simulation.RapidCollisions.Count;
            scene.SetRapidCollisions(simulation.RapidCollisions);
        }

        return scene.Snapshot();
    }

    /// <summary>Monta o quadro (roda na fila de segundo plano). Reaproveita a imagem se nada mudou.</summary>
    private GraphicsFrame Capture(IMaterialSimulation simulation, ToolPath path, GraphicsViewKind view, int generation,
        bool solid, bool section, HeightMap? map, TurningStock? turning, Bounds3 stockBounds)
    {
        SolidImage? image = null;
        TurningProfile? profile = null;

        if (solid && view != GraphicsViewKind.Orbit3D && view.Camera() is { } camera)
        {
            var key = (generation, simulation.Version, view, section);
            if (_imageKey == key && _cachedImage is not null)
            {
                image = _cachedImage;
            }
            else
            {
                image = turning is not null
                    ? LatheSolidRenderer.Render(turning, camera, section, MaxImagePixels)
                    : map is not null ? MillSolidRenderer.Render(map, camera, MaxImagePixels) : null;
                _imageKey = key;
                _cachedImage = image;
            }
        }

        if (turning is not null && view == GraphicsViewKind.LatheZX)
        {
            profile = new TurningProfile(turning.MinZ, turning.SliceLength, turning.Outer.ToArray(), turning.Inner.ToArray(), turning.InitialOuter);
        }

        IReadOnlyList<int> collisions = simulation.RapidCollisions;
        return new GraphicsFrame
        {
            Generation = generation,
            View = view,
            Image = image,
            Profile = profile,
            StockBounds = stockBounds,
            ToolPosition = simulation.ToolPosition,
            ToolDiameter = simulation.CurrentToolDiameter,
            Tool = simulation.CurrentTool,
            MoveIndex = simulation.CurrentMoveIndex,
            MoveCount = path.Moves.Count,
            CurrentMove = simulation.CurrentMove,
            LineNumber = simulation.CurrentLineNumber,
            IsFinished = simulation.IsFinished,
            Travelled = simulation.Travelled,
            Message = path.MessageAt(simulation.CurrentMoveIndex),
            CollisionLines = collisions.Select(i => path.Moves[i].LineNumber).Distinct().Take(6).ToArray(),
            CollisionCount = collisions.Count,
            CollisionMoves = [.. collisions],
        };
    }

    private void ShowFrame(GraphicsFrame frame)
    {
        if (frame.Generation != _generation)
        {
            return; // quadro de um programa antigo
        }

        _lastFrame = frame;
        View.ShowFrame(frame, _fitOnNextFrame);
        if (frame.Scene is { } scene)
        {
            Gl.Show(scene);
            if (_fitOnNextFrame || !_glFitted)
            {
                _glFitted = true;
                Gl.Fit(_viewChoice);
            }
        }

        _fitOnNextFrame = false;

        // Trajetória: executado colorido, o resto apagado (no fim, tudo colorido)
        bool atEnd = frame.MoveIndex >= frame.MoveCount;
        (int, int)? pass = null;
        if (!atEnd && frame.MoveIndex > 0 && _passStarts.Length > 0)
        {
            int p = PathPasses.PassOf(_passStarts, frame.MoveIndex - 1);
            pass = (_passStarts[p], PathPasses.EndOf(_passStarts, p, frame.MoveCount));
        }

        View.SetProgress(atEnd ? null : frame.MoveIndex, pass);
        UpdateCoordinates(frame);
        UpdateRapidsButton(frame);

        if (_deferredTarget is { } target)
        {
            _deferredTarget = null;
            Enqueue(s => s.RunToMove(target));
        }

        if (frame.IsFinished)
        {
            Pause();
        }
    }

    private void UpdateCoordinates(GraphicsFrame frame)
    {
        CultureInfo culture = CultureInfo.CurrentCulture;
        Point3 p = frame.ToolPosition;
        bool lathe = IsLatheProgram;

        XText.Text = lathe ? (p.X * 2).ToString("0.000", culture) + " Ø" : p.X.ToString("0.000", culture);
        YText.Text = p.Y.ToString("0.000", culture);
        ZText.Text = p.Z.ToString("0.000", culture);
        ToolText.Text = frame.Tool == 0 ? "—" : lathe ? $"T{frame.Tool}" : $"T{frame.Tool} Ø{frame.ToolDiameter:0.##}";
        LineText.Text = frame.LineNumber > 0 ? frame.LineNumber.ToString(culture) : "—";
        double total = _path?.TotalLength ?? 0;
        DistanceText.Text = PathMarks.FormatDistance(PathMarks.DisplayTravelled(frame.Travelled, total, frame.IsFinished), culture)
                            + " / " + PathMarks.FormatDistance(total, culture);

        if (frame.CurrentMove is { } move)
        {
            MotionText.Text = move.Motion switch
            {
                MotionMode.Rapid => "G00",
                MotionMode.Linear => "G01",
                MotionMode.ClockwiseArc => "G02",
                _ => "G03",
            };
            FeedText.Text = move.IsRapid ? "rápido" : $"{move.Feed:0} mm/min";
        }

        int pass = _passStarts.Length == 0 ? 0 : PathPasses.PassOf(_passStarts, Math.Min(frame.MoveIndex, Math.Max(0, frame.MoveCount - 1))) + 1;
        PassText.Text = _passStarts.Length == 0 ? "—" : $"{pass} / {_passStarts.Length}";

        _updatingSlider = true;
        PositionSlider.Value = frame.MoveIndex;
        _updatingSlider = false;

        string state = frame.IsFinished ? "Peça pronta (⏮ volta ao bruto)" : $"Movimento {frame.MoveIndex + 1} de {frame.MoveCount}";
        if (frame.CollisionCount > 0)
        {
            state = $"⚠ G00 dentro do material: linha(s) {string.Join(", ", frame.CollisionLines)}" +
                    (frame.CollisionCount > frame.CollisionLines.Length ? "…" : "") + " · " + state;
        }

        if (frame.Message is { } message)
        {
            state += $" · MSG: {message}";
        }

        string stock = Stock is not null && (Stock.Machine == MachineKind.Lathe) == lathe ? "bruto informado" : "bruto automático";
        StatusText.Text = $"{state} · {stock}";

        if (frame.LineNumber > 0 && frame.LineNumber != _lastReportedLine && !frame.IsFinished)
        {
            _lastReportedLine = frame.LineNumber;
            LineReached?.Invoke(this, frame.LineNumber);
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && MeasureToggle.IsChecked == true)
        {
            MeasureToggle.IsChecked = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Space)
        {
            if (_timer.IsEnabled) Pause(); else Play();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }
}
