using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CncStudio.Core.Assist;
using CncStudio.Core.Cam;

namespace CncStudio.App.Modules;

/// <summary>Aba Plano 2D do Assist: sugere furos, bolsões e contorno a partir do desenho do Trabalho de fresa.</summary>
public sealed partial class AssistView
{
    private readonly ComboBox _planMaterial = new() { MinWidth = 220 };
    private readonly TextBox _planThickness = new() { Width = 80, Text = "10" };
    private readonly TextBlock _planDrawing = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock _planResult = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private readonly Button _planApply = new() { Content = "Aplicar ao Trabalho (CAM)", Padding = new Thickness(16, 6), IsEnabled = false };
    private PlanResult? _plan;
    private readonly StackPanel _planToolsPanel = new() { Spacing = 4 };
    private readonly HashSet<int> _confirmedNewTools = [];

    /// <summary>Contornos do desenho do Trabalho de fresa e a espessura do bruto, se já definida.</summary>
    public Func<(IReadOnlyList<Contour> Contours, double? Thickness)>? PlanDrawing { get; set; }

    public Func<IReadOnlyList<PlanTool>>? PlanTools { get; set; }

    /// <summary>Põe as operações sugeridas no Trabalho de fresa.</summary>
    public Action<IReadOnlyList<CamOperationSpec>>? ApplyPlan { get; set; }

