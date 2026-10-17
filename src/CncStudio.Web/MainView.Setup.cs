using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace CncStudio.Web;

public partial class MainView
{
    private MachiningSetup _machiningSetup = new();
    private Border? _setupOverlay;
    private string SetupKey => "cc.setup.v1." + Uri.EscapeDataString(_libraryPath ?? _fileName);

    private void LoadMachiningSetup()
    {
        var key = _libraryPath ?? _fileName;
        _machiningSetup = MachiningSetup.Parse(Stored(SetupKey) ?? "") ??
            MachiningSetup.CreateDefault(key);
        Backplot.SetMachiningSetup(_machiningSetup);
    }

    private async Task EditMachiningSetupAsync()
    {
        if (_setupOverlay is not null) return;
        if (_touch)
        {
            var edited = await WebInterop.EditMachiningSetupAsync(_machiningSetup.ToJson());
            if (edited is null) return;
            var parsed = MachiningSetup.Parse(edited);
            if (parsed is null) { SetStatus("Setup invalido: confira os valores."); return; }
            Store(SetupKey, parsed.ToJson());
            _machiningSetup = parsed;
            Backplot.SetMachiningSetup(parsed);
            SetStatus("Setup salvo localmente para " + parsed.ProjectKey);
            return;
        }
        var setup = _machiningSetup;
        var stack = new StackPanel { Spacing = 9, Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = "Setup de Usinagem · visualizacao ilustrativa" });
        var fields = new Dictionary<string, TextBox>();
        void Field(string name, double value)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*") };
            row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            var input = new TextBox { Text = value.ToString(CultureInfo.InvariantCulture), MinWidth = 110 };
            Grid.SetColumn(input, 1);
            row.Children.Add(input);
            stack.Children.Add(row);
            fields[name] = input;
        }
        Field("Bruto X (mm)", setup.Stock.X);
        Field("Bruto Y (mm)", setup.Stock.Y);
        Field("Bruto Z (mm)", setup.Stock.Z);
        Field("Largura (mm)", setup.Stock.Width);
        Field("Profundidade (mm)", setup.Stock.Depth);
        Field("Altura (mm)", setup.Stock.Height);
        Field("G54 X (mm)", setup.G54.X);
        Field("G54 Y (mm)", setup.G54.Y);
        Field("G54 Z (mm)", setup.G54.Z);
        var fixtureSelect = new ComboBox { ItemsSource = new[] { "Sem fixacao", "Morsa", "Grampo", "Placa 3 castanhas" }, SelectedIndex = setup.Fixtures.Count == 0 ? 0 : (int)setup.Fixtures[0].Kind + 1 };
        stack.Children.Add(new TextBlock { Text = "Fixacao (representacao simplificada)" });
        stack.Children.Add(fixtureSelect);
        Field("Fixacao X", setup.Fixtures.FirstOrDefault()?.X ?? -25);
        Field("Fixacao Y", setup.Fixtures.FirstOrDefault()?.Y ?? -15);
        Field("Fixacao Z", setup.Fixtures.FirstOrDefault()?.Z ?? -35);
        Field("Fixacao largura", setup.Fixtures.FirstOrDefault()?.Width ?? 150);
        Field("Fixacao profund.", setup.Fixtures.FirstOrDefault()?.Depth ?? 110);
        Field("Fixacao altura", setup.Fixtures.FirstOrDefault()?.Height ?? 30);
        var error = new TextBlock { Text = "", Foreground = Avalonia.Media.Brushes.OrangeRed };
        stack.Children.Add(error);
        var save = new Button { Content = "Salvar setup", HorizontalAlignment = HorizontalAlignment.Stretch };
        stack.Children.Add(save);
        var cancel = new Button { Content = "Fechar sem salvar", HorizontalAlignment = HorizontalAlignment.Stretch };
        stack.Children.Add(cancel);
        _setupOverlay = new Border {
            Background = Avalonia.Media.Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = new ScrollViewer { Content = stack }
        };
        Grid.SetRow(_setupOverlay, 0);
        _setupOverlay.ZIndex = 200;
        GraphHost.Children.Add(_setupOverlay);
        void CloseSetup()
        {
            if (_setupOverlay is not null) GraphHost.Children.Remove(_setupOverlay);
            _setupOverlay = null;
        }
        cancel.Click += (_, _) => CloseSetup();
        save.Click += (_, _) =>
        {
            double Read(string name)
            {
                if (!double.TryParse(fields[name].Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    throw new FormatException(name + ": use ponto decimal.");
                return value;
            }
            try
            {
                var next = MachiningSetup.CreateDefault(_libraryPath ?? _fileName);
                next.Stock.X = Read("Bruto X (mm)");
                next.Stock.Y = Read("Bruto Y (mm)");
                next.Stock.Z = Read("Bruto Z (mm)");
                next.Stock.Width = Read("Largura (mm)");
                next.Stock.Depth = Read("Profundidade (mm)");
                next.Stock.Height = Read("Altura (mm)");
                next.G54.X = Read("G54 X (mm)");
                next.G54.Y = Read("G54 Y (mm)");
                next.G54.Z = Read("G54 Z (mm)");
                if (fixtureSelect.SelectedIndex > 0)
                    next.Fixtures.Add(new SetupFixture {
                        Kind = (SetupFixtureKind)(fixtureSelect.SelectedIndex - 1),
                        X = Read("Fixacao X"), Y = Read("Fixacao Y"), Z = Read("Fixacao Z"),
                        Width = Read("Fixacao largura"), Depth = Read("Fixacao profund."),
                        Height = Read("Fixacao altura") });
                next.Tools = setup.Tools.Select(t => new SetupToolGeometry { Number = t.Number, Diameter = t.Diameter, CuttingLength = t.CuttingLength, HolderDiameter = t.HolderDiameter, HolderLength = t.HolderLength }).Take(20).ToList();
                if (!next.IsValid()) { error.Text = "Dimensoes invalidas. Valores positivos obrigatorios."; return; }
                Store(SetupKey, next.ToJson());
                _machiningSetup = next;
                Backplot.SetMachiningSetup(next);
                SetStatus("Setup salvo localmente para " + next.ProjectKey);
                CloseSetup();
            }
            catch (Exception ex) { error.Text = ex.Message; }
        };
        await Task.CompletedTask;
    }
}
