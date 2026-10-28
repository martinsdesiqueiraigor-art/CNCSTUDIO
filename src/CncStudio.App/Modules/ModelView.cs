using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CncStudio.App.Graphics;
using CncStudio.Core.Cam3D;
using CncStudio.Core.Scene;

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
        Text = "Abra um arquivo STL para ver o modelo.",
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Opacity = 0.6,
    };

    private readonly ComboBox _unit = new() { HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly CheckBox _top = new() { Content = "Topo da peça em Z = 0 e canto em X0 Y0", IsChecked = true };
    private TriangleMesh? _raw;
    private string _fileName = "";

    /// <summary>Abre o seletor de STL e devolve nome e bytes (null se cancelou).</summary>
    public Func<Task<(string Name, byte[] Data)?>>? PickStl { get; set; }

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
            Text = "Primeiro passo do CAM 3D: abrir o modelo, conferir medidas e malha. O percurso 3D (varredura e desbaste) vem nas próximas etapas.",
            Opacity = 0.75, TextWrapping = TextWrapping.Wrap, FontSize = 12,
        });
        panel.Children.Add(open);
        panel.Children.Add(new TextBlock { Text = "Unidade do arquivo (o STL não guarda a unidade)", Opacity = 0.75, FontSize = 12 });
        panel.Children.Add(_unit);
        panel.Children.Add(_top);
        panel.Children.Add(_info);

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
