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
/// Tela de trabalho do CAM de torno. UX-04e: a mesma casca do Trabalho fresa (abas Preparação, Operações e Código NC, faixa de
/// ícones, árvore de 240 px, painel de 290 px, divisórias, rodapé e status numa linha). O cálculo é do Core (<c>LatheJob</c>).
/// </summary>
public partial class LatheJobView : UserControl
{
    private readonly LatheJob _job = new() { Name = "TORNO" };
    private LatheBuild _build = new("", [], [], [], []);
    private bool _busy;
    private readonly UndoStack<LatheOperationSpec> _undo = new();
    private readonly CamPanes _panes;

    /// <summary>Botões da faixa, na ordem de <see cref="CamPalette.LatheVisibleGroups"/>.</summary>
    private readonly List<(CamPaletteItem Item, Button Button)> _paletteButtons = [];

    public LatheJobView()
    {
        InitializeComponent();
        TabStrip = CamShell.BuildTabs(_tabs, SetTab);
        _paletteButtons.AddRange(CamShell.BuildPalette(Palette, CamPalette.LatheVisibleGroups, PaletteClick));
        BuildBankSelector();
        _panes = new CamPanes(Panes, TreeSplitter, PropsSplitter, () => _tab == 1);
        PointsBox.Text = "0 16\n-2 20\n-25 20\n-25 28\n-35 28\n-45 28";
        OpenDxfButton.Click += async (_, _) => await OpenDxf();
        UsePointsButton.Click += (_, _) => UsePoints();
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
        Tree.SelectionChanged += (_, _) =>
        {
            if (_busy)
            {
                return;
            }

            // Peça, Bruto, Fixação e Zero: o lugar deles é a Preparação (como o Zero na fresa)
            if ((Tree.SelectedItem as TreeViewItem)?.Tag is TreeTag { Kind: "part" or "stock" or "fixation" or "zero" } t)
            {
                SetTab(0);
                Control card = t.Kind switch { "part" => PrepProfileCard, "fixation" => PrepFixationCard, "zero" => PrepOriginCard, _ => PrepStock };
                card.BringIntoView();
            }

            BuildProps();
        };
        // Rodapé: as mesmas ações de antes (abrir no Code; o programa já é gerado a cada mudança)
        Bar.SendButton.Click += (_, _) =>
        {
            if (_build.Ok && _build.Program.Length > 0)
            {
                // UX-04h D1: não grava mais na tabela antiga (a ferramenta vem do banco ativo do torno)
                OpenInEditor?.Invoke(_build.Program, FileBase() + ".nc");
            }
        };
        // um zero só no Core: o programa gerado é o da fixação 1
        Bar.GenerateButton.Content = "Gerar G-code da fixação 1";
        Bar.GenerateButton.Click += (_, _) =>
        {
            Refresh(keep: true, props: false);
            SetTab(2);
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
        SetTab(1);
    }

    public Func<Task<(string Name, string Text)?>>? PickDxf { get; set; }

    public Func<Task<(string Name, string Text)?>>? PickJob { get; set; }

    public Func<string, string, Task>? SaveJobFile { get; set; }

    public Action<string, string>? OpenInEditor { get; set; }

    /// <summary>Bancos carregados (UX-04h D1); o seletor mostra só os de torno.</summary>
    public Func<IReadOnlyList<ToolBank>>? BanksSource { get; set; }

    /// <summary>Id do banco ativo do torno guardado no config.json (null = o principal).</summary>
    public Func<string?>? ActiveBankId { get; set; }

    /// <summary>Guarda a escolha do banco ativo do torno (no App, nunca no trabalho).</summary>
    public Action<string>? SetActiveBank { get; set; }

    /// <summary>Banco ativo do torno agora (null = não há banco de torno).</summary>
    private ToolBank? ActiveBank => ToolBankPick.ActiveLatheBank(BanksSource?.Invoke() ?? [], ActiveBankId?.Invoke());

    /// <summary>Perfil da máquina ativa (o mesmo da fresa), para o limite do fuso.</summary>
    public Func<MachineProfile?>? MachineSource { get; set; }

    public Func<string, Task>? CopyText { get; set; }

    /// <summary>Guarda as larguras (árvore, painel) nas preferências do app (as mesmas da fresa).</summary>
    public Action<double, double>? SavePanes
    {
        get => _panes.Save;
        set => _panes.Save = value;
    }

    /// <summary>Larguras guardadas (0 = padrão).</summary>
    public void SetPanes(double tree, double props) => _panes.Set(tree, props);

    private string FileBase()
    {
        string n = string.Concat(_job.Name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ')).Trim().Replace(' ', '_');
        return n.Length > 0 ? n : "TORNO";
    }

    // ---------------------------------------------------------------- abas (mesmas da fresa)

    private readonly Button[] _tabs = new Button[3];
    private int _tab = 1;

    /// <summary>Abas Preparação, Operações e Código NC; o CamView põe esta faixa no topo do módulo.</summary>
    public Control TabStrip { get; }

    /// <summary>Mostra a aba (0 Preparação, 1 Operações, 2 Código NC). A faixa de ícones e o painel direito são só de Operações.</summary>
    private void SetTab(int tab)
    {
        _tab = tab;
        CamShell.StyleTabs(_tabs, tab);
        PrepCenter.IsVisible = tab == 0;
        OpsCenter.IsVisible = tab == 1;
        CodeCenter.IsVisible = tab == 2;
        PaletteBar.IsVisible = tab == 1;
        RightColumn.IsVisible = tab == 1;
        _panes.Fit();
        if (tab == 0)
        {
            BuildPrep();
        }
    }

    // ---------------------------------------------------------------- faixa

    private async Task PaletteClick(CamPaletteItem item)
    {
        switch (item.Action)
        {
            case "abrir-dxf":
                await OpenDxf();
                return;
            case "abrir-trabalho":
                await OpenJob();
                return;
            case "salvar-trabalho":
                await SaveJob();
                return;
        }

        if (CamPalette.LatheUnavailable(item, _job) is { } why)
        {
            Errors.Text = "• " + why;
            return;
        }

        LatheTool? tool = _job.Operations.Count > 0 ? _job.Operations[^1].Tool : null;
        if (CamPalette.NewLatheOperation(item, tool) is not { } spec)
        {
            return;
        }

        Errors.Text = "";
        Snap();
        _job.Operations.Add(spec);
        Refresh(op: _job.Operations.Count - 1);
    }

    /// <summary>Apaga o botão que não serve agora (torneamento sem perfil); a dica diz o porquê.</summary>
    private void UpdatePalette()
    {
        foreach ((CamPaletteItem item, Button button) in _paletteButtons)
        {
            CamShell.SetPaletteState(item, button, CamPalette.LatheUnavailable(item, _job));
        }
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
        _job.Fixation = j.Fixation;
        _job.Operations.Clear();
        _job.Operations.AddRange(j.Operations);
        _undo.Clear();
        UndoButton.IsEnabled = false;
        Refresh();
        // avisos do arquivo (ex.: fixação inválida, que abre como "não informada")
        Errors.Text = string.Join("\n", loaded.Warnings.Select(w => "• " + w));
    }

    /// <summary>Salva o trabalho; campo vazio numa operação bloqueia (o arquivo guardaria o número antigo que não aparece).</summary>
    private async Task SaveJob()
    {
        if (LatheJobInfo.SaveBlockers(_job) is { Count: > 0 } blockers)
        {
            Errors.Text = "• Não dá para salvar com campo vazio:\n" + string.Join("\n", blockers.Select(b => "• " + b));
            return;
        }

        if (SaveJobFile is not null)
        {
            await SaveJobFile(FileBase() + ".casillas-job", LatheJobJson.ToJson(_job));
        }
    }

    // ---------------------------------------------------------------- operações

    private sealed record TreeTag(string Kind, int Index);

    private int? SelectedOp() => (Tree.SelectedItem as TreeViewItem)?.Tag is TreeTag { Kind: "op" } t ? t.Index : null;

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

    private static string OpIcon(LatheOperationSpec s) => s.Kind switch
    {
        LatheOpKind.Turning => "torneamento",
        LatheOpKind.Facing => "faceamento",
        LatheOpKind.Drilling => "furo",
        LatheOpKind.Threading => "rosca",
        _ => "canal",
    };

    /// <summary>Guarda o estado das operações antes de uma alteração (para Desfazer).</summary>
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
        string? keepTag = keep && (Tree.SelectedItem as TreeViewItem)?.Tag is TreeTag { Kind: not "op" } t ? t.Kind : null;
        _busy = true;
        try
        {
            NameBox.Text = _job.Name;
            DialectCombo.SelectedIndex = _job.Dialect == CamDialect.Siemens ? 1 : 0;
            _build = LatheJobBuilder.Build(_job);
            BuildTree(keepOp, keepTag);
        }
        finally
        {
            _busy = false;
        }

        if (props)
        {
            BuildProps();
        }

        Errors.Text = ""; // os erros de geração ficam no rodapé (clicáveis); aqui só os avisos ao abrir arquivo
        Warnings.Text = string.Join("\n", _build.Warnings.Select(w => "⚠ " + w));
        Output.Text = _build.Ok ? _build.Program : "";
        Warnings.Foreground = UiKit.Status("pending").Fg;
        bool gcode = _build.Ok && _build.Program.Length > 0;
        int ready = _build.Items.Count(x => !x.Skipped && x.Ok);
        CodeTitle.Text = gcode ? $"Código CNC ({(_job.Dialect == CamDialect.Siemens ? "Siemens" : "Fanuc")}) · gerado a partir de {ready} operaç{(ready == 1 ? "ão" : "ões")}" : "Código CNC: não gerado";
        Bar.Update(PendingItems(), ready, gcode && ready > 0, gcode);
        CopyButton.IsEnabled = gcode;
        UpdateStatus();
        UpdatePalette();
        if (props && _tab == 0)
        {
            BuildPrep();
        }

        UpdatePreview();
        Update3D();
    }

    /// <summary>Linha de status: operações incompletas e o número do programa que está no trabalho (nada inventado).</summary>
    private void UpdateStatus()
    {
        int incomplete = _job.Operations.Where((o, i) => o.Enabled && OpIncomplete(i)).Count();
        StatusIncomplete.Text = incomplete == 0
            ? (_job.Operations.Count == 0 ? "Nenhuma operação" : "Nenhuma operação incompleta")
            : $"⚠ {incomplete} {(incomplete == 1 ? "operação incompleta" : "operações incompletas")}";
        StatusIncomplete.Foreground = incomplete == 0 ? Avalonia.Media.Brush.Parse("#9aa7b5") : UiKit.Status("pending").Fg;
        StatusProgram.Text = "Programa nº: " + _job.ProgramNumber.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Operação incompleta: campo vazio ou erro ao gerar.</summary>
    private bool OpIncomplete(int i) =>
        _job.Operations[i].MissingFields.Count > 0 || (i < _build.Items.Count && !_build.Items[i].Skipped && !_build.Items[i].Ok);

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
                SetTab(0);
                PrepProfileCard.BringIntoView();
                PointsBox.Focus(NavigationMethod.Tab);
            }));
        }

        if (_job.Operations.Count == 0)
        {
            targets.Add(("pending", () =>
            {
                SetTab(1);
                _paletteButtons.FirstOrDefault(b => b.Button.IsEnabled && b.Item.LatheKind is not null).Button?.Focus(NavigationMethod.Tab);
            }));
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

    /// <summary>Seleciona e foca a operação na árvore, na aba Operações (as propriedades acompanham a seleção).</summary>
    private void GoToOp(int index)
    {
        SetTab(1);
        IEnumerable<TreeViewItem> All(ItemsControl parent) => parent.Items.OfType<TreeViewItem>().SelectMany(x => All(x).Prepend(x));
        if (All(Tree).FirstOrDefault(x => x.Tag is TreeTag { Kind: "op" } t && t.Index == index) is { } item)
        {
            Tree.SelectedItem = item;
            item.BringIntoView();
            item.Focus(NavigationMethod.Tab);
        }
    }

    /// <summary>Alterna 2D/3D e, no 3D, simula o programa atual (o 3D de antes, sem chips).</summary>
    private void Update3D()
    {
        bool on = View3DButton.IsChecked == true;
        Scene3D.IsVisible = on;
        Preview.IsVisible = !on;
        if (on)
        {
            PreviewHint.IsVisible = false;
            Scene3D.Render(_build.Ok ? _build.Program : "", _job.Dialect == CamDialect.Siemens ? Dialect.Siemens : Dialect.Fanuc);
        }
        else
        {
            UpdatePreview();
        }
    }

    /// <summary>Estado da operação na árvore como Pronta/Incompleta (o mesmo da fresa); a cor e o motivo vêm do Core.</summary>
    private static (string Kind, string Text) TreeState((string Kind, string Text) st) => st.Kind switch
    {
        "ok" => ("ok", "Pronta"),
        "bad" => ("bad", "Incompleta: " + (st.Text.StartsWith("erro: ", StringComparison.Ordinal) ? st.Text[6..] : st.Text)),
        _ => st,
    };

    private void BuildTree(int? selectOp, string? selectTag = null)
    {
        Tree.Items.Clear();
        TreeViewItem? select = null;
        TreeViewItem Node(string text, object? tag = null, string? icon = null) =>
            UiKit.StateItem(CamShell.TreeRow(text, null, icon is null ? null : UiKit.Icon(icon, "#9aa7b5", 15)), tag);

        TreeViewItem NodeState(string text, (string Kind, string Text) st, string icon, TreeTag? tag = null)
        {
            TreeViewItem item = UiKit.StateItem(CamShell.TreeRow(text, st, UiKit.Icon(icon, "#9aa7b5", 15)), tag);
            if (tag is not null && tag.Kind == selectTag)
            {
                select = item;
            }

            return item;
        }

        TreeViewItem Soon(string text, string icon, string tip)
        {
            TreeViewItem item = Node(text, null, icon);
            item.Opacity = 0.5;
            ToolTip.SetTip(item, tip);
            return item;
        }

        MachineProfile? machine = MachineSource?.Invoke();
        Tree.Items.Add(NodeState(CamJobInfo.MachineTitle(machine), CamJobInfo.MachineState(machine), "ferramenta"));
        // material ainda não existe no CAM: aparece apagado, sem valor suposto
        Tree.Items.Add(Soon("Material: não definido", "material", "Em breve: o material entra numa próxima etapa."));
        // perfil ausente é pendência (âmbar, como no rodapé)
        Tree.Items.Add(NodeState("Peça · " + LatheJobInfo.ProfileTitle(_job.Profile), _job.Profile is null ? ("pending", "falta definir") : ("ok", "OK"), "perfil", new TreeTag("part", 0)));
        // o torno não tem bruto próprio: só leitura do Ø digitado nas operações
        Tree.Items.Add(NodeState(LatheJobInfo.StockTitle(_job), ("neutral", "das operações · só leitura"), "bruto", new TreeTag("stock", 0)));
        Tree.Items.Add(Soon("Banco de ferramentas", "ferramentas", "Em breve. Hoje a ferramenta vem da tabela do Code, nas propriedades de cada operação."));

        // Fixação 1: o trabalho tem uma fixação e um zero só (Core); dentro, o zero e as operações
        string fixationName = LatheFixation.Title(_job.Fixation)["Fixação: ".Length..];
        TreeViewItem setup = NodeState($"Fixação 1 · G{_job.WorkOffset} · {fixationName}", LatheFixation.State(_job.Fixation), "fixacao", new TreeTag("fixation", 0));
        setup.IsExpanded = true;
        setup.Items.Add(NodeState("Zero: " + LatheJobInfo.OriginTitle(_job), CamJobInfo.OriginState(), "zero", new TreeTag("zero", 0)));
        for (int i = 0; i < _job.Operations.Count; i++)
        {
            TreeViewItem item = OpNode(i, machine);
            setup.Items.Add(item);
            if (selectOp == i)
            {
                select = item;
            }
        }

        Tree.Items.Add(setup);
        Tree.Items.Add(Soon("Fixação 2 · em breve", "fixacao", "Em breve: segunda fixação (ex.: virar a peça) com zero próprio."));
        if (select is not null)
        {
            Tree.SelectedItem = select;
        }
    }

    /// <summary>Linha da operação: selo ✓ (pronta) ou ! (incompleta), ícone, título e olho (o mesmo Enabled de antes).</summary>
    private TreeViewItem OpNode(int i, MachineProfile? machine)
    {
        int index = i;
        LatheOperationSpec spec = _job.Operations[i];
        (string Kind, string Text) st = TreeState(LatheJobInfo.OpState(i < _build.Items.Count ? _build.Items[i] : null, spec, machine?.MaxSpindleRpm));
        bool incomplete = OpIncomplete(i);
        (string mark, string kind) = !spec.Enabled ? ("–", "neutral") : incomplete ? ("!", "pending") : st.Kind == "ok" ? ("✓", "ok") : ("!", "pending");
        Button eye = CamShell.Eye(spec.Enabled, () =>
        {
            if (!_busy)
            {
                SetEnabled(index, !spec.Enabled);
            }
        });
        var lead = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        lead.Children.Add(CamShell.Badge(mark, kind));
        lead.Children.Add(UiKit.Icon(OpIcon(spec), "#9aa7b5", 15));
        string tip = LatheJobInfo.OpTitle(i, spec) + "\n" + st.Text;
        Grid row = CamShell.TreeRow(LatheJobInfo.OpTitle(i, spec), null, lead, eye, tip);
        row.Opacity = spec.Enabled ? 1 : 0.55;
        return UiKit.StateItem(row, new TreeTag("op", i));
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
        PreviewHint.IsVisible = _job.Profile is null && View3DButton.IsChecked != true;
    }
}
