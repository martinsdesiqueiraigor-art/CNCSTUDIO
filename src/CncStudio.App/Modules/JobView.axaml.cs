using System.Globalization;
using CncStudio.Core.Dialects;
using Avalonia.Controls;
using Avalonia.Input;
using CncStudio.App.Settings;
using CncStudio.Core;
using CncStudio.Core.Cam;
using CncStudio.Core.Setup;

namespace CncStudio.App.Modules;

/// <summary>
/// Tela de trabalho do CAM (fresa): árvore do Job (máquina, material, peça, bruto, ferramentas, fixação 1 com zero e operações),
/// vista 2D/3D e programa. UX-04c: abas Preparação, Operações e Código NC (<see cref="TabStrip"/>, posta no topo pelo CamView),
/// faixa grande de ícones e painel de parâmetros à direita. Só layout: os dados e o arquivo .casillas-job são os de antes.
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
        // chips do 3D: cada mudança vai para o config do app
        Scene3D.OptionsChanged += (_, o) => SaveViewOptions?.Invoke(o.ToText());
        TabStrip = BuildTabs();
        BuildPalette();
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
                // Zero da fixação: o lugar dele é o cartão Zero da Preparação
                if ((Tree.SelectedItem as TreeViewItem)?.Tag is TreeTag { Kind: "zero" } && _tab != 0)
                {
                    SetTab(0);
                    PrepOrigin.BringIntoView();
                }

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
            if (!CheckFresh() && _build.Ok && _build.Program.Length > 0 && CopyText is not null)
            {
                await CopyText(_build.Program);
            }
        };
        TreeSplitter.DragCompleted += (_, _) => PanesDragged();
        PropsSplitter.DragCompleted += (_, _) => PanesDragged();
        TreeSplitter.DoubleTapped += (_, _) => ResetPanes();
        PropsSplitter.DoubleTapped += (_, _) => ResetPanes();
        Panes.SizeChanged += (_, _) => FitPanes();
        NameBox.Text = _job.Name;
        Refresh();
        SetTab(1);
    }

    // ---------------------------------------------------------------- divisórias (UX-04c)

    private const double TreeDefault = 240, TreeMin = 200, TreeMax = 420;
    private const double PropsDefault = 290, PropsMin = 260, PropsMax = 520;
    private const double CenterMin = 480, SplitterWidth = 5;

    /// <summary>Larguras escolhidas (arrastadas ou guardadas); a tela pode mostrar menos se a janela for estreita.</summary>
    private double _treeWidth = TreeDefault, _propsWidth = PropsDefault;

    /// <summary>Guarda as larguras (árvore, painel) nas preferências do app.</summary>
    public Action<double, double>? SavePanes { get; set; }

    /// <summary>Escolhas dos chips do 3D (texto do config do app; inválido = padrão). Não vão para o .casillas-job.</summary>
    public string ViewOptions
    {
        set => Scene3D.Options = CamViewOptions.Parse(value);
    }

    /// <summary>Guarda as escolhas dos chips do 3D nas preferências do app.</summary>
    public Action<string>? SaveViewOptions { get; set; }

    /// <summary>Larguras guardadas (0 = padrão); fora dos limites, volta ao limite.</summary>
    public void SetPanes(double tree, double props)
    {
        _treeWidth = tree > 0 ? Math.Clamp(tree, TreeMin, TreeMax) : TreeDefault;
        _propsWidth = props > 0 ? Math.Clamp(props, PropsMin, PropsMax) : PropsDefault;
        FitPanes();
    }

    private void PanesDragged()
    {
        _treeWidth = Math.Clamp(Panes.ColumnDefinitions[0].ActualWidth, TreeMin, TreeMax);
        if (_tab == 1)
        {
            _propsWidth = Math.Clamp(Panes.ColumnDefinitions[4].ActualWidth, PropsMin, PropsMax);
        }

        SavePanes?.Invoke(_treeWidth, _propsWidth);
        FitPanes();
    }

    private void ResetPanes()
    {
        _treeWidth = TreeDefault;
        _propsWidth = PropsDefault;
        SavePanes?.Invoke(0, 0);
        FitPanes();
    }

    /// <summary>
    /// Aplica as larguras sem passar da janela: o centro fica com pelo menos 480 px; se não couber, o painel e depois a árvore
    /// encolhem até o mínimo, e só então o centro cede (nada fica além da borda direita). Fora da aba Operações o painel some.
    /// </summary>
    private void FitPanes()
    {
        bool props = _tab == 1;
        double avail = Panes.Bounds.Width;
        double tree = _treeWidth, side = props ? _propsWidth : 0;
        double fixedParts = SplitterWidth + (props ? SplitterWidth : 0);
        if (avail > 0)
        {
            double over = tree + side + fixedParts + CenterMin - avail;
            if (over > 0 && props)
            {
                double cut = Math.Min(over, side - PropsMin);
                side -= cut;
                over -= cut;
            }

            if (over > 0)
            {
                tree -= Math.Min(over, tree - TreeMin);
            }
        }

        Panes.ColumnDefinitions[0].Width = new GridLength(tree);
        Panes.ColumnDefinitions[4].MinWidth = props ? PropsMin : 0;
        Panes.ColumnDefinitions[4].Width = new GridLength(side);
        Panes.ColumnDefinitions[3].Width = new GridLength(props ? SplitterWidth : 0);
        PropsSplitter.IsVisible = props;
        Panes.ColumnDefinitions[2].MinWidth = avail > 0 ? Math.Max(0, Math.Min(CenterMin, avail - tree - side - fixedParts)) : 0;
    }

    // ---------------------------------------------------------------- abas (UX-04c)

    private readonly Button[] _tabs = new Button[3];
    private int _tab = 1;

    /// <summary>Abas Preparação, Operações e Código NC; o CamView põe esta faixa no topo do módulo.</summary>
    public Control TabStrip { get; }

    private StackPanel BuildTabs()
    {
        var strip = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 2, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
        string[] names = ["Preparação", "Operações", "Código NC"];
        for (int k = 0; k < names.Length; k++)
        {
            int index = k;
            var tab = new Button
            {
                Content = names[k], Padding = new Avalonia.Thickness(16, 7), FontSize = 13, CornerRadius = new Avalonia.CornerRadius(8, 8, 0, 0),
                BorderThickness = new Avalonia.Thickness(0, 0, 0, 2), Background = Avalonia.Media.Brushes.Transparent,
            };
            tab.Click += (_, _) => SetTab(index);
            _tabs[k] = tab;
            strip.Children.Add(tab);
        }

        return strip;
    }

    /// <summary>Mostra a aba (0 Preparação, 1 Operações, 2 Código NC). A faixa de ícones e o painel direito são só de Operações.</summary>
    private void SetTab(int tab)
    {
        _tab = tab;
        for (int k = 0; k < _tabs.Length; k++)
        {
            bool on = k == tab;
            // a aba escolhida fica laranja (seleção) com traço embaixo; as outras, cinza
            _tabs[k].Foreground = Avalonia.Media.Brush.Parse(on ? "#ffb066" : "#9aa7b5");
            _tabs[k].BorderBrush = on ? Avalonia.Media.Brush.Parse("#ff8a1f") : Avalonia.Media.Brushes.Transparent;
            _tabs[k].FontWeight = on ? Avalonia.Media.FontWeight.SemiBold : Avalonia.Media.FontWeight.Normal;
        }

        PrepCenter.IsVisible = tab == 0;
        OpsCenter.IsVisible = tab == 1;
        CodeCenter.IsVisible = tab == 2;
        PaletteBar.IsVisible = tab == 1;
        RightColumn.IsVisible = tab == 1;
        FitPanes();
        if (tab == 0)
        {
            BuildPrep();
        }
    }

    /// <summary>Onde os editores escrevem: o painel direito, ou um cartão da Preparação enquanto ele é montado.</summary>
    private StackPanel? _propsTarget;

    private StackPanel PropsPanel => _propsTarget ?? OpPanel;

    /// <summary>Campo do tipo de fixação no cartão da Preparação (a barra leva o foco até ele nessa aba).</summary>
    private ComboBox? _prepFixtureKind;

    /// <summary>Cartões da Preparação com os mesmos editores do painel direito (bruto e fixação) e a origem atual (só texto).</summary>
    private void BuildPrep()
    {
        ComboBox? keep = _fixtureKind;
        try
        {
            _propsTarget = PrepStock;
            PrepStock.Children.Clear();
            StockProps();
            _propsTarget = PrepFixture;
            PrepFixture.Children.Clear();
            FixtureProps();
            _prepFixtureKind = _fixtureKind;
        }
        finally
        {
            _propsTarget = null;
            _fixtureKind = keep;
        }

        PrepOrigin.Children.Clear();
        _propsTarget = PrepOrigin;
        try
        {
            ZeroProps();
        }
        finally
        {
            _propsTarget = null;
        }
    }

    /// <summary>Zero da fixação 1: só o que o trabalho já tem (G5x, X0 Y0 do desenho, topo das operações). Sem campo novo.</summary>
    private void ZeroProps()
    {
        PropsPanel.Children.Add(new TextBlock { Text = "Zero da fixação 1", FontSize = 18, FontWeight = Avalonia.Media.FontWeight.Bold });
        PropsPanel.Children.Add(new TextBlock { Text = CamJobInfo.OriginTitle(_job), TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 });
        PropsPanel.Children.Add(UiKit.StatusNote("Origem " + CamJobInfo.OriginState().Text + ": confira na máquina. Escolher o zero pela peça e ter um zero por fixação vêm em breve.", "pending"));
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

    /// <summary>
    /// Monta a faixa grande a partir dos grupos do Core (grupo oculto, como o 3D, não entra): ícone de 2 cores, nome e, nos
    /// botões que ainda não existem, o selo "em breve" (apagados). Arquivo abre e salva como os botões antigos da coluna.
    /// </summary>
    private void BuildPalette()
    {
        Palette.Children.Clear();
        _paletteButtons.Clear();
        bool first = true;
        foreach (CamPaletteGroup group in CamPalette.VisibleGroups)
        {
            if (!first)
            {
                Palette.Children.Add(new Border { Width = 1, Margin = new Avalonia.Thickness(6, 2), Background = Avalonia.Media.Brush.Parse("#1f2733") });
            }

            first = false;
            var buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 3 };
            foreach (CamPaletteItem item in group.Items)
            {
                var content = new StackPanel { Spacing = 2, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center };
                content.Children.Add(UiKit.Icon2(item.Icon, "#e6edf3", "#ff8a1f", 24));
                content.Children.Add(new TextBlock
                {
                    Text = item.Name, FontSize = 11, LineHeight = 13, MaxLines = 2, TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                    TextAlignment = Avalonia.Media.TextAlignment.Center, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                });
                if (item.Soon)
                {
                    content.Children.Add(new TextBlock { Text = "em breve", FontSize = 10, Foreground = Avalonia.Media.Brush.Parse("#9aa7b5"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center });
                }

                var button = new Button
                {
                    Content = content, Width = 66, Height = 76, Padding = new Avalonia.Thickness(2, 4), MinHeight = 0,
                    VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Center, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    Background = Avalonia.Media.Brush.Parse("#18202b"), BorderBrush = Avalonia.Media.Brush.Parse("#243040"),
                    BorderThickness = new Avalonia.Thickness(1), CornerRadius = new Avalonia.CornerRadius(8),
                    Opacity = item.Soon ? 0.45 : 1,
                };
                ToolTip.SetShowOnDisabled(button, true);
                Avalonia.Automation.AutomationProperties.SetName(button, item.Action is not null ? item.Name : item.Soon ? item.Name + " (em breve)" : "Adicionar " + item.Name);
                CamPaletteItem picked = item;
                button.Click += async (_, _) => await PaletteClick(picked);
                buttons.Children.Add(button);
                _paletteButtons.Add((item, button));
            }

            var box = new StackPanel { Spacing = 3 };
            box.Children.Add(buttons);
            box.Children.Add(new TextBlock { Text = group.Title.ToUpperInvariant(), FontSize = 10, Foreground = Avalonia.Media.Brush.Parse("#9aa7b5"), HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center });
            Palette.Children.Add(box);
        }

        UpdatePalette();
    }

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

        AddOperation(item);
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
            string? keepItem = keep && (Tree.SelectedItem as TreeViewItem)?.Tag is TreeTag { Kind: "stock" or "part" or "fixture" or "zero" } kt ? kt.Kind : null;
            BuildTree(keepOp, keepContour);
            if (keepItem is not null)
            {
                Tree.SelectedItem = AllItems(Tree).FirstOrDefault(x => x.Tag is TreeTag t && t.Kind == keepItem);
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
        UpdateStatus();
        if (props && _tab == 0)
        {
            BuildPrep();
        }

        UpdatePreview();
        Update3D();
    }

    /// <summary>
    /// Linha de status: operações incompletas (campo vazio, inválido ou erro ao gerar) e o número do programa que está no trabalho
    /// (o Core sempre tem um; nada é inventado aqui).
    /// </summary>
    private void UpdateStatus()
    {
        int incomplete = _job.Operations.Where((o, i) => o.Enabled && OpIncomplete(i)).Count();
        StatusIncomplete.Text = incomplete == 0
            ? (_job.Operations.Count == 0 ? "Nenhuma operação" : "Nenhuma operação incompleta")
            : $"⚠ {incomplete} {(incomplete == 1 ? "operação incompleta" : "operações incompletas")}";
        StatusIncomplete.Foreground = incomplete == 0 ? Avalonia.Media.Brush.Parse("#9aa7b5") : UiKit.Status("pending").Fg;
        StatusProgram.Text = "Programa nº: " + _job.ProgramNumber.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>Operação incompleta: campo vazio ou inválido que ela usa (CAM-12) ou erro ao gerar.</summary>
    private bool OpIncomplete(int i)
    {
        CamOperationSpec s = _job.Operations[i];
        return CamJobInfo.MissingFields(s).Count > 0 || CamJobInfo.InvalidFields(s).Count > 0 || (i < _build.Items.Count && !_build.Items[i].Skipped && !_build.Items[i].Ok);
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
            targets.Add(("pending", () =>
            {
                SetTab(1);
                GoToTree(t => t.Kind == "part");
            }));
        }

        if (_job.Stock is null)
        {
            targets.Add(("pending", () =>
            {
                GoToTree(t => t.Kind == "stock");
                SetTab(0);
            }));
        }

        if (_job.Fixture is null)
        {
            // leva ao item da árvore e ao campo do tipo, nas propriedades
            targets.Add(("pending", () =>
            {
                GoToTree(t => t.Kind == "fixture");
                SetTab(0);
                _prepFixtureKind?.Focus(NavigationMethod.Tab);
            }));
        }

        if (_job.Operations.Count == 0)
        {
            targets.Add(("pending", () =>
            {
                SetTab(1);
                _paletteButtons.FirstOrDefault(b => b.Button.IsEnabled && b.Item.Kind is not null).Button?.Focus(NavigationMethod.Tab);
            }));
        }

        for (int i = 0; i < _build.Items.Count; i++)
        {
            int index = i;
            if (!_build.Items[i].Ok)
            {
                targets.Add(("bad", () =>
                {
                    SetTab(1);
                    GoToTree(t => t.Kind == "op" && t.Index == index);
                }));
            }
        }

        foreach (int index in CamJobInfo.OverSpindle(_job, maxRpm))
        {
            targets.Add(("pending", () =>
            {
                SetTab(1);
                GoToTree(t => t.Kind == "op" && t.Index == index);
            }));
        }

        // Se um dia o Core mudar a lista sem a tela acompanhar, os itens continuam aparecendo (sem atalho).
        // O Core diz o que trava: a fixação não definida só avisa (os botões continuam ligados).
        return texts.Select((text, k) => k < targets.Count && texts.Count == targets.Count
            ? new JobBarItem(targets[k].Kind, text, targets[k].Go, CamJobInfo.Blocks(text))
            : new JobBarItem("pending", text, () => { }, CamJobInfo.Blocks(text))).ToList();
    }

    private static IEnumerable<TreeViewItem> AllItems(ItemsControl parent) => parent.Items.OfType<TreeViewItem>().SelectMany(x => AllItems(x).Prepend(x));

    /// <summary>Seleciona e foca o item da árvore (as propriedades acompanham a seleção).</summary>
    private void GoToTree(Func<TreeTag, bool> match)
    {
        if (AllItems(Tree).FirstOrDefault(x => x.Tag is TreeTag t && match(t)) is { } item)
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
            Scene3D.Render(_build.Ok ? _build.Program : "", _job.Dialect == CamDialect.Siemens ? Dialect.Siemens : Dialect.Fanuc, _job.Stock, [.. _job.Contours]);
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
        TreeViewItem Node(string text, object? tag = null, string? icon = null) =>
            UiKit.StateItem(TreeRow(text, null, icon is null ? null : UiKit.Icon(icon, "#9aa7b5", 15)), tag);

        TreeViewItem NodeState(string text, (string Kind, string Text) st, string icon, TreeTag? tag = null) =>
            UiKit.StateItem(TreeRow(text, st, UiKit.Icon(icon, "#9aa7b5", 15)), tag);

        MachineProfile? machine = MachineSource?.Invoke();
        Tree.Items.Add(NodeState(CamJobInfo.MachineTitle(machine), CamJobInfo.MachineState(machine), "ferramenta"));
        // material ainda não existe no CAM: aparece apagado, sem valor suposto
        TreeViewItem material = Node("Material: não definido", null, "material");
        material.Opacity = 0.5;
        ToolTip.SetTip(material, "Em breve: o material entra numa próxima etapa.");
        Tree.Items.Add(material);
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

        part.IsExpanded = _job.Contours.Count <= 4;
        Tree.Items.Add(part);
        // bruto ausente é pendência (âmbar, como na barra); o texto continua o do Core
        (string Kind, string Text) stock = CamJobInfo.StockState(_job.Stock);
        if (_job.Stock is null)
        {
            stock = ("pending", stock.Text);
        }

        Tree.Items.Add(NodeState(CamJobInfo.StockTitle(_job.Stock), stock, "bruto", new TreeTag("stock", 0)));
        TreeViewItem tools = Node("Ferramentas", null, "ferramentas");
        foreach (string line in CamJobInfo.ToolLines(_job))
        {
            tools.Items.Add(Node(line));
        }

        tools.IsExpanded = false;
        Tree.Items.Add(tools);

        // Fixação 1: o trabalho tem uma fixação e um zero só (Core); dentro, o zero e as operações
        string fixtureName = _job.Fixture is { } fx ? JobFixture.NameOf(fx.Kind) ?? fx.Kind : "não definida";
        TreeViewItem setup = NodeState($"Fixação 1 · G{_job.WorkOffset} · {fixtureName}", CamJobInfo.FixtureState(_job.Fixture), "fixacao", new TreeTag("fixture", 0));
        setup.Items.Add(NodeState("Zero: " + CamJobInfo.OriginTitle(_job), CamJobInfo.OriginState(), "zero", new TreeTag("zero", 0)));
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
        if (select is not null)
        {
            Tree.SelectedItem = select;
        }
    }

    /// <summary>
    /// Linha da operação: selo ✓ (pronta) ou ! (incompleta: campo vazio ou inválido, ou erro ao gerar), ícone, título e olho
    /// (liga e desliga, o mesmo Enabled de antes). O motivo fica na dica.
    /// </summary>
    private TreeViewItem OpNode(int i, MachineProfile? machine)
    {
        int index = i;
        CamOperationSpec spec = _job.Operations[i];
        (string Kind, string Text) st = TreeState(CamJobInfo.OpState(i < _build.Items.Count ? _build.Items[i] : null, spec, machine?.MaxSpindleRpm));
        bool incomplete = OpIncomplete(i);
        (string mark, string kind) = !spec.Enabled ? ("–", "neutral") : incomplete ? ("!", "pending") : st.Kind == "ok" ? ("✓", "ok") : ("!", "pending");
        (Avalonia.Media.IBrush fg, Avalonia.Media.IBrush bg) = UiKit.Status(kind);
        var badge = new Border
        {
            Width = 17, Height = 17, CornerRadius = new Avalonia.CornerRadius(9), Background = bg, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Child = new TextBlock { Text = mark, FontSize = 11, FontWeight = Avalonia.Media.FontWeight.Bold, Foreground = fg, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center },
        };
        var eye = new Button
        {
            Content = UiKit.Icon(spec.Enabled ? "olho" : "olho-fechado", spec.Enabled ? "#9aa7b5" : "#5b6675", 15),
            Padding = new Avalonia.Thickness(3), MinHeight = 0, MinWidth = 0, Background = Avalonia.Media.Brushes.Transparent, BorderThickness = new Avalonia.Thickness(0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
        };
        string eyeText = spec.Enabled ? "Desligar a operação (não entra no programa)" : "Ligar a operação";
        ToolTip.SetTip(eye, eyeText);
        Avalonia.Automation.AutomationProperties.SetName(eye, eyeText);
        eye.Click += (_, _) =>
        {
            if (!_busy)
            {
                SetEnabled(index, !spec.Enabled);
            }
        };

        var lead = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 6 };
        lead.Children.Add(badge);
        lead.Children.Add(UiKit.Icon(OpIcon(spec), "#9aa7b5", 15));
        string tip = CamJobInfo.OpTitle(i, spec) + "\n" + (incomplete && st.Kind == "ok" ? "Incompleta: falta preencher campo" : st.Text);
        if (OpOrigin.Describe(spec.Origin) is { } origin)
        {
            tip += "\n" + origin.Text;
        }

        Grid row = TreeRow(CamJobInfo.OpTitle(i, spec), null, lead, eye, tip);
        row.Opacity = spec.Enabled ? 1 : 0.55;
        return UiKit.StateItem(row, new TreeTag("op", i));
    }

    /// <summary>
    /// Linha da árvore em 240 px: ícone (e selo) à esquerda, nome com reticências (a dica mostra inteiro), estado em segunda linha
    /// pequena e uma coluna fixa à direita (olho nas operações), que nunca cobre o nome.
    /// </summary>
    private static Grid TreeRow(string title, (string Kind, string Text)? state, Control? lead, Control? right = null, string? tip = null)
    {
        var g = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,24"),
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
        };
        if (lead is not null)
        {
            lead.Margin = new Avalonia.Thickness(0, 0, 6, 0);
            lead.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            Grid.SetRowSpan(lead, 2);
            g.Children.Add(lead);
        }

        var text = new TextBlock { Text = title, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
        Grid.SetColumn(text, 1);
        g.Children.Add(text);
        if (state is { } st)
        {
            var small = new TextBlock { Text = st.Text, FontSize = 10.5, Foreground = UiKit.Status(st.Kind).Fg, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
            Grid.SetColumn(small, 1);
            Grid.SetRow(small, 1);
            g.Children.Add(small);
        }

        if (right is not null)
        {
            Grid.SetColumn(right, 2);
            Grid.SetRowSpan(right, 2);
            right.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
            g.Children.Add(right);
        }

        ToolTip.SetTip(text, tip ?? (state is { } s2 ? title + "\n" + s2.Text : title));
        return g;
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
