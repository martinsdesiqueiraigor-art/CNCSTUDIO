using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CncStudio.Core.Cam;

namespace CncStudio.App.Modules;

/// <summary>
/// Tela do banco de ferramentas (UX-04h B1, mockup G4): bancos à esquerda, abas por tipo e tabela no centro, detalhe à direita.
/// Grava ao Aplicar (um arquivo por banco). Nada é suposto: campo vazio fica "ausente" e salvar nunca é bloqueado por isso.
/// </summary>
public sealed class ToolBankView : UserControl
{
    private const string Gray = "#6b7a8c", Muted = "#9aa7b5", Orange = "#ffb066";

    private sealed record Field(string Label, int Tab, Func<BankTool, string?> Get, Func<BankTool, string?, BankTool> Set, Func<ToolBankMachine, BankToolType?, bool> Show);

    private static readonly Dictionary<BankToolType, string> Names = new()
    {
        [BankToolType.EndMill] = "Fresa de topo", [BankToolType.FaceMill] = "Fresa de facear", [BankToolType.Tap] = "Macho",
        [BankToolType.BallEndMill] = "Esférica", [BankToolType.BullNoseEndMill] = "Toroidal", [BankToolType.Drill] = "Broca",
        [BankToolType.CenterDrill] = "Broca de centro (só cadastro)", [BankToolType.Reamer] = "Alargador (só cadastro)",
        [BankToolType.BoringHead] = "Mandrilhadora (só cadastro)", [BankToolType.TurnExternal] = "Desbaste externo",
        [BankToolType.TurnInternal] = "Barra de furar (desbaste interno)", [BankToolType.PartExternal] = "Bedame externo",
        [BankToolType.PartInternal] = "Bedame interno", [BankToolType.ThreadExternal] = "Rosca externa", [BankToolType.ThreadInternal] = "Rosca interna",
    };

    private static bool Mill(ToolBankMachine m, BankToolType? _) => m == ToolBankMachine.Mill;

    private static readonly Field[] Fields =
    [
        new("NOME", 0, t => t.Name, (t, v) => t with { Name = v }, (_, _) => true),
        new("MATERIAL DA FERRAMENTA", 0, t => t.ToolMaterial, (t, v) => t with { ToolMaterial = v }, (_, _) => true),
        new("Ø (mm)", 1, t => Fmt(t.Diameter), (t, v) => t with { Diameter = Num(v) }, (_, _) => true),
        new("ARESTAS", 1, t => t.Flutes?.ToString(), (t, v) => t with { Flutes = Num(v) is { } f ? (f == Math.Floor(f) && f < 1000 ? (int)f : throw new FormatException()) : null }, Mill),
        new("COMPR. DE CORTE", 1, t => Fmt(t.CuttingLength), (t, v) => t with { CuttingLength = Num(v) }, Mill),
        new("COMPR. TOTAL", 1, t => Fmt(t.OverallLength), (t, v) => t with { OverallLength = Num(v) }, (_, _) => true),
        new("SUPORTE Ø", 1, t => Fmt(t.HolderDiameter), (t, v) => t with { HolderDiameter = Num(v) }, Mill),
        new("SUPORTE COMPR.", 1, t => Fmt(t.HolderLength), (t, v) => t with { HolderLength = Num(v) }, Mill),
        new("RAIO DE CANTO", 1, t => Fmt(t.CornerRadius), (t, v) => t with { CornerRadius = Num(v) }, Mill),
        new("ÂNGULO DA PONTA (°)", 1, t => Fmt(t.TipAngle), (t, v) => t with { TipAngle = Num(v) }, (_, y) => y is BankToolType.Drill or BankToolType.CenterDrill),
        new("LARGURA DO INSERTO", 1, t => Fmt(t.InsertWidth), (t, v) => t with { InsertWidth = Num(v) }, (_, y) => BankToolTypes.IsParting(y)),
        new("CÓDIGO ISO DO INSERTO", 1, t => t.IsoCode, (t, v) => t with { IsoCode = v }, (m, _) => m == ToolBankMachine.Lathe),
        new("RAIO DE PONTA", 1, t => Fmt(t.NoseRadius), (t, v) => t with { NoseRadius = Num(v) }, (m, _) => m == ToolBankMachine.Lathe),
        new("ROTAÇÃO (rpm)", 2, t => Fmt(t.Rpm), (t, v) => t with { Rpm = Num(v) }, (_, _) => true),
        new("AVANÇO XY (mm/min)", 2, t => Fmt(t.FeedXY), (t, v) => t with { FeedXY = Num(v) }, (_, _) => true),
        new("AVANÇO Z (mm/min)", 2, t => Fmt(t.FeedZ), (t, v) => t with { FeedZ = Num(v) }, (_, _) => true),
    ];

