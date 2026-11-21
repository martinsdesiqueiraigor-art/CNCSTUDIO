using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CncStudio.Core;
using CncStudio.Core.Cam;

namespace CncStudio.App.Modules;

/// <summary>
/// CAM-AR-01b: "começar no material" na tela (referência: Fusion, aba Heights, "Top Height from Stock top" e "Feed Height").
/// O Core (<see cref="CamStockStart"/>) decide as alturas; aqui ficam a escolha na aba Alturas, o selo da árvore, a chave da faixa
/// para operações novas e o programa feito a partir do bruto por operação calculado fora da tela (a tela nunca simula).
/// Enquanto o cálculo não volta, o programa fica "calculando o material" e não sai (Enviar e Copiar travados).
/// </summary>
public partial class JobView
{
    private const string StartPendingTitle = "Código CNC: calculando o material… (começar no material)";

    private bool _startPending;
    private bool _newOpsFromStock;
    private StackPanel? _startSection;
    private readonly Dictionary<int, Border> _startChips = [];

    /// <summary>Alguma operação ligada começa no material: o programa sai do bruto por operação.</summary>
    private bool UsesStockStart => _job.Operations.Any(o => o.Enabled && o.TopFromStock);

    /// <summary>Chave "Operações novas: começar no material" vinda das preferências do app (nasce desligada).</summary>
    public bool NewOpsFromStock
    {
        get => _newOpsFromStock;
        set
        {
            _newOpsFromStock = value;
            SyncStartToggle();
        }
    }

    /// <summary>Guarda a chave nas preferências do app (nunca no trabalho).</summary>
    public Action<bool>? SaveNewOpsFromStock { get; set; }

    private void InitStart()
    {
        NewOpsStockToggle.IsCheckedChanged += (_, _) =>
        {
            bool on = NewOpsStockToggle.IsChecked == true;
            if (on == _newOpsFromStock)
            {
                return;
            }

            _newOpsFromStock = on;
            SyncStartToggle();
            SaveNewOpsFromStock?.Invoke(on);
        };
        SyncStartToggle();
    }

    private void SyncStartToggle()
    {
        NewOpsStockToggle.IsChecked = _newOpsFromStock;
        NewOpsStockToggle.Content = "Novas no material: " + (_newOpsFromStock ? "Ligado" : "Desligado");
        ToolTip.SetTip(NewOpsStockToggle, "Operações novas (Faceamento, Contorno, Bolsão) nascem com \"Topo a partir de: Material\": começam no bruto que sobrou das operações anteriores. "
            + "Muda o G-code; confira na simulação. As operações que já existem não mudam.");
    }

    /// <summary>Operações criadas a partir de <paramref name="before"/>: nascem com a escolha da faixa (só onde vale).</summary>
    private void MarkNewOps(int before)
    {
        if (!_newOpsFromStock)
        {
            return;
        }

        for (int k = Math.Max(0, before); k < _job.Operations.Count; k++)
        {
            CamOperationSpec s = _job.Operations[k];
            if (CamStockStart.Applies(s.Kind) && s.Face is null)
            {
                _job.Operations[k] = s with { TopFromStock = true };
            }
        }
    }

    /// <summary>Programa do Refresh: com "começar no material", o digitado só para a árvore até o cálculo voltar.</summary>
    private JobBuild BuildNow(MachineProfile? machine)
    {
        _startPending = UsesStockStart;
        return _startPending ? CamJobBuilder.BuildFrom(_job, machine, null) : CamJobBuilder.Build(_job, machine);
    }

    /// <summary>O bruto por operação voltou (ou falhou): refaz o programa a partir dele, sem simular na tela.</summary>
    private void ApplyStartBuild()
    {
        if (!UsesStockStart)
        {
            return;
        }

        MachineProfile? machine = MachineSource?.Invoke();
        CamStockTimeline? t = _stockTimeline is { } tl && tl.Items.Count == _job.Operations.Count ? tl : null;
        _build = CamJobBuilder.BuildFrom(_job, machine, t);
        _builtWith = DataStamp.OfMachine(machine);
        _startPending = false;
        ShowBuildOutput();
        StartTimes();
        UpdateStatus();
        UpdatePreview();
        Update3D();
        FillStartSection();
    }

    /// <summary>Aviso de "começar no material" vindo do Core: vai como Conferir no console.</summary>
    private static bool IsStartNote(string line) =>
        line.Contains("começar no material não aplicado", StringComparison.Ordinal)
        || (line.Contains(" parte em Z", StringComparison.Ordinal) && line.Contains("confira na simulação", StringComparison.Ordinal));

    /// <summary>"Começar no material" desta operação no último cálculo (null sem cálculo ou sem estado).</summary>
    private CamStockState? StartState(int index) =>
        !_startPending && !_stockBusy && _stockTimeline is { } t && index < t.States.Count && t.Items.Count == _job.Operations.Count ? t.States[index] : null;

