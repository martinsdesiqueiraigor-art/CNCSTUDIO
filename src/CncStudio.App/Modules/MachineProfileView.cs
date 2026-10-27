using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CncStudio.Core.Cam;

namespace CncStudio.App.Modules;

/// <summary>
/// Tela "Perfil 4/5 eixos" do Casillas CAM: o programador descreve a máquina (comando, eixos rotativos A/B/C na mesa ou no
/// cabeçote, limites, pivô e modo de saída). Os perfis ficam nas configurações; a validação e o texto são do Core.
/// </summary>
public sealed class MachineProfileView : UserControl
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly string[] Presets =
    [
        "3 eixos (sem rotativo)", "4º eixo na mesa (A)", "5 eixos: mesa A + C", "5 eixos mista: cabeçote B + mesa C",
    ];

    private sealed class AxisEditor
    {
        public required CheckBox Use { get; init; }
        public required ComboBox Letter { get; init; }
        public required ComboBox Carrier { get; init; }
        public required TextBox Min { get; init; }
        public required TextBox Max { get; init; }
        public required CheckBox Reversed { get; init; }
        public required TextBox PivotX { get; init; }
        public required TextBox PivotY { get; init; }
        public required TextBox PivotZ { get; init; }
    }

    private readonly ComboBox _profiles = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 30 };
    private readonly ComboBox _preset = new() { HorizontalAlignment = HorizontalAlignment.Stretch, MinHeight = 30 };
    private readonly TextBox _name = new() { MinHeight = 28 };
    private readonly ComboBox _controller = new() { MinWidth = 160 };
    private readonly ComboBox _mode = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly AxisEditor[] _axes;
    private readonly TextBlock _summary = new() { FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _messages = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _active = new() { Opacity = 0.8, TextWrapping = TextWrapping.Wrap };
    private bool _loading;

    /// <summary>Lista de perfis guardada nas configurações.</summary>
    public Func<List<MachineProfile>>? ProfilesSource { get; set; }

    public Func<string>? ActiveSource { get; set; }

    public Action<string>? SetActive { get; set; }

    public Action? Save { get; set; }

    private List<MachineProfile> Profiles => ProfilesSource?.Invoke() ?? [];

    public MachineProfileView()
    {
        foreach (string p in Presets)
        {
            _preset.Items.Add(new ComboBoxItem { Content = p });
        }

        _preset.SelectedIndex = 3;
        _controller.Items.Add(new ComboBoxItem { Content = "Fanuc" });
        _controller.Items.Add(new ComboBoxItem { Content = "Siemens" });
        _controller.SelectedIndex = 0;
        _mode.Items.Add(new ComboBoxItem { Content = "Calculado pelo CAM (serve em qualquer máquina)" });
        _mode.Items.Add(new ComboBoxItem { Content = "Função da máquina (G68.2 / CYCLE800 / TCP)" });
        _mode.SelectedIndex = 0;
        _axes = [NewAxis(), NewAxis()];

        var root = new StackPanel { Margin = new Thickness(20, 16), Spacing = 8, MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left };
        root.Children.Add(new TextBlock { Text = "Perfil de máquina 4 e 5 eixos", FontSize = 20, FontWeight = FontWeight.Bold });
        root.Children.Add(new TextBlock
        {
            Text = "Descreva a máquina: o programador escolhe quais eixos rotativos existem e se cada um fica na mesa ou no cabeçote. " +
                   "Nada é suposto pelo modelo da máquina. Esta etapa só guarda o perfil; a geração do programa vem nas próximas.",
            Opacity = 0.75, TextWrapping = TextWrapping.Wrap,
        });

        root.Children.Add(Label("Perfis salvos"));
        root.Children.Add(_profiles);
        var presetRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        presetRow.Children.Add(new TextBlock { Text = "Novo a partir de:", VerticalAlignment = VerticalAlignment.Center });
        _preset.MinWidth = 300;
        presetRow.Children.Add(_preset);
        Button newButton = Btn("Novo");
        Button copyButton = Btn("Duplicar");
        Button deleteButton = Btn("Excluir");
        presetRow.Children.Add(newButton);
        presetRow.Children.Add(copyButton);
        presetRow.Children.Add(deleteButton);
        root.Children.Add(presetRow);

        root.Children.Add(Label("Dados do perfil"));
        root.Children.Add(Row("Nome", _name));
        root.Children.Add(Row("Comando", _controller));
        root.Children.Add(Row("Modo de saída", _mode));

        root.Children.Add(Label("Eixos rotativos (ordem: do lado da máquina para o lado da peça)"));
        for (int i = 0; i < _axes.Length; i++)
        {
            root.Children.Add(AxisPanel(i + 1, _axes[i]));
        }

        root.Children.Add(_summary);
        root.Children.Add(_messages);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button saveButton = Btn("Salvar perfil");
        saveButton.FontWeight = FontWeight.SemiBold;
        Button activeButton = Btn("Usar este perfil");
        actions.Children.Add(saveButton);
        actions.Children.Add(activeButton);
        root.Children.Add(actions);
        root.Children.Add(_active);
        Content = new ScrollViewer { Content = root, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };

        _profiles.SelectionChanged += (_, _) => LoadSelected();
        newButton.Click += (_, _) => NewProfile();
        copyButton.Click += (_, _) => DuplicateProfile();
        deleteButton.Click += (_, _) => DeleteProfile();
        saveButton.Click += (_, _) => SaveProfile();
        activeButton.Click += (_, _) => UseProfile();
        foreach (AxisEditor a in _axes)
        {
            a.Use.IsCheckedChanged += (_, _) => Preview();
            a.Letter.SelectionChanged += (_, _) => Preview();
            a.Carrier.SelectionChanged += (_, _) => Preview();
        }

        _controller.SelectionChanged += (_, _) => Preview();
        _mode.SelectionChanged += (_, _) => Preview();
        LoadDraft(MachineProfile.FiveAxisMixed());
    }

    /// <summary>Recarrega a lista (chamada quando a tela é mostrada).</summary>
    public void Refresh()
    {
        string selected = (_profiles.SelectedItem as ComboBoxItem)?.Content as string ?? "";
        FillProfiles(selected);
        ShowActive();
    }

    // ---------------------------------------------------------------- construção

    private static TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeight.Bold, Margin = new Thickness(0, 8, 0, 0) };

    private static Button Btn(string text) => new() { Content = text, Padding = new Thickness(12, 6) };

    private static Control Row(string label, Control input)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock { Text = label, Width = 130, VerticalAlignment = VerticalAlignment.Center });
        if (input is TextBox or ComboBox { MinWidth: > 0 } && input.Width is double.NaN)
        {
            input.Width = 300;
        }

        row.Children.Add(input);
        return row;
    }

    private static TextBox Num(double value) => new() { Text = value.ToString("0.###", Br), Width = 80, MinHeight = 26 };

    private static AxisEditor NewAxis()
    {
        var letter = new ComboBox { Width = 70 };
        foreach (string l in new[] { "A", "B", "C" })
        {
            letter.Items.Add(new ComboBoxItem { Content = l });
        }

        letter.SelectedIndex = 0;
        var carrier = new ComboBox { Width = 130 };
        carrier.Items.Add(new ComboBoxItem { Content = "Mesa" });
        carrier.Items.Add(new ComboBoxItem { Content = "Cabeçote" });
        carrier.SelectedIndex = 0;
        return new AxisEditor
        {
            Use = new CheckBox { Content = "Usar" },
            Letter = letter,
            Carrier = carrier,
            Min = Num(-120),
            Max = Num(120),
            Reversed = new CheckBox { Content = "Sentido invertido" },
            PivotX = Num(0),
            PivotY = Num(0),
            PivotZ = Num(0),
        };
    }

    private static Control AxisPanel(int index, AxisEditor a)
    {
        var line1 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        line1.Children.Add(new TextBlock { Text = $"Eixo {index}", FontWeight = FontWeight.SemiBold, Width = 50, VerticalAlignment = VerticalAlignment.Center });
        line1.Children.Add(a.Use);
        line1.Children.Add(new TextBlock { Text = "Letra", VerticalAlignment = VerticalAlignment.Center });
        line1.Children.Add(a.Letter);
        line1.Children.Add(new TextBlock { Text = "Fica na", VerticalAlignment = VerticalAlignment.Center });
        line1.Children.Add(a.Carrier);
        var line2 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        line2.Children.Add(new TextBlock { Text = "Limites (°)", VerticalAlignment = VerticalAlignment.Center });
        line2.Children.Add(a.Min);
        line2.Children.Add(new TextBlock { Text = "a", VerticalAlignment = VerticalAlignment.Center });
        line2.Children.Add(a.Max);
        line2.Children.Add(a.Reversed);
        var line3 = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        line3.Children.Add(new TextBlock { Text = "Pivô (mm) X", VerticalAlignment = VerticalAlignment.Center });
        line3.Children.Add(a.PivotX);
        line3.Children.Add(new TextBlock { Text = "Y", VerticalAlignment = VerticalAlignment.Center });
        line3.Children.Add(a.PivotY);
        line3.Children.Add(new TextBlock { Text = "Z", VerticalAlignment = VerticalAlignment.Center });
        line3.Children.Add(a.PivotZ);
        var box = new StackPanel { Spacing = 4 };
        box.Children.Add(line1);
        box.Children.Add(line2);
        box.Children.Add(line3);
        return new Border
        {
            Child = box,
            Padding = new Thickness(10),
            CornerRadius = new CornerRadius(6),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.Parse("#334455")),
        };
    }

    // ---------------------------------------------------------------- dados

    private static double Parse(TextBox box) =>
        double.TryParse((box.Text ?? "").Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;

    private static string Pick(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Content as string ?? "";

    private MachineProfile ReadDraft(out string? inputError)
    {
        inputError = null;
        var profile = new MachineProfile
        {
            Name = (_name.Text ?? "").Trim(),
            Controller = _controller.SelectedIndex == 1 ? CamDialect.Siemens : CamDialect.Fanuc,
            Mode = _mode.SelectedIndex == 1 ? RotaryOutputMode.Native : RotaryOutputMode.Calculated,
        };
        for (int i = 0; i < _axes.Length; i++)
        {
            AxisEditor a = _axes[i];
            if (a.Use.IsChecked != true)
            {
                continue;
            }

            double[] values = [Parse(a.Min), Parse(a.Max), Parse(a.PivotX), Parse(a.PivotY), Parse(a.PivotZ)];
            if (values.Any(double.IsNaN))
            {
                inputError = $"Eixo {i + 1}: há um campo numérico inválido.";
            }

            profile.Axes.Add(new RotaryAxisConfig
            {
                Letter = Pick(a.Letter),
                Carrier = a.Carrier.SelectedIndex == 1 ? RotaryCarrier.Head : RotaryCarrier.Table,
                MinAngle = values[0],
                MaxAngle = values[1],
                Reversed = a.Reversed.IsChecked == true,
                PivotX = values[2],
                PivotY = values[3],
                PivotZ = values[4],
            });
        }

        return profile;
    }

    private void LoadDraft(MachineProfile profile)
    {
        _loading = true;
        _name.Text = profile.Name;
        _controller.SelectedIndex = profile.Controller == CamDialect.Siemens ? 1 : 0;
        _mode.SelectedIndex = profile.Mode == RotaryOutputMode.Native ? 1 : 0;
        for (int i = 0; i < _axes.Length; i++)
        {
            AxisEditor a = _axes[i];
            RotaryAxisConfig? config = i < profile.Axes.Count ? profile.Axes[i] : null;
            a.Use.IsChecked = config is not null;
            if (config is null)
            {
                continue;
            }

            a.Letter.SelectedIndex = config.Letter switch { "B" => 1, "C" => 2, _ => 0 };
            a.Carrier.SelectedIndex = config.Carrier == RotaryCarrier.Head ? 1 : 0;
            a.Min.Text = config.MinAngle.ToString("0.###", Br);
            a.Max.Text = config.MaxAngle.ToString("0.###", Br);
            a.Reversed.IsChecked = config.Reversed;
            a.PivotX.Text = config.PivotX.ToString("0.###", Br);
            a.PivotY.Text = config.PivotY.ToString("0.###", Br);
            a.PivotZ.Text = config.PivotZ.ToString("0.###", Br);
        }

        _loading = false;
        Preview();
    }

    private void Preview()
    {
        if (_loading)
        {
            return;
        }

        MachineProfile draft = ReadDraft(out string? inputError);
        _summary.Text = draft.Describe();
        var lines = new List<string>();
        if (inputError is not null)
        {
            lines.Add("• " + inputError);
        }

        lines.AddRange(draft.Validate().Select(e => "• " + e));
        lines.AddRange(draft.Warnings().Select(w => "⚠ " + w));
        _messages.Text = string.Join("\n", lines);
        _messages.Foreground = inputError is not null || draft.Validate().Count > 0 ? new SolidColorBrush(Color.Parse("#FF9A83")) : null;
    }

    // ---------------------------------------------------------------- lista de perfis

    private void FillProfiles(string select)
    {
        _loading = true;
        _profiles.Items.Clear();
        foreach (MachineProfile p in Profiles)
        {
            _profiles.Items.Add(new ComboBoxItem { Content = p.Name });
        }

        int index = Profiles.FindIndex(p => p.Name == select);
        _profiles.SelectedIndex = index >= 0 ? index : (Profiles.Count > 0 ? 0 : -1);
        _loading = false;
        if (_profiles.SelectedIndex >= 0)
        {
            LoadDraft(Profiles[_profiles.SelectedIndex].Clone());
        }
    }

    private void LoadSelected()
    {
        if (_loading || _profiles.SelectedIndex < 0 || _profiles.SelectedIndex >= Profiles.Count)
        {
            return;
        }

        LoadDraft(Profiles[_profiles.SelectedIndex].Clone());
    }

    private void NewProfile()
    {
        MachineProfile profile = _preset.SelectedIndex switch
        {
            0 => MachineProfile.ThreeAxis(),
            1 => MachineProfile.FourAxisTableA(),
            2 => MachineProfile.FiveAxisTableTable(),
            _ => MachineProfile.FiveAxisMixed(),
        };
        profile.Name = UniqueName(profile.Name);
        LoadDraft(profile);
        _profiles.SelectedIndex = -1;
        _messages.Text = "Perfil novo: ajuste os dados e clique em Salvar perfil.";
    }

    private void DuplicateProfile()
    {
        MachineProfile draft = ReadDraft(out _);
        draft.Name = UniqueName(draft.Name + " (cópia)");
        LoadDraft(draft);
        _profiles.SelectedIndex = -1;
    }

    private string UniqueName(string baseName)
    {
        string name = baseName;
        for (int i = 2; Profiles.Any(p => p.Name == name); i++)
        {
            name = $"{baseName} {i}";
        }

        return name;
    }

    private void SaveProfile()
    {
        MachineProfile draft = ReadDraft(out string? inputError);
        List<string> errors = draft.Validate();
        if (inputError is not null)
        {
            errors.Insert(0, inputError);
        }

        if (errors.Count > 0)
        {
            _messages.Text = string.Join("\n", errors.Select(e => "• " + e));
            _messages.Foreground = new SolidColorBrush(Color.Parse("#FF9A83"));
            return;
        }

        List<MachineProfile> all = Profiles;
        int index = _profiles.SelectedIndex;
        string? previousName = index >= 0 && index < all.Count ? all[index].Name : null;
        int sameName = all.FindIndex(p => p.Name == draft.Name);
        if (sameName >= 0 && sameName != index)
        {
            _messages.Text = "• Já existe um perfil com esse nome. Escolha outro nome.";
            _messages.Foreground = new SolidColorBrush(Color.Parse("#FF9A83"));
            return;
        }

        if (index >= 0 && index < all.Count)
        {
            all[index] = draft;
            if (previousName is not null && ActiveSource?.Invoke() == previousName)
            {
                SetActive?.Invoke(draft.Name);
            }
        }
        else
        {
            all.Add(draft);
        }

        Save?.Invoke();
        FillProfiles(draft.Name);
        ShowActive();
        _messages.Text = "Perfil salvo.";
        _messages.Foreground = null;
    }

    private void DeleteProfile()
    {
        int index = _profiles.SelectedIndex;
        List<MachineProfile> all = Profiles;
        if (index < 0 || index >= all.Count)
        {
            return;
        }

        string name = all[index].Name;
        all.RemoveAt(index);
        if (ActiveSource?.Invoke() == name)
        {
            SetActive?.Invoke("");
        }

        Save?.Invoke();
        FillProfiles("");
        if (all.Count == 0)
        {
            LoadDraft(MachineProfile.FiveAxisMixed());
        }

        ShowActive();
    }

    private void UseProfile()
    {
        int index = _profiles.SelectedIndex;
        if (index < 0 || index >= Profiles.Count)
        {
            _messages.Text = "Salve o perfil primeiro para usá-lo.";
            return;
        }

        SetActive?.Invoke(Profiles[index].Name);
        Save?.Invoke();
        ShowActive();
    }

    private void ShowActive()
    {
        string active = ActiveSource?.Invoke() ?? "";
        MachineProfile? profile = Profiles.Find(p => p.Name == active);
        _active.Text = profile is null
            ? "Nenhum perfil em uso."
            : $"Perfil em uso: {profile.Name} ({profile.Describe()}).";
    }
}
