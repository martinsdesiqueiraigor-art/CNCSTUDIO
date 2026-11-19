using CncStudio.Core.Setup;
using Avalonia.Controls;
using Avalonia.Layout;
using CncStudio.App.Settings;

namespace CncStudio.App.Dialogs;

/// <summary>
/// Edita o diâmetro padrão, zeros de peça e opções do programa sobre uma CÓPIA das configurações (as ferramentas ficam no banco, UX-04h).
/// Fecha com a cópia editada (Salvar) ou null (Cancelar).
/// </summary>
public partial class MachineSettingsDialog : Window
{
    private readonly AppSettings _settings;

    // Construtor sem parâmetros exigido pelo carregador de XAML
    public MachineSettingsDialog()
        : this(new AppSettings(), [])
    {
    }

    /// <summary>DESK-01: setup do programa com as medidas próprias editadas (só vale quando o diálogo foi salvo).</summary>
    public MachiningSetup? EditedSetup { get; }

    private readonly bool _lathe;

    private static readonly (ToolKind Kind, string Name)[] MillKinds =
    [
        (ToolKind.FlatEndMill, "Fresa de topo"), (ToolKind.BullNoseEndMill, "Fresa toroidal"), (ToolKind.BallEndMill, "Fresa esférica"),
        (ToolKind.Drill, "Broca"), (ToolKind.Tap, "Macho"), (ToolKind.FaceMill, "Fresa de facear"),
    ];

    private static readonly (ToolKind Kind, string Name)[] LatheKinds =
    [
        (ToolKind.TurnExternal, "Desbaste externo"), (ToolKind.TurnInternal, "Barra interna"), (ToolKind.PartExternal, "Bedame externo"),
        (ToolKind.PartInternal, "Bedame interno"), (ToolKind.ThreadExternal, "Rosca externa"), (ToolKind.ThreadInternal, "Rosca interna"),
    ];

    public MachineSettingsDialog(AppSettings settings, IReadOnlyList<int> toolsInProgram, MachiningSetup? setup = null,
        Func<int, string>? source = null, bool lathe = false)
    {
        InitializeComponent();
        _settings = settings.Clone();
        _lathe = lathe;
        EditedSetup = setup?.Clone();

        DefaultDiameterInput.Value = (decimal)_settings.DefaultToolDiameter;
        if (EditedSetup is { } s)
        {
            if (toolsInProgram.Count == 0)
            {
                ToolRows.Children.Add(new TextBlock { Text = "O programa não chama ferramenta (T).", Opacity = 0.7 });
            }

            foreach (int t in toolsInProgram.Distinct().OrderBy(t => t))
            {
                ToolRows.Children.Add(ToolRow(s, t, source?.Invoke(t) ?? ""));
            }
        }

        foreach (WorkOffsetEntry offset in _settings.WorkOffsets)
        {
            AddOffsetRow(offset);
        }

        IntegerThousandthsOption.IsChecked = _settings.FanucIntegersAsThousandths;
        IntegerMmOption.IsChecked = !_settings.FanucIntegersAsThousandths;
        RapidRateInput.Value = (decimal)_settings.RapidRate;
        LimitsEnabledCheck.IsChecked = _settings.TravelLimitsEnabled;
        MinXInput.Value = (decimal?)_settings.LimitMinX;
        MaxXInput.Value = (decimal?)_settings.LimitMaxX;
        MinYInput.Value = (decimal?)_settings.LimitMinY;
        MaxYInput.Value = (decimal?)_settings.LimitMaxY;
        MinZInput.Value = (decimal?)_settings.LimitMinZ;
        MaxZInput.Value = (decimal?)_settings.LimitMaxZ;
        MaxSpindleInput.Value = (decimal)_settings.MaxSpindleSpeed;

        OkButton.Click += (_, _) => Close(BuildResult());
        CancelButton.Click += (_, _) => Close(null);
    }

    /// <summary>
    /// Linha de uma ferramenta: T, de onde vem a medida e, marcada "Medida deste programa", tipo, Ø e raio (de canto na fresa, da
    /// ponta no torno) ou largura do bedame, gravados no setup deste programa.
    /// </summary>
    private Control ToolRow(MachiningSetup setup, int number, string source)
    {
        var panel = new StackPanel { Spacing = 4 };
        var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        head.Children.Add(new TextBlock { Text = $"T{number}", FontWeight = Avalonia.Media.FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, MinWidth = 40 });
        head.Children.Add(new TextBlock { Text = source, Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 460 });
        panel.Children.Add(head);

        SetupTool? own = setup.ToolOf(number);
        var check = new CheckBox { Content = "Medida deste programa", IsChecked = own is not null };
        panel.Children.Add(check);
        var fields = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new(24, 0, 0, 0) };
        panel.Children.Add(fields);

