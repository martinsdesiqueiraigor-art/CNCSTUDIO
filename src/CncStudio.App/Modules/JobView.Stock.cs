using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CncStudio.Core.Cam;
using CncStudio.Core.Cam3D;
using CncStudio.Core.Scene;
using CncStudio.Core.Simulation;
using CncStudio.Core.Trajectory;

namespace CncStudio.App.Modules;

/// <summary>
/// CAM-STOCK-02: bruto depois de cada operação de fresa. O Core (<see cref="CamStockTimeline"/>) calcula fora da tela, numa cópia
/// do trabalho; aqui ficam o selo de cada operação na árvore, o material no 3D, a seção das propriedades e a chave da faixa.
/// Nada vai para o arquivo nem para o G-code. Simulação é verificação, nunca garantia.
/// </summary>
public partial class JobView
{
    /// <summary>Aviso fixo no console enquanto o bruto por operação está ligado.</summary>
    internal const string StockStepsCheck = "Bruto por operação: simulação é verificação, nunca garantia (resolução da grade). Nada vai para o arquivo nem para o G-code.";

    private CamStockTimeline? _stockTimeline;
    private CancellationTokenSource? _stockCancel;
    private int _stockVersion;
    private int _stockSceneVersion;
    private bool _stockBusy;
    private bool _stockOn = true;
    private readonly Dictionary<int, Border> _stockChips = [];
    private StackPanel? _stockSection;

    /// <summary>Chave "Bruto por operação" vinda das preferências do app (nasce ligada).</summary>
    public bool StockSteps
    {
        get => _stockOn;
        set
        {
            _stockOn = value;
            SyncStockToggle();
            Refresh(keep: true);
        }
    }

    /// <summary>Guarda a chave nas preferências do app (nunca no trabalho).</summary>
    public Action<bool>? SaveStockSteps { get; set; }

    private void InitStock()
    {
        StockStepsToggle.IsCheckedChanged += (_, _) =>
        {
            bool on = StockStepsToggle.IsChecked == true;
            if (on == _stockOn)
            {
                return;
            }

            _stockOn = on;
            SyncStockToggle();
            SaveStockSteps?.Invoke(on);
            Refresh(keep: true);
        };
        Scene3D.StepChanged += (_, _) => UpdateStockScene();
        SyncStockToggle();
        InitStart();
        InitCompare();
    }

    private void SyncStockToggle()
    {
        StockStepsToggle.IsChecked = _stockOn;
        StockStepsToggle.Content = "Bruto por operação: " + (_stockOn ? "Ligado" : "Desligado");
        ToolTip.SetTip(StockStepsToggle, _stockOn
            ? "Calcula o bruto depois de cada operação (fora da tela). Clique numa operação para ver no 3D. Desligue para voltar ao 3D de antes."
            : "Ligue para calcular o bruto depois de cada operação e ver no 3D.");
    }

