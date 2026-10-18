using Avalonia;
using Avalonia.Input;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using CncStudio.Core.Geometry;
using CncStudio.Core.Scene;

namespace CncStudio.App.Graphics;

/// <summary>
/// Simulação 3D no desktop pela placa de vídeo (OpenGL do próprio Avalonia): desenha a foto da
/// <see cref="SimulationScene"/> que a fila de segundo plano manda e envia à placa só as malhas
/// que mudaram. Giro com o botão esquerdo, deslocamento com o direito ou o do meio, roda aproxima.
/// Se o OpenGL não abrir, avisa por <see cref="Failed"/> e a tela volta ao desenho por CPU.
/// </summary>
public sealed class GlSceneView : OpenGlControlBase
{
    // Constantes do OpenGL usadas aqui
    private const int ArrayBuffer = 0x8892, ElementArrayBuffer = 0x8893, StaticDraw = 0x88E4, Float = 0x1406;
    private const int Lines = 1, Triangles = 4, UnsignedInt = 0x1405, DepthTest = 0x0B71, ColorBit = 0x4000, DepthBit = 0x100;
    private const int VertexShader = 0x8B31, FragmentShader = 0x8B30, LessOrEqual = 0x0203;

    private sealed class GpuMesh
    {
        public int Vao, Positions, Normals, Colors, Indices, IndexCount, Version;
        public int EdgeVao, EdgeBuffer, EdgeCount;
        public object? Source;
    }

    private sealed class GpuLines
    {
        public int Vao, Buffer, Count;
        public float[]? Source;
    }

    private readonly Dictionary<string, GpuMesh> _meshes = [];
    private readonly Dictionary<SceneRole, GpuLines> _lines = [];
    private SceneSnapshot? _snapshot;
    private int _meshProgram, _lineProgram;
    private int _uMvp, _uModel, _uEyeX, _uEyeY, _uEyeZ;
    private int _uLineMvp, _uLineR, _uLineG, _uLineB, _uLineLift, _uLineOffX, _uLineOffY, _uLineOffZ;
    private bool _ready;
    private Point? _drag;
    private bool _panning;

    /// <summary>Câmera atual (vistas prontas, ajustar e o mouse mudam).</summary>
    public SceneCamera Camera { get; set; } = new(Point3.Origin, 200, 35, 30);

    public bool LightTheme { get; set; }

    /// <summary>Trajetória inteira por cima da peça (padrão desligado: só o trecho atual aparece).</summary>
    public bool ShowPath { get; set; }

    public bool ShowRapids { get; set; } = true;

    /// <summary>Mostrar o material (opção "Sólido").</summary>
    public bool ShowSolid { get; set; } = true;

    /// <summary>Mostrar ferramenta e porta-ferramenta.</summary>
    public bool ShowTool { get; set; } = true;

    /// <summary>O OpenGL não abriu (placa sem suporte): a tela deve voltar ao desenho por CPU.</summary>
    public event EventHandler<string>? Failed;

    /// <summary>True depois que o OpenGL iniciou (sem placa de vídeo ele nunca inicia).</summary>
    public bool GlReady => _ready;

    /// <summary>Texto da placa de vídeo, depois de iniciar.</summary>
    public string Renderer { get; private set; } = "";

    /// <summary>Nova foto da cena (vem da fila de segundo plano pela thread da tela).</summary>
    public void Show(SceneSnapshot snapshot)
    {
        _snapshot = snapshot;
        RequestNextFrameRendering();
    }

