using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CncStudio.Core.Cam;

namespace CncStudio.App.Modules;

/// <summary>
/// Casillas CAM no desktop: abre DXF (ou desenha uma forma), escolhe operações e parâmetros, mostra o percurso e
/// gera o programa Fanuc/Siemens. Todo o cálculo é do Core (<c>Core/Cam</c>); aqui só ficam o formulário e a prévia.
/// </summary>
public partial class CamView : UserControl
{
    private static readonly string[] OpNames =
    [
        "Contorno por fora", "Contorno por dentro", "Contorno sobre a linha", "Bolsão", "Faceamento", "Furação (círculos do desenho)",
    ];

    private readonly List<Contour> _contours = [];
    private readonly List<CamOperation> _ops = [];
    private readonly Dictionary<string, TextBox> _common = [];
    private readonly Dictionary<string, TextBox> _fields = [];
    private readonly Dictionary<string, TextBox> _shape = [];
    private CheckBox? _climb, _comp;
    private ComboBox? _cycle;
    private string _program = "";
    private string _baseName = "CAM";

    /// <summary>Abre o seletor de arquivo DXF e devolve nome e texto (null se cancelou).</summary>
    public Func<Task<(string Name, string Text)?>>? PickDxf { get; set; }

    /// <summary>Abre um texto numa aba nova do editor (texto, nome sugerido).</summary>
    public Action<string, string>? OpenInEditor { get; set; }

    public Func<string, Task>? CopyText { get; set; }

    public CamView()
    {
        InitializeComponent();
        AddCommon("T", "Ferramenta (T)", 1, "");
        AddCommon("D", "Diâmetro", 10, "mm");
        AddCommon("S", "Rotação", 3000, "rpm");
        AddCommon("F", "Avanço no plano", 500, "mm/min");
        AddCommon("FZ", "Avanço de descida", 200, "mm/min");
        AddCommon("TOP", "Z topo da peça", 0, "mm");
        AddCommon("BOT", "Z fundo", -5, "mm");
        AddCommon("Q", "Passe em Z", 2.5, "mm");
        AddCommon("SAFE", "Z seguro (rápido)", 50, "mm");
        AddCommon("CLR", "Z de aproximação", 2, "mm");

        AddShapeRow("Retângulo (X, Y, largura, altura)", ["RX", "RY", "RW", "RH"], [0, 0, 60, 40], "Adicionar retângulo", AddRectangle);
        AddShapeRow("Círculo (centro X, Y, diâmetro)", ["CX", "CY", "CD"], [30, 20, 20], "Adicionar círculo", AddCircle);

        foreach (string name in OpNames)
        {
            OpCombo.Items.Add(new ComboBoxItem { Content = name });
        }

        OpCombo.SelectionChanged += (_, _) => BuildOpFields();
        OpCombo.SelectedIndex = 0;
        OpenDxfButton.Click += async (_, _) => await OpenDxf();
        AddOpButton.Click += (_, _) => AddOperation();
        RemoveOpButton.Click += (_, _) =>
        {
            if (OpList.SelectedIndex >= 0)
            {
                _ops.RemoveAt(OpList.SelectedIndex);
                RefreshOps();
            }
        };
        GenerateButton.Click += (_, _) => Generate();
        ContourList.SelectionChanged += (_, _) => RefreshPreview();
        OpList.SelectionChanged += (_, _) => RefreshPreview();
        OpenButton.Click += (_, _) =>
        {
            if (_program.Length > 0)
            {
                OpenInEditor?.Invoke(_program, _baseName + ".nc");
            }
        };
        CopyButton.Click += async (_, _) =>
        {
            if (_program.Length > 0 && CopyText is not null)
            {
                await CopyText(_program);
            }
        };
        RefreshPreview();
    }

    // ---------------------------------------------------------------- formulário

    private static TextBox Box(double value) =>
        new() { Text = value.ToString("0.###", CultureInfo.GetCultureInfo("pt-BR")), Width = 90, MinHeight = 26 };

