using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
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
    private MaterialSimulator? _simulator;
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

        PlayButton.Click += (_, _) => TogglePlay();
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

    /// <summary>Recria a simulação (ex.: diâmetros das ferramentas mudaram).</summary>
    public void Refresh() => Rebuild();

    /// <summary>Carrega uma nova trajetória: recria o bruto e volta ao início.</summary>
    public void SetToolPath(ToolPath path)
    {
        bool firstLoad = _path is null;
        _path = path;
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
        HeightMap map = HeightMap.CreateForToolPath(_path, largest);
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
                SimulationFrame frame = SimulationFrame.Capture(simulator, mode, generation, message);
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

        var p = frame.ToolPosition;
        string state = frame.IsFinished ? " · concluído" : "";
        if (frame.Message is { } message)
        {
            state += $" · MSG: {message}";
        }
        string tool = frame.Tool == 0 ? "" : $"T{frame.Tool} Ø{frame.ToolDiameter:0.##} · ";
        StatusText.Text = $"{tool}Linha {frame.LineNumber} · X {p.X:0.###}  Y {p.Y:0.###}  Z {p.Z:0.###}" +
                          $" · bruto {frame.Width:0.#} × {frame.Height:0.#} mm, grade {frame.Columns} × {frame.Rows}{state}";

        if (frame.IsFinished)
        {
            Pause();
        }
    }

    private void UpdateSpeedLabel() => SpeedLabel.Text = $"{SpeedMmPerSecond:0} mm/s";
}
