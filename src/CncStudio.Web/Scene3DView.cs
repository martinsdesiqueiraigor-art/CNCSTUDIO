using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using Avalonia.Browser;
using Avalonia.Controls;
using Avalonia.Platform;
using CncStudio.Core.Geometry;
using CncStudio.Core.Rendering;
using CncStudio.Core.Scene;

namespace CncStudio.Web;

/// <summary>Funções do wwwroot/scene3d.js (motor 3D com three.js).</summary>
internal static partial class Scene3DInterop
{
    private const string Module = "scene3d";
    private static Task? _loading;

    /// <summary>Carrega o motor 3D só na primeira vez que a vista 3D é aberta (download menor no início).</summary>
    public static Task LoadAsync() => _loading ??= JSHost.ImportAsync(Module, "../scene3d.js");

    [JSImport("create", Module)]
    public static partial JSObject Create();

    [JSImport("setMesh", Module)]
    public static partial void SetMesh(JSObject host, string id, string role,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> positions,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> normals,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> colors,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> indices);

    [JSImport("setEdges", Module)]
    public static partial void SetEdges(JSObject host, string id, [JSMarshalAs<JSType.MemoryView>] Span<byte> positions, int color);

    [JSImport("placeMesh", Module)]
    public static partial void PlaceMesh(JSObject host, string id, double x, double y, double z, bool visible);

    [JSImport("removeMesh", Module)]
    public static partial void RemoveMesh(JSObject host, string id);

    [JSImport("setLines", Module)]
    public static partial void SetLines(JSObject host, string role,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> positions, int color, bool visible, bool onTop);

    [JSImport("setCamera", Module)]
    public static partial void SetCamera(JSObject host, double tx, double ty, double tz, double distance, double azimuth, double elevation, double fov);

    [JSImport("getCamera", Module)]
    public static partial string GetCamera(JSObject host);

    [JSImport("setTheme", Module)]
    public static partial void SetTheme(JSObject host, bool light);

    [JSImport("info", Module)]
    public static partial string Info(JSObject host);

    [JSImport("dispose", Module)]
    public static partial void Dispose(JSObject host);
}

/// <summary>
/// Vista 3D da simulação na web: um div do navegador (com o three.js) encaixado no layout do
/// Avalonia. Recebe uma <see cref="SimulationScene"/> e envia à placa de vídeo só o que mudou.
/// Por ser um elemento do navegador, nada do Avalonia pode ser desenhado por cima dele.
/// </summary>
internal sealed class Scene3DView : NativeControlHost
{
    private JSObject? _host;
    private SimulationScene? _scene;
    private readonly Dictionary<string, (int Version, SceneObject Object)> _sent = [];
    private readonly Dictionary<string, (Point3 Offset, bool Visible)> _placed = [];
    private readonly Dictionary<SceneRole, (float[] Positions, bool Visible)> _sentLines = [];
    private bool _light;
    private bool _lightBackground;
    private bool _showRapids = true;
    private bool _showPath;   // a trajetória inteira fica escondida por padrão: só o trecho atual aparece
    private bool _fitPending = true;

    /// <summary>True depois que o div do three.js foi criado (o motor já foi carregado).</summary>
    public bool IsReady => _host is not null;

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        _host = Scene3DInterop.Create();
        Scene3DInterop.SetTheme(_host, _light || _lightBackground);
        _sent.Clear();
        _placed.Clear();
        _sentLines.Clear();
        Sync();
        return new JSObjectControlHandle(_host);
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (_host is { } host)
        {
            Scene3DInterop.Dispose(host);
        }