        (ToolKind Kind, string Name)[] kinds = _lathe ? LatheKinds : MillKinds;
        SetupTool tool = own ?? new SetupTool { Number = number, Kind = kinds[0].Kind, Diameter = _lathe ? 0 : 10 };
        var kind = new ComboBox { ItemsSource = kinds.Select(k => k.Name).ToArray(), SelectedIndex = Math.Max(0, Array.FindIndex(kinds, k => k.Kind == tool.Kind)), MinWidth = 170 };
        NumericUpDown Num(double v) => new() { Value = (decimal)v, Minimum = 0, Maximum = 1000, Increment = 0.1m, FormatString = "0.###", Width = 110 };
        var diameter = Num(tool.Diameter);
        var radius = Num(tool.CornerRadius);
        var width = Num(tool.InsertWidth);
        var diameterLabel = new TextBlock { Text = "Ø", VerticalAlignment = VerticalAlignment.Center };
        var radiusLabel = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        var widthLabel = new TextBlock { Text = "Largura", VerticalAlignment = VerticalAlignment.Center };
        fields.Children.Add(kind);
        fields.Children.Add(diameterLabel);
        fields.Children.Add(diameter);
        fields.Children.Add(radiusLabel);
        fields.Children.Add(radius);
        fields.Children.Add(widthLabel);
        fields.Children.Add(width);

        void Show()
        {
            bool on = check.IsChecked == true;
            fields.IsEnabled = on;
            fields.Opacity = on ? 1 : 0.5;
            ToolKind k = kinds[Math.Max(0, kind.SelectedIndex)].Kind;
            bool parting = k is ToolKind.PartExternal or ToolKind.PartInternal;
            bool corner = k is ToolKind.BullNoseEndMill or ToolKind.TurnExternal or ToolKind.TurnInternal;
            diameterLabel.IsVisible = diameter.IsVisible = !_lathe;
            radiusLabel.Text = _lathe ? "Raio da ponta" : "Raio de canto";
            radiusLabel.IsVisible = radius.IsVisible = corner;
            widthLabel.IsVisible = width.IsVisible = parting;
        }

        void Apply()
        {
            setup.Tools.RemoveAll(x => x.Number == number);
            if (check.IsChecked != true)
            {
                return;
            }

            tool.Kind = kinds[Math.Max(0, kind.SelectedIndex)].Kind;
            tool.Diameter = (double)(diameter.Value ?? 0);
            tool.CornerRadius = radius.IsVisible ? (double)(radius.Value ?? 0) : 0;
            tool.InsertWidth = width.IsVisible ? (double)(width.Value ?? (decimal)SetupTool.DefaultInsertWidth) : tool.InsertWidth;
            if (tool.Kind == ToolKind.BallEndMill)
            {
                tool.CornerRadius = tool.Diameter / 2;
            }

            setup.Tools.Add(tool);
        }

        check.IsCheckedChanged += (_, _) => { Show(); Apply(); };
        kind.SelectionChanged += (_, _) => { Show(); Apply(); };
        diameter.ValueChanged += (_, _) => Apply();
        radius.ValueChanged += (_, _) => Apply();
        width.ValueChanged += (_, _) => Apply();
        Show();
        return panel;
    }

    private void AddOffsetRow(WorkOffsetEntry offset)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("70,*,*,*") };
        row.Children.Add(new TextBlock { Text = $"G{offset.Code}", VerticalAlignment = VerticalAlignment.Center });

        NumericUpDown Axis(int column, double value, Action<double> set)
        {
            var input = new NumericUpDown { Value = (decimal)value, Minimum = -100000, Maximum = 100000, Increment = 1, FormatString = "0.###", Margin = new(0, 0, 8, 0) };
            input.ValueChanged += (_, _) => set((double)(input.Value ?? 0));
            Grid.SetColumn(input, column);
            return input;
        }

        row.Children.Add(Axis(1, offset.X, v => offset.X = v));
        row.Children.Add(Axis(2, offset.Y, v => offset.Y = v));
        row.Children.Add(Axis(3, offset.Z, v => offset.Z = v));
        OffsetRows.Children.Add(row);
    }

    private AppSettings BuildResult()
    {
        _settings.DefaultToolDiameter = (double)(DefaultDiameterInput.Value ?? 10);
        _settings.FanucIntegersAsThousandths = IntegerThousandthsOption.IsChecked == true;
        _settings.RapidRate = (double)(RapidRateInput.Value ?? 10_000);
        _settings.TravelLimitsEnabled = LimitsEnabledCheck.IsChecked == true;
        _settings.LimitMinX = (double?)MinXInput.Value;
        _settings.LimitMaxX = (double?)MaxXInput.Value;
        _settings.LimitMinY = (double?)MinYInput.Value;
        _settings.LimitMaxY = (double?)MaxYInput.Value;
        _settings.LimitMinZ = (double?)MinZInput.Value;
        _settings.LimitMaxZ = (double?)MaxZInput.Value;
        _settings.MaxSpindleSpeed = (double)(MaxSpindleInput.Value ?? 4000);
        return _settings;
    }
}
