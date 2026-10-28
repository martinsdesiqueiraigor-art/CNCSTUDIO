using System.Globalization;
using Avalonia.Controls;
using CncStudio.Core.Cam;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Parsing;

namespace CncStudio.App.Modules;

/// <summary>
/// Tela de trabalho do CAM de torno: árvore do Trabalho (perfil, ferramentas, operações), vista do perfil e do percurso simulado,
/// programa ao vivo e painel de propriedades. O cálculo é do Core (<c>LatheJob</c>).
/// </summary>
public partial class LatheJobView : UserControl
{
    private readonly LatheJob _job = new() { Name = "TORNO" };
    private LatheBuild _build = new("", [], [], [], []);
    private bool _busy;

    public LatheJobView()
    {
        InitializeComponent();
        foreach ((string name, _, _) in LatheJobInfo.NewKinds)
        {
            KindCombo.Items.Add(new ComboBoxItem { Content = name });
        }

        KindCombo.SelectedIndex = 0;
        PointsBox.Text = "0 16\n-2 20\n-25 20\n-25 28\n-35 28\n-45 28";
        OpenDxfButton.Click += async (_, _) => await OpenDxf();
        UsePointsButton.Click += (_, _) => UsePoints();
        OpenJobButton.Click += async (_, _) => await OpenJob();
        SaveJobButton.Click += async (_, _) =>
        {
            if (SaveJobFile is not null)
            {
                await SaveJobFile(FileBase() + ".casillas-job", LatheJobJson.ToJson(_job));
            }
        };
        AddButton.Click += (_, _) => AddOperation();
        UpButton.Click += (_, _) => MoveSelected(-1);
        DownButton.Click += (_, _) => MoveSelected(1);
        DuplicateButton.Click += (_, _) =>
        {
            if (SelectedOp() is { } i && _job.DuplicateOperation(i) is var at && at >= 0)
            {
                Refresh(op: at);
            }
        };
        RemoveButton.Click += (_, _) =>
        {
            if (SelectedOp() is { } i)
            {
                _job.Operations.RemoveAt(i);
                Refresh(op: Math.Min(i, _job.Operations.Count - 1));
            }
        };
        NameBox.LostFocus += (_, _) =>
        {
            string name = NameBox.Text?.Trim() ?? "";
            if (name.Length > 0 && name != _job.Name)
            {
                _job.Name = name;
                Refresh(keep: true, props: false);
            }
        };
        DialectCombo.SelectionChanged += (_, _) =>
        {
            if (!_busy)
            {
                _job.Dialect = DialectCombo.SelectedIndex == 1 ? CamDialect.Siemens : CamDialect.Fanuc;
                Refresh(keep: true, props: false);
            }
        };
        Tree.SelectionChanged += (_, _) =>
        {
            if (!_busy)
            {
                BuildProps();
            }
        };
        OpenButton.Click += (_, _) =>
        {
            if (_build.Ok && _build.Program.Length > 0)
            {
                OpenInEditor?.Invoke(_build.Program, FileBase() + ".nc");
            }
        };
        CopyButton.Click += async (_, _) =>
        {
            if (_build.Ok && _build.Program.Length > 0 && CopyText is not null)
            {
                await CopyText(_build.Program);
            }
        };
        UsePoints();
    }

    public Func<Task<(string Name, string Text)?>>? PickDxf { get; set; }

    public Func<Task<(string Name, string Text)?>>? PickJob { get; set; }

    public Func<string, string, Task>? SaveJobFile { get; set; }

    public Action<string, string>? OpenInEditor { get; set; }

    public Func<string, Task>? CopyText { get; set; }

    private string FileBase()
    {
        string n = string.Concat(_job.Name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ')).Trim().Replace(' ', '_');
        return n.Length > 0 ? n : "TORNO";
    }

    // ---------------------------------------------------------------- perfil e arquivos

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

        _job.Name = Path.GetFileNameWithoutExtension(file.Name).ToUpperInvariant();
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

        SetProfile(Enumerable.Range(1, points.Count - 1).Select(i => Segment2.Line(points[i - 1], points[i])).ToList());
    }

    private void SetProfile(List<Segment2> chain)
    {
        LatheProfile? profile = LatheProfile.From(chain, out string? error);
        if (profile is null)
        {
            Errors.Text = "• " + error;
            return;
        }

        _job.Profile = profile;
        Refresh(keep: true);
    }

    private async Task OpenJob()
    {
        if (PickJob is null || await PickJob() is not { } file)
        {
            return;
        }

        LatheLoadResult loaded = LatheJobJson.FromJson(file.Text);
        if (!loaded.Ok)
        {
            Errors.Text = "• " + loaded.Error;
            return;
        }

        LatheJob j = loaded.Job!;
        _job.Name = j.Name;
        _job.Dialect = j.Dialect;
        _job.ProgramNumber = j.ProgramNumber;
        _job.WorkOffset = j.WorkOffset;
        _job.Coolant = j.Coolant;
        _job.Profile = j.Profile;
        _job.Operations.Clear();
        _job.Operations.AddRange(j.Operations);
        Refresh();
    }

    // ---------------------------------------------------------------- operações

    private sealed record TreeTag(string Kind, int Index);

