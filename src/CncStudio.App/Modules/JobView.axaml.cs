using CncStudio.Core.Dialects;
using Avalonia.Controls;
using Avalonia.Input;
using CncStudio.App.Settings;
using CncStudio.Core;
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

    /// <summary>Cadeias abertas do último DXF (ficam fora da Peça); só para a dica dos botões apagados.</summary>
    private int _openChains;
    private JobBuild _build = new("", [], [], [], []);

    /// <summary>Perfil da máquina com que <see cref="_build"/> foi feito (o Trabalho refaz a cada mudança dele mesmo, não do perfil).</summary>
    private DataStamp? _builtWith;
    private bool _busy;
    private readonly UndoStack<CamOperationSpec> _undo = new();

    public JobView()
    {
        InitializeComponent();
        BuildPalette();
        OpenDxfButton.Click += async (_, _) => await OpenDxf();
        OpenJobButton.Click += async (_, _) => await OpenJob();
        SaveJobButton.Click += async (_, _) => await SaveJob();
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
        ActualThemeVariantChanged += (_, _) => Refresh(keep: true, props: false);
        Tree.SelectionChanged += (_, _) =>
        {
            if (!_busy)
            {
                BuildProps();
                UpdatePreview();
                UpdatePalette();
            }
        };
        Preview.ContourPicked += (_, index) =>
        {
            if (index >= 0 && index < _job.Contours.Count)
            {
                SelectContourInTree(index);
            }
        };
        // Barra inferior: as mesmas ações de antes (abrir no Code; o programa já é gerado a cada mudança)
        Bar.SendButton.Click += (_, _) =>
        {
            if (!CheckFresh() && _build.Ok && _build.Program.Length > 0)
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
            if (!CheckFresh() && _build.Ok && _build.Program.Length > 0 && CopyText is not null)
            {
                await CopyText(_build.Program);
            }
        };
        NameBox.Text = _job.Name;
        Refresh();
    }

    /// <summary>Contornos e bruto do Trabalho aberto (o Assist lê para sugerir um plano).</summary>
    public IReadOnlyList<Contour> Contours => _job.Contours;

    public JobStock? Stock => _job.Stock;

    /// <summary>Junta operações sugeridas (pelo Assist) ao fim do Trabalho; dá para desfazer.</summary>
    public void AddOperations(IEnumerable<CamOperationSpec> operations)
    {
        List<CamOperationSpec> list = operations.ToList();
        if (list.Count == 0)
        {
            return;
        }

        Snap();
        _job.Operations.AddRange(list);
        Refresh(op: _job.Operations.Count - list.Count);
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

        DxfImportResult r = DxfImporter.ParseInMillimeters(file.Text);
        if (_job.Operations.Count > 0 && _job.Contours.Count > 0)
        {
            Errors.Text = "• O desenho já tem operações. Abra o DXF num trabalho novo (Abrir trabalho… ou reabra o programa) para não misturar contornos.";
            return;
        }

        _job.Contours.Clear();
        _job.Contours.AddRange(r.Contours);
        _openChains = r.OpenChains.Count;
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
        _job.Fixture = j.Fixture;
        _job.Contours.Clear();
        _job.Contours.AddRange(j.Contours);
        _openChains = 0;
        _job.Operations.Clear();
        _job.Operations.AddRange(j.Operations);
        _undo.Clear();
        Refresh();
        // avisos do arquivo (ex.: fixação inválida, que abre como "não definida")
        Errors.Text = string.Join("\n", loaded.Warnings.Select(w => "• " + w));
    }

    /// <summary>Fixação do Trabalho aberto: o Assist lê daqui (fonte única).</summary>
    public JobFixture? Fixture => _job.Fixture;

    private async Task SaveJob()
    {
        if (SaveJobFile is null)
        {
            return;
        }

        if (CamJobInfo.SaveBlockers(_job) is { Count: > 0 } blockers)
        {
            // o arquivo guardaria o número antigo que não aparece na tela (CAM-12): só salva com todos os campos preenchidos
            Errors.Text = "• Não foi salvo. Preencha antes:\n" + string.Join("\n", blockers.Select(b => "• " + b));
            return;
        }

        await SaveJobFile(FileBase() + ".casillas-job", CamJobJson.ToJson(_job));
    }

    // ---------------------------------------------------------------- operações

    private sealed record TreeTag(string Kind, int Index);

    private int? SelectedOp() => (Tree.SelectedItem as TreeViewItem)?.Tag is TreeTag { Kind: "op" } t ? t.Index : null;

    private int? SelectedContour() => (Tree.SelectedItem as TreeViewItem)?.Tag is TreeTag { Kind: "contour" } t ? t.Index : null;

    /// <summary>Botões da faixa, na ordem de <see cref="CamPalette.VisibleGroups"/> (para apagar/acender conforme a seleção).</summary>
    private readonly List<(CamPaletteItem Item, Button Button)> _paletteButtons = [];

    /// <summary>Monta a faixa de operações a partir dos grupos do Core (grupo oculto, como o 3D, não entra).</summary>
    private void BuildPalette()
    {
        Palette.Children.Clear();
        _paletteButtons.Clear();
        bool first = true;
        foreach (CamPaletteGroup group in CamPalette.VisibleGroups)
        {
            if (!first)
            {
                Palette.Children.Add(new Border { Width = 1, Margin = new Avalonia.Thickness(6, 4), Background = Avalonia.Media.Brushes.Gray, Opacity = 0.35 });
            }

            first = false;
            var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 2 };
            foreach (CamPaletteItem item in group.Items)
            {
                var content = new StackPanel { Spacing = 2, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
                content.Children.Add(UiKit.Icon(item.Icon, "#8FB4FF", 20));
                content.Children.Add(new TextBlock { Text = item.Name, FontSize = 11, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center });
                var button = new Button { Content = content, Padding = new Avalonia.Thickness(8, 4), MinWidth = 64, MinHeight = 0, Background = Avalonia.Media.Brushes.Transparent };
                ToolTip.SetShowOnDisabled(button, true);
                Avalonia.Automation.AutomationProperties.SetName(button, "Adicionar " + item.Name);
                CamPaletteItem picked = item;
                button.Click += (_, _) => AddOperation(picked);
                buttons.Children.Add(button);
                _paletteButtons.Add((item, button));
            }

            var box = new StackPanel { Spacing = 0 };
            box.Children.Add(new TextBlock { Text = group.Title, FontSize = 10, Opacity = 0.65, Margin = new Avalonia.Thickness(4, 0, 0, 0) });
            box.Children.Add(buttons);
            Palette.Children.Add(box);
        }

        UpdatePalette();
    }

    private int PaletteTarget() =>
        CamPalette.TargetContour(_job, SelectedContour(), SelectedOp() is { } o && o < _job.Operations.Count ? _job.Operations[o].ContourIndex : null);

    /// <summary>Apaga o botão que não serve à seleção atual; a dica diz o porquê.</summary>
    private void UpdatePalette()
    {
        int target = PaletteTarget();
        foreach ((CamPaletteItem item, Button button) in _paletteButtons)
        {
            string? why = CamPalette.Unavailable(item, _job, target, _openChains);
            button.IsEnabled = why is null;
            ToolTip.SetTip(button, why is null ? item.Hint : $"{item.Hint}\nAgora não: {why}");
        }
    }

    private void AddOperation(CamPaletteItem item)
    {
        Errors.Text = "";
        int contour = PaletteTarget();
        if (CamPalette.Unavailable(item, _job, contour, _openChains) is { } why)
        {
            Errors.Text = "• " + why;
            return;
        }

        CamTool? tool = _job.Operations.Count > 0 ? _job.Operations[^1].Tool : null;
        if (CamPalette.NewOperation(item, contour, tool) is not { } spec)
        {
            return;
        }

        Snap();
        _job.Operations.Add(spec);
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
    /// <summary>Ícone da operação na árvore: o mesmo do botão da faixa que a cria (operação nova só precisa entrar em CamPalette).</summary>
    private static string OpIcon(CamOperationSpec s) => CamPalette.ItemFor(s)?.Icon ?? "operacoes";

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
    /// <summary>Ao abrir o módulo: o banco pode ter mudado no Code; refaz só os avisos (o programa não muda).</summary>
    public void RefreshBank() => ShowWarnings();

    // Avisos do programa e da ferramenta com outro diâmetro no banco (o simulador do Code usa o do banco)
    private void ShowWarnings()
    {
        var bank = (ToolsSource?.Invoke() ?? []).Select(e => (e.Number, e.Diameter));
        IEnumerable<string> lines = _build.Warnings.Concat(CamJobInfo.BankDiameterWarnings(_build.Operations.Select(o => o.Tool), bank));
        Warnings.Text = string.Join("\n", lines.Distinct().Select(w => "• " + w));
        Warnings.Foreground = UiKit.Status("pending").Fg;
    }

    private void Refresh(int? op = null, bool keep = false, bool props = true)
    {
        int? keepOp = keep ? SelectedOp() : op;
        int? keepContour = keep ? SelectedContour() : null;
        _busy = true;
        try
        {
            NameBox.Text = _job.Name;
            DialectCombo.SelectedIndex = _job.Dialect == CamDialect.Siemens ? 1 : 0;
            MachineProfile? machine = MachineSource?.Invoke();
            _build = CamJobBuilder.Build(_job, machine);
            _builtWith = DataStamp.OfMachine(machine);
            // peça, bruto e fixação: o item continua selecionado depois de refazer a árvore (o editor fica aberto)
            string? keepItem = keep && (Tree.SelectedItem as TreeViewItem)?.Tag is TreeTag { Kind: "stock" or "part" or "fixture" } kt ? kt.Kind : null;
            BuildTree(keepOp, keepContour);
            if (keepItem is not null)
            {
                Tree.SelectedItem = Tree.Items.OfType<TreeViewItem>().FirstOrDefault(x => x.Tag is TreeTag t && t.Kind == keepItem);
            }
        }
        finally
        {
            _busy = false;
        }

        UpdatePalette();
        if (props)
        {
            BuildProps();
        }

        Errors.Text = ""; // os erros de geração ficam na barra inferior (clicáveis); aqui só os avisos ao abrir arquivo
        ShowWarnings();
        Output.Text = _build.Ok ? _build.Program : "";
        bool gcode = _build.Ok && _build.Program.Length > 0;
        int ready = _build.Items.Count(x => !x.Skipped && x.Ok);
        CodeTitle.ClearValue(Avalonia.Controls.TextBlock.ForegroundProperty);
        CodeTitle.Text = gcode ? $"Código CNC ({(_job.Dialect == CamDialect.Siemens ? "Siemens" : "Fanuc")}) · gerado a partir de {ready} operaç{(ready == 1 ? "ão" : "ões")}" : "Código CNC: não gerado";
        Bar.Update(PendingItems(), ready, gcode && ready > 0, gcode);
        CopyButton.IsEnabled = gcode;
        UpdatePreview();
        Update3D();
    }

    /// <summary>
    /// Confere se o perfil da máquina mudou depois do último programa (a aba Máquina não avisa o Trabalho). Se mudou, o programa e a
    /// vista ficam como estão, marcados "desatualizado" com o motivo; a barra ganha o item (clique refaz) e Enviar, Gerar e Copiar
    /// desligam. Devolve true se está desatualizado.
    /// </summary>
    public bool CheckFresh()
    {
        if (DataStamp.StaleMessage(_builtWith, DataStamp.OfMachine(MachineSource?.Invoke())) is not { } stale)
        {
            return false;
        }

        bool gcode = _build.Ok && _build.Program.Length > 0;
        int ready = _build.Items.Count(x => !x.Skipped && x.Ok);
        var items = new List<JobBarItem> { new("pending", stale + " Clique aqui para refazer o programa.", () => Refresh(keep: true)) };
        items.AddRange(PendingItems());
        Bar.Update(items, ready, gcode && ready > 0, gcode);
        CopyButton.IsEnabled = false;
        CodeTitle.Text = "Código CNC · " + stale.ToLowerInvariant() + " Não use este código: refaça pela barra.";
        CodeTitle.Foreground = UiKit.Status("pending").Fg;
        return true;
    }

    /// <summary>
    /// Pendências do Core (<see cref="CamJobInfo.Pending"/>, mesmo texto e mesma ordem) com o lugar aonde cada uma leva. Erro de operação
    /// é "inválido" (vermelho); o resto é "pendente" (âmbar).
    /// </summary>
    private List<JobBarItem> PendingItems()
    {
        double? maxRpm = MachineSource?.Invoke()?.MaxSpindleRpm;
        IReadOnlyList<string> texts = CamJobInfo.Pending(_job, _build, maxRpm);
        var targets = new List<(string Kind, Action Go)>();
        if (_job.Contours.Count == 0)
        {
            // leva ao item Peça: lá dá para desenhar uma forma; o "Abrir DXF…" fica logo acima da árvore
            targets.Add(("pending", () => GoToTree(t => t.Kind == "part")));
        }

        if (_job.Stock is null)
        {
            targets.Add(("pending", () => GoToTree(t => t.Kind == "stock")));
        }

        if (_job.Fixture is null)
        {
            // leva ao item da árvore e ao campo do tipo, nas propriedades
            targets.Add(("pending", () =>
            {
                GoToTree(t => t.Kind == "fixture");
                _fixtureKind?.Focus(NavigationMethod.Tab);
            }));
        }

        if (_job.Operations.Count == 0)
        {
            targets.Add(("pending", () => _paletteButtons.FirstOrDefault(b => b.Button.IsEnabled).Button?.Focus(NavigationMethod.Tab)));
        }

        for (int i = 0; i < _build.Items.Count; i++)
        {
            int index = i;
            if (!_build.Items[i].Ok)
            {
                targets.Add(("bad", () => GoToTree(t => t.Kind == "op" && t.Index == index)));
            }
        }

        foreach (int index in CamJobInfo.OverSpindle(_job, maxRpm))
        {
            targets.Add(("pending", () => GoToTree(t => t.Kind == "op" && t.Index == index)));
        }

        // Se um dia o Core mudar a lista sem a tela acompanhar, os itens continuam aparecendo (sem atalho).
        // O Core diz o que trava: a fixação não definida só avisa (os botões continuam ligados).
        return texts.Select((text, k) => k < targets.Count && texts.Count == targets.Count
            ? new JobBarItem(targets[k].Kind, text, targets[k].Go, CamJobInfo.Blocks(text))
            : new JobBarItem("pending", text, () => { }, CamJobInfo.Blocks(text))).ToList();
    }

    /// <summary>Seleciona e foca o item da árvore (as propriedades acompanham a seleção).</summary>
    private void GoToTree(Func<TreeTag, bool> match)
    {
        IEnumerable<TreeViewItem> All(ItemsControl parent) => parent.Items.OfType<TreeViewItem>().SelectMany(x => All(x).Prepend(x));
        if (All(Tree).FirstOrDefault(x => x.Tag is TreeTag t && match(t)) is { } item)
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

    /// <summary>Seleciona o contorno na árvore (o clique na vista 2D); a vista e as propriedades acompanham.</summary>
    private void SelectContourInTree(int index)
    {
        foreach (TreeViewItem group in Tree.Items.OfType<TreeViewItem>())
        {
            foreach (TreeViewItem item in group.Items.OfType<TreeViewItem>())
            {
                if (item.Tag is TreeTag { Kind: "contour" } t && t.Index == index)
                {
                    Tree.SelectedItem = item;
                    return;
                }
            }
        }
    }

    /// <summary>Estado da operação na árvore como Pronta/Incompleta (UX-03a); a cor e o motivo vêm do Core.</summary>
    private static (string Kind, string Text) TreeState((string Kind, string Text) st) => st.Kind switch
    {
        "ok" => ("ok", "Pronta"),
        "bad" => ("bad", "Incompleta: " + (st.Text.StartsWith("erro: ", StringComparison.Ordinal) ? st.Text[6..] : st.Text)),
        _ => st,
    };

    private void BuildTree(int? selectOp, int? selectContour)
    {
        Tree.Items.Clear();
        TreeViewItem? select = null;
        TreeViewItem Node(string text, object? tag = null, string? icon = null) => new() { Header = icon is null ? text : UiKit.IconLabel(icon, text), Tag = tag, IsExpanded = true };

        TreeViewItem NodeState(string text, (string Kind, string Text) st, string icon, TreeTag? tag = null) =>
            UiKit.StateItem(UiKit.StateRow(text, st, UiKit.Icon(icon, "#8FB4FF", 15)), tag);

        TreeViewItem part = Node($"Peça ({_job.Contours.Count} contorno{(_job.Contours.Count == 1 ? "" : "s")})", new TreeTag("part", 0), "peca");
        for (int i = 0; i < _job.Contours.Count; i++)
        {
            TreeViewItem c = Node(CamJobInfo.ContourTitle(_job.Contours[i], i), new TreeTag("contour", i), "contorno");
            part.Items.Add(c);
            if (selectContour == i)
            {
                select = c;
            }
        }

        Tree.Items.Add(part);
        // bruto ausente é pendência (âmbar, como na barra); o texto continua o do Core
        (string Kind, string Text) stock = CamJobInfo.StockState(_job.Stock);
        if (_job.Stock is null)
        {
            stock = ("pending", stock.Text);
        }

        Tree.Items.Add(NodeState(CamJobInfo.StockTitle(_job.Stock), stock, "bruto", new TreeTag("stock", 0)));
        Tree.Items.Add(NodeState(CamJobInfo.OriginTitle(_job), CamJobInfo.OriginState(), "preparo"));
        Tree.Items.Add(NodeState(CamJobInfo.FixtureTitle(_job.Fixture), CamJobInfo.FixtureState(_job.Fixture), "fixacao", new TreeTag("fixture", 0)));
        MachineProfile? machine = MachineSource?.Invoke();
        Tree.Items.Add(NodeState(CamJobInfo.MachineTitle(machine), CamJobInfo.MachineState(machine), "ferramenta"));
        TreeViewItem tools = Node("Ferramentas", null, "ferramentas");
        foreach (string line in CamJobInfo.ToolLines(_job))
        {
            tools.Items.Add(Node(line));
        }

        Tree.Items.Add(tools);
        TreeViewItem ops = Node($"Operações ({_job.Operations.Count})", null, "operacoes");
        for (int i = 0; i < _job.Operations.Count; i++)
        {
            int index = i;
            CamOperationSpec spec = _job.Operations[i];
            var check = new CheckBox { IsChecked = spec.Enabled, MinWidth = 0 };
            // título com reticências: a etiqueta de estado (texto) nunca sai da árvore
            DockPanel row = UiKit.StateRow(CamJobInfo.OpTitle(i, spec), TreeState(CamJobInfo.OpState(i < _build.Items.Count ? _build.Items[i] : null, spec, machine?.MaxSpindleRpm)), check, UiKit.Icon(OpIcon(spec), "#8FB4FF", 15));
            row.Opacity = spec.Enabled ? 1 : 0.5;
            if (OpOrigin.Describe(spec.Origin) is { } origin)
            {
                ToolTip.SetTip(row, origin.Text);
            }

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
        List<List<Point2>> path = [];
        if (SelectedOp() is { } i && i < _build.Items.Count && _build.Items[i].Result is { Operation: { } built })
        {
            path = ToolpathView.CuttingPolylines(built);
        }
        else if (SelectedOp() is null)
        {
            // Nada selecionado (ou grupo "Operações"): mostra o percurso de todas as operações ligadas
            foreach (JobOpResult item in _build.Items)
            {
                if (!item.Skipped && item.Result is { Operation: { } op })
                {
                    path.AddRange(ToolpathView.CuttingPolylines(op));
                }
            }
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
