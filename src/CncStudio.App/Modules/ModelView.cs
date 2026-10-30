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

namespace CncStudio.App.Modules;

/// <summary>
/// CAM 3D, parte 1: abre um modelo STL, confere medidas e malha e mostra no 3D (a mesma vista
/// da simulação). Gera varredura de acabamento e desbaste por níveis.
/// </summary>
public sealed class ModelView : Grid
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
        Text = "Abra um arquivo STL para ver o modelo.",
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Opacity = 0.6,
    };

    private readonly ComboBox _unit = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox _top = new() { Content = "Topo da peça em Z = 0 e canto em X0 Y0", IsChecked = true };
    private readonly Dictionary<string, TextBox> _f = [];
    private readonly ComboBox _dialect = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _result = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly Button _openEditor = new() { Content = "Abrir no editor (simular no Code)", Padding = new Thickness(12, 6), IsEnabled = false };
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
        Button open = new() { Content = "Abrir STL…", Padding = new Thickness(14, 7), FontWeight = FontWeight.SemiBold };
        open.Click += async (_, _) => await Open();
        _unit.SelectionChanged += (_, _) => Show();
        _top.IsCheckedChanged += (_, _) => Show();

        var panel = new StackPanel { Margin = new Thickness(16, 14), Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "Modelo 3D (STL)", FontSize = 20, FontWeight = FontWeight.Bold });
        panel.Children.Add(new TextBlock
        {
            Text = "CAM 3D: abrir o modelo, conferir medidas e malha, gerar desbaste por níveis e varredura de acabamento.",
            Opacity = 0.75, TextWrapping = TextWrapping.Wrap, FontSize = 12,
        });
        panel.Children.Add(open);
        panel.Children.Add(new TextBlock { Text = "Unidade do arquivo (o STL não guarda a unidade)", Opacity = 0.75, FontSize = 12 });
        panel.Children.Add(_unit);
        panel.Children.Add(_top);
        panel.Children.Add(_info);

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
        view.Children.Add(_gl);
        view.Children.Add(_hint);
        var frame = new Border { Margin = new Thickness(12), CornerRadius = new CornerRadius(8), ClipToBounds = true, Background = new SolidColorBrush(Color.Parse("#0B1216")), Child = view };
        SetColumn(frame, 1);

        Children.Add(left);
        Children.Add(frame);
        _gl.Failed += (_, message) =>
        {
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
        panel.Children.Add(box);
    }

    private double Num(string id) =>
        double.TryParse(_f[id].Text?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : double.NaN;

    private async Task Generate()
    {
        _openEditor.IsEnabled = false;
        if (_current is not { } model)
        {
            _result.Text = "• Abra um STL primeiro.";
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

        StlResult r = await Task.Run(() => StlReader.Read(file.Data));
        if (!r.Ok)
        {
            _info.Text = "• " + r.Error;
            return;
        }

        _raw = r.Mesh;
        _fileName = file.Name;
        Show(r.Skipped);
    }

    private int _lastSkipped;

    private void Show(int? skipped = null)
    {
        if (_raw is null)
        {
            return;
        }

        _lastSkipped = skipped ?? _lastSkipped;
        double factor = Units[Math.Max(0, _unit.SelectedIndex)].Factor;
        TriangleMesh mesh = _raw.Scaled(factor);
        if (_top.IsChecked == true)
        {
            mesh = mesh.PlacedTopAtZero();
        }

        _current = mesh;
        _openEditor.IsEnabled = false;

        var size = mesh.Size;
        bool closed = mesh.IsClosed;
        string F(double v) => v.ToString("0.##", Pt);
        _info.Text =
            $"{_fileName}\n" +
            $"{mesh.TriangleCount:N0} triângulos" + (_lastSkipped > 0 ? $" ({_lastSkipped} inválidos descartados)" : "") + "\n" +
            $"Tamanho: X {F(size.X)} × Y {F(size.Y)} × Z {F(size.Z)} mm\n" +
            $"Z: de {F(mesh.Bounds.Min.Z)} a {F(mesh.Bounds.Max.Z)} mm\n" +
            $"Área da superfície: {F(mesh.SurfaceArea / 100)} cm²\n" +
            (closed
                ? $"Malha fechada. Volume: {F(mesh.Volume / 1000)} cm³ (peso e custo dependem do material)."
                : "⚠ Malha aberta (furos na superfície): dá para ver, mas o volume e o CAM 3D ficam incertos. Repare o modelo no CAD.") +
            "\nConfira as medidas: se o tamanho está 25× ou 1000× fora, troque a unidade.";

        _hint.IsVisible = false;
        _gl.IsVisible = true;
        _gl.Show(mesh.ToSnapshot());
        Dispatcher.UIThread.Post(() => _gl.Fit(SceneView.Isometric), DispatcherPriority.Background);
    }
}
