using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CncStudio.Core.Cam;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Parsing;

namespace CncStudio.App.Modules;

/// <summary>
/// Casillas CAM, torno: perfil (DXF ou pontos), operações (torneamento externo/interno, faceamento, furação axial), prévia
/// do programa simulado e geração Fanuc/Siemens. O cálculo é do Core (<c>Core/Cam/Lathe*</c>).
/// </summary>
public partial class LatheCamView : UserControl
{
    private static readonly string[] OpNames = ["Torneamento externo", "Torneamento interno", "Faceamento", "Furação axial", "Rosca externa", "Rosca interna"];
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    private readonly List<LatheOperation> _ops = [];
    private readonly Dictionary<string, TextBox> _tool = [];
    private readonly Dictionary<string, TextBox> _safe = [];
    private readonly Dictionary<string, TextBox> _fields = [];
    private CheckBox? _rough, _finish;
    private LatheProfile? _profile;
    private string _program = "";
    private string _baseName = "TORNO";

    public Func<Task<(string Name, string Text)?>>? PickDxf { get; set; }

    public Action<string, string>? OpenInEditor { get; set; }

    public Func<string, Task>? CopyText { get; set; }

    public LatheCamView()
    {
        InitializeComponent();
        AddRow(ToolFields, _tool, "T", "Ferramenta (T)", 1, "");
        AddRow(ToolFields, _tool, "VC", "Velocidade de corte (G96)", 200, "m/min");
        AddRow(ToolFields, _tool, "MAX", "Rotação máxima", 3000, "rpm");
        AddRow(ToolFields, _tool, "RPM", "Rotação fixa (0 = usar G96)", 0, "rpm");
        AddRow(SafeFields, _safe, "SZ", "Z seguro", 50, "mm");
        AddRow(SafeFields, _safe, "SD", "Ø seguro (troca de ferramenta)", 80, "mm");
        AddRow(SafeFields, _safe, "CLR", "Folga de aproximação", 2, "mm");
        PointsBox.Text = "0 16\n-2 20\n-25 20\n-25 28\n-35 28\n-45 28";
        foreach (string name in OpNames)
        {
            OpCombo.Items.Add(new ComboBoxItem { Content = name });
        }

        OpCombo.SelectionChanged += (_, _) => BuildOpFields();
        OpCombo.SelectedIndex = 0;
        OpenDxfButton.Click += async (_, _) => await OpenDxf();
        UsePointsButton.Click += (_, _) => UsePoints();
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
        UsePoints();
    }

    // ---------------------------------------------------------------- formulário

