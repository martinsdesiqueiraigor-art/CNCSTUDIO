using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CncStudio.App.Graphics;
using CncStudio.Core;
using CncStudio.Core.Cam;
using CncStudio.Core.Cam3D;
using CncStudio.Core.Dialects;
using CncStudio.Core.Geometry;
using CncStudio.Core.Scene;
using CncStudio.Core.Trajectory;

namespace CncStudio.App.Modules;

/// <summary>
/// Vista 3D do Trabalho fresa (UX-04d): bruto inteiro, peça (contornos em Z 0), trajetória e eixos do zero,
/// SEM remover material (a simulação fica no Casillas Code). Barra de chips em cima, à esquerda; a cena vem do
/// Core (<see cref="CamJobScene"/>). Não promete colisão.
/// </summary>
public sealed class MillJobSceneView : Grid
{
    private readonly GlSceneView _gl = new() { ShowPath = true, ShowRapids = true };
    private readonly TextBlock _status = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.7,
        Margin = new Thickness(24),
        IsVisible = false,
    };

    private readonly WrapPanel _bar = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Top,
        // à direita fica o botão 3D da vista
        Margin = new Thickness(8, 8, 64, 8),
        ItemSpacing = 6,
        LineSpacing = 6,
    };

    private readonly Button _stockChip = Chip();
    private readonly Button _partChip = Chip();
    private readonly ToggleButton _edgesChip = Toggle("Arestas", "Mostra as quinas do bruto.");
    private readonly ToggleButton _pathChip = Toggle("Trajetória", "Mostra a trajetória do programa gerado (azul = avanço).");
    private readonly ToggleButton _rapidsChip = Toggle("Rápidos", "Mostra os rápidos G00 (amarelo).");
    private readonly ToggleButton _axesChip = Toggle("Eixos do zero", "Setas no zero da peça escolhido no cartão Zero (G5x da fixação): X vermelho, Y verde, Z azul. Só visualização: não verifica colisão nem simula.");

    private CamViewOptions _options = CamViewOptions.Default;
    private StockSolid? _stock;
    private IReadOnlyList<Contour> _contours = [];
    private ToolPath? _path;
    private Point3 _zero;
    private int _generation;
    private bool _fitted;

    /// <summary>Id da malha da peça STEP na cena.</summary>
    private const string PartId = "peca-step";

    private int _partVersion = 1;

    // CAM-STOCK-02: bruto depois de uma operação (malha do material, no referencial do programa); null = o bruto inteiro de antes
    private IReadOnlyList<SceneObjectState>? _material;
    private Point3 _materialZero;
    private int _materialVersion = 1;

    private readonly StackPanel _stepBar = new()
    {
        Orientation = Orientation.Horizontal,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Bottom,
        Margin = new Thickness(8),
        Spacing = 2,
        IsVisible = false,
    };

    private readonly ToggleButton _whole = StepButton("Bruto inteiro", "Mostra o bruto antes de qualquer operação.");
    private readonly ToggleButton _before = StepButton("Antes da operação", "Mostra o bruto antes da operação escolhida.");
    private readonly ToggleButton _after = StepButton("Depois da operação", "Mostra o bruto depois da operação escolhida.");
    private bool _failed;
    private bool _syncing;

    public MillJobSceneView()
    {
        Children.Add(_gl);
        Children.Add(_status);
        Children.Add(_bar);
        _stepBar.Children.Add(_whole);
        _stepBar.Children.Add(_before);
        _stepBar.Children.Add(_after);
        Children.Add(new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(8),
            Padding = new Thickness(3),
            CornerRadius = new CornerRadius(6),
            Background = Brush.Parse("#e61b1d22"),
            BorderBrush = Brush.Parse("#2c3037"),
            BorderThickness = new Thickness(1),
            Child = _stepBar,
            [!IsVisibleProperty] = _stepBar[!IsVisibleProperty],
        });
        _whole.Click += (_, _) => PickStep(StockStep.Whole);
        _before.Click += (_, _) => PickStep(StockStep.Before);
        _after.Click += (_, _) => PickStep(StockStep.After);
        SyncSteps();
        _gl.Failed += (_, message) =>
        {
            _failed = true;
            _gl.IsVisible = false;
            _bar.IsVisible = false;
            _status.Text = "3D pela placa de vídeo indisponível (" + message + "). Use a vista 2D.";
            _status.IsVisible = true;
        };

        _bar.Children.Add(_stockChip);
        _bar.Children.Add(_partChip);
        _bar.Children.Add(_edgesChip);
        _bar.Children.Add(_pathChip);
        _bar.Children.Add(_rapidsChip);
        _bar.Children.Add(_axesChip);

        _stockChip.Flyout = Menu(
            ("Sólido", () => Set(_options with { Stock = StockDisplay.Solid }), () => _options.Stock == StockDisplay.Solid, null),
            ("Transparente", () => Set(_options with { Stock = StockDisplay.Transparent }), () => _options.Stock == StockDisplay.Transparent, null),
            ("Oculto", () => Set(_options with { Stock = StockDisplay.Hidden }), () => _options.Stock == StockDisplay.Hidden, null));
        // CAM-ILHAS-02: com peça STEP, Sólida e Transparente valem; só com contornos 2D ficam apagadas com a dica
        Func<string?> noSolid = () => _part is null ? CamViewOptions.MissingPartHeight : null;
        _partChip.Flyout = Menu(
            ("Sólida", () => Set(_options with { Part = PartDisplay.Solid }), () => ShownPart == PartDisplay.Solid, noSolid),
            ("Transparente", () => Set(_options with { Part = PartDisplay.Transparent }), () => ShownPart == PartDisplay.Transparent, noSolid),
            ("Aramado", () => Set(_options with { Part = PartDisplay.Wireframe }), () => ShownPart == PartDisplay.Wireframe, null),
            ("Oculta", () => Set(_options with { Part = PartDisplay.Hidden }), () => _options.Part == PartDisplay.Hidden, null));

        _edgesChip.IsCheckedChanged += (_, _) => FromToggle(o => o with { Edges = _edgesChip.IsChecked == true });
        _pathChip.IsCheckedChanged += (_, _) => FromToggle(o => o with { Path = _pathChip.IsChecked == true });
        _rapidsChip.IsCheckedChanged += (_, _) => FromToggle(o => o with { Rapids = _rapidsChip.IsChecked == true });
        _axesChip.IsCheckedChanged += (_, _) => FromToggle(o => o with { Axes = _axesChip.IsChecked == true });
        SyncChips();
    }

    /// <summary>Escolhas dos chips (vêm do config do app; não vão para o .casillas-job).</summary>
    public CamViewOptions Options
    {
        get => _options;
        set
        {
            _options = value;
            SyncChips();
            Rebuild();
        }
    }

    /// <summary>Avisa quando o usuário muda um chip (para guardar no config).</summary>
    public event EventHandler<CamViewOptions>? OptionsChanged;

    /// <summary>Qual bruto a barra de baixo pede (CAM-STOCK-02): inteiro, antes ou depois da operação escolhida.</summary>
    public StockStep Step { get; private set; } = StockStep.After;

    /// <summary>A barra de baixo mudou o passo do bruto.</summary>
    public event EventHandler<StockStep>? StepChanged;

    /// <summary>Mostra ou esconde a barra Bruto inteiro, Antes e Depois (só com bruto por operação ligado e uma operação escolhida).</summary>
    public bool StepBarVisible
    {
        get => _stepBar.IsVisible;
        set => _stepBar.IsVisible = value && !_failed;
    }

    /// <summary>
    /// Material do bruto depois de uma operação (CAM-STOCK-02), no referencial do programa; <paramref name="zero"/> o leva ao
    /// referencial do desenho, como a trajetória. Null volta ao bruto inteiro de antes. Só mostra: simulação é verificação.
    /// </summary>
    public void SetMaterial(IReadOnlyList<SceneObjectState>? material, Point3 zero)
    {
        if (material is null && _material is null)
        {
            return;
        }

        _material = material;
        _materialZero = zero;
        _materialVersion++;
        SyncChips();
        Rebuild();
    }

    /// <summary>Escolhe o passo do bruto por fora (botões Ver antes e Ver depois das propriedades).</summary>
    public void ChooseStep(StockStep step) => PickStep(step);

    private void PickStep(StockStep step)
    {
        Step = step;
        SyncSteps();
        StepChanged?.Invoke(this, step);
    }

    private void SyncSteps()
    {
        _whole.IsChecked = Step == StockStep.Whole;
        _before.IsChecked = Step == StockStep.Before;
        _after.IsChecked = Step == StockStep.After;
    }

    /// <summary>Lê a trajetória do programa (segundo plano) e mostra bruto, peça e trajetória sem simular.</summary>
    public async void Render(string program, Dialect dialect, StockSolid? stock, IReadOnlyList<Contour> contours, Point3 zero = default)
    {
        int generation = ++_generation;
        _zero = zero;
        _stock = stock;
        _contours = contours;
        if (_failed)
        {
            return;
        }

        ToolPath? path = null;
        if (!string.IsNullOrWhiteSpace(program))
        {
            try
            {
                path = await Task.Run(() => ProgramAnalyzer.Analyze(program, dialect).ToolPath);
            }
            catch (Exception)
            {
                path = null; // sem trajetória: bruto e peça continuam na tela
            }

            if (generation != _generation)
            {
                return; // chegou uma versão mais nova
            }
        }

        _path = path;
        _status.IsVisible = false;
        _gl.IsVisible = true;
        Rebuild();
        if (!_fitted)
        {
            _fitted = true;
            // espera o controle ter tamanho antes de enquadrar
            Dispatcher.UIThread.Post(() => _gl.Fit(SceneView.Isometric), DispatcherPriority.Background);
        }
    }

    /// <summary>Enquadra de novo (vista pronta).</summary>
    public void Fit(SceneView view) => _gl.Fit(view);

    /// <summary>Clique na vista 3D (raio na cena), para escolher faces da peça STEP (M3D-2).</summary>
    public event EventHandler<SceneClickEventArgs>? SceneClicked
    {
        add => _gl.SceneClicked += value;
        remove => _gl.SceneClicked -= value;
    }

    private TriangleMesh? _part;
    private int[] _selected = [];
    private float[] _edgeLines = [];

    /// <summary>
    /// Peça STEP do Trabalho já na posição (M3D-1) e os triângulos das faces escolhidas (laranja). Null = sem peça 3D (só os contornos).
    /// Só mostra: não verifica colisão nem simula.
    /// </summary>
    public void SetPart(TriangleMesh? part, IReadOnlyCollection<int> selectedTriangles, IReadOnlyList<IReadOnlyList<Point3>>? edges = null)
    {
        _edgeLines = [];
        foreach (IReadOnlyList<Point3> chain in edges ?? [])
        {
            var segs = new List<float>();
            for (int k = 0; k + 1 < chain.Count; k++)
            {
                segs.AddRange([(float)chain[k].X, (float)chain[k].Y, (float)chain[k].Z, (float)chain[k + 1].X, (float)chain[k + 1].Y, (float)chain[k + 1].Z]);
            }

            _edgeLines = [.. _edgeLines, .. segs];
        }

        bool newPart = part is not null && !ReferenceEquals(part, _part);
        _part = part;
        _selected = [.. selectedTriangles];
        SyncChips();
        Rebuild();
        if (newPart)
        {
            // DESK-01: peça nova enquadrada (antes o 3D abria no tamanho do bruto ou dos contornos e cortava a peça)
            Dispatcher.UIThread.Post(() => _gl.Fit(SceneView.Isometric), DispatcherPriority.Background);
        }
    }

    private (float[] Positions, uint Color)[] _loops = [];

    /// <summary>
    /// CAM-ILHAS-02: laços da face do bolsão automático, cada um na cor do papel (ilha, ignorado, borda aberta, misto).
    /// Linhas por cima de tudo; vazio apaga. Só mostra.
    /// </summary>
    public void SetLoops(IReadOnlyList<(IReadOnlyList<Point3> Points, uint Color)> loops)
    {
        _loops = [.. loops.GroupBy(l => l.Color).Select(g =>
        {
            var segs = new List<float>();
            foreach ((IReadOnlyList<Point3> pts, uint _) in g)
            {
                for (int k = 0; k < pts.Count; k++)
                {
                    Point3 a = pts[k], b = pts[(k + 1) % pts.Count];
                    // um fio acima da face, para não sumir dentro dela
                    segs.AddRange([(float)a.X, (float)a.Y, (float)(a.Z + 0.05), (float)b.X, (float)b.Y, (float)(b.Z + 0.05)]);
                }
            }

            return (segs.ToArray(), g.Key);
        })];
        Rebuild();
    }

    /// <summary>Modo da peça na tela: com o bruto de uma operação na tela, a peça sólida vira transparente para não esconder o material.</summary>
    private PartDisplay ShownPart
    {
        get
        {
            PartDisplay mode = _options.EffectivePart(hasSolidPart: _part is not null);
            return mode == PartDisplay.Solid && _material is not null && _part is not null ? PartDisplay.Transparent : mode;
        }
    }

    /// <summary>Opacidade da peça transparente e do aramado da peça STEP (as arestas continuam desenhadas).</summary>
    private const float PartTransparentOpacity = 0.3f, PartWireOpacity = 0.06f;

    private void Rebuild()
    {
        if (_failed)
        {
            return;
        }

        SceneSnapshot scene = CamJobScene.BuildResolved(_stock, _contours, _path, _options, zero: _zero);
        if (_material is { } material && _options.Stock != StockDisplay.Hidden)
        {
            // CAM-STOCK-02: o material simulado no lugar do bloco inteiro (mesma posição: programa + zero)
            var objects = scene.Objects.Where(o => o.Id != CamJobScene.StockId).ToList();
            Point3 shift = new(_materialZero.X, _materialZero.Y, _materialZero.Z);
            Bounds3 b = scene.Bounds;
            foreach (SceneObjectState m in material)
            {
                objects.Add(m with { Id = "bruto-passo/" + m.Id, Version = _materialVersion, Offset = shift });
            }

            scene = scene with { Objects = objects, Bounds = b };
        }
        PartDisplay partMode = ShownPart;
        if (_part is { TriangleCount: > 0 } part && partMode != PartDisplay.Hidden)
        {
            // peça STEP: sólida (papel de fixação, opaca mesmo com o bruto transparente), transparente sobre o material ou quase
            // invisível no aramado (papel peça, CAM-ILHAS-02); faces escolhidas em laranja, sempre opacas
            SceneRole role = partMode == PartDisplay.Solid ? SceneRole.Fixture : SceneRole.Part;
            var objects = scene.Objects.ToList();
            if (_selected.Length == 0)
            {
                objects.Add(new SceneObjectState(PartId, role, part.ToSolidMesh(), _partVersion, Point3.Origin, true));
            }
            else
            {
                objects.Add(new SceneObjectState(PartId, role, part.Subset(_selected, invert: true).ToSolidMesh(), _partVersion, Point3.Origin, true));
                objects.Add(new SceneObjectState(PartId + "/selecao", SceneRole.Fixture, part.Subset(_selected).ToSolidMesh(0xFF9F1C), _partVersion, Point3.Origin, true));
            }

            Bounds3 b = scene.Bounds.Include(part.Bounds.Min).Include(part.Bounds.Max);
            scene = scene with
            {
                Objects = objects,
                Bounds = b,
                PartOpacity = partMode == PartDisplay.Wireframe ? PartWireOpacity : PartTransparentOpacity,
            };
            if (_edgeLines.Length > 0)
            {
                scene = scene with { Lines = [.. scene.Lines, new SceneLinesState(SceneRole.Highlight, _edgeLines, 0xFF9F1C)] };
            }
        }

        if (_loops.Length > 0)
        {
            scene = scene with { Lines = [.. scene.Lines, .. _loops.Select(l => new SceneLinesState(SceneRole.Highlight, l.Positions, l.Color))] };
        }

        _partVersion++;
        _gl.Show(scene);
        bool hasStock = _stock is { SizeX: > 0, SizeY: > 0, SizeZ: > 0 };
        _stockChip.IsEnabled = hasStock;
        ToolTip.SetTip(_stockChip, hasStock ? "Como o bruto aparece. O 3D não remove material: a simulação fica no Casillas Code." : "Sem bruto completo: defina o bruto na Preparação.");
        bool hasPart = _contours.Count > 0 || _part is not null;
        _partChip.IsEnabled = hasPart;
        ToolTip.SetTip(_partChip, hasPart ? "Como a peça aparece. Hoje a peça são os contornos 2D, desenhados em Z 0." : "Sem desenho: abra um DXF ou desenhe uma forma.");
    }

    private void Set(CamViewOptions options)
    {
        if (options == _options)
        {
            return;
        }

        _options = options;
        SyncChips();
        Rebuild();
        OptionsChanged?.Invoke(this, options);
    }

    private void FromToggle(Func<CamViewOptions, CamViewOptions> change)
    {
        if (!_syncing)
        {
            Set(change(_options));
        }
    }

    private void SyncChips()
    {
        _syncing = true;
        _stockChip.Content = "Bruto: " + _options.Stock switch { StockDisplay.Solid => "Sólido", StockDisplay.Hidden => "Oculto", _ => "Transparente" } + " ▾";
        // mostra o que está na tela: sem peça STEP não há sólido (os contornos 2D viram aramado)
        _partChip.Content = "Peça: " + ShownPart switch { PartDisplay.Solid => "Sólida", PartDisplay.Hidden => "Oculta", PartDisplay.Transparent => "Transparente", _ => "Aramado" } + " ▾";
        _edgesChip.IsChecked = _options.Edges;
        _pathChip.IsChecked = _options.Path;
        _rapidsChip.IsChecked = _options.Rapids;
        _rapidsChip.IsEnabled = _options.Path;
        ToolTip.SetTip(_rapidsChip, _options.Path ? "Mostra os rápidos G00 (amarelo)." : "Ligue a Trajetória para ver os rápidos.");
        _axesChip.IsChecked = _options.Axes;
        _syncing = false;
    }

    private static MenuFlyout Menu(params (string Text, Action Pick, Func<bool> Current, Func<string?>? DisabledTip)[] items)
    {
        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        var entries = new List<(MenuItem Item, Func<bool> Current, string Text, Func<string?>? DisabledTip)>();
        foreach ((string text, Action pick, Func<bool> current, Func<string?>? disabledTip) in items)
        {
            var item = new MenuItem { Header = text, FontSize = 13 };
            item.Click += (_, _) => pick();
            entries.Add((item, current, text, disabledTip));
            flyout.Items.Add(item);
        }

        flyout.Opening += (_, _) =>
        {
            foreach ((MenuItem item, Func<bool> current, string text, Func<string?>? disabledTip) in entries)
            {
                // apagado com a dica do porquê enquanto não dá (ex.: peça sólida sem STEP)
                string? why = disabledTip?.Invoke();
                item.IsEnabled = why is null;
                item.Header = why is null ? text : text + " (contornos do DXF: em breve)";
                ToolTip.SetTip(item, why);
                ToolTip.SetShowOnDisabled(item, why is not null);
                item.Icon = current() && item.IsEnabled ? new TextBlock { Text = "✓", FontSize = 13 } : null;
            }
        };
        return flyout;
    }

    private static Button Chip() => new()
    {
        Padding = new Thickness(10, 3),
        MinHeight = 26,
        FontSize = 12,
        CornerRadius = new CornerRadius(13),
        Background = Brush.Parse("#cc131922"),
        BorderBrush = Brush.Parse("#2c3746"),
        BorderThickness = new Thickness(1),
    };

    private static ToggleButton StepButton(string text, string tip)
    {
        var t = new ToggleButton
        {
            Content = text,
            Padding = new Thickness(10, 3),
            MinHeight = 26,
            FontSize = 11.5,
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(0),
        };
        ToolTip.SetTip(t, tip);
        return t;
    }

    private static ToggleButton Toggle(string text, string tip)
    {
        var t = new ToggleButton
        {
            Content = text,
            Padding = new Thickness(10, 3),
            MinHeight = 26,
            FontSize = 12,
            CornerRadius = new CornerRadius(13),
            BorderBrush = Brush.Parse("#2c3746"),
            BorderThickness = new Thickness(1),
        };
        ToolTip.SetTip(t, tip);
        ToolTip.SetShowOnDisabled(t, true);
        return t;
    }
}

/// <summary>Qual bruto mostrar com bruto por operação (CAM-STOCK-02).</summary>
public enum StockStep
{
    /// <summary>Depois da operação escolhida (padrão).</summary>
    After,

    /// <summary>Antes da operação escolhida (depois da anterior com estado).</summary>
    Before,

    /// <summary>O bruto inteiro, antes de qualquer operação.</summary>
    Whole,
}
