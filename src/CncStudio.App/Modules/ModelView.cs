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
/// da simulação). Ainda não gera percurso: é a base para a varredura e o desbaste 3D.
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
    private readonly TextBlock _sel = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Text = "Clique no modelo para escolher uma região ou uma aresta (Ctrl soma)." };
    private readonly Button _drillButton = new() { Content = "Gerar furação dos furos escolhidos", Padding = new Thickness(12, 6), IsEnabled = false };
    private readonly Button _pocketButton = new() { Content = "Gerar bolsão da região escolhida", Padding = new Thickness(12, 6), IsEnabled = false };
    private readonly HashSet<int> _selFaces = [];
    private readonly HashSet<int> _selEdges = [];
    private MeshFeatures? _features;
    private int _analysis, _version = 1;
    private readonly Button _openEditor = new() { Content = "Abrir no editor (simular no Code)", Padding = new Thickness(12, 6), IsEnabled = false };
    private TriangleMesh? _current;
    private string _program = "";
    private string _programSuffix = "-varredura.nc";
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
            Text = "Primeiro passo do CAM 3D: abrir o modelo, conferir medidas e malha. O percurso 3D (varredura e desbaste) vem nas próximas etapas.",
            Opacity = 0.75, TextWrapping = TextWrapping.Wrap, FontSize = 12,
        });
        panel.Children.Add(open);
        panel.Children.Add(new TextBlock { Text = "Unidade do arquivo (o STL não guarda a unidade; o STEP já é lido em mm)", Opacity = 0.75, FontSize = 12 });
        panel.Children.Add(_unit);
        panel.Children.Add(_top);
        panel.Children.Add(_info);

        panel.Children.Add(new TextBlock { Text = "Faces e arestas", FontSize = 15, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 10, 0, 0) });
        panel.Children.Add(new TextBlock
        {
            Text = "São regiões lidas da malha (dobra acima de 30°), não as faces do CAD. Com malha grossa um furo pode virar vários pedaços.",
            Opacity = 0.75, TextWrapping = TextWrapping.Wrap, FontSize = 12,
        });
        panel.Children.Add(_sel);
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
        AddField(panel, "BOT", "Fundo mínimo Z (mm; vazio = base do modelo)", "");
        AddField(panel, "TN", "Número da ferramenta (T)", "1");
        _dialect.Items.Add(new ComboBoxItem { Content = "Fanuc" });
        _dialect.Items.Add(new ComboBoxItem { Content = "Siemens" });
        _dialect.SelectedIndex = 0;
        panel.Children.Add(_dialect);
        Button generate = new() { Content = "Gerar varredura", Padding = new Thickness(14, 7), FontWeight = FontWeight.SemiBold };
        generate.Click += async (_, _) => await Generate();
        panel.Children.Add(generate);
        _openEditor.Click += (_, _) => OpenInEditor?.Invoke(_program, Path.GetFileNameWithoutExtension(_fileName) + _programSuffix);
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
        _gl.SceneClicked += OnSceneClicked;
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
            _result.Text = "• Abra um STL ou STEP primeiro.";
            return;
        }

        double[] v = new[] { "D", "S", "FXY", "FZ", "STEP", "TOL", "TN" }.Select(Num).ToArray();
        if (v.Any(double.IsNaN))
        {
            _result.Text = "• Preencha todos os números da ferramenta e da varredura.";
            return;
        }

        double top = model.Bounds.Max.Z;
        double bottom = double.IsNaN(Num("BOT")) ? model.Bounds.Min.Z : Num("BOT");
        var tool = new CamTool((int)v[6], v[0], v[1], v[2], v[3], "Bola D" + v[0].ToString("0.##", Pt));
        var levels = new CamLevels(top + 25, top + 5, top, Math.Min(bottom, top - 0.01), 1);
        CamDialect dialect = _dialect.SelectedIndex == 1 ? CamDialect.Siemens : CamDialect.Fanuc;
        string name = Path.GetFileNameWithoutExtension(_fileName).ToUpperInvariant();
        _result.Text = "Calculando…";
        (OperationResult result, string program) = await Task.Run(() =>
        {
            OperationResult r = SurfaceFinish.Raster(model, tool, levels, new SurfaceParams(v[4], v[5]));
            return (r, r.Ok ? CamPost.Generate([r.Operation!], new PostOptions(dialect, ProgramName: name.Length > 0 ? name : "VARREDURA 3D")) : "");
        });
        if (!result.Ok)
        {
            _result.Text = "• " + result.Error;
            return;
        }

        _program = program;
        _programSuffix = "-varredura.nc";
        _openEditor.IsEnabled = true;
        int lines = program.Count(c => c == '\n');
        _result.Text = $"Programa gerado: {lines:N0} linhas, {result.Operation!.Moves.Count:N0} movimentos.\n" +
            string.Join("\n", result.Operation.Warnings.Select(w => "• " + w)) +
            "\n• Altura segura = topo + 25 mm; aproximação = topo + 5 mm (Z0 = topo do modelo).";
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
                _info.Text = "• " + step.Error;
                return;
            }

            _raw = step.Mesh;
            _fileName = file.Name;
            _unit.SelectedIndex = 0; // o OpenCASCADE já entrega em milímetros
            _unit.IsEnabled = false;
            Show(0, $"STEP lido em milímetros (malha de 0,05 mm de desvio; {step.Orientation!.FlippedTriangles:N0} triângulos reorientados).");
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
        _unit.IsEnabled = true;
        Show(r.Skipped);
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
            $"{_fileName}\n" + (_sourceNote.Length > 0 ? _sourceNote + "\n" : "") +
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
        _ = AnalyzeAsync(mesh);
    }

    // ---------------------------------------------------------------- faces e arestas

    private async Task AnalyzeAsync(TriangleMesh mesh)
    {
        int mine = ++_analysis;
        _features = null;
        _selFaces.Clear();
        _selEdges.Clear();
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
        _drillButton.IsEnabled = SelectedHoles(features).Any();
        _pocketButton.IsEnabled = _selFaces.Count == 1 && features.Faces[_selFaces.First()] is { Kind: FaceKind.Plane, Normal.Z: >= 0.99 };
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
        OperationResult r = FacePocket.Build(features, face, tool, model.Bounds.Max.Z, v[3], new PocketParams(v[4]));
        if (!r.Ok)
        {
            _result.Text = "• " + r.Error;
            return;
        }

        CamDialect dialect = _dialect.SelectedIndex == 1 ? CamDialect.Siemens : CamDialect.Fanuc;
        string name = Path.GetFileNameWithoutExtension(_fileName).ToUpperInvariant();
        _program = CamPost.Generate([r.Operation!], new PostOptions(dialect, ProgramName: name.Length > 0 ? name + " BOLSAO" : "BOLSAO 3D"));
        _programSuffix = "-bolsao.nc";
        _openEditor.IsEnabled = true;
        string F(double x) => x.ToString("0.##", Pt);
        _result.Text = $"Bolsão gerado: fundo em Z {F(face.Center.Z)}, {r.Operation!.Moves.Count:N0} movimentos.\n" +
            string.Join("\n", r.Operation!.Warnings.Select(w => "• " + w)) +
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

        double smallest = holes.Min(h => h.Diameter);
        if (v[0] > smallest + 0.01)
        {
            _result.Text = $"• A broca ({v[0].ToString("0.##", Pt)} mm) é maior que o menor furo escolhido ({smallest.ToString("0.##", Pt)} mm).";
            return;
        }

        double top = holes.Max(h => h.Box.Max.Z), bottom = holes.Min(h => h.Box.Min.Z);
        var tool = new CamTool((int)v[4], v[0], v[1], v[2] * 4, v[2], "Broca D" + v[0].ToString("0.##", Pt));
        OperationResult r = CamOperations.Drilling(tool, model.Bounds.Max.Z + 25, holes.Select(h => new Point2(h.Center.X, h.Center.Y)), DrillCycleKind.Peck,
            top, bottom, top + 2, v[3]);
        if (!r.Ok)
        {
            _result.Text = "• " + r.Error;
            return;
        }

        CamDialect dialect = _dialect.SelectedIndex == 1 ? CamDialect.Siemens : CamDialect.Fanuc;
        string name = Path.GetFileNameWithoutExtension(_fileName).ToUpperInvariant();
        _program = CamPost.Generate([r.Operation!], new PostOptions(dialect, ProgramName: name.Length > 0 ? name + " FUROS" : "FUROS 3D"));
        _programSuffix = "-furos.nc";
        _openEditor.IsEnabled = true;
        string F(double x) => x.ToString("0.##", Pt);
        _result.Text = $"Furação gerada: {holes.Length} furo(s), de Z {F(top)} até Z {F(bottom)} (ponta da broca), bicada {F(v[3])} mm.\n" +
            "• A profundidade é a da parede do furo na malha; furo cego com fundo cônico não é considerado.\n" +
            "• O ponto do furo é o centro lido da malha: confira antes de rodar. Primeira vez, no ar.";
    }
}
