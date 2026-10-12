using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Rendering;
using CncStudio.Core.Simulation;
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
    private double _distanceBudget;

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

        IsoView.IsCheckedChanged += (_, _) => OnViewChecked(IsoView, GraphicsViewKind.Iso);
        TopView.IsCheckedChanged += (_, _) => OnViewChecked(TopView, GraphicsViewKind.Top);
        FrontView.IsCheckedChanged += (_, _) => OnViewChecked(FrontView, GraphicsViewKind.Front);
        SideView.IsCheckedChanged += (_, _) => OnViewChecked(SideView, GraphicsViewKind.Side);
        Lathe3DView.IsCheckedChanged += (_, _) => OnViewChecked(Lathe3DView, GraphicsViewKind.Lathe3D);
        LatheZXView.IsCheckedChanged += (_, _) => OnViewChecked(LatheZXView, GraphicsViewKind.LatheZX);

        SolidCheck.IsCheckedChanged += (_, _) => { View.ShowSolid = SolidCheck.IsChecked == true; Enqueue(null); };
        PathCheck.IsCheckedChanged += (_, _) => { View.ShowPath = PathCheck.IsChecked == true; View.Redraw(); };
        ToolCheck.IsCheckedChanged += (_, _) => { View.ShowTool = ToolCheck.IsChecked == true; View.Redraw(); };
        SectionCheck.IsCheckedChanged += (_, _) => Enqueue(null);
        FitButton.Click += (_, _) => View.FitToContent();
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

        _timer.Tick += OnTick;
        UpdateSpeed();
    }

    // ───────────────────────── API para a janela principal ─────────────────────────

    /// <summary>Diâmetro de cada ferramenta (T → mm).</summary>
    public Func<int, double> DiameterOf { get; set; } = _ => 10;

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

    public void FitToContent() => View.FitToContent();

    /// <summary>Nova trajetória: recria o bruto e mostra a peça pronta (o ⏮ volta ao bruto).</summary>
    public void SetToolPath(ToolPath path, IReadOnlyList<PathPolyline> figures, bool fit)
    {
        Pause();
        bool machineChanged = _path?.Machine != path.Machine;
        _path = path;
        _passStarts = PathPasses.Starts(path.Moves);
        View.SetToolPath(path, figures);

        // Vistas que fazem sentido para o tipo de máquina
        bool lathe = path.Machine == MachineKind.Lathe;
        MillViews.IsVisible = !lathe;
        LatheViews.IsVisible = lathe;
        SectionCheck.IsVisible = lathe;
        YLabel.IsVisible = !lathe;
        YBox.IsVisible = !lathe;
        if (machineChanged || ViewKind.IsLathe() != lathe)
        {
            (lathe ? Lathe3DView : IsoView).IsChecked = true;
            View.View = lathe ? GraphicsViewKind.Lathe3D : GraphicsViewKind.Iso;
        }

        _updatingSlider = true;
        PositionSlider.Maximum = Math.Max(1, path.Moves.Count);
        PositionSlider.Value = path.Moves.Count;
        _updatingSlider = false;

        _fitOnNextFrame |= fit;
        Rebuild();
        Enqueue(s => s.RunToEnd());
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
            _turning = Stock is { Machine: MachineKind.Lathe } bar
                ? TurningStock.CreateForStock(bar)
                : TurningStock.CreateForToolPath(_path);
            _simulation = new TurningSimulator(_turning, _path.Moves, diameterOf);
            _stockBounds = Bounds3.Empty.Include(new Point3(0, 0, _turning.MinZ)).Include(new Point3(_turning.Radius, 0, _turning.MaxZ));
        }
        else
        {
            _turning = null;
            double largest = _path.Tools.Count == 0 ? diameterOf(0) : _path.Tools.Max(diameterOf);
            _map = Stock is { Machine: MachineKind.Mill } block
                ? HeightMap.CreateForStock(block)
                : HeightMap.CreateForToolPath(_path, largest);
            _simulation = new MaterialSimulator(_map, _path.Moves, diameterOf);
            _stockBounds = Bounds3.Empty
                .Include(new Point3(_map.OriginX, _map.OriginY, _map.BottomZ))
                .Include(new Point3(_map.OriginX + _map.Width, _map.OriginY + _map.Height, _map.TopZ));
        }

        _generation++;
        _imageKey = null;
        _cachedImage = null;
    }

    private void OnViewChecked(RadioButton button, GraphicsViewKind kind)
    {
        if (button.IsChecked != true)
        {
            return;
        }

        View.View = kind;
        _fitOnNextFrame = true;
        Enqueue(null);
    }

    // ───────────────────────── Execução ─────────────────────────

    private bool LineMode => ModeCombo.SelectedIndex == 0;

    /// <summary>Linha a linha: 0,5 a ~150 linhas/s. Contínuo: 10 a 100.000 mm/s (escala logarítmica).</summary>
    private double Speed => LineMode ? 0.5 * Math.Pow(10, SpeedSlider.Value / 40) : 10 * Math.Pow(10, SpeedSlider.Value / 25);

    private void UpdateSpeed()
    {
        SpeedLabel.Text = LineMode ? $"{Speed:0.#} linhas/s" : $"{Speed:0} mm/s";
        _timer.Interval = LineMode ? TimeSpan.FromMilliseconds(Math.Max(16, 1000 / Speed)) : TimeSpan.FromMilliseconds(30);
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

        _distanceBudget = 0;
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

        if (LineMode)
        {
            Enqueue(s => s.AdvanceLine());
            return;
        }

        TimeSpan now = _clock.Elapsed;
        double seconds = Math.Min(0.1, (now - _lastTick).TotalSeconds);
        _lastTick = now;
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
        Bounds3 stockBounds = _stockBounds;
        Interlocked.Increment(ref _pendingWork);

        _workQueue = _workQueue.ContinueWith(_ =>
        {
            try
            {
                action?.Invoke(simulation);
                GraphicsFrame frame = Capture(simulation, path, view, generation, solid, section, map, turning, stockBounds);
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

    /// <summary>Monta o quadro (roda na fila de segundo plano). Reaproveita a imagem se nada mudou.</summary>
    private GraphicsFrame Capture(IMaterialSimulation simulation, ToolPath path, GraphicsViewKind view, int generation,
        bool solid, bool section, HeightMap? map, TurningStock? turning, Bounds3 stockBounds)
    {
        SolidImage? image = null;
        TurningProfile? profile = null;

        if (solid && view.Camera() is { } camera)
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
        DistanceText.Text = frame.Travelled >= 10_000 ? $"{frame.Travelled / 1000:0.00} m" : $"{frame.Travelled:0.0} mm";

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
