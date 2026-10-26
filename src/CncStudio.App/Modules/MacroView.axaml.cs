using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CncStudio.Core.Generators;

namespace CncStudio.App.Modules;

/// <summary>
/// Casillas Macro no desktop: formulário de cada gerador (Core/Generators), programa pronto ou G65 + macro,
/// e "Abrir no editor" para simular no Code. O cálculo é todo do Core.
/// </summary>
public partial class MacroView : UserControl
{
    private ProgramGenerator? _generator;
    private readonly Dictionary<string, TextBox> _numbers = [];
    private readonly Dictionary<string, TextBox> _texts = [];
    private string _program = "";

    /// <summary>Abre um texto numa aba nova do editor (nome sugerido).</summary>
    public Action<string, string>? OpenInEditor { get; set; }

    /// <summary>Copia texto para a área de transferência.</summary>
    public Func<string, Task>? CopyText { get; set; }

    public MacroView()
    {
        InitializeComponent();
        foreach (ProgramGenerator g in Generators.All)
        {
            GeneratorList.Items.Add(new ListBoxItem { Content = g.Title, Tag = g });
        }

        GeneratorList.SelectionChanged += (_, _) =>
        {
            if (GeneratorList.SelectedItem is ListBoxItem { Tag: ProgramGenerator g })
            {
                Select(g);
            }
        };
        GenerateButton.Click += (_, _) => Generate();
        OpenButton.Click += (_, _) =>
        {
            if (_generator is not null && _program.Length > 0)
            {
                OpenInEditor?.Invoke(_program, _generator.Id + ".nc");
            }
        };
        CopyButton.Click += async (_, _) =>
        {
            if (_program.Length > 0 && CopyText is not null)
            {
                await CopyText(_program);
            }
        };
        if (GeneratorList.Items.Count > 0)
        {
            GeneratorList.SelectedIndex = 0;
        }
    }

    private void Select(ProgramGenerator g)
    {
        _generator = g;
        _program = "";
        Title.Text = g.Title;
        Description.Text = g.Description;
        Output.Text = "";
        Errors.Text = "";
        OpenButton.IsEnabled = false;
        CopyButton.IsEnabled = false;
        ModePanel.IsVisible = g.SupportsMacro;
        ModeCombo.SelectedIndex = 0;
        Fields.Children.Clear();
        _numbers.Clear();
        _texts.Clear();

        foreach (GeneratorParameter p in g.Parameters)
        {
            var box = new TextBox { Text = p.Default.ToString("0.###", CultureInfo.GetCultureInfo("pt-BR")), Width = 120, MinHeight = 28 };
            _numbers[p.Id] = box;
            Fields.Children.Add(Row(p.Label, p.Unit, p.Hint, box));
        }

        foreach (GeneratorTextParameter t in g.TextParameters)
        {
            var box = new TextBox { Text = t.Default, Width = 260, MinHeight = 28, MaxLength = t.MaxLength };
            _texts[t.Id] = box;
            Fields.Children.Add(Row(t.Label, "", t.Hint, box));
        }
    }

    private static Control Row(string label, string unit, string hint, TextBox box)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock { Text = label, Width = 280, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        row.Children.Add(box);
        row.Children.Add(new TextBlock { Text = unit, Width = 40, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
        if (hint.Length > 0)
        {
            ToolTip.SetTip(row, hint);
        }

        return row;
    }

    private static double ParseNumber(string? text)
    {
        string t = (text ?? "").Trim().Replace(',', '.');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
    }

    private void Generate()
    {
        if (_generator is null)
        {
            return;
        }

        var values = _numbers.ToDictionary(kv => kv.Key, kv => ParseNumber(kv.Value.Text));
        var texts = _texts.ToDictionary(kv => kv.Key, kv => kv.Value.Text ?? "");
        GeneratorOutput mode = _generator.SupportsMacro && ModeCombo.SelectedIndex == 1 ? GeneratorOutput.MacroCall : GeneratorOutput.Plain;
        GeneratorResult result = _generator.Generate(values, texts, mode);
        if (!result.Ok)
        {
            _program = "";
            Output.Text = "";
            Errors.Text = string.Join("\n", result.Errors.Select(e => "• " + e));
            OpenButton.IsEnabled = false;
            CopyButton.IsEnabled = false;
            return;
        }

        _program = result.Program;
        Errors.Text = "";
        Output.Text = result.Program;
        OpenButton.IsEnabled = true;
        CopyButton.IsEnabled = true;
    }
}