    private void AddPlanTab()
    {
        var form = new StackPanel { Spacing = 8, MaxWidth = 700 };
        form.Children.Add(new TextBlock
        {
            Text = "Sugere furos, bolsões e o contorno da peça a partir do desenho aberto em CAM › Trabalho fresa. Só sugere o que o CAM sabe gerar; revise, simule e rode a primeira vez no ar.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.8,
        });
        form.Children.Add(_planDrawing);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock { Text = "Material", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        row.Children.Add(_planMaterial);
        row.Children.Add(new TextBlock { Text = "Espessura do bruto", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        row.Children.Add(_planThickness);
        row.Children.Add(new TextBlock { Text = "mm", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.65, FontSize = 11 });
        form.Children.Add(row);
        _planMaterial.SelectionChanged += (_, _) => OnPlanInputChanged();
        _planThickness.TextChanged += (_, _) => OnPlanInputChanged();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button suggest = new() { Content = "Sugerir plano", Padding = new Thickness(16, 6) };
        suggest.Click += (_, _) => SuggestPlan();
        _planApply.Click += (_, _) =>
        {
            if (_plan is { Ok: true } plan && ApplyPlan is not null)
            {
                // Confere de novo na hora de aplicar: o desenho, a espessura, o material ou as ferramentas podem ter mudado
                if (CheckPlanStillValid() is { } stale)
                {
                    InvalidatePlan(stale);
                    return;
                }

                // E as ferramentas novas (fora do banco) só entram se cada uma foi confirmada
                if (CheckPlanConfirmed() is { } pending)
                {
                    _planResult.Text += "\n⚠ " + pending;
                    _planApply.IsEnabled = false;
                    return;
                }

                ApplyPlan(plan.Operations);
                _planResult.Text += "\n(operações adicionadas ao Trabalho fresa)";
                _planApply.IsEnabled = false;
                _plan = null;
                ClearPlanTools();
            }
        };
        buttons.Children.Add(suggest);
        buttons.Children.Add(_planApply);
        form.Children.Add(buttons);
        form.Children.Add(_planResult);
        form.Children.Add(_planToolsPanel);
        var tab = new TabItem { Header = "Plano 2D", Content = new Border { Padding = new Thickness(0, 10), Child = form } };
        tab.PropertyChanged += (_, e) =>
        {
            if (e.Property == TabItem.IsSelectedProperty && tab.IsSelected)
            {
                LoadPlan();
            }
        };
        _tabs.Items.Add(tab);
    }

    private bool _loadingPlan;

    /// <summary>Espessura ou material mudaram: o plano já sugerido deixa de valer.</summary>
    private void OnPlanInputChanged()
    {
        if (!_loadingPlan && CheckPlanStillValid() is { } stale)
        {
            InvalidatePlan(stale);
        }
    }

    /// <summary>Descarta o plano sugerido e explica por quê (o botão Aplicar fica desligado até sugerir de novo).</summary>
    private void InvalidatePlan(string reason)
    {
        _plan = null;
        _planApply.IsEnabled = false;
        ClearPlanTools();
        _planResult.Text = "⚠ " + reason;
    }

    private void ClearPlanTools()
    {
        _confirmedNewTools.Clear();
        _planToolsPanel.Children.Clear();
    }

    /// <summary>Motivo se alguma ferramenta nova (fora do banco) ainda não foi confirmada; null se tudo certo.</summary>
    private string? CheckPlanConfirmed()
    {
        if (_plan is not { } plan)
        {
            return null;
        }

        (PlanInput? input, string? error) = BuildPlanInput();
        return input is null ? error : PlanValidity.CheckApply(plan, input, _confirmedNewTools);
    }

    /// <summary>Uma caixa por ferramenta nova: o botão Aplicar só liga quando todas estiverem marcadas.</summary>
    private void ShowPlanTools(PlanResult plan, PlanInput input)
    {
        ClearPlanTools();
        List<PlannedTool> news = PlanValidity.ToolsOf(plan, input).Where(t => t.Origin == ToolOrigin.New).ToList();
        if (news.Count == 0)
        {
            return;
        }

        _planToolsPanel.Children.Add(new TextBlock
        {
            Text = "Ferramentas novas: não estão no banco de ferramentas. O programa usaria estes números e diâmetros inventados pelo plano. Confira na máquina e confirme cada uma (ou cadastre no banco e sugira de novo).",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, FontWeight = FontWeight.SemiBold,
        });
        foreach (PlannedTool t in news)
        {
            var box = new CheckBox { Content = $"Confirmo T{t.Number} · Ø{t.Diameter.ToString("0.##", Br)} mm · {t.Name}" };
            int number = t.Number;
            box.IsCheckedChanged += (_, _) =>
            {
                if (box.IsChecked == true)
                {
                    _confirmedNewTools.Add(number);
                }
                else
                {
                    _confirmedNewTools.Remove(number);
                }

                _planApply.IsEnabled = _plan is { Ok: true } && CheckPlanConfirmed() is null;
            };
            _planToolsPanel.Children.Add(box);
        }
    }

    /// <summary>Mensagem do motivo se o plano guardado não vale mais; null se vale (ou se não há plano).</summary>
    private string? CheckPlanStillValid()
    {
        if (_plan is not { } plan)
        {
            return null;
        }

        // Só desatualização: a falta de confirmação das ferramentas novas não descarta o plano (por isso conta todas como confirmadas aqui)
        (PlanInput? input, string? error) = BuildPlanInput();
        return input is null ? error : PlanValidity.CheckApply(plan, input, PlanValidity.NewToolNumbers(plan, input));
    }

    /// <summary>Monta os dados do plano com o que está na tela agora (desenho do Trabalho, espessura, material e ferramentas).</summary>
    private (PlanInput? Input, string? Error) BuildPlanInput()
    {
        (IReadOnlyList<Contour> contours, double? thickness)? d = PlanDrawing?.Invoke();
        if (d is not { } drawing || drawing.contours.Count == 0)
        {
            return (null, "O desenho do Trabalho fresa não está mais aberto. Abra um DXF em CAM › Trabalho fresa e sugira de novo.");
        }

        if (!double.TryParse((_planThickness.Text ?? "").Trim().Replace('.', ','), NumberStyles.Float, Br, out double thick) || !(thick > 0))
        {
            return (null, "Confira a espessura do bruto.");
        }

        MaterialEntry? material = _planMaterial.SelectedIndex >= 0 && _planMaterial.SelectedIndex < _lib.Materials.Count ? _lib.Materials[_planMaterial.SelectedIndex] : null;
        return (new PlanInput(drawing.contours, thick, material, PlanTools?.Invoke() ?? []), null);
    }

    private void LoadPlan()
    {
        _loadingPlan = true;
        try
        {
            LoadPlanFields();
        }
        finally
        {
            _loadingPlan = false;
        }

        // Voltou para a aba: o desenho, o material ou as ferramentas podem ter mudado fora daqui
        if (CheckPlanStillValid() is { } stale)
        {
            InvalidatePlan(stale);
        }
    }

    private void LoadPlanFields()
    {
        int keep = _planMaterial.SelectedIndex;
        _planMaterial.Items.Clear();
        foreach (MaterialEntry m in _lib.Materials)
        {
            _planMaterial.Items.Add(m.Name);
        }

        _planMaterial.SelectedIndex = Math.Clamp(keep, _lib.Materials.Count > 0 ? 0 : -1, _lib.Materials.Count - 1);
        (IReadOnlyList<Contour> contours, double? thickness)? d = PlanDrawing?.Invoke();
        int n = d?.contours.Count ?? 0;
        _planDrawing.Text = n > 0
            ? $"Desenho do Trabalho fresa: {n} contorno(s)."
            : "Nenhum desenho no Trabalho fresa. Abra um DXF em CAM › Trabalho fresa e volte aqui.";
        if (d?.thickness is { } t && t > 0)
        {
            _planThickness.Text = t.ToString("0.##", Br);
        }
    }

    private void SuggestPlan()
    {
        _plan = null;
        _planApply.IsEnabled = false;
        ClearPlanTools();
        (PlanInput? input, string? error) = BuildPlanInput();
        if (input is null)
        {
            _planResult.Text = error is not null && error.StartsWith("O desenho", StringComparison.Ordinal) ? "Abra um DXF em CAM › Trabalho fresa primeiro." : error;
            return;
        }

        PlanResult plan = AssistPlanner.Plan(input);
        var lines = new List<string>();
        for (int i = 0; i < plan.Operations.Count; i++)
        {
            CamOperationSpec op = plan.Operations[i];
            string origin = PlanValidity.ToolsOf(plan, input).First(t => t.Number == op.Tool.Number && t.Diameter == op.Tool.Diameter).Origin == ToolOrigin.New ? "NOVA, fora do banco" : "banco";
            lines.Add(CamJobInfo.OpTitle(i, op) + $" · T{op.Tool.Number} ({origin}) · {op.Tool.SpindleRpm:0} rpm · F{op.Tool.FeedXY:0}");
        }

        if (plan.Ok)
        {
            lines.Insert(0, "Operações sugeridas:");
        }

        lines.AddRange(plan.Notes.Select(n => "• " + n));
        _planResult.Text = string.Join("\n", lines);
        _plan = plan;
        ShowPlanTools(plan, input);
        _planApply.IsEnabled = plan.Ok && CheckPlanConfirmed() is null;
    }
}
