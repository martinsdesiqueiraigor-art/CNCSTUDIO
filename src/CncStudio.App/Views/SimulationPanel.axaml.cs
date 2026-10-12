using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using CncStudio.Core.Execution;
using CncStudio.Core.Simulation;
using CncStudio.Core.Trajectory;

namespace CncStudio.App.Views;

/// <summary>
/// Painel de simulação 2.5D: controles (executar, passo, velocidade, diâmetro) e a vista.
///
/// Todo trabalho com o HeightMap (remover material e gerar imagens) roda numa fila
/// serial em segundo plano; a thread de UI só recebe quadros prontos.
/// </summary>
public partial class SimulationPanel : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly Stopwatch _clock = new();
    private TimeSpan _lastTick;

    private ToolPath? _path;
    private int[] _passStarts = [];
    private MaterialSimulator? _simulator;
    private SimulationFrame? _lastFrame;
    private bool _updatingSlider;
    private int? _deferredTarget;      // régua arrastada enquanto o quadro anterior era calculado
    private int _lastReportedLine;
    private int _generation;
    private bool _fitOnNextFrame = true;

    // Fila serial de trabalho em segundo plano
    private Task _workQueue = Task.CompletedTask;
    private int _pendingWork;

    public SimulationPanel()
    {
        InitializeComponent();

        TopMode.IsCheckedChanged += (_, _) => OnModeChanged(TopMode, SimulationViewMode.Top);
        FrontMode.IsCheckedChanged += (_, _) => OnModeChanged(FrontMode, SimulationViewMode.Front);
        SideMode.IsCheckedChanged += (_, _) => OnModeChanged(SideMode, SimulationViewMode.Side);
        IsoMode.IsCheckedChanged += (_, _) => OnModeChanged(IsoMode, SimulationViewMode.Iso);
        FitButton.Click += (_, _) => View.FitToContent();

        ShowPathCheck.IsCheckedChanged += (_, _) => View.ShowPath = ShowPathCheck.IsChecked == true;
        View.Clicked += (_, click) =>
        {
            // Clique na trajetória desenhada: vai para a linha no editor
            if (_path is null || !View.ShowPath)
            {
                return;
            }

            SimulationViewMode mode = Mode;
            int index = PathHitTest.NearestMove(_path.Moves, p =>
            {
                var q = SimulationView.Project(mode, p);
                return (q.X, q.Y);
            }, click.World.X, click.World.Y, click.Tolerance, PathPolylineBuilder.ScreenTolerance(_path));
            if (index >= 0)
            {
                LineRequested?.Invoke(this, _path.Moves[index].LineNumber);
            }
        };

        PlayButton.Click += (_, _) => TogglePlay();
        PreviousPassButton.Click += (_, _) =>
        {
            Pause();
            int position = _lastFrame?.MoveIndex ?? 0;
            if (position > 0 && _passStarts.Length > 0)
            {
                GoToMove(_passStarts[PathPasses.PassOf(_passStarts, position - 1)]);
            }
        };
        NextPassButton.Click += (_, _) =>
        {
            Pause();
            int position = _lastFrame?.MoveIndex ?? 0;
            if (_path is not null && position < _path.Moves.Count && _passStarts.Length > 0)
            {
                GoToMove(PathPasses.EndOf(_passStarts, PathPasses.PassOf(_passStarts, position), _path.Moves.Count));
            }
        };
        PositionSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty && !_updatingSlider)
            {
                Pause();
                GoToMove((int)Math.Round(PositionSlider.Value));
            }
        };
        StepButton.Click += (_, _) =>
        {
            Pause();
            Enqueue(s => s.AdvanceToNextLine());
        };
        EndButton.Click += (_, _) =>
        {
            Pause();
            Enqueue(s => s.RunToEnd());
        };
        ResetButton.Click += (_, _) =>
        {
            Pause();
            Enqueue(s => s.Reset());
        };

        SpeedSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
            {
                UpdateSpeedLabel();
            }
        };
        ToolsButton.Click += (_, _) => ToolSettingsRequested?.Invoke(this, EventArgs.Empty);
        StockButton.Click += (_, _) => StockSettingsRequested?.Invoke(this, EventArgs.Empty);

        _timer.Tick += OnTick;
        UpdateSpeedLabel();
    }

    private SimulationViewMode Mode { get; set; } = SimulationViewMode.Top;

    /// <summary>Velocidade de avanço da simulação (mm de trajetória por segundo), escala logarítmica.</summary>
    private double SpeedMmPerSecond => 10 * Math.Pow(10, SpeedSlider.Value / 25); // 10 a 100.000 mm/s

    /// <summary>Diâmetro de cada ferramenta (T → mm). Definido pela janela principal a partir da configuração.</summary>
    public Func<int, double> DiameterOf { get; set; } = _ => 10;

    /// <summary>O usuário pediu para editar as ferramentas.</summary>
    public event EventHandler? ToolSettingsRequested;

    /// <summary>O usuário pediu para editar o bruto.</summary>
    public event EventHandler? StockSettingsRequested;

    /// <summary>Bruto informado (fresa). Null = automático pela trajetória. Vale na próxima trajetória carregada.</summary>
    public Stock? Stock { get; set; }

    /// <summary>Clicou na trajetória: ir para a linha no editor.</summary>
    public event EventHandler<int>? LineRequested;

    /// <summary>A simulação chegou a outra linha do programa: o editor pode acompanhar.</summary>
    public event EventHandler<int>? LineReached;

    /// <summary>Recria a simulação (ex.: diâmetros das ferramentas mudaram).</summary>
    public void Refresh() => Rebuild();

    /// <summary>Carrega uma nova trajetória: recria o bruto e volta ao início.</summary>
    public void SetToolPath(ToolPath path, IReadOnlyList<PathPolyline> figures)
    {
        if (path.Machine == MachineKind.Lathe)
        {
            // Torno: só backplot (a simulação de remoção de material é de fresa)
            Pause();
            _path = null;
            _simulator = null;
            _generation++;
            _fitOnNextFrame = true;
            View.Clear();
            View.SetPath([]);
            _passStarts = [];
            StatusText.Text = "Programa de torno: use o Backplot (vista Torno ZX, com o passe a passe). A simulação 2.5D é só para fresa.";
            return;
        }

        bool firstLoad = _path is null;
        _path = path;
        _passStarts = PathPasses.Starts(path.Moves);
        View.SetPath(figures);
        _updatingSlider = true;
        PositionSlider.Maximum = Math.Max(1, path.Moves.Count);
        PositionSlider.Value = 0;
        _updatingSlider = false;
        _fitOnNextFrame |= firstLoad;
        Rebuild();
    }

    /// <summary>Recria bruto e simulador (novo programa ou novo diâmetro).</summary>
    private void Rebuild()
    {
        Pause();
        if (_path is null)
        {
            return;
        }

        Func<int, double> diameterOf = DiameterOf;
        double largest = _path.Tools.Count == 0 ? diameterOf(0) : _path.Tools.Max(diameterOf);
        HeightMap map = Stock is { Machine: MachineKind.Mill } stock
            ? HeightMap.CreateForStock(stock)
            : HeightMap.CreateForToolPath(_path, largest);
        _simulator = new MaterialSimulator(map, _path.Moves, diameterOf);
        _generation++;
        Enqueue(null);
    }

    private void OnModeChanged(RadioButton button, SimulationViewMode mode)
    {
        if (button.IsChecked != true)
        {
            return;
        }

        Mode = mode;
        _fitOnNextFrame = true;
        Enqueue(null); // só redesenha
    }

    // ───────────────────────── Execução ─────────────────────────

    private void TogglePlay()
    {
        if (_timer.IsEnabled)
        {
            Pause();
            return;
        }

        if (_simulator is null)
        {
            return;
        }

        _clock.Restart();
        _lastTick = TimeSpan.Zero;
        _timer.Start();
        PlayButton.Content = "⏸ Pausar";
    }

    private void Pause()
    {
        _timer.Stop();
        PlayButton.Content = "▶ Executar";
    }

    private void OnTick(object? sender, EventArgs e)
    {
        TimeSpan now = _clock.Elapsed;
        double seconds = Math.Min(0.1, (now - _lastTick).TotalSeconds); // limita saltos após travadas
        _lastTick = now;

        // Se o quadro anterior ainda está sendo calculado, pula este (mantém a UI leve)
        if (_pendingWork > 0)
        {
            return;
        }

        double distance = SpeedMmPerSecond * seconds;
        Enqueue(s => s.Advance(distance));
    }

    /// <summary>
    /// Executa "action" no simulador (em segundo plano, em ordem) e depois gera o quadro
    /// da vista atual. Com action = null, apenas redesenha.
    /// </summary>
    private void Enqueue(Action<MaterialSimulator>? action)
    {
        MaterialSimulator? simulator = _simulator;
        if (simulator is null)
        {
            return;
        }

        SimulationViewMode mode = Mode;
        int generation = _generation;
        ToolPath? path = _path;
        Interlocked.Increment(ref _pendingWork);

        _workQueue = _workQueue.ContinueWith(_ =>
        {
            try
            {
                action?.Invoke(simulator);
                string? message = path?.MessageAt(simulator.CurrentMoveIndex);
                SimulationFrame frame = SimulationFrame.Capture(simulator, mode, generation, path?.Moves.Count ?? 0, message);
                if (path is not null && simulator.RapidCollisions.Count > 0)
                {
                    frame = frame with
                    {
                        CollisionLines = simulator.RapidCollisions.Select(i => path.Moves[i].LineNumber).Distinct().Take(6).ToArray(),
                        CollisionCount = simulator.RapidCollisions.Count,
                    };
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

    private void ShowFrame(SimulationFrame frame)
    {
        // Quadro de um programa/diâmetro antigo: descarta
        if (frame.Generation != _generation)
        {
            return;
        }

        View.ShowFrame(frame, _fitOnNextFrame);
        _fitOnNextFrame = false;
        _lastFrame = frame;
        UpdateCoordinates(frame);

        if (_deferredTarget is { } target)
        {
            _deferredTarget = null;
            Enqueue(s => s.RunToMove(target));
        }

        string state = frame.IsFinished ? "Concluído · " : "";
        if (frame.CollisionCount > 0)
        {
            state = $"⚠ G00 dentro do material: linha(s) {string.Join(", ", frame.CollisionLines)}" +
                    (frame.CollisionCount > frame.CollisionLines.Length ? "…" : "") + " · " + state;
        }
        if (frame.Message is { } message)
        {
            state += $"MSG: {message} · ";
        }

        string stock = Stock is { Machine: MachineKind.Mill } ? "bruto informado" : "bruto automático";
        StatusText.Text = $"{state}{stock} {frame.Width:0.#} × {frame.Height:0.#} mm, grade {frame.Columns} × {frame.Rows}";

        if (frame.IsFinished)
        {
            Pause();
        }
    }

    /// <summary>Leva a simulação ao início do movimento indicado (régua e passes).</summary>
    private void GoToMove(int moveIndex)
    {
        if (_pendingWork > 0)
        {
            _deferredTarget = moveIndex; // só o último pedido importa
            return;
        }

        Enqueue(s => s.RunToMove(moveIndex));
    }

    /// <summary>Painel de coordenadas, régua e acompanhamento do editor.</summary>
    private void UpdateCoordinates(SimulationFrame frame)
    {
        var p = frame.ToolPosition;
        ToolText.Text = frame.Tool == 0 ? $"Ø{frame.ToolDiameter:0.##}" : $"T{frame.Tool} Ø{frame.ToolDiameter:0.##}";
        XText.Text = $"{p.X,9:0.000}";
        YText.Text = $"{p.Y,9:0.000}";
        ZText.Text = $"{p.Z,9:0.000}";
        LineText.Text = frame.LineNumber > 0 ? frame.LineNumber.ToString() : "—";
        DistanceText.Text = frame.Travelled >= 10_000 ? $"{frame.Travelled / 1000:0.00} m" : $"{frame.Travelled:0.0} mm";

        if (frame.CurrentMove is { } move)
        {
            string code = move.Motion switch
            {
                MotionMode.Rapid => "G00",
                MotionMode.Linear => "G01",
                MotionMode.ClockwiseArc => "G02",
                _ => "G03",
            };
            MotionText.Text = move.IsRapid ? code : $"{code} F{move.Feed:0}";
        }
        else
        {
            MotionText.Text = "—";
        }

        int pass = _passStarts.Length == 0 ? 0 : PathPasses.PassOf(_passStarts, Math.Min(frame.MoveIndex, Math.Max(0, frame.MoveCount - 1))) + 1;
        PassText.Text = _passStarts.Length == 0 ? "—" : $"{pass} / {_passStarts.Length}";

        _updatingSlider = true;
        PositionSlider.Value = frame.MoveIndex;
        _updatingSlider = false;

        if (frame.LineNumber > 0 && frame.LineNumber != _lastReportedLine)
        {
            _lastReportedLine = frame.LineNumber;
            LineReached?.Invoke(this, frame.LineNumber);
        }
    }

    private void UpdateSpeedLabel() => SpeedLabel.Text = $"{SpeedMmPerSecond:0} mm/s";
}