    private static void AddRow(Panel parent, Dictionary<string, TextBox> map, string id, string label, double value, string unit)
    {
        var box = new TextBox { Text = value.ToString("0.###", Br), Width = 90, MinHeight = 26 };
        map[id] = box;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock { Text = label, Width = 190, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        row.Children.Add(box);
        row.Children.Add(new TextBlock { Text = unit, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
        parent.Children.Add(row);
    }

    private void BuildOpFields()
    {
        OpFields.Children.Clear();
        _fields.Clear();
        _rough = _finish = null;
        switch (Math.Max(0, OpCombo.SelectedIndex))
        {
            case 0:
            case 1:
                bool internalBore = OpCombo.SelectedIndex == 1;
                AddRow(OpFields, _fields, "STOCK", internalBore ? "Ø do pré-furo" : "Ø do bruto", internalBore ? 14 : 40, "mm");
                _rough = new CheckBox { Content = "Desbaste", IsChecked = true };
                _finish = new CheckBox { Content = "Acabamento", IsChecked = true };
                OpFields.Children.Add(_rough);
                OpFields.Children.Add(_finish);
                AddRow(OpFields, _fields, "DOC", "Profundidade de corte (raio)", 1.5, "mm");
                AddRow(OpFields, _fields, "RET", "Recuo", 0.5, "mm");
                AddRow(OpFields, _fields, "ALR", "Sobremetal radial", 0.2, "mm");
                AddRow(OpFields, _fields, "ALZ", "Sobremetal em Z", 0.1, "mm");
                AddRow(OpFields, _fields, "FR", "Avanço do desbaste", 0.25, "mm/rot");
                AddRow(OpFields, _fields, "FF", "Avanço do acabamento", 0.1, "mm/rot");
                break;
            case 2:
                AddRow(OpFields, _fields, "STOCK", "Ø do bruto", 40, "mm");
                AddRow(OpFields, _fields, "INNER", "Ø interno (0 = até o centro)", 0, "mm");
                AddRow(OpFields, _fields, "ZS", "Z da face do bruto", 2, "mm");
                AddRow(OpFields, _fields, "ZF", "Z da face pronta", 0, "mm");
                AddRow(OpFields, _fields, "DOC", "Passe em Z", 1, "mm");
                AddRow(OpFields, _fields, "F", "Avanço", 0.15, "mm/rot");
                break;
            case 4:
            case 5:
                bool inner = OpCombo.SelectedIndex == 5;
                AddRow(OpFields, _fields, "DM", inner ? "Ø nominal (raiz)" : "Ø nominal (crista)", inner ? 16 : 20, "mm");
                AddRow(OpFields, _fields, "PIT", "Passo", 1.5, "mm");
                AddRow(OpFields, _fields, "ZS", "Z do início", 0, "mm");
                AddRow(OpFields, _fields, "ZE", "Z do fim", -18, "mm");
                AddRow(OpFields, _fields, "TRPM", "Rotação (fixa)", 600, "rpm");
                AddRow(OpFields, _fields, "TH", "Altura do filete (0 = ISO)", 0, "mm");
                AddRow(OpFields, _fields, "FIN", "Passes de acabamento", 2, "");
                AddRow(OpFields, _fields, "AL", "Sobremetal de acabamento", 0.05, "mm");
                break;
            default:
                AddRow(OpFields, _fields, "ZT", "Z da face", 0, "mm");
                AddRow(OpFields, _fields, "ZB", "Z do fundo", -25, "mm");
                AddRow(OpFields, _fields, "PECK", "Bicada", 8, "mm");
                AddRow(OpFields, _fields, "F", "Avanço", 0.12, "mm/rot");
                break;
        }
    }

    private static double Parse(TextBox box)
    {
        string t = (box.Text ?? "").Trim().Replace(',', '.');
        return double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;
    }

    private double T(string id) => Parse(_tool[id]);

    private double S(string id) => Parse(_safe[id]);

    private double F(string id) => Parse(_fields[id]);

    // ---------------------------------------------------------------- perfil

    private async Task OpenDxf()
    {
        if (PickDxf is null || await PickDxf() is not { } file)
        {
            return;
        }

        DxfImportResult r = DxfImporter.Parse(file.Text);
        List<Segment2>? chain = r.OpenChains.OrderByDescending(c => c.Sum(s => s.Length)).FirstOrDefault()?.ToList();
        if (chain is null)
        {
            Errors.Text = "• O DXF não tem um perfil aberto (linhas e arcos encadeados). Desenhe só o perfil, sem fechar pelo eixo.";
            return;
        }

        if (DiameterCheck.IsChecked == true)
        {
            chain = chain.Select(s => s with { Start = new Point2(s.Start.X, s.Start.Y / 2), End = new Point2(s.End.X, s.End.Y / 2), Center = new Point2(s.Center.X, s.Center.Y / 2) }).ToList();
        }

        _baseName = Path.GetFileNameWithoutExtension(file.Name);
        SetProfile(chain);
    }

    private void UsePoints()
    {
        var points = new List<Point2>();
        foreach (string raw in (PointsBox.Text ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] parts = raw.Replace(';', ' ').Replace(',', '.').Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2
                || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double z)
                || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
            {
                Errors.Text = $"• Linha inválida nos pontos: \"{raw.Trim()}\". Use \"Z Ø\" (ex.: -25 28).";
                return;
            }

            points.Add(new Point2(z, d / 2));
        }

        if (points.Count < 2)
        {
            Errors.Text = "• Informe pelo menos dois pontos.";
            return;
        }

        Errors.Text = "";
        SetProfile(Enumerable.Range(1, points.Count - 1).Select(i => Segment2.Line(points[i - 1], points[i])).ToList());
    }

    private void SetProfile(List<Segment2> chain)
    {
        _profile = LatheProfile.From(chain, out string? error);
        if (_profile is null)
        {
            Errors.Text = "• " + error;
            ProfileInfo.Text = "";
        }
        else
        {
            Errors.Text = "";
            (double min, double max) = _profile.RadiusRange();
            ProfileInfo.Text = $"Perfil: {_profile.Segments.Count} trecho(s), Z {_profile.Start.X.ToString("0.##", Br)} a {_profile.End.X.ToString("0.##", Br)}, Ø {(min * 2).ToString("0.##", Br)} a {(max * 2).ToString("0.##", Br)}.";
        }

        RefreshPreview(null);
    }

    private void RefreshPreview(List<List<Point2>>? cuts)
    {
        var lines = new List<List<Point2>>();
        var contours = new List<Contour>();
        if (_profile is not null)
        {
            lines.Add(_profile.Segments.SelectMany(s => s.Sample(0.02)).ToList());
            (_, double max) = _profile.RadiusRange();
            double r = Math.Max(max, 1);
            contours.Add(Contour.FromPoints([new Point2(_profile.Start.X, 0), new Point2(_profile.End.X, 0), new Point2(_profile.End.X, r), new Point2(_profile.Start.X, r)]));
        }

        if (cuts is not null)
        {
            lines.AddRange(cuts);
        }

        Preview.Set(contours, 0, lines);
        PreviewHint.IsVisible = _profile is null;
    }

    // ---------------------------------------------------------------- operações

    private void AddOperation()
    {
        Errors.Text = "";
        if (_tool.Values.Concat(_safe.Values).Concat(_fields.Values).Any(b => double.IsNaN(Parse(b))))
        {
            Errors.Text = "• Há um campo com valor inválido.";
            return;
        }

        var tool = new LatheTool((int)T("T"), "", T("VC"), T("MAX"), T("RPM"));
        double safeZ = S("SZ"), safeR = S("SD") / 2, clearance = S("CLR");
        int kind = OpCombo.SelectedIndex;
        LatheResult result;
        if (kind <= 1)
        {
            if (_profile is null)
            {
                Errors.Text = "• Informe o perfil da peça primeiro.";
                return;
            }

            result = LatheOperations.Turning(_profile, tool, safeZ, safeR,
                new TurningParams(F("STOCK"), kind == 1, _rough?.IsChecked == true, _finish?.IsChecked == true, clearance, F("DOC"), F("RET"), F("ALR"), F("ALZ"), F("FR"), F("FF")));
        }
        else if (kind == 2)
        {
            result = LatheOperations.Facing(tool, safeZ, safeR, F("STOCK"), F("ZS"), F("ZF"), F("DOC"), F("F"), F("INNER"), clearance);
        }
        else if (kind >= 4)
        {
            result = LatheOperations.Threading(tool, safeZ, safeR,
                new ThreadingParams(kind == 5, F("DM"), F("PIT"), F("ZS"), F("ZE"), F("TRPM"), F("TH"), 0, 0.05, (int)F("FIN"), F("AL"), 0, 0, clearance));
        }
        else
        {
            result = LatheOperations.Drilling(tool, safeZ, safeR, F("ZT"), F("ZB"), F("PECK"), F("F"), clearance);
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

    private void RefreshOps()
    {
        OpList.Items.Clear();
        for (int i = 0; i < _ops.Count; i++)
        {
            OpList.Items.Add(new ListBoxItem { Content = $"{i + 1} · {_ops[i].Name} — T{_ops[i].Tool.Number}" });
        }

        _program = "";
        Output.Text = "";
        OpenButton.IsEnabled = CopyButton.IsEnabled = false;
        Warnings.Text = string.Join("\n", _ops.SelectMany(o => o.Warnings).Distinct().Select(w => "⚠ " + w));
        RefreshPreview(null);
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
        _program = LathePost.Generate(_ops, new LathePostOptions(dialect, ProgramName: _baseName));
        Output.Text = _program;
        OpenButton.IsEnabled = CopyButton.IsEnabled = true;

        // Prévia: o próprio programa gerado, simulado pelo mesmo executor do Code
        try
        {
            var path = MachineExecutor.Execute(GCodeParser.Parse(_program, dialect == CamDialect.Fanuc ? Dialect.Fanuc : Dialect.Siemens), new MachineSettings { Machine = MachineKind.Lathe });
            var cuts = new List<List<Point2>>();
            foreach (var m in path.Moves.Where(m => !m.IsRapid))
            {
                var pts = new List<Core.Geometry.Point3> { m.Start };
                m.AppendPolyline(pts, 0.02);
                pts.Add(m.End);
                cuts.Add(pts.Select(p => new Point2(p.Z, p.X)).ToList());
            }

            RefreshPreview(cuts);
        }
        catch (Exception e)
        {
            Errors.Text = "• Prévia do percurso indisponível: " + e.Message;
        }
    }
}