    private string _folder = "";
    private readonly List<StoredToolBank> _banks = [];
    private readonly List<string> _notes = [];
    private int _bank;
    private string _tab = "all";
    private int? _selected;
    private BankTool? _draft;
    private bool _isNew;
    private int _detailTab;
    private readonly Dictionary<string, string?> _texts = [];

    private readonly StackPanel _bankList = new() { Spacing = 6 }, _rows = new(), _detail = new() { Spacing = 9 };
    private readonly WrapPanel _tabs = new();

    // Avisos no pé da coluna da esquerda: DockPanel (não StackPanel) para a lista da migração rolar no espaço que sobra e o "Entendi" ficar à vista
    private readonly DockPanel _notesPanel = new() { LastChildFill = true };
    private readonly TextBox _search = new() { Width = 200, Watermark = "Buscar por T, nome ou Ø" };
    private readonly TextBlock _message = new() { FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 420 };
    private readonly Button _newTool = Btn("+ Nova ferramenta", true), _duplicate = Btn("Duplicar"), _delete = Btn("Excluir"), _export = Btn("Exportar banco");

    /// <summary>Os bancos mudaram (gravados): o simulador relê.</summary>
    public event Action<IReadOnlyList<ToolBank>>? BanksChanged;

    /// <summary>O usuário dispensou a lista do que não migrou.</summary>
    public event Action? NotesDismissed;

    public ToolBankView()
    {
        Background = Brush.Parse("#0d1117");
        var import = Btn("Importar banco");
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        bar.Children.AddRange([_newTool, _duplicate, _delete, new Border { Width = 1, Height = 20, Background = Brush.Parse("#243040") }, import, _export, _message]);
        var top = new DockPanel { Background = Brush.Parse("#121821") };
        DockPanel.SetDock(_search, Dock.Right);
        top.Children.AddRange([_search, bar]);
        var topBorder = new Border { Child = top, Padding = new Thickness(14, 8), BorderBrush = Brush.Parse("#1f2733"), BorderThickness = new Thickness(0, 0, 0, 1), Background = Brush.Parse("#121821") };

        var newBank = Btn("+ Novo banco");
        var left = new DockPanel { Width = 210, Background = Brush.Parse("#131922") };
        var leftTop = new StackPanel { Spacing = 6, Margin = new Thickness(10) };
        leftTop.Children.AddRange([Label("BANCOS"), _bankList, newBank]);
        // O pé ocupa só o que sobra abaixo da lista de bancos, alinhado embaixo: com poucos avisos fica onde estava; com muitos, a lista rola
        var leftBottom = new DockPanel { Margin = new Thickness(10), VerticalAlignment = VerticalAlignment.Bottom };
        var limits = Muted11("Fresa: T de 1 a 9999.\nTorno: T de 1 a 99.\nO T é único dentro de cada banco.");
        DockPanel.SetDock(limits, Dock.Top);
        leftBottom.Children.AddRange([limits, _notesPanel]);
        DockPanel.SetDock(leftTop, Dock.Top);
        left.Children.AddRange([leftTop, leftBottom]);

        var center = new DockPanel();
        var tabsBorder = new Border { Child = _tabs, Background = Brush.Parse("#0f141b"), Padding = new Thickness(8, 0) };
        var header = Row(["T", "Nome", "Tipo", "Ø (mm)", "Compr. corte", "Estado"], Muted);
        header.Background = Brush.Parse("#121821");
        var foot = Muted11("\"Incompleta\" = falta algo para escolher a ferramenta na operação (o tipo, o Ø ou a largura do inserto). Salvar nunca é bloqueado por campo vazio.");
        foot.Margin = new Thickness(12, 8);
        foreach ((Control c, Dock d) in new (Control, Dock)[] { (tabsBorder, Dock.Top), (header, Dock.Top), (foot, Dock.Bottom) })
        {
            DockPanel.SetDock(c, d);
            center.Children.Add(c);
        }

        center.Children.Add(new ScrollViewer { Content = _rows });
        var right = new ScrollViewer { Width = 372, Background = Brush.Parse("#131922"), Content = new Border { Child = _detail, Padding = new Thickness(12) } };
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(center, 1);
        Grid.SetColumn(right, 2);
        body.Children.AddRange([left, center, right]);
        var root = new DockPanel();
        DockPanel.SetDock(topBorder, Dock.Top);
        root.Children.AddRange([topBorder, body]);
        Content = root;

        _search.TextChanged += (_, _) => Rebuild();
        _newTool.Click += (_, _) => NewTool();
        _duplicate.Click += (_, _) => Duplicate();
        _delete.Click += (_, _) => AskDelete();
        import.Click += async (_, _) => await ImportAsync();
        _export.Click += async (_, _) => await ExportAsync();
        newBank.Click += (_, _) => NewBankForm();
    }