    /// <summary>Enquadra a cena inteira na vista pronta.</summary>
    public void Fit(SceneView view)
    {
        if (_snapshot is null)
        {
            return;
        }

        double aspect = Bounds.Height > 1 ? Bounds.Width / Bounds.Height : 1;
        Camera = SceneCamera.Fit(_snapshot.Bounds, view, aspect, Camera.FieldOfView, _snapshot.IsLathe);
        RequestNextFrameRendering();
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            bool es = GlVersion.Type == GlProfileType.OpenGLES;
            string header = es ? "#version 300 es\nprecision highp float;\n" : "#version 330 core\n";
            _meshProgram = Link(gl, header, MeshVertex, MeshFragment);
            _uMvp = gl.GetUniformLocationString(_meshProgram, "uMvp");
            _uModel = gl.GetUniformLocationString(_meshProgram, "uModel");
            _uEyeX = gl.GetUniformLocationString(_meshProgram, "uEyeX");
            _uEyeY = gl.GetUniformLocationString(_meshProgram, "uEyeY");
            _uEyeZ = gl.GetUniformLocationString(_meshProgram, "uEyeZ");

            _lineProgram = Link(gl, header, LineVertex, LineFragment);
            _uLineMvp = gl.GetUniformLocationString(_lineProgram, "uMvp");
            _uLineR = gl.GetUniformLocationString(_lineProgram, "uR");
            _uLineG = gl.GetUniformLocationString(_lineProgram, "uG");
            _uLineB = gl.GetUniformLocationString(_lineProgram, "uB");
            _uLineLift = gl.GetUniformLocationString(_lineProgram, "uLift");
            _uLineOffX = gl.GetUniformLocationString(_lineProgram, "uOffX");
            _uLineOffY = gl.GetUniformLocationString(_lineProgram, "uOffY");
            _uLineOffZ = gl.GetUniformLocationString(_lineProgram, "uOffZ");
            Renderer = $"{GlVersion.Type} {GlVersion.Major}.{GlVersion.Minor} · {gl.Renderer}";
            _ready = true;
        }
        catch (Exception ex)
        {
            _ready = false;
            Failed?.Invoke(this, ex.Message);
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        foreach (GpuMesh m in _meshes.Values)
        {
            Release(gl, m);
        }

        foreach (GpuLines l in _lines.Values)
        {
            gl.DeleteBuffer(l.Buffer);
            gl.DeleteVertexArray(l.Vao);
        }

        _meshes.Clear();
        _lines.Clear();
        if (_meshProgram != 0) gl.DeleteProgram(_meshProgram);
        if (_lineProgram != 0) gl.DeleteProgram(_lineProgram);
        _ready = false;
    }

    protected override void OnOpenGlLost()
    {
        _meshes.Clear();
        _lines.Clear();
        _ready = false;
    }

    protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
    {
        double scaling = VisualRoot?.RenderScaling ?? 1;
        int width = Math.Max(1, (int)(Bounds.Width * scaling));
        int height = Math.Max(1, (int)(Bounds.Height * scaling));
        gl.Viewport(0, 0, width, height);
        if (LightTheme)
        {
            gl.ClearColor(0.953f, 0.961f, 0.973f, 1);
        }
        else
        {
            gl.ClearColor(0.051f, 0.067f, 0.09f, 1);
        }

        gl.ClearDepth(1);
        gl.Enable(DepthTest);
        gl.DepthFunc(LessOrEqual);
        gl.Clear(ColorBit | DepthBit);
        if (!_ready || _snapshot is not { } snapshot)
        {
            return;
        }

        SyncMeshes(gl, snapshot);
        float[] mvp = Camera.ViewProjection(width / (double)height);
        Point3 eye = Camera.Eye;

        gl.UseProgram(_meshProgram);
        fixed (float* m = mvp)
        {
            gl.UniformMatrix4fv(_uMvp, 1, false, m);
        }

        gl.Uniform1f(_uEyeX, (float)eye.X);
        gl.Uniform1f(_uEyeY, (float)eye.Y);
        gl.Uniform1f(_uEyeZ, (float)eye.Z);
        float* model = stackalloc float[16];
        foreach (SceneObjectState o in snapshot.Objects)
        {
            bool hidden = (o.Role == SceneRole.Material && !ShowSolid) || (o.Role is SceneRole.Tool or SceneRole.Holder && !ShowTool);
            if (!o.Visible || hidden || !_meshes.TryGetValue(o.Id, out GpuMesh? gpu) || gpu.IndexCount == 0)
            {
                continue;
            }

            for (int i = 0; i < 16; i++)
            {
                model[i] = i % 5 == 0 ? 1 : 0;
            }

            model[12] = (float)o.Offset.X;
            model[13] = (float)o.Offset.Y;
            model[14] = (float)o.Offset.Z;
            gl.UniformMatrix4fv(_uModel, 1, false, model);
            gl.BindVertexArray(gpu.Vao);
            gl.DrawElements(Triangles, gpu.IndexCount, UnsignedInt, IntPtr.Zero);
        }

        // Arestas marcadas: traço fino nas quinas (paredes e fundos dos cortes, fixações, ferramenta)
        gl.UseProgram(_lineProgram);
        fixed (float* m = mvp)
        {
            gl.UniformMatrix4fv(_uLineMvp, 1, false, m);
        }

        uint edge = snapshot.Palette.Edge;
        gl.Uniform1f(_uLineR, ((edge >> 16) & 0xFF) / 255f);
        gl.Uniform1f(_uLineG, ((edge >> 8) & 0xFF) / 255f);
        gl.Uniform1f(_uLineB, (edge & 0xFF) / 255f);
        gl.Uniform1f(_uLineLift, 0.0004f);
        foreach (SceneObjectState o in snapshot.Objects)
        {
            bool hidden = (o.Role == SceneRole.Material && !ShowSolid) || (o.Role is SceneRole.Tool or SceneRole.Holder && !ShowTool);
            if (!o.Visible || hidden || !_meshes.TryGetValue(o.Id, out GpuMesh? gpu) || gpu.EdgeCount == 0)
            {
                continue;
            }

            gl.Uniform1f(_uLineOffX, (float)o.Offset.X);
            gl.Uniform1f(_uLineOffY, (float)o.Offset.Y);
            gl.Uniform1f(_uLineOffZ, (float)o.Offset.Z);
            gl.BindVertexArray(gpu.EdgeVao);
            gl.DrawArrays(Lines, 0, gpu.EdgeCount);
        }

        gl.Uniform1f(_uLineOffX, 0);
        gl.Uniform1f(_uLineOffY, 0);
        gl.Uniform1f(_uLineOffZ, 0);

        // Trajetória; o rápido que encostou no material vai por último, por cima de tudo
        foreach (SceneLinesState lines in snapshot.Lines.OrderBy(l => l.Role is SceneRole.RapidInMaterial or SceneRole.CurrentPath))
        {
            bool visible = lines.Role switch
            {
                SceneRole.FeedPath => ShowPath,
                SceneRole.RapidPath => ShowPath && ShowRapids,
                _ => true,
            };
            if (!visible || lines.Positions.Length == 0)
            {
                continue;
            }

            GpuLines gpu = UploadLines(gl, lines);
            bool onTop = lines.Role is SceneRole.RapidInMaterial or SceneRole.CurrentPath;
            if (onTop)
            {
                gl.Disable(DepthTest);
            }

            gl.Uniform1f(_uLineR, ((lines.Color >> 16) & 0xFF) / 255f);
            gl.Uniform1f(_uLineG, ((lines.Color >> 8) & 0xFF) / 255f);
            gl.Uniform1f(_uLineB, (lines.Color & 0xFF) / 255f);
            gl.Uniform1f(_uLineLift, 0.0006f);
            gl.BindVertexArray(gpu.Vao);
            gl.DrawArrays(Lines, 0, gpu.Count);
            if (onTop)
            {
                gl.Enable(DepthTest);
            }
        }

        gl.BindVertexArray(0);
    }

