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

    /// <summary>Coloca o campo de arquivo nativo (transparente) em cima do botão "Abrir".</summary>
    [JSImport("placeFileInput", Module)]
    public static partial void PlaceFileInput(
        [JSMarshalAs<JSType.Number>] double x,
        [JSMarshalAs<JSType.Number>] double y,
        [JSMarshalAs<JSType.Number>] double width,
        [JSMarshalAs<JSType.Number>] double height,
        [JSMarshalAs<JSType.Boolean>] bool visible);

    /// <summary>Próximo arquivo escolhido pelo campo sobre o botão "Abrir" ("nome\nconteúdo").</summary>
    [JSImport("nextPickedFile", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string?> NextPickedFileAsync();

    /// <summary>O navegador permite instalar o app agora (PWA)?</summary>
    [JSImport("canInstall", Module)]
    [return: JSMarshalAs<JSType.Boolean>]
    public static partial bool CanInstall();

    /// <summary>Abre a janela de instalação do navegador ("accepted"/"dismissed").</summary>
    [JSImport("promptInstall", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string?> PromptInstallAsync();

    [JSImport("storageGet", Module)]
    [return: JSMarshalAs<JSType.String>]
    public static partial string? StorageGet([JSMarshalAs<JSType.String>] string key);

    [JSImport("storageSet", Module)]
    public static partial void StorageSet([JSMarshalAs<JSType.String>] string key, [JSMarshalAs<JSType.String>] string value);

    [JSImport("vibrate", Module)]
    public static partial void Vibrate([JSMarshalAs<JSType.Number>] int milliseconds);

    /// <summary>Avisa quando o app vai para segundo plano.</summary>
    [JSImport("onHidden", Module)]
    public static partial void OnHidden([JSMarshalAs<JSType.Function>] Action callback);

    /// <summary>Editor nativo em tela cheia; texto novo ou null se cancelar.</summary>
    [JSImport("editText", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string?> EditTextAsync(
        [JSMarshalAs<JSType.String>] string text,
        [JSMarshalAs<JSType.Number>] int line,
        [JSMarshalAs<JSType.String>] string title);
}
