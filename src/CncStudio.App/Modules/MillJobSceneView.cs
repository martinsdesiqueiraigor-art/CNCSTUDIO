using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CncStudio.App.Graphics;
using CncStudio.Core;
using CncStudio.Core.Cam;
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
    private bool _failed;
    private bool _syncing;

    public MillJobSceneView()
    {
        Children.Add(_gl);
        Children.Add(_status);
        Children.Add(_bar);
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
        _bar.Children.Add(ViewButton("Iso", SceneView.Isometric, "Vista isométrica"));
        _bar.Children.Add(ViewButton("Topo", SceneView.Top, "Vista de topo"));
        _bar.Children.Add(ViewButton("Frente", SceneView.Front, "Vista de frente"));

        _stockChip.Flyout = Menu(
            ("Sólido", () => Set(_options with { Stock = StockDisplay.Solid }), () => _options.Stock == StockDisplay.Solid, null),
            ("Transparente", () => Set(_options with { Stock = StockDisplay.Transparent }), () => _options.Stock == StockDisplay.Transparent, null),
            ("Oculto", () => Set(_options with { Stock = StockDisplay.Hidden }), () => _options.Stock == StockDisplay.Hidden, null));
        _partChip.Flyout = Menu(
            ("Sólida", () => Set(_options with { Part = PartDisplay.Solid }), () => _options.Part == PartDisplay.Solid, CamViewOptions.MissingPartHeight),
            ("Aramado", () => Set(_options with { Part = PartDisplay.Wireframe }), () => _options.EffectivePart(hasSolidPart: false) == PartDisplay.Wireframe, null),
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

    private void Rebuild()
    {
        if (_failed)
        {
            return;
        }

        _gl.Show(CamJobScene.BuildResolved(_stock, _contours, _path, _options, zero: _zero));
        bool hasStock = _stock is { SizeX: > 0, SizeY: > 0, SizeZ: > 0 };
        _stockChip.IsEnabled = hasStock;
        ToolTip.SetTip(_stockChip, hasStock ? "Como o bruto aparece. O 3D não remove material: a simulação fica no Casillas Code." : "Sem bruto completo: defina o bruto na Preparação.");
        bool hasPart = _contours.Count > 0;
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
        // a peça sólida ainda não existe (falta a altura): mostra o que está na tela
        PartDisplay shown = _options.EffectivePart(hasSolidPart: false);
        _partChip.Content = "Peça: " + shown switch { PartDisplay.Solid => "Sólida", PartDisplay.Hidden => "Oculta", _ => "Aramado" } + " ▾";
        _edgesChip.IsChecked = _options.Edges;
        _pathChip.IsChecked = _options.Path;
        _rapidsChip.IsChecked = _options.Rapids;
        _rapidsChip.IsEnabled = _options.Path;
        ToolTip.SetTip(_rapidsChip, _options.Path ? "Mostra os rápidos G00 (amarelo)." : "Ligue a Trajetória para ver os rápidos.");
        _axesChip.IsChecked = _options.Axes;
        _syncing = false;
    }

    private MenuFlyout Menu(params (string Text, Action Pick, Func<bool> Current, string? DisabledTip)[] items)
    {
        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        var entries = new List<(MenuItem Item, Func<bool> Current, string Text)>();
        foreach ((string text, Action pick, Func<bool> current, string? disabledTip) in items)
        {
            var item = new MenuItem { Header = text, FontSize = 13 };
            if (disabledTip is not null)
            {
                // fica apagado com a dica do porquê
                item.IsEnabled = false;
                item.Header = text + " (falta a altura da peça)";
                ToolTip.SetTip(item, disabledTip);
                ToolTip.SetShowOnDisabled(item, true);
            }

            item.Click += (_, _) => pick();
            entries.Add((item, current, text));
            flyout.Items.Add(item);
        }

        flyout.Opening += (_, _) =>
        {
            foreach ((MenuItem item, Func<bool> current, string _) in entries)
            {
                item.Icon = current() && item.IsEnabled ? new TextBlock { Text = "✓", FontSize = 13 } : null;
            }
        };
        return flyout;
    }

    private Button ViewButton(string text, SceneView view, string tip)
    {
        Button b = Chip();
        b.Content = text;
        ToolTip.SetTip(b, tip);
        b.Click += (_, _) => _gl.Fit(view);
        return b;
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