    private static Control Row(string label, TextBox box, string unit)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock { Text = label, Width = 170, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        row.Children.Add(box);
        row.Children.Add(new TextBlock { Text = unit, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
        return row;
    }

    private void AddCommon(string id, string label, double value, string unit)
    {
        TextBox box = Box(value);
        _common[id] = box;
        CommonFields.Children.Add(Row(label, box, unit));
    }

    private void AddShapeRow(string title, string[] ids, double[] defaults, string button, Action click)
    {
        ShapeFields.Children.Add(new TextBlock { Text = title, Opacity = 0.8, Margin = new Thickness(0, 4, 0, 0) });
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        for (int i = 0; i < ids.Length; i++)
        {
            TextBox box = Box(defaults[i]);
            box.Width = 62;
            _shape[ids[i]] = box;
            line.Children.Add(box);
        }

        ShapeFields.Children.Add(line);
        var add = new Button { Content = button, Padding = new Thickness(10, 5) };
        add.Click += (_, _) => click();
        ShapeFields.Children.Add(add);
    }

    private void BuildOpFields()
    {
        OpFields.Children.Clear();
        _fields.Clear();
        _climb = _comp = null;
        _cycle = null;
        int kind = Math.Max(0, OpCombo.SelectedIndex);
        void Num(string id, string label, double value, string unit)
        {
            TextBox box = Box(value);
            _fields[id] = box;
            OpFields.Children.Add(Row(label, box, unit));
        }

        if (kind <= 2)
        {
            _climb = new CheckBox { Content = "Corte concordante (recomendado)", IsChecked = true };
            _comp = new CheckBox { Content = "Compensação no controle (G41/G42)", IsChecked = false };
            OpFields.Children.Add(_climb);
            OpFields.Children.Add(_comp);
            Num("ALLOW", "Sobremetal", 0, "mm");
            Num("LEAD", "Raio de entrada e saída", 3, "mm");
        }
        else if (kind == 3)
        {
            Num("STEP", "Passo lateral", 60, "% do diâmetro");
            Num("FIN", "Sobremetal de acabamento", 0, "mm");
            Num("LEAD", "Raio de entrada do acabamento", 2, "mm");
        }
        else if (kind == 4)
        {
            Num("STEP", "Passo lateral", 70, "% do diâmetro");
        }
        else
        {
            _cycle = new ComboBox { SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
            foreach (string c in new[] { "Furação simples (G81)", "Com tempo no fundo (G82)", "Com bicada (G83)", "Quebra-cavaco (G73)" })
            {
                _cycle.Items.Add(new ComboBoxItem { Content = c });
            }

            OpFields.Children.Add(_cycle);
            Num("PECK", "Bicada (Q)", 3, "mm");
            Num("DWELL", "Tempo no fundo", 0.5, "s");
        }
    }

    private static double Parse(TextBox box)
    {
        string t = (box.Text ?? "").Trim().Replace(',', '.');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
    }

    private double C(string id) => Parse(_common[id]);

    private double F(string id) => Parse(_fields[id]);

    // ---------------------------------------------------------------- desenho

    private async Task OpenDxf()
    {
        if (PickDxf is null || await PickDxf() is not { } file)
        {
            return;
        }

        DxfImportResult r = DxfImporter.Parse(file.Text);
        _contours.Clear();
        _contours.AddRange(r.Contours);
        _baseName = Path.GetFileNameWithoutExtension(file.Name);
        FileName.Text = file.Name;
        Errors.Text = r.Warnings.Count > 0 ? string.Join("\n", r.Warnings.Select(w => "• " + w)) : "";
        RefreshContours();
    }

    private bool Shape(out double[] v, params string[] ids)
    {
        v = ids.Select(i => Parse(_shape[i])).ToArray();
        return v.All(d => !double.IsNaN(d));
    }

    private void AddRectangle()
    {
        if (!Shape(out double[] v, "RX", "RY", "RW", "RH") || v[2] <= 0 || v[3] <= 0)
        {
            Errors.Text = "• Retângulo: informe X, Y e largura e altura maiores que zero.";
            return;
        }

        _contours.Add(Contour.Rectangle(v[0], v[1], v[2], v[3]));
        Errors.Text = "";
        RefreshContours();
    }

    private void AddCircle()
    {
        if (!Shape(out double[] v, "CX", "CY", "CD") || v[2] <= 0)
        {
            Errors.Text = "• Círculo: informe o centro e um diâmetro maior que zero.";
            return;
        }

        _contours.Add(Contour.Circle(new Point2(v[0], v[1]), v[2] / 2));
        Errors.Text = "";
        RefreshContours();
    }

    private static string Describe(Contour c, int index)
    {
        (double x0, double y0, double x1, double y1) = c.BoundsXY();
        string size = $"{(x1 - x0).ToString("0.##", CultureInfo.GetCultureInfo("pt-BR"))} × {(y1 - y0).ToString("0.##", CultureInfo.GetCultureInfo("pt-BR"))} mm";
        bool circle = c.Segments.Count == 1 && c.Segments[0].IsArc;
        return $"{index + 1} · {(circle ? "Círculo Ø" + (c.Segments[0].Radius * 2).ToString("0.##", CultureInfo.GetCultureInfo("pt-BR")) : "Contorno")} ({size})";
    }

    private void RefreshContours()
    {
        ContourList.Items.Clear();
        for (int i = 0; i < _contours.Count; i++)
        {
            ContourList.Items.Add(new ListBoxItem { Content = Describe(_contours[i], i) });
        }

        if (_contours.Count > 0 && ContourList.SelectedIndex < 0)
        {
            ContourList.SelectedIndex = 0;
        }

        RefreshPreview();
    }

    private void RefreshPreview()
    {
        List<List<Point2>> path = OpList.SelectedIndex >= 0 && OpList.SelectedIndex < _ops.Count
            ? ToolpathView.CuttingPolylines(_ops[OpList.SelectedIndex])
            : [];
        Preview.Set(_contours, ContourList.SelectedIndex, path);
        PreviewHint.IsVisible = _contours.Count == 0;
    }

    // ---------------------------------------------------------------- operações

    private void AddOperation()
    {
        Errors.Text = "";
        double[] common = _common.Values.Select(Parse).ToArray();
        if (common.Any(double.IsNaN) || _fields.Values.Any(b => double.IsNaN(Parse(b))))
        {
            Errors.Text = "• Há um campo com valor inválido.";
            return;
        }

        var tool = new CamTool((int)C("T"), C("D"), C("S"), C("F"), C("FZ"));
        var levels = new CamLevels(C("SAFE"), C("CLR"), C("TOP"), C("BOT"), C("Q"));
        int kind = OpCombo.SelectedIndex;
        Contour? selected = ContourList.SelectedIndex >= 0 && ContourList.SelectedIndex < _contours.Count ? _contours[ContourList.SelectedIndex] : null;
        OperationResult result;
        if (kind == 5)
        {
            IReadOnlyList<DrillHole> holes = CamOperations.HolesFromCircles(_contours);
            if (holes.Count == 0)
            {
                Errors.Text = "• Nenhum círculo no desenho para furar.";
                return;
            }

            DrillCycleKind cycle = (_cycle?.SelectedIndex ?? 0) switch
            {
                1 => DrillCycleKind.Dwell,
                2 => DrillCycleKind.Peck,
                3 => DrillCycleKind.ChipBreak,
                _ => DrillCycleKind.Drill,
            };
            result = CamOperations.Drilling(tool, levels.SafeZ, holes.Select(h => h.Center), cycle, levels.TopZ, levels.BottomZ, levels.ClearanceZ, F("PECK"), F("DWELL"));
        }
        else
        {
            if (selected is null)
            {
                Errors.Text = "• Escolha um contorno na lista.";
                return;
            }

            result = kind switch
            {
                <= 2 => CamOperations.Contour(selected, tool, levels,
                    new ContourParams(kind switch { 0 => ContourSide.Outside, 1 => ContourSide.Inside, _ => ContourSide.OnLine },
                        _climb?.IsChecked == true, _comp?.IsChecked == true, F("ALLOW"), F("LEAD"))),
                3 => CamOperations.Pocket(selected, tool, levels, new PocketParams(F("STEP"), F("FIN"), F("LEAD"))),
                _ => FacingOf(selected, tool, levels),
            };
        }

        if (!result.Ok)
        {
            Errors.Text = "• " + result.Error;
            return;
        }

        _ops.Add(result.Operation!);
        RefreshOps();
        OpList.SelectedIndex = _ops.Count - 1;
    }

    private OperationResult FacingOf(Contour selected, CamTool tool, CamLevels levels)
    {
        (double x0, double y0, double x1, double y1) = selected.BoundsXY();
        return CamOperations.Facing(tool, levels, x0, y0, x1 - x0, y1 - y0, F("STEP"));
    }

    private void RefreshOps()
    {
        int keep = OpList.SelectedIndex;
        OpList.Items.Clear();
        for (int i = 0; i < _ops.Count; i++)
        {
            CamOperation op = _ops[i];
            OpList.Items.Add(new ListBoxItem { Content = $"{i + 1} · {op.Name} — T{op.Tool.Number} Ø{op.Tool.Diameter.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR"))}" });
        }

        if (_ops.Count > 0)
        {
            OpList.SelectedIndex = Math.Clamp(keep, 0, _ops.Count - 1);
        }

        _program = "";
        Output.Text = "";
        OpenButton.IsEnabled = CopyButton.IsEnabled = false;
        Warnings.Text = string.Join("\n", _ops.SelectMany(o => o.Warnings).Distinct().Select(w => "⚠ " + w));
        RefreshPreview();
    }

    private void Generate()
    {
        if (_ops.Count == 0)
        {
            Errors.Text = "• Adicione ao menos uma operação.";
            return;
        }

        Errors.Text = "";
        var dialect = DialectCombo.SelectedIndex == 1 ? CamDialect.Siemens : CamDialect.Fanuc;
        _program = CamPost.Generate(_ops, new PostOptions(dialect, ProgramName: _baseName));
        Output.Text = _program;
        OpenButton.IsEnabled = CopyButton.IsEnabled = true;
    }
}
