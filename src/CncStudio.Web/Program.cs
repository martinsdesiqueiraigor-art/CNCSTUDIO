using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Browser;

[assembly: SupportedOSPlatform("browser")]

namespace CncStudio.Web;

/// <summary>Ponto de entrada no navegador: o Avalonia desenha dentro do elemento "out" do index.html.</summary>
internal sealed class Program
{
    private static async Task Main(string[] args)
    {
        await WebInterop.LoadAsync();
        await BuildAvaloniaApp()
            .WithInterFont()
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>();
}
