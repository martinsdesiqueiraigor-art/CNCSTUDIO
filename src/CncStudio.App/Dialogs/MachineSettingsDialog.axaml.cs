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

    public MachineSettingsDialog(AppSettings settings, IReadOnlyList<int> toolsInProgram)
    {
        InitializeComponent();
        _settings = settings.Clone();

        DefaultDiameterInput.Value = (decimal)_settings.DefaultToolDiameter;

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
