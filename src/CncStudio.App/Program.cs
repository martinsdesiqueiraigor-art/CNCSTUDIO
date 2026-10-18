using Avalonia;

namespace CncStudio.App;

internal static class Program
{
    // Ponto de entrada. Nada de Avalonia deve ser usado antes de AppMain ser chamado.
    [STAThread]
    public static void Main(string[] args)
    {
        // Diagnóstico: CASILLAS_TRACE=1 manda os avisos do Avalonia (ex.: OpenGL) para o console
        if (Environment.GetEnvironmentVariable("CASILLAS_TRACE") == "1")
        {
            System.Diagnostics.Trace.Listeners.Add(new System.Diagnostics.ConsoleTraceListener());
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Também usado pelo designer visual do Avalonia
    public static AppBuilder BuildAvaloniaApp()
    {
        AppBuilder builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

        // Testes sem placa de vídeo (Linux com renderização por software): libera o OpenGL do Mesa,
        // que o Avalonia recusa por padrão. Só com CASILLAS_SOFTWARE_GL=1.
        if (Environment.GetEnvironmentVariable("CASILLAS_SOFTWARE_GL") == "1")
        {
            builder = builder.With(new X11PlatformOptions { GlxRendererBlacklist = [] });
        }

        return builder;
    }
}
