using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CncStudio.Core.Execution;
using CncStudio.Core.Trajectory;

namespace CncStudio.App.Views;

/// <summary>
/// Aba do backplot: escolha da vista, legenda de cores e a barra "passe a passe", que percorre
/// a trajetória movimento a movimento (ou passe a passe) mostrando o caminho da ferramenta.
/// </summary>
public partial class BackplotPanel : UserControl
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    private readonly Stopwatch _clock = new();
    private TimeSpan _lastTick;
    private double _distanceBudget;

    private ToolPath? _path;
    private int[] _passStarts = [];
    private int? _limit;          // null = trajetória inteira (sem passe a passe)
    private bool _updatingSlider;
    private int _lastReportedLine;

    public BackplotPanel()
    {
        InitializeComponent();

        PlaneXY.IsCheckedChanged += (_, _) => OnPlaneChecked(PlaneXY, ViewPlane.XY);
        PlaneXZ.IsCheckedChanged += (_, _) => OnPlaneChecked(PlaneXZ, ViewPlane.XZ);
        PlaneYZ.IsCheckedChanged += (_, _) => OnPlaneChecked(PlaneYZ, ViewPlane.YZ);
        PlaneIso.IsCheckedChanged += (_, _) => OnPlaneChecked(PlaneIso, ViewPlane.Iso);
        PlaneLathe.IsCheckedChanged += (_, _) => OnPlaneChecked(PlaneLathe, ViewPlane.Lathe);
        FitButton.Click += (_, _) => View.FitToContent();
        MeasureToggle.IsCheckedChanged += (_, _) => View.MeasureMode = MeasureToggle.IsChecked == true;
        View.MoveClicked += (_, index) =>
        {
            if (_path is not null && index < _path.Moves.Count)
            {
                LineRequested?.Invoke(this, _path.Moves[index].LineNumber);
            }
        };

        StartButton.Click += (_, _) => { Pause(); SetLimit(0); };
        EndButton.Click += (_, _) => { Pause(); SetLimit(null); };
        PreviousPassButton.Click += (_, _) => { Pause(); PreviousPass(); };
        NextPassButton.Click += (_, _) => { Pause(); NextPass(); };
        PlayButton.Click += (_, _) => TogglePlay();

        PositionSlider.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty && !_updatingSlider && _path is not null)
            {
                Pause();
                int value = (int)Math.Round(PositionSlider.Value);
                SetLimit(value >= _path.Moves.Count ? null : value);
            }
        };

        _timer.Tick += OnTick;
        UpdateStatus();
    }

    /// <summary>Clicou num movimento: ir para a linha dele no editor.</summary>
    public event EventHandler<int>? LineRequested;

    /// <summary>A barra passe a passe chegou a outra linha: o editor pode acompanhar.</summary>
    public event EventHandler<int>? LineReached;

    /// <summary>Vista atual.</summary>
    public ViewPlane Plane => View.Plane;

    /// <summary>Seleciona a vista (marca o botão correspondente).</summary>
    public void ShowPlane(ViewPlane plane)
    {
        RadioButton button = plane switch
        {
            ViewPlane.XZ => PlaneXZ,
            ViewPlane.YZ => PlaneYZ,
            ViewPlane.Iso => PlaneIso,
            ViewPlane.Lathe => PlaneLathe,
            _ => PlaneXY,
        };
        button.IsChecked = true;
        View.Plane = plane;
    }

    public void SetToolPath(ToolPath path, IReadOnlyList<PathPolyline> figures, bool fit)
    {
        Pause();
        _path = path;
        _passStarts = PathPasses.Starts(path.Moves);
        _limit = null;
        View.SetToolPath(path, figures, fit);

        _updatingSlider = true;
        PositionSlider.Maximum = Math.Max(1, path.Moves.Count);
        PositionSlider.Value = PositionSlider.Maximum;
        _updatingSlider = false;
        UpdateStatus();
    }

    public void SetHighlightedLine(int lineNumber) => View.SetHighlightedLine(lineNumber);

    public void FitToContent() => View.FitToContent();

    /// <summary>Atalhos: Page Up / Page Down = passe anterior / próximo (quando o backplot tem o foco).</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && MeasureToggle.IsChecked == true)
        {
            MeasureToggle.IsChecked = false;
            e.Handled = true;
        }
        else if (e.Key == Key.PageDown)
        {
            NextPass();
            e.Handled = true;
        }
        else if (e.Key == Key.PageUp)
        {
            PreviousPass();
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private void OnPlaneChecked(RadioButton button, ViewPlane plane)
    {
        if (button.IsChecked == true)
        {
            View.Plane = plane;
        }
    }

    // ───────────────────────── Navegação ─────────────────────────

    private int MoveCount => _path?.Moves.Count ?? 0;

    /// <summary>Movimentos já executados (a trajetória inteira quando fora do passe a passe).</summary>
    private int Position => _limit ?? MoveCount;

    private void NextPass()
    {
        int position = Position;
        if (_path is null || position >= MoveCount)
        {
            return;
        }

        int pass = PathPasses.PassOf(_passStarts, position);
        SetLimit(PathPasses.EndOf(_passStarts, pass, MoveCount));
    }

    private void PreviousPass()
    {
        int position = Position;
        if (_path is null || position <= 0)
        {
            return;
        }

        int pass = PathPasses.PassOf(_passStarts, position - 1);
        SetLimit(_passStarts[pass]);
    }

    /// <summary>Muda o ponto da execução. null = mostra tudo (modo normal).</summary>
    private void SetLimit(int? limit)
    {
        if (_path is null)
        {
            return;
        }

        if (limit is { } l)
        {
            limit = Math.Clamp(l, 0, MoveCount);
        }

        _limit = limit;
        (int, int)? currentPass = null;
        if (limit is { } current && current > 0 && _passStarts.Length > 0)
        {
            int pass = PathPasses.PassOf(_passStarts, current - 1);
            currentPass = (_passStarts[pass], PathPasses.EndOf(_passStarts, pass, MoveCount));
        }

        View.SetLimit(limit, currentPass);

        _updatingSlider = true;
        PositionSlider.Value = limit ?? PositionSlider.Maximum;
        _updatingSlider = false;
        UpdateStatus();
    }

    // ───────────────────────── Execução animada ─────────────────────────

    /// <summary>Velocidade da animação em mm de trajetória por segundo (escala logarítmica).</summary>
    private double SpeedMmPerSecond => 10 * Math.Pow(10, SpeedSlider.Value / 25);

    private void TogglePlay()
    {
        if (_timer.IsEnabled)
        {
            Pause();
            return;
        }

        if (_path is null || MoveCount == 0)
        {
            return;
        }

        if (Position >= MoveCount)
        {
            SetLimit(0); // terminou: recomeça do início
        }

        _distanceBudget = 0;
        _clock.Restart();
        _lastTick = TimeSpan.Zero;
        _timer.Start();
        PlayButton.Content = "⏸";
    }

    private void Pause()
    {
        _timer.Stop();
        PlayButton.Content = "▶";
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_path is null)
        {
            Pause();
            return;
        }

        TimeSpan now = _clock.Elapsed;
        double seconds = Math.Min(0.1, (now - _lastTick).TotalSeconds);
        _lastTick = now;
        _distanceBudget += SpeedMmPerSecond * seconds;

        // Avança movimentos inteiros enquanto houver "distância" disponível
        int position = Position;
        while (position < MoveCount)
        {
            double length = Math.Max(_path.Moves[position].Length, 0.01);
            if (length > _distanceBudget)
            {
                break;
            }

            _distanceBudget -= length;
            position++;
        }

        if (position != Position)
        {
            SetLimit(position);
        }

        if (position >= MoveCount)
        {
            Pause();
        }
    }

    // ───────────────────────── Status ─────────────────────────

    private void UpdateStatus()
    {
        if (_path is null || MoveCount == 0)
        {
            StatusText.Text = "Sem movimentos.";
            return;
        }

        int passes = _passStarts.Length;
        if (_limit is not { } limit)
        {
            StatusText.Text = $"Trajetória inteira · {passes} passe(s) · {MoveCount} movimentos — use ◀ Passe / Passe ▶ para ver cada passe";
            return;
        }

        if (limit == 0)
        {
            StatusText.Text = $"Início · {passes} passe(s) · {MoveCount} movimentos";
            return;
        }

        Move move = _path.Moves[limit - 1];
        int pass = PathPasses.PassOf(_passStarts, limit - 1) + 1;
        bool lathe = _path.Machine == MachineKind.Lathe;
        string code = move.Motion switch
        {
            MotionMode.Rapid => "G00",
            MotionMode.Linear => "G01",
            MotionMode.ClockwiseArc => "G02",
            _ => "G03",
        };
        string position = lathe
            ? Local($"X {move.End.X * 2:0.###} (Ø)  Z {move.End.Z:0.###}")
            : Local($"X {move.End.X:0.###}  Y {move.End.Y:0.###}  Z {move.End.Z:0.###}");
        string feed = move.IsRapid ? "" : $" · F {move.Feed:0.#} mm/min";

        StatusText.Text = $"Passe {pass}/{passes} · movimento {limit}/{MoveCount} · linha {move.LineNumber} · {code} · {position}{feed}";

        if (move.LineNumber != _lastReportedLine)
        {
            _lastReportedLine = move.LineNumber;
            LineReached?.Invoke(this, move.LineNumber);
        }
    }

    private static string Local(FormattableString text) => text.ToString(CultureInfo.CurrentCulture);
}
