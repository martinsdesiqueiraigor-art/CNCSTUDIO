using Avalonia;
using Avalonia.Controls;
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
Shoot("4-torno-zx.png", Path.Combine(samples, "torno", "REBAIXO_CANAL.nc"), window =>
{
    RadioButton? radio = window.GetVisualDescendantsOf<RadioButton>().FirstOrDefault(r => r.Name == "LatheZXView");
    if (radio is not null)
    {
        radio.IsChecked = true;
    }
});

Console.WriteLine("ok");

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