    /// <summary>Selo da árvore: "parte em Z…" quando ajustada; nada quando a operação não pede.</summary>
    private (string Text, string Kind, string Tip)? StartBadge(int index)
    {
        if (index >= _job.Operations.Count || _job.Operations[index] is not { Enabled: true, TopFromStock: true })
        {
            return null;
        }

        if (_startPending || _stockBusy)
        {
            return ("mat.…", "info", "Começar no material: calculando o bruto das operações anteriores.");
        }

        CamStockState? st = StartState(index);
        if (st?.Start is { Adjusted: true } s)
        {
            return ($"Z{StartFmt(s.Levels.TopZ)} mat.", "ok", "Parte em Z" + StartFmt(s.Levels.TopZ) + " (começar no material).\n" + (st.StartNote ?? ""));
        }

        return ("digitado", "pending", st?.StartNote ?? "Começar no material não aplicado: gera pelo topo digitado.");
    }

    /// <summary>Selo "parte em Z…" (fica ao lado do selo do bruto, na segunda linha da operação).</summary>
    private Border MakeStartChip(int index)
    {
        var text = new TextBlock { FontSize = 10, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var chip = new Border
        {
            Child = text,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 0),
            Margin = new Thickness(6, 1, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _startChips[index] = chip;
        PaintStartChip(index, chip);
        return chip;
    }

    private void PaintStartChip(int index, Border chip)
    {
        if (StartBadge(index) is not { } b)
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

    private void PaintStartChips()
    {
        foreach ((int index, Border chip) in _startChips)
        {
            PaintStartChip(index, chip);
        }
    }

    /// <summary>Aba Alturas: "Topo a partir de: Digitado / Material" e os números do último cálculo.</summary>
    private void StartSection(int i, CamOperationSpec s)
    {
        _startSection = null;
        Section("Topo a partir de");
        if (!CamStockStart.Applies(s.Kind))
        {
            PropsPanel.Children.Add(StartNoteText("Começar no material vale só para Faceamento, Contorno e Bolsão. Aqui o topo é sempre o digitado."));
            return;
        }

        if (s.Face is not null)
        {
            PropsPanel.Children.Add(StartNoteText("Face girada: o bruto é calculado em voxel e o topo fica o digitado."));
            return;
        }

        Pick(i, "Topo", ["Digitado", "Material (bruto que sobrou)"], s.TopFromStock ? 1 : 0, (x, n) => x with { TopFromStock = n == 1 }, rebuild: true);
        if (!s.TopFromStock)
        {
            return;
        }

        _startSection = new StackPanel { Spacing = 3, Tag = i };
        PropsPanel.Children.Add(_startSection);
        FillStartSection();
    }

    private void FillStartSection()
    {
        if (_startSection is not { Tag: int index } panel)
        {
            return;
        }

        panel.Children.Clear();
        if (_startPending || _stockBusy)
        {
            panel.Children.Add(StartNoteText("Calculando o bruto das operações anteriores…"));
            return;
        }

        CamStockState? st = StartState(index);
        if (st?.Start is { } s && s.Adjusted)
        {
            Row(panel, "Topo do material aqui", StartFmt(s.Levels.TopZ), UiKit.Status("ok").Fg);
            Row(panel, "Folga de segurança", StartFmt(s.Clearance), null);
            Row(panel, "Altura de avanço", StartFmt(s.Levels.ClearanceZ), CamColors.AccentBrush);
            Row(panel, "Passes em Z", $"{s.PassesBefore} → {s.PassesAfter}", null);
            panel.Children.Add(StartNoteText($"Folga = {StartFmt(CamStockStart.BaseClearance)} mm + célula do bruto. Passes contados do topo do material. "
                + (s.FromOperation < 0 ? "Pelo bruto inicial." : $"Pelo bruto depois da operação {s.FromOperation + 1}.") + " Acima do material nada é cortado."));
        }
        else
        {
            panel.Children.Add(UiKit.StatusNote(st?.StartNote ?? "Começar no material não aplicado: gera pelo topo digitado.", "pending"));
        }

        panel.Children.Add(UiKit.StatusNote("Isto muda o G-code. Verificação, nunca garantia: o bruto é o simulado e deve ser igual ao da máquina.", "pending"));
    }

    private static void Row(StackPanel panel, string label, string value, IBrush? color)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        g.Children.Add(new TextBlock { Text = label, FontSize = 12, Opacity = 0.85 });
        var v = new TextBlock { Text = value, FontSize = 12, FontFamily = new FontFamily("monospace") };
        if (color is not null)
        {
            v.Foreground = color;
        }

        Grid.SetColumn(v, 1);
        g.Children.Add(v);
        panel.Children.Add(g);
    }

    private static TextBlock StartNoteText(string text) => new() { Text = text, Opacity = 0.65, FontSize = 11, TextWrapping = TextWrapping.Wrap };

    private static string StartFmt(double v) => v.ToString("0.###", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));
}