    /// <summary>Bancos lidos da pasta, o motivo dos arquivos que não abriram e o que não migrou.</summary>
    public void Load(string folder, ToolBankFolder loaded, IEnumerable<string> migrationNotes)
    {
        _folder = folder;
        _banks.Clear();
        _banks.AddRange(loaded.Banks);
        _notes.Clear();
        _notes.AddRange(migrationNotes);
        _notes.AddRange(loaded.Problems.Select(p => "Arquivo ignorado: " + p));
        Select(_bank = 0, null);
    }

    private StoredToolBank? Current => _bank < _banks.Count ? _banks[_bank] : null;

    private bool Editable => Current is { ReadOnly: false };

    // ── montagem ──

    private void Rebuild()
    {
        _bankList.Children.Clear();
        for (int i = 0; i < _banks.Count; i++)
        {
            int index = i;
            ToolBank b = _banks[i].Bank;
            var item = new Button { HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(9, 7), Background = Brush.Parse(i == _bank ? "#2b1a0a" : "#131922"), BorderBrush = Brush.Parse(i == _bank ? "#ff8a1f" : "#131922") };
            var row = new DockPanel();
            var count = new TextBlock { Text = b.Tools.Count.ToString(), Foreground = Brush.Parse(i == _bank ? Orange : Muted) };
            DockPanel.SetDock(count, Dock.Right);
            row.Children.AddRange([count, new TextBlock { Text = b.Name + (b.Machine == ToolBankMachine.Lathe && !b.Name.Contains("torno", StringComparison.CurrentCultureIgnoreCase) ? " · torno" : "") + (_banks[i].ReadOnly ? " · só leitura" : ""), TextWrapping = TextWrapping.Wrap, Foreground = Brush.Parse(i == _bank ? Orange : "#e6edf3") }]);
            item.Content = row;
            item.Click += (_, _) =>
            {
                _message.Text = "";
                Select(_bank = index, null);
            };
            _bankList.Children.Add(item);
        }

        _notesPanel.Children.Clear();
        void AddNote(Control note, Dock dock)
        {
            note.Margin = new Thickness(0, 6, 0, 0);
            DockPanel.SetDock(note, dock);
            _notesPanel.Children.Add(note);
        }

        int toCheck = _banks.Sum(s => s.Bank.Tools.Count(t => t.ToCheck));
        if (toCheck > 0)
        {
            AddNote(UiKit.StatusNote($"{toCheck} ferramenta(s) vieram da tabela antiga ou de importação e estão \"a conferir\".", "pending"), Dock.Top);
        }

        if (Current is { Warning: { } warn })
        {
            AddNote(UiKit.StatusNote(warn, "pending"), Dock.Top);
        }

        if (_notes.Count > 0)
        {
            var dismiss = Btn("Entendi");
            dismiss.Click += (_, _) =>
            {
                _notes.Clear();
                NotesDismissed?.Invoke();
                Rebuild();
            };
            // "Entendi" preso embaixo; a caixa dos avisos é o último filho e rola no espaço que sobra
            AddNote(dismiss, Dock.Bottom);
            AddNote(new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = UiKit.StatusNote("Avisos da migração:\n• " + string.Join("\n• ", _notes), "bad"),
            }, Dock.Bottom);
        }

