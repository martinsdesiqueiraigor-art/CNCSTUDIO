using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CncStudio.App;

// Abre a janela principal com um programa, espera a análise/simulação e salva PNGs.
// Uso: dotnet run --project tools/CncStudio.Screenshots -- <pasta de saída>
string output = args.Length > 0 ? args[0] : "screenshots";
Directory.CreateDirectory(output);

AppBuilder.Configure<App>()
    .UseSkia()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .SetupWithoutStarting();

string samples = Path.Combine(AppContext.BaseDirectory, "samples");

// Largura da janela: 1500 px (desktop). A web é fotografada por outro caminho.
Shoot("1-fresa.png", Path.Combine(samples, "EXEMPLO.nc"), window => { });
Shoot("2-torno.png", Path.Combine(samples, "torno", "REBAIXO_CANAL.nc"), window => { });
Shoot("3-fresa-linha-a-linha.png", Path.Combine(samples, "EXEMPLO.nc"), window =>
{
    Click(window, "ResetButton");
    for (int i = 0; i < 14; i++)
    {
        Click(window, "NextLineButton");
        Pump(150);
    }
});
Shoot("4-torno-2d.png", Path.Combine(samples, "torno", "REBAIXO_CANAL.nc"), window => Click(window, "Mode2D"));
Shoot("5-fresa-so-3d.png", Path.Combine(samples, "EXEMPLO.nc"), window => Click(window, "Mode3D"));
Shoot("6-editor-recolhido.png", Path.Combine(samples, "EXEMPLO.nc"), window => Click(window, "CollapseEditorButton"));
Shoot("7-fresa-so-2d-iso.png", Path.Combine(samples, "EXEMPLO.nc"), window =>
{
    Click(window, "Mode2D");
    Click(window, "IsoView");
});

// Conferência do mouse no 3D (CODE-06): no modo 2D + 3D e no só 3D, girar, deslocar e aproximar mudam a câmera
bool mouseOk = MouseCheck(Path.Combine(samples, "EXEMPLO.nc"), "Mode2D3D")
             & MouseCheck(Path.Combine(samples, "EXEMPLO.nc"), "Mode3D")
             & MouseCheck(Path.Combine(samples, "torno", "REBAIXO_CANAL.nc"), "Mode2D3D");

Console.WriteLine(mouseOk ? "ok" : "FALHOU: o mouse não chegou ao 3D");
return mouseOk ? 0 : 1;

bool MouseCheck(string file, string mode)
{
    var window = new MainWindow([file]) { Width = 1500, Height = 900 };
    window.Show();
    Pump(1500);
    Click(window, mode);
    Pump(500);
    CncStudio.App.Graphics.GlSceneView? gl = window.GetVisualDescendantsOf<CncStudio.App.Graphics.GlSceneView>().FirstOrDefault();
    bool ok = false;
    if (gl is not null && gl.IsVisible && gl.Bounds.Width > 50)
    {
        Point center = gl.TranslatePoint(new Point(gl.Bounds.Width / 2, gl.Bounds.Height / 2), window) ?? default;
        var start = gl.Camera;

        window.MouseDown(center, Avalonia.Input.MouseButton.Left);
        window.MouseMove(center + new Point(60, 25));
        window.MouseUp(center + new Point(60, 25), Avalonia.Input.MouseButton.Left);
        var rotated = gl.Camera;

        window.MouseDown(center, Avalonia.Input.MouseButton.Right);
        window.MouseMove(center + new Point(-40, 30));
        window.MouseUp(center + new Point(-40, 30), Avalonia.Input.MouseButton.Right);
        var panned = gl.Camera;

        window.MouseWheel(center, new Vector(0, 2));
        var zoomed = gl.Camera;

        bool rotates = rotated.Azimuth != start.Azimuth || rotated.Elevation != start.Elevation;
        bool pans = panned.Target != rotated.Target;
        bool zooms = zoomed.Distance != panned.Distance;
        ok = rotates && pans && zooms;
        Console.WriteLine($"mouse {mode} ({Path.GetFileName(file)}): gira={rotates} move={pans} zoom={zooms}");
    }
    else
    {
        Console.WriteLine($"mouse {mode}: o 3D não está visível");
    }

    window.Close();
    return ok;
}

void Shoot(string name, string file, Action<Window> action)
{
    var window = new MainWindow([file]) { Width = 1500, Height = 900 };
    window.Show();
    Pump(2500);
    action(window);
    Pump(2500);
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    window.CaptureRenderedFrame()?.Save(Path.Combine(output, name));
    window.Close();
}

static void Click(Window window, string name)
{
    Button? button = window.GetVisualDescendantsOf<Button>().FirstOrDefault(b => b.Name == name);
    button?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}

static void Pump(int milliseconds)
{
    var until = DateTime.UtcNow.AddMilliseconds(milliseconds);
    while (DateTime.UtcNow < until)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Thread.Sleep(15);
    }
}

static class VisualSearch
{
    public static IEnumerable<T> GetVisualDescendantsOf<T>(this Avalonia.Visual root) where T : Avalonia.Visual =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(root).OfType<T>();
}