    /// <summary>Recalcula o bruto por operação depois de cada mudança (cancela o cálculo anterior; só da operação mudada em diante).</summary>
    private async void StartStock()
    {
        int version = ++_stockVersion;
        _stockCancel?.Cancel();
        _stockCancel = null;
        if ((!_stockOn && !UsesStockStart) || _job.Operations.Count == 0)
        {
            _stockTimeline = null;
            ForgetCompare();
            _stockBusy = false;
            ShowStockStates();
            UpdateStockScene();
            return;
        }

        // cópia do trabalho feita aqui, na tela: o cálculo roda em outra thread
        CamJob copy;
        try
        {
            JobLoadResult again = CamJobJson.FromJson(CamJobJson.ToJson(_job));
            if (!again.Ok)
            {
                throw new InvalidOperationException(again.Error);
            }

            copy = again.Job!;
            copy.Contours.Clear();
            copy.Contours.AddRange(_job.Contours);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            _stockTimeline = null;
            ForgetCompare();
            _stockBusy = false;
            ApplyStartBuild();
            ShowStockStates();
            UpdateStockScene();
            return;
        }

        MachineProfile? machine = MachineSource?.Invoke();
        ToolBank? bank = ActiveBank;
        CamStockTimeline? previous = _stockTimeline;
        // peça STEP na posição (a mesma do 3D); sem ela não há comparação
        TriangleMesh? partMesh = _partMesh is { } pm && _job.PartStep?.Path == pm.Path ? pm.Placed : null;
        var cancel = new CancellationTokenSource();
        _stockCancel = cancel;
        _stockBusy = true;
        ShowStockStates();
        try
        {
            await Task.Delay(250, cancel.Token); // digitação seguida: espera parar
            (CamStockTimeline result, CamStockCompare? compare, IReadOnlyList<CamStockCompareSummary>? summary, string? problem) = await Task.Run(() =>
            {
                CamStockTimeline t = CamStockTimeline.Compute(copy, machine, bank, SimulationFactory.DesktopCells, previous, cancel.Token);
                // CAM-STOCK-03b: peça × bruto no mesmo cálculo (rápido perto da simulação); o Summary mexe no material, por isso o lock
                lock (t)
                {
                    CamStockCompare? c = CamStockCompare.Create(t, copy, partMesh, out string? why);
                    return (t, c, c?.Summary(), why);
                }
            }, cancel.Token);
            if (version != _stockVersion)
            {
                return;
            }

            _stockTimeline = result;
            _compare = compare;
            _compareSummary = summary;
            _compareProblem = problem;
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (version != _stockVersion)
            {
                return;
            }

            _stockTimeline = null; // sem bruto por operação: o resto da tela continua igual
            ForgetCompare();
        }

        _stockBusy = false;
        // CAM-AR-01b: o programa sai do bruto calculado (ou do digitado, com aviso, quando o cálculo falhou)
        ApplyStartBuild();
        ShowStockStates();
        UpdateStockScene();
    }

    /// <summary>Estado do bruto da operação para o selo: texto, tipo de cor e dica.</summary>
    private (string Text, string Kind, string Tip)? StockBadge(int index)
    {
        if (!_stockOn || index >= _job.Operations.Count || !_job.Operations[index].Enabled)
        {
            return null;
        }

        if (_stockBusy || _stockTimeline is null || index >= _stockTimeline.States.Count)
        {
            return ("calculando…", "info", "Bruto por operação: calculando fora da tela.");
        }

        CamStockState s = _stockTimeline.States[index];
        if (s.Status == CamStockStatus.Ready && CompareBadge(index) is { } verdict)
        {
            return verdict;
        }

        return s.Status switch
        {
            CamStockStatus.Ready => ("bruto ✓", "ok", $"Bruto depois desta operação calculado.\nTirou {Cm3(s.RemovedVolume)}; sobra {Cm3(s.Volume)}."),
            CamStockStatus.NoState when s.Reason?.StartsWith("Operação ", StringComparison.Ordinal) == true
                && s.Reason.EndsWith("anterior sem estado.", StringComparison.Ordinal) => ("sem estado", "neutral", s.Reason),
            CamStockStatus.NoState => ("sem estado", "pending", "Sem bruto depois desta operação: " + s.Reason),
            _ => null,
        };
    }