        _newTool.IsEnabled = Editable;
        _duplicate.IsEnabled = _delete.IsEnabled = Editable && _selected is not null && !_isNew;
        _export.IsEnabled = Current is not null;
        BuildList();
        BuildDetail();
    }

    private void BuildList()
    {
        _tabs.Children.Clear();
        _rows.Children.Clear();
        if (Current is not { } stored)
        {
            _rows.Children.Add(Muted11("Nenhum banco. Use \"+ Novo banco\" ou \"Importar banco\".", 12));
            return;
        }

        IReadOnlyList<BankTool> tools = stored.Bank.Tools;
        void Tab(string key, string text)
        {
            var b = new Button { Content = text, Background = Brushes.Transparent, Padding = new Thickness(12, 7), FontSize = 12, BorderThickness = new Thickness(0, 0, 0, 2), BorderBrush = Brush.Parse(_tab == key ? "#ff8a1f" : "#0f141b"), Foreground = Brush.Parse(_tab == key ? Orange : Muted) };
            b.Click += (_, _) =>
            {
                _tab = key;
                BuildList();
            };
            _tabs.Children.Add(b);
        }

        Tab("all", $"Todas ({tools.Count})");
        foreach (BankToolType type in tools.Where(t => t.Type is not null).Select(t => t.Type!.Value).Distinct().Order())
        {
            Tab(BankToolTypes.Code(type), Names[type].Replace(" (só cadastro)", ""));
        }

        if (tools.Any(t => t.Type is null))
        {
            Tab("none", "Sem tipo");
        }

        Tab("inactive", $"Inativas ({tools.Count(t => !t.Active)})");
        foreach (BankTool t in tools.OrderBy(t => t.Number).Where(Visible))
        {
            Grid row = Row([$"T{t.Number}", t.Name, t.Type is { } y ? Names[y] : null, Fmt(t.Diameter), Fmt(t.CuttingLength), null], "#e6edf3");
            var state = t.Status switch
            {
                BankToolStatus.ToCheck => new Border { BorderBrush = Brush.Parse("#ff8a1f"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 1), HorizontalAlignment = HorizontalAlignment.Left, Child = new TextBlock { Text = "a conferir", FontSize = 10, Foreground = Brush.Parse(Orange) } },
                BankToolStatus.Incomplete => (Control)new TextBlock { Text = "◌ Incompleta", FontSize = 12, Foreground = Brush.Parse(Orange) },
                _ => new TextBlock { Text = "● Completa", FontSize = 12, Foreground = Brush.Parse("#6fd36f") },
            };
            Grid.SetColumn(state, 5);
            row.Children.Add(state);
            var button = new Button { Content = row, Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch, Opacity = t.Active ? 1 : 0.5, Background = Brush.Parse(t.Number == _selected && !_isNew ? "#2b1a0a" : "#0d1117"), CornerRadius = new CornerRadius(0) };
            int number = t.Number;
            button.Click += (_, _) => Select(_bank, number);
            _rows.Children.Add(button);
        }
    }

    private bool Visible(BankTool t)
    {
        bool tab = _tab switch
        {
            "all" => true,
            "inactive" => !t.Active,
            "none" => t.Type is null,
            _ => t.Type is { } y && BankToolTypes.Code(y) == _tab,
        };
        string q = (_search.Text ?? "").Trim();
        if (!tab || q.Length == 0)
        {
            return tab;
        }

        string digits = q.StartsWith('T') || q.StartsWith('t') ? q[1..] : q;
        return (int.TryParse(digits, out int n) && n == t.Number)
            || (double.TryParse(q.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && t.Diameter == d)
            || (t.Name ?? "").Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    private void Select(int bank, int? number, bool isNew = false, BankTool? draft = null)
    {
        _selected = number;
        _isNew = isNew;
        _draft = draft ?? Current?.Bank.Tools.FirstOrDefault(t => t.Number == number);
        _texts.Clear();
        if (_draft is { } d)
        {
            _texts["T"] = d.Number.ToString();
            foreach (Field f in Fields)
            {
                _texts[f.Label] = f.Get(d);
            }
        }

        Rebuild();
    }

    private void BuildDetail()
    {
        _detail.Children.Clear();
        if (_draft is not { } d || Current is not { } stored)
        {
            _detail.Children.Add(Muted11("Escolha uma ferramenta na lista ou crie uma nova.", 12));
            return;
        }

        ToolBankMachine machine = stored.Bank.Machine;
        var title = new DockPanel();
        var kindName = Muted11(d.Type is { } y ? Names[y] : "sem tipo", 12);
        DockPanel.SetDock(kindName, Dock.Right);
        title.Children.AddRange([kindName, new TextBlock { Text = _isNew ? "Nova ferramenta" : $"T{d.Number} · {d.Name ?? "sem nome"}", FontWeight = FontWeight.SemiBold, FontSize = 14 }]);
        var drawing = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 18, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        drawing.Children.AddRange([Drawing(d.Type, machine), Muted11("Desenho genérico\ndo tipo, sem medidas.\nNão é a ferramenta\nreal cadastrada.")]);
        var tabs = new StackPanel { Orientation = Orientation.Horizontal };
        string[] names = ["Detalhes", "Geometria", "Corte"];
        for (int k = 0; k < 3; k++)
        {
            int tab = k;
            var b = new Button { Content = names[k], Background = Brushes.Transparent, FontSize = 12, BorderThickness = new Thickness(0, 0, 0, 2), BorderBrush = Brush.Parse(k == _detailTab ? "#ff8a1f" : "#131922"), Foreground = Brush.Parse(k == _detailTab ? Orange : Muted) };
            b.Click += (_, _) =>
            {
                _detailTab = tab;
                BuildDetail();
            };
            tabs.Children.Add(b);
        }

        _detail.Children.AddRange([title, new Border { Height = 120, CornerRadius = new CornerRadius(9), Background = Brush.Parse("#0f141b"), Child = drawing }, tabs]);
        var grid = new UniformGrid { Columns = 2 };
        if (_detailTab == 0)
        {
            grid.Children.Add(Input("NÚMERO T", "T"));
            var type = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = Editable };
            var choices = new List<BankToolType?> { null };
            choices.AddRange(Enum.GetValues<BankToolType>().Where(t => BankToolTypes.Accepts(machine, t)).Select(t => (BankToolType?)t));
            type.ItemsSource = choices.Select(c => c is { } t ? Names[t] : "ausente").ToList();
            type.SelectedIndex = choices.IndexOf(d.Type);
            type.SelectionChanged += (_, _) =>
            {
                _draft = d with { Type = choices[Math.Max(0, type.SelectedIndex)] };
                BuildDetail();
            };
            grid.Children.Add(Labeled("TIPO", type));
        }

        foreach (Field f in Fields.Where(f => f.Tab == _detailTab && f.Show(machine, d.Type)))
        {
            grid.Children.Add(Input(f.Label, f.Label));
        }

        _detail.Children.Add(grid);
        if (_detailTab == 0)
        {
            var active = new ToggleButton2("Ativa", d.Active, Editable, v => _draft = _draft! with { Active = v }, BuildDetail);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            row.Children.AddRange([Label("SITUAÇÃO"), active.On, active.Off]);
            var check = new CheckBox { Content = "A conferir (veio da tabela antiga ou de importação)", IsChecked = d.ToCheck, IsEnabled = Editable, FontSize = 12 };
            check.IsCheckedChanged += (_, _) => _draft = _draft! with { ToCheck = check.IsChecked == true };
            _detail.Children.AddRange([row, Muted11("Inativa = fora de uso (quebrou, aguarda reposição). Continua no banco com o T reservado, mas não aparece para escolher na operação."), check]);
        }

        _detail.Children.Add(Muted11("Cinza = ainda não cadastrado. Nada é preenchido sozinho. Rotação e avanços só existem se você os cadastrar (aba Corte)."));
        var cancel = Btn("Cancelar");
        var apply = Btn("Aplicar", true);
        cancel.Click += (_, _) => Select(_bank, _isNew ? null : _selected);
        apply.Click += (_, _) => Apply();
        apply.IsEnabled = Editable;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.AddRange([cancel, apply]);
        _detail.Children.Add(buttons);
    }

    private Control Input(string label, string key)
    {
        var box = new TextBox { Text = _texts.GetValueOrDefault(key), Watermark = "ausente", IsReadOnly = !Editable, FontSize = 12 };
        box.TextChanged += (_, _) => _texts[key] = box.Text;
        return Labeled(label, box);
    }

    // ── ações ──

    private void Apply()
    {
        if (Current is not { ReadOnly: false } stored || _draft is not { } d)
        {
            return;
        }

        BankTool tool;
        try
        {
            if (!int.TryParse(_texts.GetValueOrDefault("T")?.Trim().TrimStart('T', 't'), out int number))
            {
                throw new FormatException("NÚMERO T");
            }

            tool = d with { Number = number };
            foreach (Field f in Fields)
            {
                // campo escondido pelo tipo guarda o valor; só a largura do inserto sai quando deixa de ser bedame (seria recusada)
                string? text = f.Label == "LARGURA DO INSERTO" && !f.Show(stored.Bank.Machine, d.Type) ? null : _texts.GetValueOrDefault(f.Label)?.Trim();
                try
                {
                    tool = f.Set(tool, string.IsNullOrEmpty(text) ? null : text);
                }
                catch (FormatException)
                {
                    throw new FormatException(f.Label);
                }
            }
        }
        catch (FormatException ex)
        {
            Say($"✕ {ex.Message}: número inválido.", true);
            return;
        }

        if (stored.Bank.CanAdd(tool, _isNew ? null : _selected) is { } error)
        {
            Say("✕ " + error, true);
            return;
        }

        ToolBank bank = (_isNew || _selected is not { } old ? stored.Bank : stored.Bank.Without(old)).With(tool);
        if (Commit(bank))
        {
            Say($"T{tool.Number} gravada.", false);
            Select(_bank, tool.Number);
        }
    }

    /// <summary>Grava o banco; falhou, diz o motivo e deixa a tela como está (nada é perdido).</summary>
    private bool Commit(ToolBank bank, bool readOnly = false, string? warning = null)
    {
        if (!readOnly && ToolBankStore.Save(_folder, bank) is { } error)
        {
            Say($"✕ {error} O que está na tela continua; tente de novo.", true);
            return false;
        }

        int i = _banks.FindIndex(s => s.Bank.Id == bank.Id);
        var stored = new StoredToolBank(bank, readOnly, warning);
        if (i < 0)
        {
            _banks.Add(stored);
            _bank = _banks.Count - 1;
        }
        else
        {
            _banks[i] = stored;
        }

        BanksChanged?.Invoke([.. _banks.Select(s => s.Bank)]);
        return true;
    }

    private void NewTool()
    {
        if (Current is not { } stored || stored.Bank.NextFreeNumber(stored.Bank.Tools.Select(t => t.Number).DefaultIfEmpty(0).Max()) is not { } n)
        {
            Say("✕ Não há T livre neste banco.", true);
            return;
        }

        BankToolType? type = Enum.GetValues<BankToolType>().Cast<BankToolType?>().FirstOrDefault(y => BankToolTypes.Code(y!.Value) == _tab);
        _detailTab = 0;
        Select(_bank, n, true, new BankTool(n) { Type = type });
    }

    private void Duplicate()
    {
        if (Current is not { } stored || _selected is not { } n)
        {
            return;
        }

        (ToolBank? bank, BankTool? copy, string? error) = stored.Bank.Duplicate(n);
        if (error is not null)
        {
            Say("✕ " + error, true);
        }
        else if (Commit(bank!))
        {
            Say($"Cópia gravada em T{copy!.Number}.", false);
            Select(_bank, copy.Number);
        }
    }

    private void AskDelete()
    {
        if (Current is not { } stored || _selected is not { } n)
        {
            return;
        }

        var yes = Btn($"Sim, excluir T{n}");
        var no = Btn("Cancelar");
        yes.Click += (_, _) =>
        {
            if (Commit(stored.Bank.Without(n)))
            {
                Say($"T{n} excluída.", false);
                Select(_bank, null);
            }
        };
        no.Click += (_, _) => BuildDetail();
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.AddRange([yes, no]);
        _detail.Children.Insert(0, row);
        _detail.Children.Insert(0, UiKit.StatusNote($"Excluir T{n} do banco \"{stored.Bank.Name}\"? Não dá para desfazer. Para tirar de uso sem perder, use Inativa.", "bad"));
    }

    private void NewBankForm()
    {
        var name = new TextBox { Watermark = "Nome do banco" };
        var machine = new ComboBox { ItemsSource = new[] { "Fresa", "Torno" }, SelectedIndex = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        var create = Btn("Criar banco", true);
        create.Click += (_, _) =>
        {
            string text = (name.Text ?? "").Trim();
            var bank = new ToolBank(ToolBank.NewId(), text, machine.SelectedIndex == 1 ? ToolBankMachine.Lathe : ToolBankMachine.Mill);
            string? error = _banks.Any(s => string.Equals(s.Bank.Name.Trim(), text, StringComparison.CurrentCultureIgnoreCase)) ? "Já existe um banco com esse nome." : bank.Validate();
            if (error is not null)
            {
                Say("✕ " + error, true);
            }
            else if (Commit(bank))
            {
                Say($"Banco \"{text}\" criado.", false);
                Select(_bank, null);
            }
        };
        _bankList.Children.AddRange([name, machine, create]);
    }

    private async Task ImportAsync()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Importar banco de ferramentas",
            FileTypeFilter = [new FilePickerFileType("Banco de ferramentas do Casillas") { Patterns = ["*" + ToolBankJson.Extension] }],
        });
        if (files.Count == 0 || files[0].TryGetLocalPath() is not { } path)
        {
            return;
        }

        try
        {
            string text = await File.ReadAllTextAsync(path);
            ToolBankReadResult read = ToolBankImport.Import(text, _banks.Select(s => s.Bank));
            if (read.Bank is not { } bank)
            {
                Say("✕ Não importado: " + read.Error, true);
                return;
            }

            if (read.ReadOnly)
            {
                // versão mais nova: guarda o arquivo como veio (sem regravar e perder o que esta versão não conhece)
                Directory.CreateDirectory(_folder);
                File.Copy(path, System.IO.Path.Combine(_folder, ToolBankJson.FileName(bank)), false);
            }

            if (Commit(bank, read.ReadOnly, read.Warning))
            {
                Say($"Banco \"{bank.Name}\" importado" + (read.ReadOnly ? " só para leitura." : "."), false);
                Select(_bank, null);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Say("✕ Não foi possível importar: " + ex.Message, true);
        }
    }

    private async Task ExportAsync()
    {
        if (Current is not { } stored || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Exportar banco de ferramentas",
            SuggestedFileName = ToolBankJson.FileName(stored.Bank),
            DefaultExtension = ToolBankJson.Extension,
        });
        if (file?.TryGetLocalPath() is { } path)
        {
            string? error = ToolBankStore.Export(_folder, stored.Bank, path);
            Say(error is null ? $"Banco exportado: {path}" : "✕ " + error, error is not null);
        }
    }

    private void Say(string text, bool bad)
    {
        _message.Text = text;
        _message.Foreground = Brush.Parse(bad ? "#ff7b72" : "#6fd36f");
    }

    // ── peças ──

    private sealed class ToggleButton2
    {
        public readonly Button On, Off;

        public ToggleButton2(string label, bool value, bool enabled, Action<bool> set, Action redraw)
        {
            On = Btn(label, value);
            Off = Btn("Inativa", !value);
            On.IsEnabled = Off.IsEnabled = enabled;
            On.Click += (_, _) => { set(true); redraw(); };
            Off.Click += (_, _) => { set(false); redraw(); };
        }
    }

    private static Button Btn(string text, bool primary = false) => new()
    {
        Content = text, FontSize = 12, Padding = new Thickness(11, 6), CornerRadius = new CornerRadius(7),
        Background = Brush.Parse(primary ? "#2b1a0a" : "#0f141b"), BorderBrush = Brush.Parse(primary ? "#ff8a1f" : "#2b3647"),
        BorderThickness = new Thickness(1), Foreground = Brush.Parse(primary ? Orange : "#e6edf3"),
    };

    private static TextBlock Label(string text) => new() { Text = text, FontSize = 10, Foreground = Brush.Parse(Muted), VerticalAlignment = VerticalAlignment.Center };

    private static TextBlock Muted11(string text, double size = 11) => new() { Text = text, FontSize = size, Foreground = Brush.Parse(Muted), TextWrapping = TextWrapping.Wrap };

    private static StackPanel Labeled(string label, Control input)
    {
        var s = new StackPanel { Spacing = 3, Margin = new Thickness(0, 0, 8, 8) };
        s.Children.AddRange([Label(label), input]);
        return s;
    }

    private static Grid Row(string?[] cells, string color)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("52,*,150,56,90,100"), Margin = new Thickness(10, 7) };
        for (int k = 0; k < cells.Length; k++)
        {
            if (k == 5 && cells[k] is null)
            {
                continue;
            }

            var t = new TextBlock { Text = cells[k] ?? "ausente", FontSize = 12, Foreground = Brush.Parse(cells[k] is null ? Gray : color), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) };
            Grid.SetColumn(t, k);
            g.Children.Add(t);
        }

        return g;
    }

    /// <summary>Desenho genérico por família (sem medidas): fresa, esférica, broca, macho ou inserto de torno.</summary>
    private static Control Drawing(BankToolType? type, ToolBankMachine machine)
    {
        string body = type switch
        {
            BankToolType.BallEndMill => "M26,34 h18 v53 a9,9 0 0 1 -18,0 Z",
            BankToolType.Drill or BankToolType.CenterDrill => "M26,34 h18 v52 l-9,12 l-9,-12 Z",
            BankToolType.Tap => "M26,34 h18 v62 h-18 Z M26,48 h18 M26,60 h18 M26,72 h18 M26,84 h18",
            _ => "M26,34 h18 v62 h-18 Z",
        };
        string data = machine == ToolBankMachine.Lathe && type != BankToolType.Drill
            ? "M2,44 h48 v16 h-48 Z M50,40 l16,12 l-16,12 Z"
            : "M22,4 h26 v30 h-26 Z " + body;
        return new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(data), Stroke = Brush.Parse(Orange), StrokeThickness = 1.6, Fill = Brush.Parse("#2b1a0a"), Width = 70, Height = 104 };
    }

    private static string? Fmt(double? v) => v?.ToString("0.###", CultureInfo.GetCultureInfo("pt-BR"));

    private static double? Num(string? text) =>
        text is null ? null : double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : throw new FormatException();
}
