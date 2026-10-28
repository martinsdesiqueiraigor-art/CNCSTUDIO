using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CncStudio.Core.Assist;

namespace CncStudio.App.Modules;

/// <summary>
/// Casillas Assist, fase 1: bibliotecas de materiais, morsas e castanhas de torno, com um teste rápido
/// ("que morsa ou castanha serve?"). Só sugere; a conferência final é do usinador.
/// </summary>
public sealed class AssistView : UserControl
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    private sealed record Field(string Label, string Unit, Func<int, string> Get, Func<int, string, bool> Set);

    private sealed record Section(string Title, Func<int> Count, Func<int, string> Name, Func<int, bool> IsDraft, Action New, Action<int> Duplicate, Action<int> Remove, Func<List<Field>> Fields, Action<int, bool> SetDraft);

    private static readonly (MaterialGroup Group, string Name)[] GroupNames =
    [
        (MaterialGroup.Steel, "Aço"), (MaterialGroup.Stainless, "Inox"), (MaterialGroup.Aluminum, "Alumínio"), (MaterialGroup.Brass, "Latão"),
        (MaterialGroup.CastIron, "Ferro fundido"), (MaterialGroup.Titanium, "Titânio"), (MaterialGroup.Plastic, "Plástico"),
    ];

    private static string GroupName(MaterialGroup g) => GroupNames.First(x => x.Group == g).Name;

    private AssistLibrary _lib = AssistLibrary.Defaults();
    private readonly TextBlock _message = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TabControl _tabs = new();
    private readonly List<(ListBox List, StackPanel Form, Section Sec)> _sections = [];
    private readonly TextBlock _testResult = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private bool _busy;

    /// <summary>Texto JSON guardado nas configurações (vazio = itens iniciais).</summary>
    public Func<string>? LibrarySource { get; set; }

    public Action<string>? SaveLibrary { get; set; }

    /// <summary>Seletor de arquivo de biblioteca (.casillas-assist) e gravação em arquivo.</summary>
    public Func<Task<(string Name, string Text)?>>? PickFile { get; set; }

    public Func<string, string, Task>? SaveFile { get; set; }

    public AssistView()
    {
        var root = new ScrollViewer { HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var stack = new StackPanel { Margin = new Thickness(28, 22), Spacing = 12, MaxWidth = 1000, HorizontalAlignment = HorizontalAlignment.Stretch };
        stack.Children.Add(new TextBlock { Text = "Casillas Assist · Bibliotecas", FontSize = 24, FontWeight = FontWeight.Bold });
        stack.Children.Add(Note("Os itens iniciais são rascunho: pontos de partida a conferir com o catálogo do fabricante. O Assist só sugere; confira, simule e rode a primeira vez no ar."));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button open = new() { Content = "Abrir biblioteca…", Padding = new Thickness(12, 6) };
        Button save = new() { Content = "Salvar em arquivo…", Padding = new Thickness(12, 6) };
        Button reset = new() { Content = "Voltar aos itens iniciais", Padding = new Thickness(12, 6) };
        open.Click += async (_, _) => await OpenFile();
        save.Click += async (_, _) =>
        {
            if (SaveFile is not null)
            {
                await SaveFile("biblioteca-assist.casillas-assist", AssistLibraryJson.ToJson(_lib));
            }
        };
        reset.Click += (_, _) =>
        {
            _lib = AssistLibrary.Defaults();
            Changed("Itens iniciais restaurados.");
        };
        buttons.Children.Add(open);
        buttons.Children.Add(save);
        buttons.Children.Add(reset);
        stack.Children.Add(buttons);
        stack.Children.Add(_message);

        AddSection(new Section("Materiais", () => _lib.Materials.Count, i => _lib.Materials[i].Name, i => _lib.Materials[i].Draft,
            () => _lib.Materials.Add(new MaterialEntry(Unique("Novo material", _lib.Materials.Select(m => m.Name)), MaterialGroup.Steel, 100, 0.05, 0.5)),
            i => _lib.Materials.Insert(i + 1, _lib.Materials[i] with { Name = Unique(_lib.Materials[i].Name + " cópia", _lib.Materials.Select(m => m.Name)) }),
            i => _lib.Materials.RemoveAt(i), MaterialFields, (i, d) => _lib.Materials[i] = _lib.Materials[i] with { Draft = d }));
        AddSection(new Section("Morsas", () => _lib.Vises.Count, i => _lib.Vises[i].Name, i => _lib.Vises[i].Draft,
            () => _lib.Vises.Add(new ViseEntry(Unique("Nova morsa", _lib.Vises.Select(v => v.Name)), 100, 30, 100, 150, 50)),
            i => _lib.Vises.Insert(i + 1, _lib.Vises[i] with { Name = Unique(_lib.Vises[i].Name + " cópia", _lib.Vises.Select(v => v.Name)) }),
            i => _lib.Vises.RemoveAt(i), ViseFields, (i, d) => _lib.Vises[i] = _lib.Vises[i] with { Draft = d }));
        AddSection(new Section("Castanhas de torno", () => _lib.Jaws.Count, i => _lib.Jaws[i].Name, i => _lib.Jaws[i].Draft,
            () => _lib.Jaws.Add(new ChuckJawEntry(Unique("Nova castanha", _lib.Jaws.Select(j => j.Name)), 160, 20, 160, 0, 0, 25)),
            i => _lib.Jaws.Insert(i + 1, _lib.Jaws[i] with { Name = Unique(_lib.Jaws[i].Name + " cópia", _lib.Jaws.Select(j => j.Name)) }),
            i => _lib.Jaws.RemoveAt(i), JawFields, (i, d) => _lib.Jaws[i] = _lib.Jaws[i] with { Draft = d }));
        stack.Children.Add(_tabs);
        stack.Children.Add(BuildTest());
        root.Content = stack;
        Content = root;
        Reload();
    }

    /// <summary>Lê a biblioteca guardada (ou os itens iniciais) e atualiza a tela.</summary>
    public void Reload()
    {
        string text = LibrarySource?.Invoke() ?? "";
        _lib = AssistLibrary.Defaults();
        if (text.Length > 0 && AssistLibraryJson.FromJson(text) is { Ok: true } loaded)
        {
            _lib = loaded.Library!;
        }

        Redraw();
    }

    // ---------------------------------------------------------------- campos

    private List<Field> MaterialFields() =>
    [
        T("Nome", i => _lib.Materials[i].Name, (i, v) => _lib.Materials[i] = _lib.Materials[i] with { Name = v }),
        new("Grupo", "", i => GroupName(_lib.Materials[i].Group), (i, v) =>
        {
            var found = GroupNames.Where(x => string.Equals(x.Name, v.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (found.Count == 0)
            {
                _message.Text = "Grupos: " + string.Join(", ", GroupNames.Select(x => x.Name));
                return false;
            }

            _lib.Materials[i] = _lib.Materials[i] with { Group = found[0].Group };
            return true;
        }),
        N("Velocidade de corte", "m/min", i => _lib.Materials[i].SurfaceSpeed, (i, v) => _lib.Materials[i] = _lib.Materials[i] with { SurfaceSpeed = v }),
        N("Avanço por dente", "mm", i => _lib.Materials[i].FeedPerTooth, (i, v) => _lib.Materials[i] = _lib.Materials[i] with { FeedPerTooth = v }),
        N("Passe (fração do Ø)", "×Ø", i => _lib.Materials[i].DepthFactor, (i, v) => _lib.Materials[i] = _lib.Materials[i] with { DepthFactor = v }),
    ];

    private List<Field> ViseFields() =>
    [
        T("Nome", i => _lib.Vises[i].Name, (i, v) => _lib.Vises[i] = _lib.Vises[i] with { Name = v }),
        N("Abertura máxima", "mm", i => _lib.Vises[i].MaxOpening, (i, v) => _lib.Vises[i] = _lib.Vises[i] with { MaxOpening = v }),
        N("Altura da mandíbula", "mm", i => _lib.Vises[i].JawHeight, (i, v) => _lib.Vises[i] = _lib.Vises[i] with { JawHeight = v }),
        N("Comprimento da mandíbula", "mm", i => _lib.Vises[i].JawLength, (i, v) => _lib.Vises[i] = _lib.Vises[i] with { JawLength = v }),
        N("Largura do corpo", "mm", i => _lib.Vises[i].BodyWidth, (i, v) => _lib.Vises[i] = _lib.Vises[i] with { BodyWidth = v }),
        N("Altura do corpo", "mm", i => _lib.Vises[i].BodyHeight, (i, v) => _lib.Vises[i] = _lib.Vises[i] with { BodyHeight = v }),
    ];

    private List<Field> JawFields() =>
    [
        T("Nome", i => _lib.Jaws[i].Name, (i, v) => _lib.Jaws[i] = _lib.Jaws[i] with { Name = v }),
        N("Ø da placa", "mm", i => _lib.Jaws[i].ChuckDiameter, (i, v) => _lib.Jaws[i] = _lib.Jaws[i] with { ChuckDiameter = v }),
        N("Pega por fora: mínimo", "mm", i => _lib.Jaws[i].ExternalMin, (i, v) => _lib.Jaws[i] = _lib.Jaws[i] with { ExternalMin = v }),
        N("Pega por fora: máximo (0 = não serve)", "mm", i => _lib.Jaws[i].ExternalMax, (i, v) => _lib.Jaws[i] = _lib.Jaws[i] with { ExternalMax = v }),
        N("Pega por dentro: mínimo", "mm", i => _lib.Jaws[i].InternalMin, (i, v) => _lib.Jaws[i] = _lib.Jaws[i] with { InternalMin = v }),
        N("Pega por dentro: máximo (0 = não serve)", "mm", i => _lib.Jaws[i].InternalMax, (i, v) => _lib.Jaws[i] = _lib.Jaws[i] with { InternalMax = v }),
        N("Altura da castanha", "mm", i => _lib.Jaws[i].JawHeight, (i, v) => _lib.Jaws[i] = _lib.Jaws[i] with { JawHeight = v }),
    ];

    private static Field T(string label, Func<int, string> get, Action<int, string> set) =>
        new(label, "", get, (i, v) =>
        {
            set(i, v.Trim());
            return true;
        });

    private Field N(string label, string unit, Func<int, double> get, Action<int, double> set) =>
        new(label, unit, i => get(i).ToString("0.###", Br), (i, v) =>
        {
            if (!double.TryParse(v.Trim().Replace('.', ','), NumberStyles.Float, Br, out double d) || !double.IsFinite(d))
            {
                _message.Text = $"Valor inválido em \"{label}\".";
                return false;
            }

            set(i, d);
            return true;
        });

    // ---------------------------------------------------------------- tela

    private void AddSection(Section sec)
    {
        var list = new ListBox { MinHeight = 160, MaxHeight = 190, MinWidth = 260 };
        var form = new StackPanel { Spacing = 6 };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        Button add = new() { Content = "Novo", Padding = new Thickness(10, 4) };
        Button dup = new() { Content = "Duplicar", Padding = new Thickness(10, 4) };
        Button del = new() { Content = "Remover", Padding = new Thickness(10, 4) };
        bar.Children.Add(add);
        bar.Children.Add(dup);
        bar.Children.Add(del);
        add.Click += (_, _) =>
        {
            sec.New();
            Changed("", sec.Count() - 1, sec.Title);
        };
        dup.Click += (_, _) =>
        {
            if (list.SelectedIndex is >= 0 and var i)
            {
                sec.Duplicate(i);
                Changed("", i + 1, sec.Title);
            }
        };
        del.Click += (_, _) =>
        {
            if (list.SelectedIndex is >= 0 and var i)
            {
                sec.Remove(i);
                Changed("", Math.Min(i, sec.Count() - 1), sec.Title);
            }
        };
        list.SelectionChanged += (_, _) =>
        {
            if (!_busy)
            {
                BuildForm(form, sec, list.SelectedIndex);
            }
        };
        var left = new StackPanel { Spacing = 6, Width = 300 };
        left.Children.Add(list);
        left.Children.Add(bar);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("300,16,*") };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(form, 2);
        grid.Children.Add(left);
        grid.Children.Add(form);
        _tabs.Items.Add(new TabItem { Header = sec.Title, Content = new Border { Padding = new Thickness(0, 10), Child = grid } });
        _sections.Add((list, form, sec));
    }

    private void BuildForm(StackPanel form, Section sec, int index)
    {
        form.Children.Clear();
        if (index < 0 || index >= sec.Count())
        {
            form.Children.Add(new TextBlock { Text = "Selecione um item para editar.", Opacity = 0.7 });
            return;
        }

        foreach (Field f in sec.Fields())
        {
            var box = new TextBox { Text = f.Get(index), MinHeight = 26 };
            void Commit()
            {
                if (box.Text == f.Get(index))
                {
                    return;
                }

                _message.Text = "";
                var backup = AssistLibraryJson.ToJson(_lib);
                if (!f.Set(index, box.Text ?? ""))
                {
                    box.Foreground = Brushes.IndianRed;
                    return;
                }

                IReadOnlyList<string> problems = _lib.Validate();
                if (problems.Count > 0)
                {
                    _lib = AssistLibraryJson.FromJson(backup).Library!;
                    _message.Text = problems[0];
                    box.Text = f.Get(index);
                    return;
                }

                Changed("", index, sec.Title);
            }

            box.LostFocus += (_, _) => Commit();
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Avalonia.Input.Key.Enter)
                {
                    Commit();
                }
            };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("230,*,44") };
            var label = new TextBlock { Text = f.Label, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
            var unit = new TextBlock { Text = f.Unit, Opacity = 0.65, FontSize = 11, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(box, 1);
            Grid.SetColumn(unit, 2);
            row.Children.Add(label);
            row.Children.Add(box);
            row.Children.Add(unit);
            form.Children.Add(row);
        }

        var draft = new CheckBox { Content = "Rascunho (ainda não revisado)", IsChecked = sec.IsDraft(index) };
        draft.IsCheckedChanged += (_, _) =>
        {
            bool now = draft.IsChecked == true;
            if (now != sec.IsDraft(index))
            {
                sec.SetDraft(index, now);
                Changed("", index, sec.Title);
            }
        };
        form.Children.Add(draft);
    }

    /// <summary>Guarda nas configurações e refaz as listas.</summary>
    private void Changed(string message, int? select = null, string? tab = null)
    {
        SaveLibrary?.Invoke(AssistLibraryJson.ToJson(_lib));
        if (message.Length > 0)
        {
            _message.Text = message;
        }

        Redraw(select, tab);
    }

    private void Redraw(int? select = null, string? tab = null)
    {
        _busy = true;
        try
        {
            foreach ((ListBox list, StackPanel form, Section sec) in _sections)
            {
                int keep = tab == sec.Title && select is { } s ? s : list.SelectedIndex;
                list.Items.Clear();
                for (int i = 0; i < sec.Count(); i++)
                {
                    list.Items.Add(sec.Name(i) + (sec.IsDraft(i) ? "  · rascunho" : ""));
                }

                list.SelectedIndex = Math.Clamp(keep, -1, sec.Count() - 1);
                BuildForm(form, sec, list.SelectedIndex);
            }
        }
        finally
        {
            _busy = false;
        }

        RefreshTestMaterials();
    }

    private async Task OpenFile()
    {
        if (PickFile is null || await PickFile() is not { } file)
        {
            return;
        }

        AssistLoadResult r = AssistLibraryJson.FromJson(file.Text);
        if (!r.Ok)
        {
            _message.Text = r.Error ?? "Arquivo inválido.";
            return;
        }

        _lib = r.Library!;
        Changed($"Biblioteca \"{file.Name}\" aberta.");
    }

    private static string Unique(string name, IEnumerable<string> existing)
    {
        var set = existing.ToHashSet();
        string candidate = name;
        for (int n = 2; set.Contains(candidate); n++)
        {
            candidate = $"{name} {n}";
        }

        return candidate;
    }

    private static Border Note(string text) => new()
    {
        Background = new SolidColorBrush(Color.Parse("#3B2F12")),
        BorderBrush = new SolidColorBrush(Color.Parse("#7A6122")),
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(8),
        Padding = new Thickness(12),
        Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12 },
    };

    // ---------------------------------------------------------------- teste rápido

    private readonly ComboBox _testMaterial = new() { MinWidth = 220 };
    private readonly TextBox _testW = new() { Text = "80", Width = 70 };
    private readonly TextBox _testH = new() { Text = "20", Width = 70 };
    private readonly TextBox _testD = new() { Text = "50", Width = 70 };
    private readonly TextBox _testTool = new() { Text = "10", Width = 70 };
    private readonly TextBox _testTeeth = new() { Text = "4", Width = 70 };

    private Border BuildTest()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Teste rápido", FontWeight = FontWeight.SemiBold });
        panel.Children.Add(Row("Fresa: largura da peça", _testW, "altura", _testH, "mm"));
        panel.Children.Add(Row("Torno: Ø da peça", _testD, "", null, "mm"));
        var mat = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        mat.Children.Add(new TextBlock { Text = "Material", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        mat.Children.Add(_testMaterial);
        mat.Children.Add(new TextBlock { Text = "Ø fresa", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        mat.Children.Add(_testTool);
        mat.Children.Add(new TextBlock { Text = "dentes", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        mat.Children.Add(_testTeeth);
        panel.Children.Add(mat);
        var go = new Button { Content = "Sugerir", Padding = new Thickness(16, 6), HorizontalAlignment = HorizontalAlignment.Left };
        go.Click += (_, _) => RunTest();
        panel.Children.Add(go);
        panel.Children.Add(_testResult);
        return new Border { Background = new SolidColorBrush(Color.Parse("#131a22")), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = panel };
    }

    private static StackPanel Row(string a, TextBox boxA, string b, TextBox? boxB, string unit)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        row.Children.Add(new TextBlock { Text = a, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
        row.Children.Add(boxA);
        if (boxB is not null)
        {
            row.Children.Add(new TextBlock { Text = b, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
            row.Children.Add(boxB);
        }

        row.Children.Add(new TextBlock { Text = unit, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.65, FontSize = 11 });
        return row;
    }

    private void RefreshTestMaterials()
    {
        int keep = _testMaterial.SelectedIndex;
        _testMaterial.Items.Clear();
        foreach (MaterialEntry m in _lib.Materials)
        {
            _testMaterial.Items.Add(m.Name);
        }

        _testMaterial.SelectedIndex = Math.Clamp(keep, _lib.Materials.Count > 0 ? 0 : -1, _lib.Materials.Count - 1);
    }

    private static double Parse(TextBox box) =>
        double.TryParse((box.Text ?? "").Trim().Replace('.', ','), NumberStyles.Float, Br, out double d) ? d : double.NaN;

    private void RunTest()
    {
        var lines = new List<string>();
        double w = Parse(_testW), h = Parse(_testH), d = Parse(_testD), tool = Parse(_testTool);
        if (double.IsNaN(w) || double.IsNaN(h) || double.IsNaN(d) || double.IsNaN(tool) || !int.TryParse(_testTeeth.Text, out int teeth))
        {
            _testResult.Text = "Confira os números digitados.";
            return;
        }

        IReadOnlyList<ViseEntry> vises = _lib.FindVises(w, h);
        lines.Add(vises.Count > 0
            ? "Morsas que servem: " + string.Join("; ", vises.Select(v => v.Name + (v.Draft ? " (rascunho)" : "")))
            : "Nenhuma morsa da biblioteca serve: " + (_lib.Vises.Count > 0 ? _lib.Vises.OrderByDescending(v => v.MaxOpening).First().Check(w, h).Reason : "biblioteca vazia."));
        IReadOnlyList<ChuckJawEntry> outer = _lib.FindJaws(d, external: true), inner = _lib.FindJaws(d, external: false);
        lines.Add("Castanhas por fora: " + (outer.Count > 0 ? string.Join("; ", outer.Select(j => j.Name)) : "nenhuma"));
        lines.Add("Castanhas por dentro (furo): " + (inner.Count > 0 ? string.Join("; ", inner.Select(j => j.Name)) : "nenhuma"));
        if (_testMaterial.SelectedIndex >= 0 && _lib.Materials[_testMaterial.SelectedIndex] is { } m)
        {
            (double Rpm, double Feed)? s = m.SuggestMill(tool, teeth);
            lines.Add(s is { } v
                ? $"{m.Name}, fresa Ø{tool:0.##} Z{teeth}: S{v.Rpm:0} F{v.Feed:0} mm/min, passe ≈ {m.SuggestDepth(tool):0.##} mm" + (m.Draft ? " (rascunho)" : "")
                : "Dados insuficientes para sugerir o corte.");
        }

        lines.Add("Ponto de partida: confira no catálogo, simule e rode a primeira vez no ar.");
        _testResult.Text = string.Join("\n", lines);
    }
}
