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
            if (parsed is null) { SetStatus("Setup inválido: confira os valores."); return; }
            Store(SetupKey, parsed.ToJson());
            _machiningSetup = parsed;
            Backplot.SetMachiningSetup(parsed);
            SetStatus("Setup salvo neste aparelho para " + parsed.ProjectKey);
            return;
        }
        var setup = _machiningSetup;
        var first = setup.Fixtures.FirstOrDefault();
        var stack = new StackPanel { Spacing = 9, Margin = new Thickness(16) };
        stack.Children.Add(new TextBlock { Text = "Setup de usinagem · visualização ilustrativa" });
        var fields = new Dictionary<string, TextBox>();
        void Field(string name, double value)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*") };
            row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
            var input = new TextBox { Text = value.ToString("0.###", BrazilianCulture.Instance), MinWidth = 110 };
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
        var fixtureSelect = new ComboBox { ItemsSource = new[] { "Sem fixação", "Morsa", "Grampo", "Placa de 3 castanhas" }, SelectedIndex = first is null ? 0 : (int)first.Kind + 1 };
        stack.Children.Add(new TextBlock { Text = "Fixação (representação simplificada)" });
        stack.Children.Add(fixtureSelect);
        Field("Fixação X", first?.X ?? -25);
        Field("Fixação Y", first?.Y ?? -15);
        Field("Fixação Z", first?.Z ?? -35);
        Field("Fixação largura", first?.Width ?? 150);
        Field("Fixação profund.", first?.Depth ?? 110);
        Field("Fixação altura", first?.Height ?? 30);
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
            // Aceita vírgula ou ponto decimal ("12,5" e "12.5")
            double Read(string name)
            {
                string text = (fields[name].Text ?? "").Trim().Replace(',', '.');
                if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                    throw new FormatException(name + ": número inválido.");
                return value;
            }
            try
            {
                // Parte de uma cópia do setup atual: o que este formulário não mostra
                // (tipo do bruto, diâmetros, tipo das ferramentas, mordentes) é preservado.
                var next = setup.Clone();
                next.ProjectKey = _libraryPath ?? _fileName;
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
                {
                    var kind = (SetupFixtureKind)(fixtureSelect.SelectedIndex - 1);
                    var fixture = next.Fixtures.FirstOrDefault() is { } existing && existing.Kind == kind
                        ? existing
                        : new SetupFixture { Kind = kind };
                    fixture.X = Read("Fixação X");
                    fixture.Y = Read("Fixação Y");
                    fixture.Z = Read("Fixação Z");
                    fixture.Width = Read("Fixação largura");
                    fixture.Depth = Read("Fixação profund.");
                    fixture.Height = Read("Fixação altura");
                    if (next.Fixtures.Count == 0) next.Fixtures.Add(fixture);
                    else next.Fixtures[0] = fixture;
                }
                else
                {
                    next.Fixtures.Clear();
                }
                if (!next.IsValid()) { error.Text = "Dimensões inválidas: use valores positivos."; return; }
                Store(SetupKey, next.ToJson());
                _machiningSetup = next;
                Backplot.SetMachiningSetup(next);
                SetStatus("Setup salvo neste aparelho para " + next.ProjectKey);
                CloseSetup();
            }
            catch (Exception ex) { error.Text = ex.Message; }
        };
        await Task.CompletedTask;
    }
}