    private int? SelectedOp() => (Tree.SelectedItem as TreeViewItem)?.Tag is TreeTag { Kind: "op" } t ? t.Index : null;

    private void AddOperation()
    {
        Errors.Text = "";
        (_, LatheOpKind kind, bool isInternal) = LatheJobInfo.NewKinds[Math.Max(0, KindCombo.SelectedIndex)];
        LatheTool? tool = _job.Operations.Count > 0 ? _job.Operations[^1].Tool : null;
        _job.Operations.Add(LatheJobInfo.NewOperation(kind, isInternal, tool));
        Refresh(op: _job.Operations.Count - 1);
    }

    private void MoveSelected(int delta)
    {
        if (SelectedOp() is { } i && _job.MoveOperation(i, i + delta))
        {
            Refresh(op: i + delta);
        }
    }

    private void SetEnabled(int index, bool enabled)
    {
        _job.Operations[index] = _job.Operations[index] with { Enabled = enabled };
        Refresh(op: index, props: false);
    }

    // ---------------------------------------------------------------- tela

    private void Refresh(int? op = null, bool keep = false, bool props = true)
    {
        int? keepOp = keep ? SelectedOp() : op;
        _busy = true;
        try
        {
            NameBox.Text = _job.Name;
            DialectCombo.SelectedIndex = _job.Dialect == CamDialect.Siemens ? 1 : 0;
            _build = LatheJobBuilder.Build(_job);
            BuildTree(keepOp);
        }
        finally
        {
            _busy = false;
        }

        if (props)
        {
            BuildProps();
        }

        Errors.Text = _build.Errors.Count > 0 && _job.Operations.Count > 0 ? string.Join("\n", _build.Errors.Select(e => "• " + e)) : "";
        Warnings.Text = string.Join("\n", _build.Warnings.Select(w => "⚠ " + w));
        Output.Text = _build.Ok ? _build.Program : "";
        OpenButton.IsEnabled = CopyButton.IsEnabled = _build.Ok && _build.Program.Length > 0;
        UpdatePreview();
    }

    private void BuildTree(int? selectOp)
    {
        Tree.Items.Clear();
        TreeViewItem? select = null;
        TreeViewItem Node(string text) => new() { Header = text, IsExpanded = true };

        Tree.Items.Add(Node(LatheJobInfo.ProfileTitle(_job.Profile)));
        TreeViewItem tools = Node("Ferramentas");
        foreach (LatheTool t in LatheJobInfo.ToolsUsed(_job))
        {
            tools.Items.Add(Node($"T{t.Number} · {(t.Rpm > 0 ? $"{t.Rpm:0} rpm" : $"G96 {t.SurfaceSpeed:0} m/min")} · máx. {t.MaxRpm:0} rpm"));
        }

        Tree.Items.Add(tools);
        TreeViewItem ops = Node($"Operações ({_job.Operations.Count})");
        for (int i = 0; i < _job.Operations.Count; i++)
        {
            int index = i;
            LatheOperationSpec spec = _job.Operations[i];
            var check = new CheckBox { IsChecked = spec.Enabled, MinWidth = 0 };
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, Opacity = spec.Enabled ? 1 : 0.5 };
            row.Children.Add(check);
            row.Children.Add(new TextBlock { Text = LatheJobInfo.OpTitle(i, spec), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
            check.IsCheckedChanged += (_, _) =>
            {
                if (!_busy)
                {
                    SetEnabled(index, check.IsChecked == true);
                }
            };
            var item = new TreeViewItem { Header = row, Tag = new TreeTag("op", i), IsExpanded = true };
            ops.Items.Add(item);
            if (selectOp == i)
            {
                select = item;
            }
        }

        Tree.Items.Add(ops);
        if (select is not null)
        {
            Tree.SelectedItem = select;
        }
    }

    private void UpdatePreview()
    {
        var lines = new List<List<Point2>>();
        var contours = new List<Contour>();
        if (_job.Profile is { } profile)
        {
            lines.Add(profile.Segments.SelectMany(s => s.Sample(0.02)).ToList());
            (_, double max) = profile.RadiusRange();
            double r = Math.Max(max, 1);
            contours.Add(Contour.FromPoints([new Point2(profile.Start.X, 0), new Point2(profile.End.X, 0), new Point2(profile.End.X, r), new Point2(profile.Start.X, r)]));
        }

        // Percurso: o próprio programa gerado, simulado pelo mesmo executor do Code
        if (_build.Ok && _build.Program.Length > 0)
        {
            try
            {
                var path = MachineExecutor.Execute(GCodeParser.Parse(_build.Program, _job.Dialect == CamDialect.Fanuc ? Dialect.Fanuc : Dialect.Siemens), new MachineSettings { Machine = MachineKind.Lathe });
                foreach (var m in path.Moves.Where(m => !m.IsRapid))
                {
                    var pts = new List<Core.Geometry.Point3> { m.Start };
                    m.AppendPolyline(pts, 0.02);
                    pts.Add(m.End);
                    lines.Add(pts.Select(p => new Point2(p.Z, p.X)).ToList());
                }
            }
            catch (Exception e)
            {
                Errors.Text = "• Prévia do percurso indisponível: " + e.Message;
            }
        }

        Preview.Set(contours, 0, lines);
        PreviewHint.IsVisible = _job.Profile is null;
    }
}