    // Envia as malhas novas ou trocadas e apaga as que saíram da cena
    private unsafe void SyncMeshes(GlInterface gl, SceneSnapshot snapshot)
    {
        var alive = new HashSet<string>();
        foreach (SceneObjectState o in snapshot.Objects)
        {
            alive.Add(o.Id);
            if (_meshes.TryGetValue(o.Id, out GpuMesh? gpu) && gpu.Version == o.Version && ReferenceEquals(gpu.Source, o.Mesh))
            {
                continue;
            }

            if (gpu is null)
            {
                gpu = new GpuMesh
                {
                    Vao = gl.GenVertexArray(),
                    Positions = gl.GenBuffer(),
                    Normals = gl.GenBuffer(),
                    Colors = gl.GenBuffer(),
                    Indices = gl.GenBuffer(),
                    EdgeVao = gl.GenVertexArray(),
                    EdgeBuffer = gl.GenBuffer(),
                };
                _meshes[o.Id] = gpu;
            }

            gl.BindVertexArray(gpu.Vao);
            Upload(gl, gpu.Positions, 0, o.Mesh.Positions);
            Upload(gl, gpu.Normals, 1, o.Mesh.Normals);
            Upload(gl, gpu.Colors, 2, o.Mesh.Colors);
            gl.BindBuffer(ElementArrayBuffer, gpu.Indices);
            fixed (int* p = o.Mesh.Indices)
            {
                gl.BufferData(ElementArrayBuffer, (IntPtr)(o.Mesh.IndexCount * 4), (IntPtr)p, StaticDraw);
            }

            ReadOnlySpan<float> edges = o.Mesh.Edges;
            gl.BindVertexArray(gpu.EdgeVao);
            Upload(gl, gpu.EdgeBuffer, 0, edges);
            gpu.EdgeCount = edges.Length / 3;
            gpu.IndexCount = o.Mesh.IndexCount;
            gpu.Version = o.Version;
            gpu.Source = o.Mesh;
        }

        foreach (string id in _meshes.Keys.Where(id => !alive.Contains(id)).ToList())
        {
            Release(gl, _meshes[id]);
            _meshes.Remove(id);
        }
    }

    private unsafe GpuLines UploadLines(GlInterface gl, SceneLinesState lines)
    {
        if (!_lines.TryGetValue(lines.Role, out GpuLines? gpu))
        {
            gpu = new GpuLines { Vao = gl.GenVertexArray(), Buffer = gl.GenBuffer() };
            _lines[lines.Role] = gpu;
        }

        if (!ReferenceEquals(gpu.Source, lines.Positions))
        {
            gl.BindVertexArray(gpu.Vao);
            Upload(gl, gpu.Buffer, 0, lines.Positions);
            gpu.Count = lines.Positions.Length / 3;
            gpu.Source = lines.Positions;
        }

        return gpu;
    }

    private static unsafe void Upload(GlInterface gl, int buffer, int attribute, ReadOnlySpan<float> data)
    {
        gl.BindBuffer(ArrayBuffer, buffer);
        fixed (float* p = data)
        {
            gl.BufferData(ArrayBuffer, (IntPtr)(data.Length * 4), (IntPtr)p, StaticDraw);
        }

        gl.VertexAttribPointer(attribute, 3, Float, 0, 12, IntPtr.Zero);
        gl.EnableVertexAttribArray(attribute);
    }

    private static void Release(GlInterface gl, GpuMesh m)
    {
        gl.DeleteBuffer(m.Positions);
        gl.DeleteBuffer(m.Normals);
        gl.DeleteBuffer(m.Colors);
        gl.DeleteBuffer(m.Indices);
        gl.DeleteVertexArray(m.Vao);
        gl.DeleteBuffer(m.EdgeBuffer);
        gl.DeleteVertexArray(m.EdgeVao);
    }

