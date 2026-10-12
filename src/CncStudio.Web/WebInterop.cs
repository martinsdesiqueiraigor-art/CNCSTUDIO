using System.Runtime.InteropServices.JavaScript;

namespace CncStudio.Web;

/// <summary>Funções do navegador em wwwroot/web.js (seletor de arquivo e editor de texto nativos).</summary>
internal static partial class WebInterop
{
    public const string Module = "casillas";

    /// <summary>Carrega o web.js (caminho relativo à pasta do dotnet.js).</summary>
    public static Task LoadAsync() => JSHost.ImportAsync(Module, "../web.js");

    [JSImport("isTouch", Module)]
    [return: JSMarshalAs<JSType.Boolean>]
    public static partial bool IsTouch();

    /// <summary>"nome\nconteúdo" do arquivo escolhido, ou null se cancelar.</summary>
    [JSImport("pickFile", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string?> PickFileAsync();

    /// <summary>Editor nativo em tela cheia; texto novo ou null se cancelar.</summary>
    [JSImport("editText", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string?> EditTextAsync(
        [JSMarshalAs<JSType.String>] string text,
        [JSMarshalAs<JSType.Number>] int line,
        [JSMarshalAs<JSType.String>] string title);
}
