using System.Globalization;
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
        // Números e datas no formato brasileiro (4,5 s · 13/10/2026). A versão web roda sem os dados
        // de idioma do .NET (InvariantGlobalization), então o formato é montado aqui.
        CultureInfo.DefaultThreadCurrentCulture = BrazilianCulture.Instance;
        CultureInfo.DefaultThreadCurrentUICulture = BrazilianCulture.Instance;
        CultureInfo.CurrentCulture = BrazilianCulture.Instance;
        CultureInfo.CurrentUICulture = BrazilianCulture.Instance;

        await WebInterop.LoadAsync();

        // Tela de teste do 3D (Fase 1): só com ?poc3d na URL
        App.ProofOfConcept3D = args.Any(a => a.Contains("poc3d", StringComparison.OrdinalIgnoreCase));
        App.ProofOfConcept3DStart = args.Any(a => a.Contains("poc3d-a", StringComparison.OrdinalIgnoreCase)) ? "a"
            : args.Any(a => a.Contains("poc3d-cpu", StringComparison.OrdinalIgnoreCase)) ? "cpu" : "b";
        if (App.ProofOfConcept3D)
        {
            await Poc.Poc3DInterop.LoadAsync();
        }
        await BuildAvaloniaApp()
            .WithInterFont()
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>();
}