        _host = null;
        _sent.Clear();
        _placed.Clear();
        _sentLines.Clear();
        base.DestroyNativeControlCore(control);
    }

    /// <summary>Cena a desenhar (null limpa). Uma cena nova é enquadrada inteira.</summary>
    public void SetScene(SimulationScene? scene)
    {
        if (_host is { } host)
        {
            foreach (string id in _sent.Keys)
            {
                Scene3DInterop.RemoveMesh(host, id);
            }
        }

        _sent.Clear();
        _placed.Clear();
        _sentLines.Clear();
        _scene = scene;
        _fitPending = true;
        Sync();
        SyncLines();
    }

    public bool LightTheme
    {
        get => _light;
        set
        {
            _light = value;
            ApplyBackground();
        }
    }

    /// <summary>Fundo claro só no 3D (opção do menu), independente do tema do app.</summary>
    public bool LightBackground
    {
        get => _lightBackground;
        set
        {
            _lightBackground = value;
            ApplyBackground();
        }
    }

    private void ApplyBackground()
    {
        if (_host is { } host)
        {
            Scene3DInterop.SetTheme(host, _light || _lightBackground);
        }
    }

    public bool ShowRapids
    {
        get => _showRapids;
        set
        {
            _showRapids = value;
            SyncLines();
        }
    }

    public bool ShowPath
    {
        get => _showPath;
        set
        {
            _showPath = value;
            SyncLines();
        }
    }

    /// <summary>Enquadra a cena na vista pronta dada (botões Iso, Topo, Frente, Lado e "Ajustar").</summary>
    public void Fit(SceneView view)
    {
        if (_scene is null)
        {
            return;
        }

        double aspect = Bounds.Height > 1 ? Bounds.Width / Bounds.Height : 1;
        _scene.Fit(view, aspect);
        PushCamera();
    }

    /// <summary>
    /// Envia o que mudou na cena: malhas trocadas (pela versão), posição da ferramenta e visibilidade.
    /// Chamado a cada quadro pela tela; quando nada mudou, não faz nada.
    /// </summary>
    public void Sync()
    {
        if (_host is not { } host || _scene is not { } scene)
        {
            return;
        }

        foreach (SceneObject o in scene.Objects)
        {
            if (!_sent.TryGetValue(o.Id, out var sent) || sent.Version != o.Version || !ReferenceEquals(sent.Object, o))
            {
                SolidMesh mesh = o.Mesh;
                Scene3DInterop.SetMesh(host, o.Id, o.Role.ToString(),
                    MemoryMarshal.AsBytes(mesh.Positions).ToArray(),
                    MemoryMarshal.AsBytes(mesh.Normals).ToArray(),
                    MemoryMarshal.AsBytes(mesh.Colors).ToArray(),
                    MemoryMarshal.AsBytes(mesh.Indices).ToArray());
                Scene3DInterop.SetEdges(host, o.Id, MemoryMarshal.AsBytes(mesh.Edges).ToArray(), (int)scene.Palette.Edge);
                _sent[o.Id] = (o.Version, o);
                _placed.Remove(o.Id);
            }

            bool visible = o.Visible && o.Mesh.VertexCount > 0;
            if (!_placed.TryGetValue(o.Id, out var placed) || placed.Offset != o.Offset || placed.Visible != visible)
            {
                Scene3DInterop.PlaceMesh(host, o.Id, o.Offset.X, o.Offset.Y, o.Offset.Z, visible);
                _placed[o.Id] = (o.Offset, visible);
            }
        }

        if (_fitPending && Bounds.Width > 1)
        {
            _fitPending = false;
            Fit(SceneView.Isometric);
        }
    }

    /// <summary>
    /// Reenvia as linhas da trajetória (mudam quando aparece um rápido no material e a cada novo
    /// trecho em execução). Só vai ao navegador o que mudou desde o último envio.
    /// </summary>
    public void SyncLines()
    {
        if (_host is not { } host || _scene is not { } scene)
        {
            return;
        }

        foreach (SceneLines lines in scene.Lines)
        {
            float[] positions = lines.PositionArray;
            bool visible = lines.Role switch
            {
                SceneRole.FeedPath => _showPath,
                SceneRole.RapidPath => _showPath && _showRapids,
                _ => true, // rápido no material e trecho atual: sempre à vista
            };
            if (_sentLines.TryGetValue(lines.Role, out var sent) && ReferenceEquals(sent.Positions, positions) && sent.Visible == visible)
            {
                continue;
            }

            _sentLines[lines.Role] = (positions, visible);
            Scene3DInterop.SetLines(host, lines.Role.ToString(), MemoryMarshal.AsBytes(positions.AsSpan()).ToArray(),
                (int)scene.Palette.Of(lines.Role), visible, lines.Role is SceneRole.RapidInMaterial or SceneRole.CurrentPath);
        }
    }

    /// <summary>Placa de vídeo e triângulos desenhados (para o rodapé de diagnóstico).</summary>
    public string Info() => _host is { } host ? Scene3DInterop.Info(host) : "";

    private void PushCamera()
    {
        if (_host is not { } host || _scene is null)
        {
            return;
        }

        SceneCamera c = _scene.Camera;
        Scene3DInterop.SetCamera(host, c.Target.X, c.Target.Y, c.Target.Z, c.Distance, c.Azimuth, c.Elevation, c.FieldOfView);
    }
}