    /// <summary>Selo do bruto na segunda linha da operação, à direita do tempo.</summary>
    private void AddStockChip(int index, Grid row)
    {
        var text = new TextBlock { FontSize = 10, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var chip = new Border
        {
            Child = text,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 0),
            Margin = new Thickness(6, 1, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        // CAM-AR-01b: selo "parte em Z…" ao lado, na mesma célula
        var cell = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        cell.Children.Add(MakeStartChip(index));
        cell.Children.Add(chip);
        Grid.SetColumn(cell, 1);
        Grid.SetRow(cell, 1);
        row.Children.Add(cell);
        _stockChips[index] = chip;
        PaintStockChip(index, chip);
    }

    private void PaintStockChip(int index, Border chip)
    {
        if (StockBadge(index) is not { } b)
        {
            chip.IsVisible = false;
            return;
        }

        (IBrush fg, IBrush bg) = b.Kind == "info" ? (CamColors.SelectBrush, CamColors.SelectBgBrush) : UiKit.Status(b.Kind);
        chip.IsVisible = true;
        chip.Background = bg;
        ((TextBlock)chip.Child!).Text = b.Text;
        ((TextBlock)chip.Child!).Foreground = fg;
        ToolTip.SetTip(chip, b.Tip);
    }

    /// <summary>Selos, seção das propriedades e rodapé depois de um cálculo (sem refazer a árvore).</summary>
    private void ShowStockStates()
    {
        foreach ((int index, Border chip) in _stockChips)
        {
            PaintStockChip(index, chip);
        }

        FillStockSection();
        FillCompareTab();
        PaintStartChips();
        FillStartSection();
        UpdateStockStatus();
        ShowWarnings();
        SyncCompareToggle();
    }

    private void UpdateStockStatus()
    {
        if (!_stockOn || _job.Operations.Count == 0)
        {
            StatusStock.Text = "";
            return;
        }

        if (_stockBusy || _stockTimeline is null)
        {
            StatusStock.Text = "Bruto por operação: calculando…";
            return;
        }

        if (_stockTimeline.Problem is { } problem)
        {
            StatusStock.Text = "Bruto por operação: " + problem.TrimEnd('.');
            return;
        }

        int ready = _stockTimeline.States.Count(s => s.Status == CamStockStatus.Ready);
        int none = _stockTimeline.States.Count(s => s.Status == CamStockStatus.NoState);
        StatusStock.Text = $"Bruto por operação: {ready} calculad{(ready == 1 ? "o" : "os")}" + (none > 0 ? $", {none} sem estado" : "");
    }

    /// <summary>Seção "Bruto depois desta operação" no fim das propriedades da operação.</summary>
    private void AddStockSection(int index)
    {
        _stockSection = null;
        if (!_stockOn)
        {
            return;
        }

        _stockSection = new StackPanel { Spacing = 4, Tag = index, Margin = new Thickness(0, 8, 0, 0) };
        OpPanel.Children.Add(_stockSection);
        FillStockSection();
    }

    private void FillStockSection()
    {
        if (_stockSection is not { Tag: int index } panel)
        {
            return;
        }

        panel.Children.Clear();
        panel.Children.Add(new TextBlock { Text = "BRUTO DEPOIS DESTA OPERAÇÃO", FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = CamColors.MutedBrush });
        if (StockBadge(index) is not { } badge)
        {
            panel.Children.Add(new TextBlock { Text = "Operação desligada: o bruto passa igual.", FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap });
            return;
        }

        CamStockState? s = !_stockBusy && _stockTimeline is { } t && index < t.States.Count ? t.States[index] : null;
        panel.Children.Add(Line("Estado", badge.Text));
        if (s is { Status: CamStockStatus.Ready })
        {
            panel.Children.Add(Line("Material tirado", Cm3(s.RemovedVolume)));
            panel.Children.Add(Line("Sobra no bruto", Cm3(s.Volume)));
            panel.Children.Add(Line("Motor", _stockTimeline!.UsesVoxels ? "voxels" : "grade de alturas"));
            panel.Children.Add(Line("Cálculo", s.Reused ? "reaproveitado" : s.Elapsed.TotalSeconds < 0.1 ? "menos de 0,1 s" : $"{s.Elapsed.TotalSeconds:0.0} s".Replace('.', ',')));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 4, 0, 0) };
            var before = new Button { Content = "Ver antes", Padding = new Thickness(10, 3), FontSize = 12 };
            var after = new Button { Content = "Ver depois", Padding = new Thickness(10, 3), FontSize = 12 };
            before.Click += (_, _) => ShowStockStep(StockStep.Before);
            after.Click += (_, _) => ShowStockStep(StockStep.After);
            buttons.Children.Add(before);
            buttons.Children.Add(after);
            panel.Children.Add(buttons);
        }
        else if (s is { Reason: { } reason })
        {
            panel.Children.Add(new TextBlock { Text = reason, FontSize = 12, Opacity = 0.8, TextWrapping = TextWrapping.Wrap });
        }

        panel.Children.Add(UiKit.StatusNote("Verificação, nunca garantia: recalculado ao abrir e a cada mudança (só da operação mudada em diante). Nada vai para o arquivo nem para o G-code.", "neutral"));
    }

