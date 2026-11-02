using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using CncStudio.App.Settings;
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
    private readonly UndoStack<LatheOperationSpec> _undo = new();

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
        UndoButton.Click += (_, _) => Undo();
        UpButton.Click += (_, _) => MoveSelected(-1);
        DownButton.Click += (_, _) => MoveSelected(1);
        DuplicateButton.Click += (_, _) =>
        {
            if (SelectedOp() is { } i && i < _job.Operations.Count)
            {
                Snap();
                int at = _job.DuplicateOperation(i);
                Refresh(op: at);
            }
        };
        RemoveButton.Click += (_, _) =>
        {
            if (SelectedOp() is { } i)
            {
                Snap();
                _job.Operations.RemoveAt(i);
                Refresh(op: Math.Min(i, _job.Operations.Count - 1));
            }
        };
        View3DButton.IsCheckedChanged += (_, _) => Update3D();
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
        ActualThemeVariantChanged += (_, _) => Refresh(keep: true, props: false);
        Tree.SelectionChanged += (_, _) =>
        {
            if (!_busy)
            {
                BuildProps();
            }
        };
        // Barra inferior: as mesmas ações de antes (abrir no Code; o programa já é gerado a cada mudança)
        Bar.SendButton.Click += (_, _) =>
        {
            if (_build.Ok && _build.Program.Length > 0)
            {
                EnsureToolsInBank();
                OpenInEditor?.Invoke(_build.Program, FileBase() + ".nc");
            }
        };
        Bar.GenerateButton.Click += (_, _) =>
        {
            Refresh(keep: true, props: false);
            Output.CaretIndex = 0;
            Output.Focus();
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

    public Func<List<ToolEntry>>? ToolsSource { get; set; }

    /// <summary>Perfil da máquina ativa (o mesmo da fresa), para o limite do fuso.</summary>
    public Func<MachineProfile?>? MachineSource { get; set; }

    public Action? SaveTools { get; set; }

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

        DxfImportResult r = DxfImporter.ParseInMillimeters(file.Text);
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
        _undo.Clear();
        Refresh();
    }

    // ---------------------------------------------------------------- operações

    private sealed record TreeTag(string Kind, int Index);

    private int? SelectedOp() => (Tree.SelectedItem as TreeViewItem)?.Tag is TreeTag { Kind: "op" } t ? t.Index : null;

    private bool FixationSelected() => (Tree.SelectedItem as TreeViewItem)?.Tag is TreeTag { Kind: "fixation" };

    private void AddOperation()
    {
        Errors.Text = "";
        (_, LatheOpKind kind, bool isInternal) = LatheJobInfo.NewKinds[Math.Max(0, KindCombo.SelectedIndex)];
        LatheTool? tool = _job.Operations.Count > 0 ? _job.Operations[^1].Tool : null;
        Snap();
        _job.Operations.Add(LatheJobInfo.NewOperation(kind, isInternal, tool));
        Refresh(op: _job.Operations.Count - 1);
    }

    private void MoveSelected(int delta)
    {
        if (SelectedOp() is { } i && i + delta >= 0 && i + delta < _job.Operations.Count)
        {
            Snap();
            _job.MoveOperation(i, i + delta);
            Refresh(op: i + delta);
        }
    }

    private void SetEnabled(int index, bool enabled)
    {
        Snap();
        _job.Operations[index] = _job.Operations[index] with { Enabled = enabled };
        Refresh(op: index, props: false);
    }

    /// <summary>Guarda o estado das operações antes de uma alteração (para Desfazer).</summary>
    private static string OpIcon(LatheOperationSpec s) => s.Kind switch
    {
        LatheOpKind.Turning => "torneamento",
        LatheOpKind.Facing => "faceamento",
        LatheOpKind.Drilling => "furo",
        LatheOpKind.Threading => "rosca",
        _ => "canal",
    };

    private void Snap()
    {
        _undo.Push(_job.Operations);
        UndoButton.IsEnabled = true;
    }

    private void Undo()
    {
        IReadOnlyList<LatheOperationSpec>? back = _undo.Pop();
        UndoButton.IsEnabled = _undo.CanUndo;
        if (back is null)
        {
            return;
        }

        int? keep = SelectedOp();
        _job.Operations.Clear();
        _job.Operations.AddRange(back);
        Errors.Text = "";
        Refresh(op: keep is { } k ? Math.Min(k, _job.Operations.Count - 1) : null);
    }

    // ---------------------------------------------------------------- tela

    private void Refresh(int? op = null, bool keep = false, bool props = true)
    {
        int? keepOp = keep ? SelectedOp() : op;
        bool keepFixation = keep && FixationSelected();
        _busy = true;
        try
        {
            NameBox.Text = _job.Name;
            DialectCombo.SelectedIndex = _job.Dialect == CamDialect.Siemens ? 1 : 0;
            _build = LatheJobBuilder.Build(_job);
            BuildTree(keepOp, keepFixation);
        }
        finally
        {
            _busy = false;
        }

        if (props)
        {
            BuildProps();
        }

        Errors.Text = ""; // os erros de geração ficam na barra inferior (clicáveis); aqui só os avisos ao abrir arquivo
        Warnings.Text = string.Join("\n", _build.Warnings.Select(w => "⚠ " + w));
        Output.Text = _build.Ok ? _build.Program : "";
        Warnings.Foreground = UiKit.Status("pending").Fg;
        bool gcode = _build.Ok && _build.Program.Length > 0;
        int ready = _build.Items.Count(x => !x.Skipped && x.Ok);
        CodeTitle.Text = gcode ? $"Código CNC ({(_job.Dialect == CamDialect.Siemens ? "Siemens" : "Fanuc")}) · gerado a partir de {ready} operaç{(ready == 1 ? "ão" : "ões")}" : "Código CNC: não gerado";
        Bar.Update(PendingItems(), ready, gcode && ready > 0, gcode);
        CopyButton.IsEnabled = gcode;
        UpdatePreview();
        Update3D();
    }

    /// <summary>
    /// Pendências do Core (<see cref="LatheJobInfo.Pending"/>, mesmo texto e mesma ordem) com o lugar aonde cada uma leva. Erro de
    /// operação é "inválido" (vermelho); o resto é "pendente" (âmbar).
    /// </summary>
    private List<JobBarItem> PendingItems()
    {
        double? maxRpm = MachineSource?.Invoke()?.MaxSpindleRpm;
        IReadOnlyList<string> texts = LatheJobInfo.Pending(_job, _build, maxRpm);
        var targets = new List<(string Kind, Action Go)>();
        if (_job.Profile is null)
        {
            targets.Add(("pending", () =>
            {
                if (PointsBox.Parent?.Parent is Expander e)
                {
                    e.IsExpanded = true;
                }

                PointsBox.BringIntoView();
                PointsBox.Focus(NavigationMethod.Tab);
            }));
        }

        if (_job.Operations.Count == 0)
        {
            targets.Add(("pending", () => KindCombo.Focus(NavigationMethod.Tab)));
        }

        for (int i = 0; i < _build.Items.Count; i++)
        {
            int index = i;
            if (!_build.Items[i].Ok)
            {
                targets.Add(("bad", () => GoToOp(index)));
            }
        }

        foreach (int index in LatheJobInfo.OverSpindle(_job, maxRpm))
        {
            targets.Add(("pending", () => GoToOp(index)));
        }

        // Se um dia o Core mudar a lista sem a tela acompanhar, os itens continuam aparecendo (sem atalho)
        return texts.Select((text, k) => k < targets.Count && texts.Count == targets.Count
            ? new JobBarItem(targets[k].Kind, text, targets[k].Go)
            : new JobBarItem("pending", text, () => { })).ToList();
    }

    /// <summary>Seleciona e foca a operação na árvore (as propriedades acompanham a seleção).</summary>
    private void GoToOp(int index)
    {
        IEnumerable<TreeViewItem> All(ItemsControl parent) => parent.Items.OfType<TreeViewItem>().SelectMany(x => All(x).Prepend(x));
        if (All(Tree).FirstOrDefault(x => x.Tag is TreeTag { Kind: "op" } t && t.Index == index) is { } item)
        {
            Tree.SelectedItem = item;
            item.BringIntoView();
            item.Focus(NavigationMethod.Tab);
        }
    }

    /// <summary>Alterna 2D/3D e, no 3D, simula o programa atual.</summary>
    private void Update3D()
    {
        bool on = View3DButton.IsChecked == true;
        Scene3D.IsVisible = on;
        Preview.IsVisible = !on;
        if (on)
        {
            PreviewHint.IsVisible = false;
        }
        else
        {
            UpdatePreview();
        }

        if (on)
        {
            Scene3D.Render(_build.Ok ? _build.Program : "", _job.Dialect == CamDialect.Siemens ? Dialect.Siemens : Dialect.Fanuc);
        }
    }

    private void BuildTree(int? selectOp, bool selectFixation = false)
    {
        Tree.Items.Clear();
        TreeViewItem? select = null;
        TreeViewItem Node(string text, string? icon = null) => new() { Header = icon is null ? text : UiKit.IconLabel(icon, text), IsExpanded = true };

        // Mesma tabela de estados da fresa: OK, desligada, erro com motivo, não verificada
        TreeViewItem NodeState(string text, (string Kind, string Text) st, string icon) =>
            UiKit.StateItem(UiKit.StateRow(text, st, UiKit.Icon(icon, "#8FB4FF", 15)));

        // perfil ausente é pendência (âmbar, como na barra)
        Tree.Items.Add(NodeState(LatheJobInfo.ProfileTitle(_job.Profile), _job.Profile is null ? ("pending", "falta definir") : ("ok", "OK"), "perfil"));
        Tree.Items.Add(NodeState(LatheJobInfo.OriginTitle(_job), CamJobInfo.OriginState(), "preparo"));
        TreeViewItem fixation = UiKit.StateItem(UiKit.StateRow(LatheFixation.Title(_job.Fixation), LatheFixation.State(_job.Fixation), UiKit.Icon("fixacao", "#8FB4FF", 15)), new TreeTag("fixation", 0));
        Tree.Items.Add(fixation);
        if (selectFixation)
        {
            select = fixation;
        }

        MachineProfile? machine = MachineSource?.Invoke();
        Tree.Items.Add(NodeState(CamJobInfo.MachineTitle(machine), CamJobInfo.MachineState(machine), "ferramenta"));
        TreeViewItem tools = Node("Ferramentas", "ferramentas");
        foreach (LatheTool t in LatheJobInfo.ToolsUsed(_job))
        {
            tools.Items.Add(Node($"T{t.Number} · {(t.Rpm > 0 ? $"{t.Rpm:0} rpm" : $"G96 {t.SurfaceSpeed:0} m/min")} · máx. {t.MaxRpm:0} rpm"));
        }

        Tree.Items.Add(tools);
        TreeViewItem ops = Node($"Operações ({_job.Operations.Count})", "operacoes");
        for (int i = 0; i < _job.Operations.Count; i++)
        {
            int index = i;
            LatheOperationSpec spec = _job.Operations[i];
            var check = new CheckBox { IsChecked = spec.Enabled, MinWidth = 0 };
            // título com reticências: a etiqueta de estado (texto) nunca sai da árvore
            DockPanel row = UiKit.StateRow(LatheJobInfo.OpTitle(i, spec), LatheJobInfo.OpState(i < _build.Items.Count ? _build.Items[i] : null, spec, machine?.MaxSpindleRpm), check, UiKit.Icon(OpIcon(spec), "#8FB4FF", 15));
            row.Opacity = spec.Enabled ? 1 : 0.5;
            check.IsCheckedChanged += (_, _) =>
            {
                if (!_busy)
                {
                    SetEnabled(index, check.IsChecked == true);
                }
            };
            TreeViewItem item = UiKit.StateItem(row, new TreeTag("op", i));
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

    /// <summary>Tipo da ferramenta no banco para uma operação de torno.</summary>
    private static string BankKind(LatheOperationSpec s) => s.Kind switch
    {
        LatheOpKind.Threading => s.IsInternal ? "ThreadInternal" : "ThreadExternal",
        LatheOpKind.Grooving => s.IsInternal ? "PartInternal" : "PartExternal",
        _ => s.IsInternal ? "TurnInternal" : "TurnExternal",
    };

    /// <summary>Põe no banco as ferramentas do trabalho que ainda não existem lá (por T).</summary>
    private void EnsureToolsInBank()
    {
        List<ToolEntry>? tools = ToolsSource?.Invoke();
        if (tools is null)
        {
            return;
        }

        bool changed = false;
        foreach (LatheOperationSpec s in _job.Operations)
        {
            if (tools.All(e => e.Number != s.Tool.Number))
            {
                tools.Add(new ToolEntry { Number = s.Tool.Number, Description = s.Tool.Name, Kind = BankKind(s), SpindleRpm = s.Tool.Rpm });
                changed = true;
            }
        }

        if (changed)
        {
            SaveTools?.Invoke();
        }
    }
}
