using System.Runtime.InteropServices.JavaScript;
using Avalonia.Browser;
using Avalonia.Controls;
using Avalonia.Platform;

namespace CncStudio.Web;

/// <summary>Funções do wwwroot/control-panel.js (cartão do painel de controle).</summary>
internal static partial class ControlPanelInterop
{
    private const string Module = "controlpanel";
    private static Task? _loading;

    public static Task LoadAsync() => _loading ??= JSHost.ImportAsync(Module, "../control-panel.js");

    [JSImport("create", Module)]
    public static partial JSObject Create();

    [JSImport("update", Module)]
    public static partial void Update(JSObject host, string json);

    [JSImport("dispose", Module)]
    public static partial void Dispose(JSObject host);
}

/// <summary>
/// Camada transparente por cima do gráfico (2D ou 3D) com o cartão do painel de controle: coordenadas
/// absolutas, distância a percorrer, ferramenta, G ativos e zero de peça. É um elemento do navegador
/// (como o 3D), por isso some enquanto um menu do app está aberto. Só o cartão pega toques.
/// </summary>
internal sealed class ControlPanelView : NativeControlHost
{
    private JSObject? _host;
    private string _json = "";

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _host = ControlPanelInterop.Create();
        ControlPanelInterop.Update(_host, _json);
        return new JSObjectControlHandle(_host);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (_host is { } host)
        {
            ControlPanelInterop.Dispose(host);
        }

        _host = null;
        base.DestroyNativeControlCore(control);
    }

    /// <summary>Valores a mostrar (<c>ControlPanelState.ToJson</c>); null esconde o cartão.</summary>
    public void SetState(string? json)
    {
        _json = json ?? "";
        if (_host is { } host)
        {
            ControlPanelInterop.Update(host, _json);
        }
    }
}