    private static Grid Line(string label, string value)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        g.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = CamColors.MutedBrush, VerticalAlignment = VerticalAlignment.Center });
        var v = new TextBlock { Text = value, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(v, 1);
        g.Children.Add(v);
        return g;
    }

    private void ShowStockStep(StockStep step)
    {
        if (View3DButton.IsChecked != true)
        {
            View3DButton.IsChecked = true;
        }

        Scene3D.ChooseStep(step);
    }

    private static string Cm3(double mm3) => (mm3 / 1000).ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")) + " cm³";

    /// <summary>
    /// 3D: com a chave ligada, uma operação escolhida e estado, mostra o material depois (ou antes) dela; senão o 3D de antes.
    /// A malha é montada fora da tela; um pedido novo descarta o anterior.
    /// </summary>
    private async void UpdateStockScene()
    {
        int version = ++_stockSceneVersion;
        CamStockTimeline? timeline = _stockTimeline;
        int? op = SelectedOp();
        bool on = _stockOn && timeline is not null && !_stockBusy && op is { } o && o < timeline.States.Count && View3DButton.IsChecked == true;
        Scene3D.StepBarVisible = _stockOn && op is not null && View3DButton.IsChecked == true;
        if (!on || timeline!.States[op!.Value].Status == CamStockStatus.NoState)
        {
            Scene3D.SetMaterial(null, default);
            ClearCompareScene();
            return;
        }

        int index = Scene3D.Step switch { StockStep.Whole => -1, StockStep.Before => op.Value - 1, _ => op.Value };
        CamStockCompare? compare = _compareOn ? _compare : null;
        if (compare is not null && _compareView == CompareView.Final)
        {
            // fim do programa: o material e as cores são os da última operação com estado
            index = timeline.States.Count - 1;
        }

        if (index < 0 || !timeline.States.Take(index + 1).Any(s => s.Status == CamStockStatus.Ready))
        {
            // bruto inteiro: o sólido do bruto que o 3D já desenha (o instantâneo inicial do Core não serve: ver CAM-STOCK-02-volta)
            Scene3D.SetMaterial(null, default);
            ClearCompareScene();
            return;
        }

        Core.Geometry.Point3 zero = CamJobZero.Position(_job) ?? default;
        IReadOnlyList<SceneObjectState>? material = null, overlay = null;
        CamStockCompareResult? result = null;
        try
        {
            (material, overlay, result) = await Task.Run(() => MaterialAndCompare(timeline, compare, index));
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            material = null;
            overlay = null;
            result = null;
        }

        if (version == _stockSceneVersion)
        {
            Scene3D.SetMaterial(material, zero);
            Scene3D.SetCompare(overlay, zero);
            _compareShown = result;
            FillCompareTab();
        }
    }

    private void ClearCompareScene()
    {
        Scene3D.SetCompare(null, default);
        _compareShown = null;
        FillCompareTab();
    }

    /// <summary>Esquece a comparação (sem bruto por operação, ou cálculo que falhou).</summary>
    private void ForgetCompare()
    {
        _compare = null;
        _compareSummary = null;
        _compareProblem = null;
        _compareShown = null;
        _compareFocus = null;
    }

    /// <summary>Malha do material depois da operação <paramref name="index"/> (−1 = bruto inteiro). Um cálculo de cada vez por bruto.</summary>
    private static IReadOnlyList<SceneObjectState>? MaterialAfter(CamStockTimeline timeline, int index)
    {
        ToolPath? path = timeline.States.FirstOrDefault(s => s.Path is not null)?.Path;
        if (path is null)
        {
            return null;
        }

        lock (timeline)
        {
            if (!timeline.ShowAfter(index))
            {
                return null;
            }

            SimulationScene scene = timeline.Voxels is { } voxels
                ? SimulationScene.ForVoxels(voxels, path, timeline.Profiles)
                : SimulationScene.ForMill(timeline.Map!, path, timeline.Profiles);
            return [.. scene.Snapshot().Objects.Where(o => o.Role == SceneRole.Material)];
        }
    }
}
