using Avalonia.Controls;
using CncStudio.App.Settings;
using CncStudio.Core.Cam;
using CncStudio.Core.Setup;

namespace CncStudio.App.Modules;

/// <summary>
/// Tela de trabalho do CAM (fresa): árvore do Job (peça, bruto, fixação, ferramentas, operações), vista 2D e programa.
/// J2: criar, ligar/desligar, reordenar, duplicar e remover operações; salvar e abrir o trabalho. Os números da operação
/// ainda não se editam aqui (painel de propriedades na J3): a operação nova nasce com os valores padrão.
/// </summary>
public partial class JobView : UserControl
{
    private readonly CamJob _job = new() { Name = "TRABALHO" };
    private JobBuild _build = new("", [], [], [], []);
    private bool _busy;
    private readonly UndoStack<CamOperationSpec> _undo = new();

    public JobView()
    {
        InitializeComponent();
        foreach ((string name, _, _) in CamJobInfo.NewKinds)
        {
            KindCombo.Items.Add(new ComboBoxItem { Content = name });
        }

        KindCombo.SelectedIndex = 0;
        OpenDxfButton.Click += async (_, _) => await OpenDxf();
        OpenJobButton.Click += async (_, _) => await OpenJob();
        SaveJobButton.Click += async (_, _) => await SaveJob();
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
        NameBox.LostFocus += (_, _) =>
        {
            string name = NameBox.Text?.Trim() ?? "";
            if (name.Length > 0 && name != _job.Name)
            {
                _job.Name = name;
                Refresh(keep: true);
            }
        };
        DialectCombo.SelectionChanged += (_, _) =>
        {
            if (_busy)
            {
                return;
            }

            _job.Dialect = DialectCombo.SelectedIndex == 1 ? CamDialect.Siemens : CamDialect.Fanuc;
            Refresh(keep: true);
        };
        Tree.SelectionChanged += (_, _) =>
        {
            if (!_busy)
            {
                BuildProps();
                UpdatePreview();
            }
        };
        OpenButton.Click += (_, _) =>
        {
            if (_build.Ok && _build.Program.Length > 0)
            {
                EnsureToolsInBank();
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
        NameBox.Text = _job.Name;
        Refresh();
    }

    public Func<Task<(string Name, string Text)?>>? PickDxf { get; set; }

    /// <summary>Abre o seletor de trabalho salvo (.casillas-job); devolve nome e texto.</summary>
    public Func<Task<(string Name, string Text)?>>? PickJob { get; set; }

    /// <summary>Salva o texto do trabalho num arquivo escolhido pelo usuário (nome sugerido, texto).</summary>
    public Func<string, string, Task>? SaveJobFile { get; set; }

    public Action<string, string>? OpenInEditor { get; set; }

    public Func<string, Task>? CopyText { get; set; }

    public Func<List<ToolEntry>>? ToolsSource { get; set; }

    public Action? SaveTools { get; set; }

    /// <summary>Perfil de máquina escolhido na aba Máquina (null = nenhum).</summary>
    public Func<MachineProfile?>? MachineSource { get; set; }

    private string FileBase()
    {
        string n = string.Concat(_job.Name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ')).Trim().Replace(' ', '_');
        return n.Length > 0 ? n : "TRABALHO";
    }

    // ---------------------------------------------------------------- arquivos

    private async Task OpenDxf()
    {
        if (PickDxf is null || await PickDxf() is not { } file)
        {
            return;
        }

        DxfImportResult r = DxfImporter.Parse(file.Text);
        if (_job.Operations.Count > 0 && _job.Contours.Count > 0)
        {
            Errors.Text = "• O desenho já tem operações. Abra o DXF num trabalho novo (Abrir trabalho… ou reabra o programa) para não misturar contornos.";
            return;
        }

        _job.Contours.Clear();
        _job.Contours.AddRange(r.Contours);
        _job.Name = Path.GetFileNameWithoutExtension(file.Name).ToUpperInvariant();
        Refresh();
        if (r.Warnings.Count > 0)
        {
            Errors.Text = string.Join("\n", r.Warnings.Select(w => "• " + w));
        }
    }

    private async Task OpenJob()
    {
        if (PickJob is null || await PickJob() is not { } file)
        {
            return;
        }

        JobLoadResult loaded = CamJobJson.FromJson(file.Text);
        if (!loaded.Ok)
        {
            Errors.Text = "• " + loaded.Error;
            return;
        }

        CamJob j = loaded.Job!;
        _job.Name = j.Name;
        _job.Dialect = j.Dialect;
        _job.ProgramNumber = j.ProgramNumber;
        _job.WorkOffset = j.WorkOffset;
        _job.Coolant = j.Coolant;
        _job.Stock = j.Stock;
        _job.Contours.Clear();
        _job.Contours.AddRange(j.Contours);
        _job.Operations.Clear();
        _job.Operations.AddRange(j.Operations);
        _undo.Clear();
        Refresh();
    }

    private async Task SaveJob()
    {
        if (SaveJobFile is null)
        {
            return;
        }

        await SaveJobFile(FileBase() + ".casillas-job", CamJobJson.ToJson(_job));
    }

    // ---------------------------------------------------------------- operações

    private sealed record TreeTag(string Kind, int Index);

    private int? SelectedOp() => (Tree.SelectedItem as TreeViewItem)?.Tag is TreeTag { Kind: "op" } t ? t.Index : null;

    private int? SelectedContour() => (Tree.SelectedItem as TreeViewItem)?.Tag is TreeTag { Kind: "contour" } t ? t.Index : null;

    private void AddOperation()
    {
        Errors.Text = "";
        (_, JobOpKind kind, ContourSide side) = CamJobInfo.NewKinds[Math.Max(0, KindCombo.SelectedIndex)];
        int contour = SelectedContour() ?? (SelectedOp() is { } o ? _job.Operations[o].ContourIndex : -1);
        if (kind is not (JobOpKind.Drilling or JobOpKind.WrapText) && (contour < 0 || contour >= _job.Contours.Count))
        {
            contour = _job.Contours.Count > 0 ? 0 : -1;
        }

        if (kind is not (JobOpKind.Drilling or JobOpKind.WrapText) && contour < 0)
        {
            Errors.Text = "• Abra um DXF ou um trabalho com desenho antes de adicionar operações.";
            return;
        }

        CamTool? tool = _job.Operations.Count > 0 ? _job.Operations[^1].Tool : null;
        Snap();
        _job.Operations.Add(CamJobInfo.NewOperation(kind, side, contour, tool));
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
        Refresh(op: index);
    }

    /// <summary>Guarda o estado das operações antes de uma alteração (para Desfazer).</summary>
    private void Snap()
    {
        _undo.Push(_job.Operations);
        UndoButton.IsEnabled = true;
    }

    private void Undo()
    {
        IReadOnlyList<CamOperationSpec>? back = _undo.Pop();
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

    /// <summary>Refaz a árvore, o programa e a vista. <paramref name="op"/>: operação a deixar selecionada; <paramref name="keep"/>: manter a seleção atual.</summary>
    private void Refresh(int? op = null, bool keep = false, bool props = true)
    {
        int? keepOp = keep ? SelectedOp() : op;
        int? keepContour = keep ? SelectedContour() : null;
        _busy = true;
        try
        {
            NameBox.Text = _job.Name;
            DialectCombo.SelectedIndex = _job.Dialect == CamDialect.Siemens ? 1 : 0;
            _build = CamJobBuilder.Build(_job, MachineSource?.Invoke());
            BuildTree(keepOp, keepContour);
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
        Warnings.Text = string.Join("\n", _build.Warnings.Select(w => "• " + w));
        Output.Text = _build.Ok ? _build.Program : "";
        OpenButton.IsEnabled = CopyButton.IsEnabled = _build.Ok && _build.Program.Length > 0;
        UpdatePreview();
    }

    private void BuildTree(int? selectOp, int? selectContour)
    {
        Tree.Items.Clear();
        TreeViewItem? select = null;
        TreeViewItem Node(string text, object? tag = null) => new() { Header = text, Tag = tag, IsExpanded = true };

        TreeViewItem part = Node($"Peça ({_job.Contours.Count} contorno{(_job.Contours.Count == 1 ? "" : "s")})");
        for (int i = 0; i < _job.Contours.Count; i++)
        {
            TreeViewItem c = Node(CamJobInfo.ContourTitle(_job.Contours[i], i), new TreeTag("contour", i));
            part.Items.Add(c);
            if (selectContour == i)
            {
                select = c;
            }
        }

        Tree.Items.Add(part);
        Tree.Items.Add(Node(CamJobInfo.StockTitle(_job.Stock)));
        Tree.Items.Add(Node("Fixação: em breve"));
        TreeViewItem tools = Node("Ferramentas");
        foreach (CamTool t in CamJobInfo.ToolsUsed(_job))
        {
            tools.Items.Add(Node($"T{t.Number} · Ø{t.Diameter:0.##} · {t.SpindleRpm:0} rpm"));
        }

        Tree.Items.Add(tools);
        TreeViewItem ops = Node($"Operações ({_job.Operations.Count})");
        for (int i = 0; i < _job.Operations.Count; i++)
        {
            int index = i;
            CamOperationSpec spec = _job.Operations[i];
            var check = new CheckBox { IsChecked = spec.Enabled, MinWidth = 0 };
            var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6, Opacity = spec.Enabled ? 1 : 0.5 };
            row.Children.Add(check);
            row.Children.Add(new TextBlock { Text = CamJobInfo.OpTitle(i, spec), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center });
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
        List<List<Point2>> path = [];
        if (SelectedOp() is { } i && i < _build.Items.Count && _build.Items[i].Result is { Operation: { } built })
        {
            path = ToolpathView.CuttingPolylines(built);
        }

        int selected = SelectedContour() ?? (SelectedOp() is { } o && o < _job.Operations.Count ? _job.Operations[o].ContourIndex : -1);
        Preview.Set(_job.Contours, selected, path);
        PreviewHint.IsVisible = _job.Contours.Count == 0;
    }

    private void EnsureToolsInBank()
    {
        List<ToolEntry>? tools = ToolsSource?.Invoke();
        if (tools is null)
        {
            return;
        }

        bool changed = false;
        foreach (CamTool t in CamJobInfo.ToolsUsed(_job))
        {
            if (tools.All(e => e.Number != t.Number))
            {
                tools.Add(new ToolEntry { Number = t.Number, Diameter = t.Diameter, Description = t.Name, SpindleRpm = t.SpindleRpm, FeedXY = t.FeedXY, FeedZ = t.FeedZ });
                changed = true;
            }
        }

        if (changed)
        {
            SaveTools?.Invoke();
        }
    }
}
