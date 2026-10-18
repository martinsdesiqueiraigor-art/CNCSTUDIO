using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CncStudio.Core;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Parsing;
using CncStudio.Core.Rendering;
using CncStudio.Core.Reports;
using CncStudio.Core.Setup;
using CncStudio.Core.Simulation;

namespace CncStudio.Web.Poc;

/// <summary>Funções do wwwroot/poc3d.js (caminho B, three.js).</summary>
internal static partial class Poc3DInterop
{
    private const string Module = "poc3d";

    public static Task LoadAsync() => JSHost.ImportAsync(Module, "../poc3d.js");

    [JSImport("start", Module)]
    public static partial string Start(double topFraction);

    [JSImport("setMesh", Module)]
    public static partial void SetMesh(
        [JSMarshalAs<JSType.MemoryView>] Span<byte> positions,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> normals,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> colors,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> indices);

    [JSImport("setTop", Module)]
    public static partial void SetTop(double topFraction);

    [JSImport("getFps", Module)]
    public static partial double GetFps();

    [JSImport("stop", Module)]
    public static partial void Stop();
}

/// <summary>
/// Tela de teste da Fase 1 (abre com ?poc3d na URL): o mesmo programa simulado com remoção de material
/// e desenhado por três caminhos, para medir no próprio aparelho. Não faz parte do app normal.
/// </summary>
internal sealed class Poc3DView : UserControl
{
    private enum Mode { ThreeJs, OpenGl, Cpu }

    private readonly Panel _host = new() { Background = new SolidColorBrush(Color.Parse("#0d1117")) };
    private readonly StackPanel _bar = new() { Spacing = 6, Margin = new Thickness(8, 6) };
    private readonly TextBlock _info = new() { Foreground = Brushes.Gainsboro, FontSize = 13, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _fps = new() { Foreground = new SolidColorBrush(Color.Parse("#f0883e")), FontSize = 16, FontWeight = FontWeight.Bold };
    private readonly Image _cpuImage = new() { Stretch = Stretch.Uniform };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };

    private Mode _mode = Mode.ThreeJs;
    private int _cells = 200;
    private GlSolidView? _gl;
    private string _renderer = "";

    private ToolPathData? _data;
    private HeightMap? _map;
    private MaterialSimulator? _sim;
    private double _total;
    private int _lastVersion = -1;
    private readonly Stopwatch _sinceMesh = Stopwatch.StartNew();
    private double _buildMs, _sendMs, _cpuMs;
    private int _triangles;

    // Câmera em órbita (caminhos A e CPU; o three.js usa os próprios controles)
    private double _azimuth = 35, _elevation = 35, _distance = 300;
    private float[] _target = [0, 0, 0];
    private Point? _drag;

    private sealed record ToolPathData(ProgramAnalysis Analysis, Stock Stock, Func<int, ToolProfile> Profiles);

