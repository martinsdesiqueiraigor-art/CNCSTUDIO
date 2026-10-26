namespace CncStudio.App.Modules;

/// <summary>Um módulo da plataforma Casillas e se já existe no desktop.</summary>
public sealed record PlatformModule(string Id, string Name, string Description, bool Available);

/// <summary>Um módulo que vive só na versão web: no desktop é um botão que abre o endereço no navegador.</summary>
public sealed record WebModule(string Id, string Name, string Description, string Url);

/// <summary>Lista única dos módulos (barra lateral e tela Início). O desktop é voltado para o operador (decisão de 26/10).</summary>
public static class PlatformModules
{
    public static readonly IReadOnlyList<PlatformModule> All =
    [
        new("code", "Casillas Code", "Editor e simulador de programas Fanuc e Siemens, fresa e torno.", true),
        new("otimizador", "Casillas Otimizador", "Acha repetições em programas de CAM e propõe laços Macro B só para simulador.", true),
        new("macro", "Casillas Macro", "Geradores de programa: furação, bolsões, rosca, texto. Simule no Code.", true),
        new("cam", "Casillas CAM", "Do desenho ao programa 2,5D.", false),
    ];

    /// <summary>Endereço da plataforma na web. Troca só aqui quando o casillas.com.br ativar.</summary>
    public const string WebBase = "https://casillas-code.pages.dev";

    public static readonly IReadOnlyList<WebModule> Web =
    [
        new("academy", "Casillas Academy", "Cursos de programação CNC com aulas, exercícios e quizzes.", WebBase + "/#academy"),
        new("loja", "Casillas Loja", "Ferramentas e insumos, pedido pelo WhatsApp (em teste).", WebBase + "/#loja"),
        new("calculadoras", "Casillas Calculadoras", "Cálculos técnicos de usinagem.", "https://martinsdesiqueiraigor-art.github.io/Casillas_app_2/"),
    ];

    /// <summary>Abre o endereço no navegador padrão; devolve false se não conseguiu.</summary>
    public static bool OpenWeb(string url)
    {
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
}
