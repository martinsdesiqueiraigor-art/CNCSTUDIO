using Avalonia;

namespace CncStudio.App;

internal static class Program
{
    // Ponto de entrada. Nada de Avalonia deve ser usado antes de AppMain ser chamado.
    [STAThread]
    public static void Main(string[] args) =>
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    // Também usado pelo designer visual do Avalonia
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
