using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CncStudio.App.Graphics;
using CncStudio.Core.Cam;
using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;
using CncStudio.Core.Scene;
using CncStudio.Step;

namespace CncStudio.App.Modules;

/// <summary>
/// CAM 3D, parte 1: abre um modelo STL, confere medidas e malha e mostra no 3D (a mesma vista
/// da simulação). Gera varredura de acabamento e desbaste por níveis.
/// </summary>
public sealed partial class ModelView : Grid
{
    private static readonly CultureInfo Pt = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly (string Name, double Factor)[] Units =
    [
        ("Milímetros (mm)", 1), ("Polegadas (pol) × 25,4", 25.4), ("Centímetros (cm) × 10", 10), ("Metros (m) × 1000", 1000),
    ];

    private readonly GlSceneView _gl = new();
    private readonly TextBlock _info = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private readonly TextBlock _hint = new()
    {
        Text = "Abra um arquivo STL ou STEP para ver o modelo.",
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Opacity = 0.6,
    };

    private readonly ComboBox _unit = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox _top = new() { Content = "Topo da peça em Z = 0 e canto em X0 Y0", IsChecked = true };
    private readonly Dictionary<string, TextBox> _f = [];
    private readonly ComboBox _dialect = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock _zInfo = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _sel = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Text = "Clique no modelo para escolher uma região ou uma aresta (Ctrl soma)." };
    private readonly Button _drillButton = new() { Content = "Gerar furação dos furos escolhidos", Padding = new Thickness(12, 6), IsEnabled = false };
    private readonly Button _pocketButton = new() { Content = "Gerar bolsão da região escolhida", Padding = new Thickness(12, 6), IsEnabled = false };
    private readonly HashSet<int> _selFaces = [];
    private readonly HashSet<int> _selEdges = [];
    private MeshFeatures? _features;
    private int _analysis, _version = 1;
    private readonly Button _openEditor = new() { Content = "Abrir no editor (simular no Code)", Padding = new Thickness(12, 6), IsEnabled = false };
    private readonly Dictionary<string, string> _labels = [];
    private readonly TextBlock _pendingTitle = new() { FontWeight = FontWeight.Bold };
    private readonly WrapPanel _pendingItems = new();
    private Button? _openButton;
    private bool _glFailed;

    // Giros pedidos para o modelo aberto (voltam a zero ao abrir outro arquivo); o arquivo e _raw não mudam
    private readonly List<ModelTurn> _turns = [];
    private readonly List<Button> _turnButtons = [];
    private readonly TextBlock _turnInfo = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.75 };

    private static readonly (string Label, ModelTurn? Turn)[] TurnButtons =
    [
        ("X +90°", ModelTurn.XPlus90), ("X −90°", ModelTurn.XMinus90), ("Y +90°", ModelTurn.YPlus90), ("Y −90°", ModelTurn.YMinus90),
        ("Virar", ModelTurn.Flip), ("Zerar orientação", null),
    ];

    private TriangleMesh? _current;
    private string _program = "";
    private string _programSuffix = "-varredura";
    private TriangleMesh? _raw;
    private string _fileName = "";

    /// <summary>Abre o seletor de STL e devolve nome e bytes (null se cancelou).</summary>
    public Func<Task<(string Name, byte[] Data)?>>? PickStl { get; set; }

    /// <summary>Entrega o programa gerado ao editor (nome do arquivo, texto).</summary>
    public Action<string, string>? OpenInEditor { get; set; }

    public ModelView()
    {
        ColumnDefinitions = new ColumnDefinitions("340,*");
        foreach ((string name, _) in Units)
        {
            _unit.Items.Add(new ComboBoxItem { Content = name });
        }

        _unit.SelectedIndex = 0;
        Button open = new() { Content = "Abrir STL ou STEP…", Padding = new Thickness(14, 7), FontWeight = FontWeight.SemiBold };
        open.Click += async (_, _) => await Open();
        _unit.SelectionChanged += (_, _) => Show();
        _top.IsCheckedChanged += (_, _) => Show();

        var panel = new StackPanel { Margin = new Thickness(16, 14), Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Modelo 3D (STL ou STEP)", FontSize = 20, FontWeight = FontWeight.Bold });
        panel.Children.Add(new TextBlock
        {
            Text = "CAM 3D: abrir o modelo, conferir medidas e malha, gerar desbaste por níveis e varredura de acabamento.",
            Opacity = 0.75, TextWrapping = TextWrapping.Wrap, FontSize = 12,
        });
        panel.Children.Add(open);
        panel.Children.Add(new TextBlock { Text = "Unidade do arquivo (o STL não guarda a unidade; o STEP já é lido em mm)", Opacity = 0.75, FontSize = 12 });
        panel.Children.Add(_unit);
        panel.Children.Add(_top);
        panel.Children.Add(_info);

        panel.Children.Add(new TextBlock { Text = "Orientação da peça", FontSize = 15, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 6, 0, 0) });
        var turns = new WrapPanel();
        foreach ((string label, ModelTurn? turn) in TurnButtons)
        {
            var button = new Button { Content = label, Padding = new Thickness(10, 5), Margin = new Thickness(0, 0, 6, 6), IsEnabled = false };
            button.Click += (_, _) => Turn(turn);
            _turnButtons.Add(button);
            turns.Children.Add(button);
        }

        panel.Children.Add(turns);
        panel.Children.Add(_turnInfo);
        panel.Children.Add(new TextBlock
        {
            Text = "Confira a orientação: o Z+ é o lado de cima da usinagem.",
            TextWrapping = TextWrapping.Wrap, FontSize = 12, FontWeight = FontWeight.SemiBold,
        });

        panel.Children.Add(new TextBlock { Text = "Varredura de acabamento (fresa esférica)", FontSize = 15, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
        panel.Children.Add(new TextBlock
        {
            Text = "3 eixos, linhas ao longo de X em zigue-zague, só onde há modelo. Confere só a esfera (não a haste). Simule e rode a primeira vez no ar.",
            Opacity = 0.75, TextWrapping = TextWrapping.Wrap, FontSize = 12,
        });
        AddField(panel, "D", "Diâmetro da bola (mm)", "6");
        AddField(panel, "S", "Rotação (rpm)", "8000");
        AddField(panel, "FXY", "Avanço XY (mm/min)", "1200");
        AddField(panel, "FZ", "Avanço de descida (mm/min)", "400");
        AddField(panel, "STEP", "Passo lateral (mm)", "1");
        AddField(panel, "TOL", "Tolerância da linha (mm)", "0,02");
        panel.Children.Add(new TextBlock
        {
            Text = "Alturas Z (você informa; nada é suposto). Z0 = o do próprio modelo.",
            Opacity = 0.75, TextWrapping = TextWrapping.Wrap, FontSize = 12,
        });
        AddField(panel, "ZS", "Z seguro (mm; acima do ponto mais alto)", "");
        AddField(panel, "ZA", "Z de aproximação (mm; acima do topo, abaixo do Z seguro)", "");
        AddField(panel, "BOT", "Z do fundo (mm; não abaixo da base do modelo)", "");
        AddField(panel, "TN", "Número da ferramenta (T)", "1");
        _dialect.Items.Add(new ComboBoxItem { Content = "Fanuc" });
        _dialect.Items.Add(new ComboBoxItem { Content = "Siemens" });
        _dialect.SelectedIndex = 0;
        panel.Children.Add(_dialect);
        Button generate = new() { Content = "Gerar varredura", Padding = new Thickness(14, 7), FontWeight = FontWeight.SemiBold };
        generate.Click += async (_, _) => await Generate();
        panel.Children.Add(generate);

        panel.Children.Add(new TextBlock { Text = "Desbaste por níveis (fresa esférica)", FontSize = 15, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
        panel.Children.Add(new TextBlock
        {
            Text = "Usa a ferramenta, as alturas Z, o fundo e o comando acima. Planos de Z constante, linhas em X em zigue-zague, só onde sobra material; deixa sobra para o acabamento. Bruto = prisma do tamanho do modelo. Confere só a esfera; simule e rode no ar.",
            Opacity = 0.75, TextWrapping = TextWrapping.Wrap, FontSize = 12,
        });
        AddField(panel, "RZ", "Passo em Z (mm)", "2");
        AddField(panel, "RSTEP", "Passo lateral do desbaste (mm)", "3");
        AddField(panel, "RAX", "Sobra axial, no fundo (mm)", "0,3");
        AddField(panel, "RRAD", "Sobra radial, nas paredes (mm)", "0,3");
        Button rough = new() { Content = "Gerar desbaste", Padding = new Thickness(14, 7), FontWeight = FontWeight.SemiBold };
        rough.Click += async (_, _) => await GenerateRough();
        panel.Children.Add(rough);

        panel.Children.Add(new TextBlock { Text = "Faces e arestas", FontSize = 15, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
        panel.Children.Add(new TextBlock
        {
            Text = "São regiões lidas da malha (dobra acima de 30°), não as faces do CAD. Com malha grossa um furo pode virar vários pedaços.",
            Opacity = 0.75, TextWrapping = TextWrapping.Wrap, FontSize = 12,
        });
        panel.Children.Add(_sel);
        panel.Children.Add(new TextBlock { Text = "Fundos da furação e do bolsão: você informa (R-05)", FontSize = 15, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
        panel.Children.Add(new TextBlock
        {
            Text = "O Z seguro e a aproximação são os mesmos da varredura, acima. Valem na coordenada do modelo, que é a do programa. Nada é suposto: sem valor digitado, não gera. O número cinza é só a referência da malha. " +
                   "Estar acima do modelo não garante estar livre de morsa, grampos ou castanhas (o CAM 3D não verifica isso).",
            Opacity = 0.75, TextWrapping = TextWrapping.Wrap, FontSize = 12,
        });
        panel.Children.Add(_zInfo);
        panel.Children.Add(_originInfo);
        AddField(panel, "DZ", "Furação: Z do fundo do furo (ponta da broca)", "");
        AddField(panel, "PZB", "Bolsão: Z do fundo do bolsão", "");
        AddField(panel, "DD", "Broca (mm)", "6");
        AddField(panel, "DS", "Rotação da broca (rpm)", "2500");
        AddField(panel, "DF", "Avanço de furação (mm/min)", "150");
        AddField(panel, "DQ", "Bicada Q (mm)", "3");
        _drillButton.Click += (_, _) => GenerateDrilling();
        panel.Children.Add(_drillButton);
        panel.Children.Add(new TextBlock
        {
            Text = "Bolsão: a região plana escolhida (voltada para cima) é o fundo e a borda dela é o contorno. Sem ilhas nem furos dentro.",
            Opacity = 0.75, TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 6, 0, 0),
        });
        AddField(panel, "PD", "Fresa de topo (mm)", "6");
        AddField(panel, "PS", "Rotação da fresa (rpm)", "8000");
        AddField(panel, "PF", "Avanço da fresa (mm/min)", "800");
        AddField(panel, "PZ", "Passo em Z (mm)", "1");
        AddField(panel, "PE", "Passo lateral (% do diâmetro)", "60");
        _pocketButton.Click += (_, _) => GeneratePocket();
        panel.Children.Add(_pocketButton);
        _openEditor.Click += (_, _) => OpenInEditor?.Invoke(_program, Path.GetFileNameWithoutExtension(_fileName) + _programSuffix + ".nc");
        panel.Children.Add(_openEditor);
        panel.Children.Add(_result);

        var left = new Border
        {
            Child = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled },
            BorderThickness = new Thickness(0, 0, 1, 0),
        };
        left.Bind(Border.BackgroundProperty, left.GetResourceObservable("CardBackground"));
        left.Bind(Border.BorderBrushProperty, left.GetResourceObservable("SubtleBorder"));
        SetColumn(left, 0);

        _gl.IsVisible = false;
        var view = new Grid { Margin = new Thickness(12) };
        _gl.Focusable = true; // Esc no menu da face devolve o foco ao 3D
        view.Children.Add(_gl);
        view.Children.Add(_hint);
        view.Children.Add(_menuLayer);
        var frame = new Border { Margin = new Thickness(12, 12, 12, 8), CornerRadius = new CornerRadius(8), ClipToBounds = true, Background = new SolidColorBrush(Color.Parse("#0B1216")), Child = view };

        // Barra de pendências: as mesmas regras da fresa e do torno; tocar num item leva ao campo
        _openButton = open;
        var bar = new StackPanel { Spacing = 3 };
        bar.Children.Add(_pendingTitle);
        bar.Children.Add(_pendingItems);
        bar.Children.Add(new TextBlock { Text = "Fixação e colisões: o app não verifica. Simule e rode a primeira vez no ar.", FontSize = 11, Opacity = 0.6, TextWrapping = TextWrapping.Wrap });
        var barBorder = new Border { Margin = new Thickness(12, 0, 12, 12), CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 8), BorderThickness = new Thickness(1), Child = bar };
        barBorder.Bind(Border.BackgroundProperty, barBorder.GetResourceObservable("CardBackground"));
        barBorder.Bind(Border.BorderBrushProperty, barBorder.GetResourceObservable("SubtleBorder"));
        var right = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        right.Children.Add(frame);
        Grid.SetRow(barBorder, 1);
        right.Children.Add(barBorder);
        SetColumn(right, 1);

        Children.Add(left);
        Children.Add(right);
        _gl.SceneClicked += OnSceneClicked;
        foreach (TextBox box in _f.Values)
        {
            box.TextChanged += (_, _) => UpdatePending();
        }

        UpdatePending();
        ActualThemeVariantChanged += (_, _) => UpdatePending();
        _gl.Failed += (_, message) =>
        {
            _glFailed = true;
            _gl.IsVisible = false;
            _hint.Text = "3D pela placa de vídeo indisponível (" + message + ").";
            _hint.IsVisible = true;
        };
    }

    private void AddField(StackPanel panel, string id, string label, string value)
    {
        panel.Children.Add(new TextBlock { Text = label, Opacity = 0.75, FontSize = 12 });
        var box = new TextBox { Text = value };
        _f[id] = box;
        _labels[id] = label;
        panel.Children.Add(box);
    }

    private static string Fmt(double x) => x.ToString("0.##", Pt);

    /// <summary>Lê Z seguro e aproximação digitados (sem padrão) e confere com o modelo; nada é suposto.</summary>
    private bool TryZ(TriangleMesh model, double workTopZ, out ModelZ? z, out string? error)
    {
        z = null;
        double safe = Num("ZS"), clearance = Num("ZA");
        if (double.IsNaN(safe) || double.IsNaN(clearance))
        {
            error = $"Digite o Z seguro e a aproximação (o topo do modelo está em Z {Fmt(model.Bounds.Max.Z)}). Nada é suposto.";
            return false;
        }

        z = new ModelZ(safe, clearance);
        error = z.Validate(model.Bounds.Max.Z, workTopZ);
        return error is null;
    }

    /// <summary>
    /// Pendências (âmbar, "pendente: …") e valores inválidos (vermelho, "inválido: …") antes de gerar, sempre com texto.
    /// As alturas usam a mesma conferência do Core que a geração usa; nada é calculado de novo aqui.
    /// </summary>
    private void UpdatePending()
    {
        var items = new List<(string Kind, string Text, Control Target)>();
        if (_current is null && _openButton is not null)
        {
            items.Add(("pending", "abra um modelo STL", _openButton));
        }

        foreach (string id in new[] { "D", "S", "FXY", "FZ", "TN", "STEP", "TOL", "RZ", "RSTEP", "RAX", "RRAD" })
        {
            if (double.IsNaN(Num(id)))
            {
                items.Add(("pending", "preencha " + _labels[id], _f[id]));
            }
        }

        double zs = Num("ZS"), za = Num("ZA"), bottom = Num("BOT");
        if (double.IsNaN(zs))
        {
            items.Add(("pending", "digite o Z seguro", _f["ZS"]));
        }

        if (double.IsNaN(za))
        {
            items.Add(("pending", "digite o Z de aproximação", _f["ZA"]));
        }

        if (double.IsNaN(bottom))
        {
            items.Add(("pending", "digite o Z do fundo", _f["BOT"]));
        }

        if (_current is { } model && !double.IsNaN(zs) && !double.IsNaN(za))
        {
            double top = model.Bounds.Max.Z;
            if (new ModelZ(zs, za).Validate(top, top) is { } zError)
            {
                items.Add(("bad", zError, za <= top ? _f["ZA"] : _f["ZS"]));
            }
            else if (!double.IsNaN(bottom) && SurfaceLevels.Build(model, new ModelZ(zs, za), bottom, 1).Error is { } bottomError)
            {
                items.Add(("bad", bottomError, _f["BOT"]));
            }
        }

        _pendingTitle.Text = items.Count == 0 ? "✓ Pronto para gerar o G-code" : $"Pendências ({items.Count})";
        _pendingTitle.Foreground = UiKit.Status(items.Count == 0 ? "ok" : items.Any(x => x.Kind == "bad") ? "bad" : "pending").Fg;
        _pendingItems.Children.Clear();
        foreach ((string kind, string text, Control target) in items)
        {
            _pendingItems.Children.Add(JobBar.ItemButton(new JobBarItem(kind, text, () =>
            {
                target.BringIntoView();
                target.Focus();
            })));
        }
    }

    private double Num(string id) =>
        double.TryParse(_f[id].Text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;

    private async Task Generate()
    {
        _openEditor.IsEnabled = false;
        if (_current is not { } model)
        {
            _result.Text = "• Abra um STL ou STEP primeiro.";
            return;
        }

        double[] v = new[] { "D", "S", "FXY", "FZ", "STEP", "TOL", "TN" }.Select(Num).ToArray();
        if (v.Any(double.IsNaN))
        {
            _result.Text = "• Preencha todos os números da ferramenta e da varredura.";
            return;
        }

        var tool = new CamTool((int)v[6], v[0], v[1], v[2], v[3], "Bola D" + v[0].ToString("0.##", Pt));
        if (!TryLevels(model, 1, out CamLevels? levels, out string? levelsError))
        {
            _result.Text = "• " + levelsError;
            return;
        }

        CamDialect dialect = _dialect.SelectedIndex == 1 ? CamDialect.Siemens : CamDialect.Fanuc;
        string name = Path.GetFileNameWithoutExtension(_fileName).ToUpperInvariant();
        _result.Text = "Calculando…";
        (OperationResult result, string program) = await Task.Run(() =>
        {
            OperationResult r = SurfaceFinish.Raster(model, tool, levels!, new SurfaceParams(v[4], v[5]));
            return (r, r.Ok ? CamPost.Generate([r.Operation!], new PostOptions(dialect, ProgramName: name.Length > 0 ? name : "VARREDURA 3D")) : "");
        });
        if (!result.Ok)
        {
            _result.Text = "• " + result.Error;
            return;
        }

        _program = program;
        _programSuffix = "-varredura";
        _openEditor.IsEnabled = true;
        int lines = program.Count(c => c == '\n');
        _result.Text = $"Programa gerado: {lines:N0} linhas, {result.Operation!.Moves.Count:N0} movimentos.\n" +
            string.Join("\n", result.Operation.Warnings.Select(w => "• " + w)) +
            $"\n• Z seguro {levels!.SafeZ:0.##}, aproximação {levels.ClearanceZ:0.##}, fundo {levels.BottomZ:0.##} (valores seus). Alturas e fixação não verificadas: acima do modelo não é o mesmo que livre de morsa, grampos ou castanhas. Primeira vez, no ar.";
    }

    /// <summary>Alturas digitadas (Z seguro, aproximação, fundo): vazio ou incoerente bloqueia; nada é suposto (R-05).</summary>
    private bool TryLevels(TriangleMesh model, double stepDown, out CamLevels? levels, out string? error)
    {
        levels = null;
        double zs = Num("ZS"), za = Num("ZA");
        if (double.IsNaN(zs) || double.IsNaN(za))
        {
            error = $"Digite o Z seguro e a aproximação (o topo do modelo está em Z {model.Bounds.Max.Z.ToString("0.##", Pt)}). Nada é suposto.";
            return false;
        }

        if (double.IsNaN(Num("BOT")))
        {
            error = $"Digite o Z do fundo (a base do modelo está em Z {model.Bounds.Min.Z.ToString("0.##", Pt)}, mas a malha não diz até onde cortar).";
            return false;
        }

        (levels, error) = SurfaceLevels.Build(model, new ModelZ(zs, za), Num("BOT"), stepDown);
        return levels is not null;
    }

    private async Task GenerateRough()
    {
        _openEditor.IsEnabled = false;
        if (_current is not { } model)
        {
            _result.Text = "• Abra um STL primeiro.";
            return;
        }

        double[] v = new[] { "D", "S", "FXY", "FZ", "TN", "RZ", "RSTEP", "RAX", "RRAD" }.Select(Num).ToArray();
        if (v.Any(double.IsNaN))
        {
            _result.Text = "• Preencha todos os números da ferramenta e do desbaste.";
            return;
        }

        var tool = new CamTool((int)v[4], v[0], v[1], v[2], v[3], "Bola D" + v[0].ToString("0.##", Pt));
        if (!TryLevels(model, v[5], out CamLevels? levels, out string? levelsError))
        {
            _result.Text = "• " + levelsError;
            return;
        }

        var rp = new RoughParams(v[6], v[7], v[8]);
        CamDialect dialect = _dialect.SelectedIndex == 1 ? CamDialect.Siemens : CamDialect.Fanuc;
        string name = Path.GetFileNameWithoutExtension(_fileName).ToUpperInvariant();
        _result.Text = "Calculando…";
        (OperationResult result, string program) = await Task.Run(() =>
        {
            OperationResult r = SurfaceRoughing.ByLevels(model, tool, levels!, rp);
            return (r, r.Ok ? CamPost.Generate([r.Operation!], new PostOptions(dialect, ProgramName: name.Length > 0 ? name : "DESBASTE 3D")) : "");
        });
        if (!result.Ok)
        {
            _result.Text = "• " + result.Error;
            return;
        }

        _program = program;
        _programSuffix = "-desbaste";
        _openEditor.IsEnabled = true;
        int lines = program.Count(c => c == '\n');
        _result.Text = $"Desbaste gerado: {lines:N0} linhas, {result.Operation!.Moves.Count:N0} movimentos.\n" +
            string.Join("\n", result.Operation.Warnings.Select(w => "• " + w)) +
            $"\n• Z seguro {levels!.SafeZ:0.##}, aproximação {levels.ClearanceZ:0.##}, fundo {levels.BottomZ:0.##} (valores seus). Alturas e fixação não verificadas: acima do modelo não é o mesmo que livre de morsa, grampos ou castanhas. Primeira vez, no ar. Depois do desbaste, rode a varredura de acabamento com a mesma ferramenta ou uma menor.";
    }

    private async Task Open()
    {
        if (PickStl is null || await PickStl() is not { } file)
        {
            return;
        }

        bool isStep = file.Name.EndsWith(".step", StringComparison.OrdinalIgnoreCase) || file.Name.EndsWith(".stp", StringComparison.OrdinalIgnoreCase);
        if (isStep)
        {
            _info.Text = "Lendo o STEP…";
            StepResult step = await Task.Run(() => StepMeshReader.Read(file.Data));
            if (!step.Ok)
            {
                Clear(step.Error!);
                return;
            }

            _turns.Clear(); // arquivo novo começa como veio
            _raw = step.Mesh;
            _fileName = file.Name;
            _unit.SelectedIndex = 0; // o OpenCASCADE já entrega em milímetros
            _unit.IsEnabled = false;
            string collapsed = step.Collapsed > 0 ? $"; {step.Collapsed:N0} triângulos sem área descartados" : "";
            string notice = step.Notice is { } n ? "\n" + n : "";
            Show(0, $"STEP lido em milímetros (malha de 0,05 mm de desvio; {step.Orientation!.FlippedTriangles:N0} triângulos reorientados{collapsed}).{notice}");
            return;
        }

        StlResult r = await Task.Run(() => StlReader.Read(file.Data));
        if (!r.Ok)
        {
            Clear(r.Error!);
            return;
        }

        _turns.Clear();
        _raw = r.Mesh;
        _fileName = file.Name;
        _unit.IsEnabled = true;
        Show(r.Skipped);
    }

    // Botão de orientação: null = zerar. O programa gerado antes era do modelo na posição antiga: descarta
    private void Turn(ModelTurn? turn)
    {
        if (_raw is null)
        {
            return;
        }

        if (turn is { } t)
        {
            _turns.Add(t);
        }
        else
        {
            _turns.Clear();
        }

        _program = "";
        _result.Text = "";
        Show();
    }

    private static string TurnName(ModelTurn turn) => turn switch
    {
        ModelTurn.XPlus90 => "X +90°",
        ModelTurn.XMinus90 => "X −90°",
        ModelTurn.YPlus90 => "Y +90°",
        ModelTurn.YMinus90 => "Y −90°",
        _ => "virada",
    };

    // Leitura falhou: nada do modelo anterior fica na tela nem vai para a varredura
    private void Clear(string error)
    {
        _raw = null;
        _current = null;
        _fileName = "";
        _sourceNote = "";
        _program = "";
        _unit.IsEnabled = true;
        _openEditor.IsEnabled = false;
        _result.Text = "";
        _turns.Clear();
        _turnInfo.Text = "";
        _turnButtons.ForEach(b => b.IsEnabled = false);
        _info.Text = "• " + error;
        _gl.IsVisible = false;
        if (!_glFailed)
        {
            _hint.Text = "Abra um arquivo STL ou STEP para ver o modelo.";
        }

        _hint.IsVisible = true;
        UpdatePending(); // sem modelo: a barra de pendências volta a pedir o arquivo
    }

    private string _sourceNote = "";

    private int _lastSkipped;

    private void Show(int? skipped = null, string? note = null)
    {
        if (note is not null || skipped is not null)
        {
            _sourceNote = note ?? "";
        }

        if (_raw is null)
        {
            return;
        }

        _lastSkipped = skipped ?? _lastSkipped;
        double factor = Units[Math.Max(0, _unit.SelectedIndex)].Factor;
        TriangleMesh mesh = _raw.Scaled(factor);
        foreach (ModelTurn turn in _turns)
        {
            mesh = mesh.Turned(turn);
        }

        _turnInfo.Text = _turns.Count == 0 ? "Orientação: como veio no arquivo." : "Orientação: " + string.Join(", ", _turns.Select(TurnName)) + ".";
        _turnButtons.ForEach(b => b.IsEnabled = true);
        if (_top.IsChecked == true)
        {
            mesh = mesh.PlacedTopAtZero();
        }

        _current = mesh;
        _openEditor.IsEnabled = false;

        var size = mesh.Size;
        MeshCheck check = MeshCheck.Of(mesh);
        string F(double v) => v.ToString("0.##", Pt);
        _info.Text =
            $"{_fileName}\n" + (_sourceNote.Length > 0 ? _sourceNote + "\n" : "") +
            $"{mesh.TriangleCount:N0} triângulos" + (_lastSkipped > 0 ? $" ({_lastSkipped} inválidos descartados)" : "") + "\n" +
            $"Tamanho: X {F(size.X)} × Y {F(size.Y)} × Z {F(size.Z)} mm\n" +
            $"Z: de {F(mesh.Bounds.Min.Z)} a {F(mesh.Bounds.Max.Z)} mm\n" +
            $"Área da superfície: {F(mesh.SurfaceArea / 100)} cm²\n" +
            (check.IsClosed
                ? $"Malha fechada. Volume: {F(mesh.Volume / 1000)} cm³ (peso e custo dependem do material)."
                : $"⚠ Malha aberta: {check.BorderEdges:N0} arestas de borda ({F(check.BorderLength)} mm no total), " +
                  $"{check.NonManifoldEdges:N0} arestas com mais de 2 triângulos, {check.Pieces:N0} pedaço(s). " +
                  "Pode ser defeito do próprio arquivo; o volume é só estimativa. Confira o modelo no CAD.") +
            "\nConfira as medidas: se o tamanho está 25× ou 1000× fora, troque a unidade.";

        UpdatePending();
        _hint.IsVisible = false;
        _gl.IsVisible = true;
        _gl.Show(mesh.ToSnapshot());
        Dispatcher.UIThread.Post(() => _gl.Fit(SceneView.Isometric), DispatcherPriority.Background);
        _ = AnalyzeAsync(mesh);
    }

    // ---------------------------------------------------------------- faces e arestas

    private async Task AnalyzeAsync(TriangleMesh mesh)
    {
        int mine = ++_analysis;
        _features = null;
        _selFaces.Clear();
        _selEdges.Clear();
        CloseFaceMenu();
        _originInfo.IsVisible = false;
        _drillButton.IsEnabled = false;
        _pocketButton.IsEnabled = false;
        _sel.Text = "Lendo faces e arestas…";
        MeshFeatures features = await Task.Run(() => MeshFeatures.Analyze(mesh));
        if (mine != _analysis)
        {
            return; // abriu outro modelo no meio
        }

        _features = features;
        _sel.Text = $"{features.Faces.Count:N0} regiões e {features.Edges.Count:N0} arestas. Clique no modelo para escolher (Ctrl soma).";
    }

    private void OnSceneClicked(object? sender, SceneClickEventArgs e)
    {
        if (_features is not { } features || _current is null)
        {
            return;
        }

        (MeshFace? face, MeshEdge? edge) = features.Pick(e.Origin, e.Direction, e.Tolerance);
        if (!e.Additive)
        {
            bool same = (face is not null && _selFaces.Count == 1 && _selFaces.Contains(face.Id) && _selEdges.Count == 0)
                || (edge is not null && _selEdges.Count == 1 && _selEdges.Contains(edge.Id) && _selFaces.Count == 0);
            _selFaces.Clear();
            _selEdges.Clear();
            if (same)
            {
                face = null;
                edge = null;
            }
        }

        if (face is not null && !_selFaces.Remove(face.Id))
        {
            _selFaces.Add(face.Id);
        }

        if (edge is not null && !_selEdges.Remove(edge.Id))
        {
            _selEdges.Add(edge.Id);
        }

        PublishSelection(features, _current);

        // Menu da face (UX-03b): abre para o que acabou de ser escolhido; desmarcar fecha
        if (face is not null && _selFaces.Contains(face.Id))
        {
            ShowFaceMenu(FaceMenuRules.For(features, face, _current.Bounds.Max.Z), e.Screen, face.Id);
        }
        else if (edge is not null && _selEdges.Contains(edge.Id))
        {
            ShowFaceMenu(FaceMenuRules.For(edge), e.Screen, null);
        }
        else
        {
            CloseFaceMenu();
        }
    }

    private IEnumerable<MeshFace> SelectedHoles(MeshFeatures features) =>
        _selFaces.Select(i => features.Faces[i]).Where(f => f.Kind == FaceKind.Hole && f.Axis is { } a && Math.Abs(a.Z) >= 0.99);

    private void PublishSelection(MeshFeatures features, TriangleMesh mesh)
    {
        _version++;
        var objects = new List<SceneObjectState>();
        var lines = new List<SceneLinesState>();
        if (_selFaces.Count == 0)
        {
            objects.Add(new SceneObjectState("modelo/0", SceneRole.Material, mesh.ToSolidMesh(), _version, Point3.Origin, true));
        }
        else
        {
            int[] chosen = [.. _selFaces.SelectMany(i => features.Faces[i].Triangles)];
            objects.Add(new SceneObjectState("modelo/0", SceneRole.Material, mesh.Subset(chosen, invert: true).ToSolidMesh(), _version, Point3.Origin, true));
            objects.Add(new SceneObjectState("selecao/0", SceneRole.Material, mesh.Subset(chosen).ToSolidMesh(0xFF9F1C), _version, Point3.Origin, true));
        }

        if (_selEdges.Count > 0)
        {
            var pos = new List<float>();
            foreach (MeshEdge ed in _selEdges.Select(i => features.Edges[i]))
            {
                for (int i = 0; i + 1 < ed.Points.Count; i++)
                {
                    pos.AddRange([(float)ed.Points[i].X, (float)ed.Points[i].Y, (float)ed.Points[i].Z, (float)ed.Points[i + 1].X, (float)ed.Points[i + 1].Y, (float)ed.Points[i + 1].Z]);
                }
            }

            lines.Add(new SceneLinesState(SceneRole.CurrentPath, [.. pos], 0x00E5FF));
        }

        _gl.Show(new SceneSnapshot(objects, lines, mesh.Bounds, ScenePalette.Realistic));
        _sel.Text = Describe(features);
        UpdateZReference(features, mesh);
        _drillButton.IsEnabled = SelectedHoles(features).Any();
        _pocketButton.IsEnabled = _selFaces.Count == 1 && features.Faces[_selFaces.First()] is { Kind: FaceKind.Plane, Normal.Z: >= 0.99 };
        if (_originFace is { } of && !_selFaces.Contains(of))
        {
            _originFace = null;
            _originInfo.IsVisible = false; // a face de origem saiu da seleção
        }
    }

    /// <summary>Referências da malha (só dicas cinza nos campos; nenhum valor é preenchido).</summary>
    private void UpdateZReference(MeshFeatures features, TriangleMesh mesh)
    {
        _zInfo.Text = $"Modelo: topo em Z {Fmt(mesh.Bounds.Max.Z)}, base em Z {Fmt(mesh.Bounds.Min.Z)}.";
        _f["ZS"].Watermark = $"acima de {Fmt(mesh.Bounds.Max.Z)}";
        _f["ZA"].Watermark = $"acima do topo trabalhado";
        MeshFace[] holes = [.. SelectedHoles(features)];
        _f["DZ"].Watermark = holes.Length > 0 ? $"malha: {Fmt(FaceDrilling.MeshBottomZ(holes))}" : "escolha furos";
        _f["PZB"].Watermark = _selFaces.Count == 1 && features.Faces[_selFaces.First()] is { Kind: FaceKind.Plane } f ? $"malha: {Fmt(f.Center.Z)}" : "escolha a região do fundo";
    }

    private void GeneratePocket()
    {
        _openEditor.IsEnabled = false;
        if (_features is not { } features || _current is not { } model || _selFaces.Count != 1)
        {
            return;
        }

        double[] v = new[] { "PD", "PS", "PF", "PZ", "PE", "TN" }.Select(Num).ToArray();
        if (v.Any(double.IsNaN))
        {
            _result.Text = "• Preencha fresa, rotação, avanço, passo em Z e passo lateral.";
            return;
        }

        var tool = new CamTool((int)v[5], v[0], v[1], v[2], v[2] / 2, "Fresa D" + v[0].ToString("0.##", Pt));
        MeshFace face = features.Faces[_selFaces.First()];
        if (!TryZ(model, model.Bounds.Max.Z, out ModelZ? z, out string? zError))
        {
            _result.Text = "• " + zError;
            return;
        }

        double floorZ = Num("PZB");
        if (double.IsNaN(floorZ))
        {
            _result.Text = $"• Digite o Z do fundo do bolsão (a região escolhida está em Z {Fmt(face.Center.Z)} na malha).";
            return;
        }

        OperationResult r = FacePocket.Build(features, face, tool, z!, floorZ, model.Bounds.Max.Z, v[3], new PocketParams(v[4]));
        if (!r.Ok)
        {
            _result.Text = "• " + r.Error;
            return;
        }

        CamDialect dialect = _dialect.SelectedIndex == 1 ? CamDialect.Siemens : CamDialect.Fanuc;
        string name = Path.GetFileNameWithoutExtension(_fileName).ToUpperInvariant();
        _program = CamPost.Generate([r.Operation!], new PostOptions(dialect, ProgramName: name.Length > 0 ? name + " BOLSAO" : "BOLSAO 3D"));
        _programSuffix = "-bolsao";
        _openEditor.IsEnabled = true;
        string F(double x) => x.ToString("0.##", Pt);
        _result.Text = $"Bolsão gerado: fundo em Z {F(floorZ)} (informado por você; a malha mostra {F(face.Center.Z)}), Z seguro {F(z!.SafeZ)}, aproximação {F(z.ClearanceZ)}, {r.Operation!.Moves.Count:N0} movimentos.\n" +
            string.Join("\n", r.Operation!.Warnings.Select(w => "• " + w)) +
            "\n• Alturas e fixação não verificadas: acima do modelo não é o mesmo que livre de morsa, grampos ou castanhas." +
            "\n• O contorno é o da malha: confira se o bolsão é aberto por cima e se a fresa cabe nos cantos. Primeira vez, no ar.";
    }

    private string Describe(MeshFeatures features)
    {
        string F(double v) => v.ToString("0.###", Pt);
        string P(Point3 p) => $"X {F(p.X)} Y {F(p.Y)} Z {F(p.Z)}";
        var lines = new List<string>();
        foreach (MeshFace f in _selFaces.Select(i => features.Faces[i]).Take(8))
        {
            lines.Add(f.Kind switch
            {
                FaceKind.Plane => $"• Plano: {F(f.Area)} mm², normal ({F(f.Normal.X)}; {F(f.Normal.Y)}; {F(f.Normal.Z)}), centro {P(f.Center)}",
                FaceKind.Hole => $"• Furo Ø {F(f.Diameter)} mm, profundidade da parede {F(f.Depth)} mm, centro {P(f.Center)}" + (f.Axis is { } a && Math.Abs(a.Z) < 0.99 ? " (não é vertical: sem furação automática)" : ""),
                FaceKind.Boss => $"• Pino Ø {F(f.Diameter)} mm, altura {F(f.Depth)} mm, centro {P(f.Center)}",
                FaceKind.ConcaveCurve => $"• Raio interno (côncavo) Ø {F(f.Diameter)} mm, {F(f.Area)} mm²",
                FaceKind.ConvexCurve => $"• Arredondamento (convexo) Ø {F(f.Diameter)} mm, {F(f.Area)} mm²",
                _ => $"• Superfície livre: {F(f.Area)} mm²",
            });
        }

        foreach (MeshEdge ed in _selEdges.Select(i => features.Edges[i]).Take(8))
        {
            lines.Add(ed.Kind switch
            {
                EdgeKind.Line => $"• Aresta reta: {F(ed.Length)} mm, de {P(ed.Points[0])} a {P(ed.Points[^1])}",
                EdgeKind.Circle => $"• Círculo Ø {F(ed.Diameter)} mm, centro {P(ed.Center)}",
                EdgeKind.Arc => $"• Arco Ø {F(ed.Diameter)} mm, comprimento {F(ed.Length)} mm",
                _ => $"• Aresta curva: {F(ed.Length)} mm" + (ed.Closed ? " (fechada)" : ""),
            });
        }

        int total = _selFaces.Count + _selEdges.Count;
        if (total == 0)
        {
            return $"{features.Faces.Count:N0} regiões e {features.Edges.Count:N0} arestas. Clique no modelo para escolher (Ctrl soma).";
        }

        if (total > 16)
        {
            lines.Add($"… e mais {total - 16} itens");
        }

        return string.Join("\n", lines);
    }

    private void GenerateDrilling()
    {
        _openEditor.IsEnabled = false;
        if (_features is not { } features || _current is not { } model)
        {
            return;
        }

        MeshFace[] holes = [.. SelectedHoles(features)];
        double[] v = new[] { "DD", "DS", "DF", "DQ", "TN" }.Select(Num).ToArray();
        if (holes.Length == 0 || v.Any(double.IsNaN))
        {
            _result.Text = "• Escolha furos verticais e preencha broca, rotação, avanço e bicada.";
            return;
        }

        double top = holes.Max(h => h.Box.Max.Z);
        if (!TryZ(model, top, out ModelZ? z, out string? zError))
        {
            _result.Text = "• " + zError;
            return;
        }

        double bottom = Num("DZ");
        if (double.IsNaN(bottom))
        {
            _result.Text = $"• Digite o Z do fundo do furo (a malha mostra Z {Fmt(FaceDrilling.MeshBottomZ(holes))}, mas não diz se o furo é cego, passante ou tem fundo cônico).";
            return;
        }

        var tool = new CamTool((int)v[4], v[0], v[1], v[2] * 4, v[2], "Broca D" + v[0].ToString("0.##", Pt));
        OperationResult r = FaceDrilling.Build(holes, tool, z!, model.Bounds.Max.Z, bottom, v[3]);
        if (!r.Ok)
        {
            _result.Text = "• " + r.Error;
            return;
        }

        CamDialect dialect = _dialect.SelectedIndex == 1 ? CamDialect.Siemens : CamDialect.Fanuc;
        string name = Path.GetFileNameWithoutExtension(_fileName).ToUpperInvariant();
        _program = CamPost.Generate([r.Operation!], new PostOptions(dialect, ProgramName: name.Length > 0 ? name + " FUROS" : "FUROS 3D"));
        _programSuffix = "-furos";
        _openEditor.IsEnabled = true;
        string F(double x) => x.ToString("0.##", Pt);
        _result.Text = $"Furação gerada: {holes.Length} furo(s), de Z {F(top)} até Z {F(bottom)} (ponta da broca), bicada {F(v[3])} mm; Z seguro {F(z!.SafeZ)}, aproximação {F(z.ClearanceZ)} (valores seus).\n" +
            $"• Fundo informado por você; a malha mostra Z {F(FaceDrilling.MeshBottomZ(holes))}.\n" +
            "• Alturas e fixação não verificadas: acima do modelo não é o mesmo que livre de morsa, grampos ou castanhas.\n" +
            "• O ponto do furo é o centro lido da malha: confira antes de rodar. Primeira vez, no ar.";
    }
}