    private static int Link(GlInterface gl, string header, string vertex, string fragment)
    {
        int vs = gl.CreateShader(VertexShader);
        string? error = gl.CompileShaderAndGetError(vs, header + vertex);
        if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException("vertex: " + error);
        int fs = gl.CreateShader(FragmentShader);
        error = gl.CompileShaderAndGetError(fs, header + fragment);
        if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException("fragment: " + error);
        int program = gl.CreateProgram();
        gl.AttachShader(program, vs);
        gl.AttachShader(program, fs);
        error = gl.LinkProgramAndGetError(program);
        if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException("link: " + error);
        return program;
    }

    // Luz: céu claro em cima e chão escuro embaixo (hemisfério), luz principal de cima por trás do
    // ombro de quem olha e um brilho suave (metal)
    private const string MeshVertex = """
        layout(location = 0) in vec3 aPos;
        layout(location = 1) in vec3 aNormal;
        layout(location = 2) in vec3 aColor;
        uniform mat4 uMvp;
        uniform mat4 uModel;
        out vec3 vNormal;
        out vec3 vColor;
        out vec3 vWorld;
        void main() {
            vec4 world = uModel * vec4(aPos, 1.0);
            gl_Position = uMvp * world;
            vNormal = aNormal;
            vColor = aColor;
            vWorld = world.xyz;
        }
        """;

    private const string MeshFragment = """
        in vec3 vNormal;
        in vec3 vColor;
        in vec3 vWorld;
        uniform float uEyeX;
        uniform float uEyeY;
        uniform float uEyeZ;
        out vec4 fragColor;
        void main() {
            vec3 n = normalize(vNormal);
            vec3 toEye = normalize(vec3(uEyeX, uEyeY, uEyeZ) - vWorld);
            vec3 key = normalize(toEye * 0.6 + vec3(0.0, 0.0, 1.0));
            float hemi = 0.5 + 0.5 * n.z;
            float diffuse = max(dot(n, key), 0.0);
            float spec = pow(max(dot(n, normalize(key + toEye)), 0.0), 40.0) * 0.07;
            vec3 color = vColor * (0.30 + 0.25 * hemi + 0.55 * diffuse) + vec3(spec);
            fragColor = vec4(min(color, vec3(1.0)), 1.0);
        }
        """;

    private const string LineVertex = """
        layout(location = 0) in vec3 aPos;
        uniform mat4 uMvp;
        uniform float uLift;
        uniform float uOffX;
        uniform float uOffY;
        uniform float uOffZ;
        void main() {
            gl_Position = uMvp * vec4(aPos + vec3(uOffX, uOffY, uOffZ), 1.0);
            gl_Position.z -= uLift * gl_Position.w; // um pouco à frente da superfície (não some no corte)
        }
        """;

    private const string LineFragment = """
        uniform float uR;
        uniform float uG;
        uniform float uB;
        out vec4 fragColor;
        void main() { fragColor = vec4(uR, uG, uB, 1.0); }
        """;

    // ── Mouse ──

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        PointerPointProperties props = e.GetCurrentPoint(this).Properties;
        _panning = props.IsRightButtonPressed || props.IsMiddleButtonPressed;
        _drag = e.GetPosition(this);
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag is not { } from)
        {
            return;
        }

        Point to = e.GetPosition(this);
        double dx = to.X - from.X, dy = to.Y - from.Y;
        if (_panning)
        {
            double mmPerPixel = 2 * Camera.Distance * Math.Tan(Camera.FieldOfView * Math.PI / 360) / Math.Max(1, Bounds.Height);
            Camera = Camera.Pan(-dx * mmPerPixel, dy * mmPerPixel);
        }
        else
        {
            Camera = Camera.Orbit(-dx * 0.4, dy * 0.4);
        }

        _drag = to;
        RequestNextFrameRendering();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _drag = null;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        Camera = Camera.Zoom(Math.Pow(1.15, -e.Delta.Y));
        RequestNextFrameRendering();
        e.Handled = true;
    }
}
