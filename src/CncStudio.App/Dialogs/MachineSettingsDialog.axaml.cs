using CncStudio.Core.Setup;
using Avalonia.Controls;
using Avalonia.Layout;
using CncStudio.App.Settings;

namespace CncStudio.App.Dialogs;

/// <summary>
/// Edita ferramentas, zeros de peça e opções do programa sobre uma CÓPIA das configurações.
/// Fecha com a cópia editada (Salvar) ou null (Cancelar).
/// </summary>
public partial class MachineSettingsDialog : Window
{
    private readonly AppSettings _settings;
    private readonly IReadOnlyList<int> _toolsInProgram;

    // Linhas da tabela de ferramentas: cada uma liga os controles à entrada
    private readonly List<(ToolEntry Entry, Control Row)> _toolRows = [];

    // Construtor sem parâmetros exigido pelo carregador de XAML
    public MachineSettingsDialog()
        : this(new AppSettings(), [])
    {
    }

    public MachineSettingsDialog(AppSettings settings, IReadOnlyList<int> toolsInProgram)
    {
        InitializeComponent();
        _settings = settings.Clone();
        _toolsInProgram = toolsInProgram;

        DefaultDiameterInput.Value = (decimal)_settings.DefaultToolDiameter;
        foreach (ToolEntry tool in _settings.Tools.OrderBy(t => t.Number))
        {
            AddToolRow(tool);
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

        AddToolButton.Click += (_, _) =>
        {
            int next = _toolRows.Count == 0 ? 1 : _toolRows.Max(r => r.Entry.Number) + 1;
            AddToolRow(new ToolEntry { Number = next, Diameter = _settings.DefaultToolDiameter });
        };

        AddUsedToolsButton.IsEnabled = toolsInProgram.Count > 0;
        AddUsedToolsButton.Click += (_, _) =>
        {
            foreach (int tool in _toolsInProgram)
            {
                if (!_toolRows.Exists(r => r.Entry.Number == tool))
                {
                    AddToolRow(new ToolEntry { Number = tool, Diameter = _settings.DefaultToolDiameter });
                }
            }
        };

        OkButton.Click += (_, _) => Close(BuildResult());
        CancelButton.Click += (_, _) => Close(null);
    }

    private static readonly List<(string Code, string Label)> KindChoices =
    [
        ("", "Padrão (desbaste externo)"),
        (nameof(ToolKind.TurnInternal), "Torno · barra interna"),
        (nameof(ToolKind.PartExternal), "Torno · bedame externo"),
        (nameof(ToolKind.PartInternal), "Torno · bedame interno"),
        (nameof(ToolKind.ThreadExternal), "Torno · rosca externa"),
        (nameof(ToolKind.ThreadInternal), "Torno · rosca interna"),
    ];

    private static bool IsParting(string kind) => kind is nameof(ToolKind.PartExternal) or nameof(ToolKind.PartInternal);

    private void AddToolRow(ToolEntry entry)
    {
        var number = new NumericUpDown { Value = entry.Number, Minimum = 0, Maximum = 9999, Increment = 1, FormatString = "0", Margin = new(0, 0, 8, 0) };
        var diameter = new NumericUpDown { Value = (decimal)entry.Diameter, Minimum = 0.01m, Maximum = 500, Increment = 1, FormatString = "0.###", Margin = new(0, 0, 8, 0) };
        var description = new TextBox { Text = entry.Description, Watermark = "ex.: fresa de topo D10" };
        var kind = new ComboBox { ItemsSource = KindChoices.Select(k => k.Label).ToList(), Margin = new(0, 0, 8, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
        kind.SelectedIndex = Math.Max(0, KindChoices.FindIndex(k => k.Code == entry.Kind));
        ToolTip.SetTip(kind, "Tipo da ferramenta de torno (muda o desenho e o corte no simulador do torno)");
        var width = new NumericUpDown { Value = (decimal)entry.InsertWidth, Minimum = 0.2m, Maximum = 50, Increment = 0.5m, FormatString = "0.##", Margin = new(0, 0, 8, 0), IsVisible = IsParting(entry.Kind) };
        ToolTip.SetTip(width, "Largura do inserto do bedame (mm)");
        kind.SelectionChanged += (_, _) =>
        {
            entry.Kind = KindChoices[Math.Max(0, kind.SelectedIndex)].Code;
            width.IsVisible = IsParting(entry.Kind);
        };
        width.ValueChanged += (_, _) => entry.InsertWidth = (double)(width.Value ?? 3);
        var remove = new Button { Content = "✕", HorizontalAlignment = HorizontalAlignment.Right };
        ToolTip.SetTip(remove, "Remover");

        number.ValueChanged += (_, _) => entry.Number = (int)(number.Value ?? 0);
        diameter.ValueChanged += (_, _) => entry.Diameter = (double)(diameter.Value ?? 0);
        description.TextChanged += (_, _) => entry.Description = description.Text ?? "";

        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("90,130,210,80,*,40") };
        Grid.SetColumn(diameter, 1);
        Grid.SetColumn(kind, 2);
        Grid.SetColumn(width, 3);
        Grid.SetColumn(description, 4);
        Grid.SetColumn(remove, 5);
        row.Children.Add(number);
        row.Children.Add(diameter);
        row.Children.Add(kind);
        row.Children.Add(width);
        row.Children.Add(description);
        row.Children.Add(remove);

        remove.Click += (_, _) =>
        {
            ToolRows.Children.Remove(row);
            _toolRows.RemoveAll(r => r.Row == row);
        };

        ToolRows.Children.Add(row);
        _toolRows.Add((entry, row));
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

        // Uma entrada por número de ferramenta (a última vence), em ordem
        _settings.Tools = _toolRows
            .Select(r => r.Entry)
            .Where(t => t.Diameter > 0)
            .GroupBy(t => t.Number)
            .Select(g => g.Last())
            .OrderBy(t => t.Number)
            .ToList();

        return _settings;
    }
}
