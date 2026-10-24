using System.Runtime.InteropServices.JavaScript;

namespace CncStudio.Web;

/// <summary>Funções do navegador em wwwroot/web.js (seletor de arquivo e editor de texto nativos).</summary>
internal static partial class WebInterop
{
    public const string Module = "casillas";

    /// <summary>Carrega o web.js (caminho relativo à pasta do dotnet.js).</summary>
    public static Task LoadAsync() => JSHost.ImportAsync(Module, "../web.js");

    [JSImport("editMachiningSetup", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string?> EditMachiningSetupAsync([JSMarshalAs<JSType.String>] string json);

    [JSImport("isTouch", Module)]
    [return: JSMarshalAs<JSType.Boolean>]
    public static partial bool IsTouch();

    /// <summary>"nome\nconteúdo" do arquivo escolhido, ou null se cancelar.</summary>
    [JSImport("pickFile", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string?> PickFileAsync();

    /// <summary>Coloca o campo de arquivo nativo "id" (transparente) em cima de um botão.</summary>
    [JSImport("placeFileInput", Module)]
    public static partial void PlaceFileInput(
        [JSMarshalAs<JSType.String>] string id,
        [JSMarshalAs<JSType.Boolean>] bool multiple,
        [JSMarshalAs<JSType.Number>] double x,
        [JSMarshalAs<JSType.Number>] double y,
        [JSMarshalAs<JSType.Number>] double width,
        [JSMarshalAs<JSType.Number>] double height,
        [JSMarshalAs<JSType.Boolean>] bool visible);

    /// <summary>Próxima escolha no campo "id": JSON [{name, text}].</summary>
    [JSImport("nextPickedFiles", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string?> NextPickedFilesAsync([JSMarshalAs<JSType.String>] string id);

    // ── Biblioteca (IndexedDB) ──

    [JSImport("libList", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string?> LibListAsync();

    [JSImport("libRead", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string?> LibReadAsync([JSMarshalAs<JSType.String>] string path);

    [JSImport("libWrite", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.Boolean>>]
    public static partial Task<bool> LibWriteAsync([JSMarshalAs<JSType.String>] string path, [JSMarshalAs<JSType.String>] string text);

    [JSImport("libMakeFolder", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.Boolean>>]
    public static partial Task<bool> LibMakeFolderAsync([JSMarshalAs<JSType.String>] string path);

    [JSImport("libExists", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.Boolean>>]
    public static partial Task<bool> LibExistsAsync([JSMarshalAs<JSType.String>] string path);

    [JSImport("libDelete", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.Boolean>>]
    public static partial Task<bool> LibDeleteAsync([JSMarshalAs<JSType.String>] string path);

    [JSImport("libRename", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.Boolean>>]
    public static partial Task<bool> LibRenameAsync([JSMarshalAs<JSType.String>] string from, [JSMarshalAs<JSType.String>] string to);

    /// <summary>Compartilha ou baixa arquivos (JSON [{name, text}]).</summary>
    [JSImport("shareFiles", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string?> ShareFilesAsync([JSMarshalAs<JSType.String>] string filesJson, [JSMarshalAs<JSType.String>] string title);

    // ── Diálogos nativos ──

    [JSImport("promptText", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string?> PromptTextAsync(
        [JSMarshalAs<JSType.String>] string title,
        [JSMarshalAs<JSType.String>] string initial,
        [JSMarshalAs<JSType.String>] string okText);

    [JSImport("confirmBox", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.Boolean>>]
    public static partial Task<bool> ConfirmAsync(
        [JSMarshalAs<JSType.String>] string message,
        [JSMarshalAs<JSType.String>] string okText,
        [JSMarshalAs<JSType.Boolean>] bool danger);

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

    // ───────────── Conta (account.js) ─────────────

    /// <summary>Situação da conta neste aparelho (JSON; ver account.js).</summary>
    [JSImport("accountCheck", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string?> AccountCheckAsync();

    /// <summary>Tela de entrar/criar conta: "ok" ou "guest".</summary>
    [JSImport("accountLogin", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string?> AccountLoginAsync([JSMarshalAs<JSType.String>] string reason);

    /// <summary>Senha nova depois do link de recuperação.</summary>
    [JSImport("accountNewPassword", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.Boolean>>]
    public static partial Task<bool> AccountNewPasswordAsync();

    /// <summary>Frase curta da situação ("teste grátis: 12 dias").</summary>
    [JSImport("accountSummary", Module)]
    [return: JSMarshalAs<JSType.String>]
    public static partial string AccountSummary([JSMarshalAs<JSType.String>] string json);

    /// <summary>Painel da conta: "signout", "login" ou "close".</summary>
    [JSImport("accountPanel", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string?> AccountPanelAsync([JSMarshalAs<JSType.String>] string json);

    /// <summary>Sair da conta (libera a vaga do aparelho).</summary>
    [JSImport("accountSignOut", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.Boolean>>]
    public static partial Task<bool> AccountSignOutAsync();

    /// <summary>Folha de setup em tela cheia (PDF / imprimir / compartilhar); resolve ao voltar.</summary>
    [JSImport("showSetupSheet", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string?> ShowSetupSheetAsync([JSMarshalAs<JSType.String>] string html, [JSMarshalAs<JSType.String>] string fileBase);

    /// <summary>
    /// Geradores de programa (generators-ui.js): o formulário é em HTML e pede o programa ao Core pelo
    /// callback (id, valores JSON, modo "plain"/"macro") → JSON. Devolve JSON {id, mode, text, view3d, values} ou null.
    /// </summary>
    [JSImport("openGenerators", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string?> OpenGeneratorsAsync(
        [JSMarshalAs<JSType.String>] string catalogJson,
        [JSMarshalAs<JSType.Function<JSType.String, JSType.String, JSType.String, JSType.String>>] Func<string, string, string, string> generate,
        [JSMarshalAs<JSType.String>] string lastJson);

    /// <summary>
    /// Academy (academy-ui.js): trilhas, aulas, quizzes e teste final em HTML. O conteúdo e a correção vêm do Core pelo callback
    /// (comando, argumentos JSON) → JSON. O progresso fica no aparelho (localStorage). Devolve JSON {name, text, view3d} ou null.
    /// </summary>
    [JSImport("openAcademy", Module)]
    [return: JSMarshalAs<JSType.Promise<JSType.String>>]
    public static partial Task<string?> OpenAcademyAsync(
        [JSMarshalAs<JSType.Function<JSType.String, JSType.String, JSType.String>>] Func<string, string, string> call);
}
