using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace CncStudio.App.Modules;

/// <summary>Um módulo da plataforma Casillas e se já existe no desktop. <see cref="Color"/> é a cor do produto (igual à web).</summary>
public sealed record PlatformModule(string Id, string Name, string Description, bool Available, string Color);

/// <summary>Um módulo que vive só na versão web: no desktop é um botão que abre o endereço no navegador (Url vazia = em breve).</summary>
public sealed record WebModule(string Id, string Name, string Description, string Url, string Color);

/// <summary>Lista única dos módulos (barra lateral e tela Início). O desktop é voltado para o operador (decisão de 26/10).</summary>
public static class PlatformModules
{
    public static readonly IReadOnlyList<PlatformModule> All =
    [
        new("code", "Casillas Code", "Editor G-code e M-code com simulação 3D", true, "#e36bf0"),
        new("otimizador", "Casillas Otimizador", "Encontra repetições e simplifica o programa", true, "#ffb454"),
        new("macro", "Casillas Macro", "Geradores de programa: furação, bolsões, rosca, texto", true, "#9b7bff"),
        new("cam", "Casillas CAM", "Do desenho ao programa 2,5D", false, "#4d8dff"),
    ];

    /// <summary>Endereço da plataforma na web. Troca só aqui quando o casillas.com.br ativar.</summary>
    public const string WebBase = "https://casillas-code.pages.dev";

    public static readonly IReadOnlyList<WebModule> Web =
    [
        new("academy", "Casillas Academy", "Formação profissional", WebBase + "/#academy", "#3ddc84"),
        new("loja", "Casillas Loja", "Ferramentas e insumos (em teste)", WebBase + "/#loja", "#2fd0c8"),
        new("calculadoras", "Casillas Calculadoras", "Cálculos técnicos de usinagem", "https://martinsdesiqueiraigor-art.github.io/Casillas_app_2/", "#ff9a4a"),
        new("biblioteca", "Casillas Biblioteca CNC", "Banco de dados G-code", "", "#ee5a45"),
    ];

    /// <summary>Ilustração do módulo (a mesma da web); null se não achar.</summary>
    public static Bitmap? Art(string id)
    {
        try
        {
            return new Bitmap(AssetLoader.Open(new Uri("avares://CasillasCode/Assets/plataforma/ic-" + id + ".webp")));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Logo com os mascotes (a mesma da web).</summary>
    public static Bitmap? Logo()
    {
        try
        {
            return new Bitmap(AssetLoader.Open(new Uri("avares://CasillasCode/Assets/plataforma/topo.webp")));
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Abre o endereço no navegador padrão; devolve false se não conseguiu.</summary>
    public static bool OpenWeb(string url)
    {
        if (url.Length == 0)
        {
            return false;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public static IBrush Brush(string hex, byte alpha = 255)
    {
        Color c = Color.Parse(hex);
        return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
    }
}