    public Poc3DView()
    {
        var buttons = new WrapPanel { Orientation = Orientation.Horizontal };
        Button Add(string text, Action click)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 5), FontSize = 13 };
            b.Click += (_, _) => Guard(text, click);
            buttons.Children.Add(b);
            return b;
        }

        Add("B three.js", () => SwitchTo(Mode.ThreeJs));
        Add("A OpenGL", () => SwitchTo(Mode.OpenGl));
        Add("CPU", () => SwitchTo(Mode.Cpu));
        Add("200", () => Restart(200));
        Add("400", () => Restart(400));
        Add("Reiniciar", () => Restart(_cells));

        _bar.Children.Add(new TextBlock { Text = "Teste do 3D · Fase 1", Foreground = Brushes.White, FontWeight = FontWeight.SemiBold, FontSize = 14 });
        _bar.Children.Add(_fps);
        _bar.Children.Add(_info);
        _bar.Children.Add(buttons);

        var root = new DockPanel { Background = new SolidColorBrush(Color.Parse("#161b22")) };
        DockPanel.SetDock(_bar, Dock.Top);
        root.Children.Add(_bar);
        root.Children.Add(_host);
        Content = root;

        _host.PointerPressed += (_, e) => _drag = e.GetPosition(_host);
        _host.PointerReleased += (_, _) => _drag = null;
        _host.PointerMoved += (_, e) =>
        {
            if (_drag is not { } from) return;
            Point to = e.GetPosition(_host);
            _azimuth += (to.X - from.X) * 0.4;
            _elevation = Math.Clamp(_elevation + (to.Y - from.Y) * 0.4, -85, 85);
            _drag = to;
            if (_mode == Mode.Cpu) RenderCpu();
        };
        _host.PointerWheelChanged += (_, e) =>
        {
            _distance *= Math.Pow(1.15, -e.Delta.Y);
            if (_mode == Mode.Cpu) RenderCpu();
        };

        _timer.Tick += (_, _) => Guard("Tick", Tick);

        AttachedToVisualTree += (_, _) => Dispatcher.UIThread.Post(() =>
        {
            Guard("início", () => Restart(_cells));
            Guard("início", () => SwitchTo(App.ProofOfConcept3DStart switch { "a" => Mode.OpenGl, "cpu" => Mode.Cpu, _ => Mode.ThreeJs }));
            _timer.Start();
        }, DispatcherPriority.Background);
    }

    // Erro na tela de teste: mostra na própria tela (e no console) em vez de travar o laço
    private string _error = "";

    private void Guard(string where, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            _error = $"Erro em {where}: {ex.GetType().Name}: {ex.Message}";
            Console.WriteLine(_error + "\n" + ex);
            _info.Text = _error;
        }
    }

    private ToolPathData Load()
    {
        using Stream stream = typeof(Poc3DView).Assembly.GetManifestResourceStream("samples/EXEMPLO.nc")!;
        string text = new StreamReader(stream).ReadToEnd();
        ParseResult parse = GCodeParser.Parse(text, Dialect.Fanuc);
        Dictionary<int, double> diameters = ToolCommentDiameters.Read(text, parse, Dialect.Fanuc);
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(text, Dialect.Fanuc);
        Stock stock = SetupBinding.SuggestStock(MachineKind.Mill, text, analysis.ToolPath.CuttingBounds);
        return new ToolPathData(analysis, stock, tool => SetupBinding.ProfileOf(null, tool, diameters.GetValueOrDefault(tool, 10)));
    }

    private void Restart(int cells)
    {
        _cells = cells;
        _data ??= Load();
        _map = HeightMap.CreateForStock(_data.Stock, cells);
        _sim = new MaterialSimulator(_map, _data.Analysis.ToolPath.Moves, _data.Profiles);
        _total = _data.Analysis.ToolPath.Moves.Sum(m => m.Length);
        _lastVersion = -1;
        _target = [(float)(_map.OriginX + _map.Width / 2), (float)(_map.OriginY + _map.Height / 2), (float)((_map.TopZ + _map.BottomZ) / 2)];
        _distance = Math.Max(_map.Width, _map.Height) * 2.4;
        PushMesh();
    }

    private void SwitchTo(Mode mode)
    {
        if (_mode == Mode.ThreeJs)
        {
            Poc3DInterop.Stop();
        }

        _host.Children.Clear();
        _gl = null;
        _mode = mode;
        _renderer = "";
        switch (mode)
        {
            case Mode.ThreeJs:
                _placedTop = HostTop();
                _renderer = Poc3DInterop.Start(_placedTop);
                break;
            case Mode.OpenGl:
                _gl = new GlSolidView { Camera = () => (_azimuth, _elevation, _distance, _target) };
                _host.Children.Add(_gl);
                break;
            case Mode.Cpu:
                _host.Children.Add(_cpuImage);
                break;
        }

        _lastVersion = -1;
        PushMesh();
    }

    // A área 3D do three.js fica por cima da página: acompanha onde a área de desenho começa.
    // Vai como fração da altura da janela, pois a unidade do Avalonia e o px da página podem diferir.
    private double _placedTop = -1;

    private double HostTop()
    {
        if (TopLevel.GetTopLevel(this) is not { } top || top.Bounds.Height <= 0)
        {
            return 0.3;
        }

        double y = _host.TranslatePoint(new Point(0, 0), top)?.Y ?? _bar.Bounds.Height;
        return Math.Clamp(y / top.Bounds.Height, 0, 0.9);
    }

    private void Tick()
    {
        if (_mode == Mode.ThreeJs && Math.Abs(HostTop() - _placedTop) > 0.0005)
        {
            _placedTop = HostTop();
            Poc3DInterop.SetTop(_placedTop);
        }

        if (_sim is null || _map is null)
        {
            return;
        }

        // Simulação em fatias de até 10 ms por quadro (a tela continua respondendo)
        var budget = Stopwatch.StartNew();
        while (!_sim.IsFinished && budget.ElapsedMilliseconds < 10)
        {
            _sim.Advance(2);
        }

        if (_map.Version != _lastVersion && (_sinceMesh.ElapsedMilliseconds >= 120 || _sim.IsFinished))
        {
            PushMesh();
        }

        double fps = _mode switch
        {
            Mode.ThreeJs => Poc3DInterop.GetFps(),
            Mode.OpenGl => _gl?.Fps ?? 0,
            // CPU só redesenha quando algo muda: o teto é 1 s ÷ tempo de um desenho
            _ => _cpuMs > 0 ? 1000 / _cpuMs : 0,
        };

        string modeName = _mode switch { Mode.ThreeJs => "B · three.js", Mode.OpenGl => "A · OpenGL Avalonia", _ => "CPU · atual" };
        string status = _mode == Mode.OpenGl ? _gl?.Status ?? "" : _renderer;
        double progress = _total > 0 ? Math.Min(100, _sim.Travelled / _total * 100) : 100;
        _fps.Text = (_mode == Mode.Cpu ? "até " : "") + $"{fps:0} quadros/s · {modeName}";
        _info.Text = $"Grade {_map.Columns}×{_map.Rows} · {_triangles:N0} triângulos · malha {_buildMs:0} ms · " +
                     (_mode == Mode.Cpu ? $"desenho {_cpuMs:0} ms" : $"envio {_sendMs:0} ms") +
                     $" · simulação {progress:0}%\n{(status.Length > 60 ? status[..60] + "…" : status)}" +
                     (_error.Length > 0 ? "\n" + _error : "");
    }

    private void PushMesh()
    {
        if (_map is null)
        {
            return;
        }

        _lastVersion = _map.Version;
        _sinceMesh.Restart();
        if (_mode == Mode.Cpu)
        {
            RenderCpu();
            return;
        }

        var watch = Stopwatch.StartNew();
        SolidMesh mesh = SolidMesh.FromHeightMap(_map);
        _buildMs = watch.Elapsed.TotalMilliseconds;
        _triangles = mesh.TriangleCount;

        watch.Restart();
        if (_mode == Mode.ThreeJs)
        {
            Poc3DInterop.SetMesh(
                MemoryMarshal.AsBytes(mesh.Positions).ToArray(),
                MemoryMarshal.AsBytes(mesh.Normals).ToArray(),
                MemoryMarshal.AsBytes(mesh.Colors).ToArray(),
                MemoryMarshal.AsBytes(mesh.Indices).ToArray());
        }
        else
        {
            _gl?.SetMesh(mesh);
        }

        _sendMs = watch.Elapsed.TotalMilliseconds;
    }

    private unsafe void RenderCpu()
    {
        if (_map is null)
        {
            return;
        }

        var watch = Stopwatch.StartNew();
        int maxPixels = (int)Math.Clamp(Math.Max(_host.Bounds.Width, _host.Bounds.Height) * (VisualRoot?.RenderScaling ?? 1), 200, 1400);
        SolidImage image = MillSolidRenderer.Render(_map, ViewCamera.Orbit(_azimuth, _elevation), maxPixels);
        var bitmap = new WriteableBitmap(new PixelSize(image.Width, image.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (ILockedFramebuffer buffer = bitmap.Lock())
        {
            fixed (uint* source = image.Pixels)
            {
                for (int row = 0; row < image.Height; row++)
                {
                    Buffer.MemoryCopy(source + row * image.Width, (byte*)buffer.Address + row * buffer.RowBytes, buffer.RowBytes, image.Width * 4);
                }
            }
        }

        _cpuImage.Source = bitmap;
        _cpuMs = watch.Elapsed.TotalMilliseconds;
        _triangles = 0;
    }
}
