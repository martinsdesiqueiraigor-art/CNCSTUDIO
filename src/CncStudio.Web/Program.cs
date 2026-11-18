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
        await BuildAvaloniaApp()
            .ConfigureFonts(fonts => fonts.AddFontCollection(new InterFonts())) // o tema Fluent usa "fonts:Inter#Inter"
            .StartBrowserAppAsync("out");
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>();
}

/// <summary>
/// WEB-03: a fonte Inter do tema, embutida só com os pesos usados (Regular, SemiBold, Bold) e os caracteres latinos, gregos e
/// símbolos (antes: o pacote Avalonia.Fonts.Inter, com 6 pesos e todos os alfabetos). Mesmo nome de coleção do pacote.
/// </summary>
internal sealed class InterFonts : Avalonia.Media.Fonts.EmbeddedFontCollection
{
    public InterFonts()
        : base(new Uri("fonts:Inter", UriKind.Absolute), new Uri("avares://CncStudio.Web/Assets/Fonts/Inter", UriKind.Absolute))
    {
    }
}
