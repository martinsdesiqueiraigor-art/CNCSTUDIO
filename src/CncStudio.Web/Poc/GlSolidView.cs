using System.Diagnostics;
using Avalonia.OpenGL;
using Avalonia.OpenGL.Controls;
using CncStudio.Core.Rendering;

namespace CncStudio.Web.Poc;

/// <summary>
/// Prova de conceito do 3D, caminho A: o controle OpenGL do próprio Avalonia (WebGL 2 no navegador)
/// desenha a <see cref="SolidMesh"/> com profundidade e luz direcional.
/// </summary>
internal sealed class GlSolidView : OpenGlControlBase
{
    private const int ArrayBuffer = 0x8892, ElementArrayBuffer = 0x8893, DynamicDraw = 0x88E8, Float = 0x1406;
    private const int Triangles = 4, UnsignedInt = 0x1405, DepthTest = 0x0B71, ColorBit = 0x4000, DepthBit = 0x100;
    private const int VertexShader = 0x8B31, FragmentShader = 0x8B30, LessOrEqual = 0x0203;

    private int _program, _vao, _positions, _normals, _colors, _indices, _mvpLocation;
    private int _indexCount;
    private SolidMesh? _pendingMesh;
    private int _frames;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public Func<(double Azimuth, double Elevation, double Distance, float[] Target)>? Camera { get; set; }

    /// <summary>Quadros por segundo medidos no último segundo.</summary>
    public double Fps { get; private set; }

    public string Status { get; private set; } = "iniciando OpenGL…";

    public bool Ready { get; private set; }

    public void SetMesh(SolidMesh mesh)
    {
        _pendingMesh = mesh;
        RequestNextFrameRendering();
    }

    protected override void OnOpenGlInit(GlInterface gl)
    {
        try
        {
            bool es = GlVersion.Type == GlProfileType.OpenGLES;
            string header = es ? "#version 300 es\nprecision highp float;\n" : "#version 330 core\n";
            const string vertex = """
                layout(location = 0) in vec3 aPos;
                layout(location = 1) in vec3 aNormal;
                layout(location = 2) in vec3 aColor;
                uniform mat4 uMvp;
                out vec3 vNormal;
                out vec3 vColor;
                void main() { gl_Position = uMvp * vec4(aPos, 1.0); vNormal = aNormal; vColor = aColor; }
                """;
            const string fragment = """
                in vec3 vNormal;
                in vec3 vColor;
                out vec4 fragColor;
                void main() {
                    vec3 light = normalize(vec3(0.5, -0.8, 1.2));
                    float diffuse = max(dot(normalize(vNormal), light), 0.0);
                    fragColor = vec4(vColor * (0.38 + 0.62 * diffuse), 1.0);
                }
                """;

            int vs = gl.CreateShader(VertexShader);
            string? error = gl.CompileShaderAndGetError(vs, header + vertex);
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException("vertex: " + error);
            int fs = gl.CreateShader(FragmentShader);
            error = gl.CompileShaderAndGetError(fs, header + fragment);
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException("fragment: " + error);

            _program = gl.CreateProgram();
            gl.AttachShader(_program, vs);
            gl.AttachShader(_program, fs);
            error = gl.LinkProgramAndGetError(_program);
            if (!string.IsNullOrEmpty(error)) throw new InvalidOperationException("link: " + error);
            _mvpLocation = gl.GetUniformLocationString(_program, "uMvp");

            _vao = gl.GenVertexArray();
            _positions = gl.GenBuffer();
            _normals = gl.GenBuffer();
            _colors = gl.GenBuffer();
            _indices = gl.GenBuffer();
            Ready = true;
            Status = $"{GlVersion.Type} {GlVersion.Major}.{GlVersion.Minor} · {gl.Renderer}";
        }
        catch (Exception ex)
        {
            Status = "OpenGL falhou: " + ex.Message;
        }
    }

    protected override void OnOpenGlDeinit(GlInterface gl)
    {
        Ready = false;
    }

    protected override void OnOpenGlLost()
    {
        Ready = false;
        Status = "Contexto OpenGL perdido.";
    }

    protected override unsafe void OnOpenGlRender(GlInterface gl, int fb)
    {
        if (!Ready)
        {
            return;
        }

        if (_pendingMesh is { } mesh)
        {
            _pendingMesh = null;
            gl.BindVertexArray(_vao);
            Upload(gl, _positions, 0, mesh.Positions);
            Upload(gl, _normals, 1, mesh.Normals);
            Upload(gl, _colors, 2, mesh.Colors);
            gl.BindBuffer(ElementArrayBuffer, _indices);
            fixed (int* p = mesh.Indices)
            {
                gl.BufferData(ElementArrayBuffer, (IntPtr)(mesh.IndexCount * 4), (IntPtr)p, DynamicDraw);
            }

            _indexCount = mesh.IndexCount;
        }

        double scaling = VisualRoot?.RenderScaling ?? 1;
        int width = Math.Max(1, (int)(Bounds.Width * scaling));
        int height = Math.Max(1, (int)(Bounds.Height * scaling));
        gl.Viewport(0, 0, width, height);
        gl.ClearColor(0.051f, 0.067f, 0.09f, 1);
        gl.Enable(DepthTest);
        gl.DepthFunc(LessOrEqual);
        gl.Clear(ColorBit | DepthBit);

        if (_indexCount > 0 && Camera is { } cameraOf)
        {
            (double az, double el, double distance, float[] target) = cameraOf();
            float[] mvp = Mvp(az, el, distance, target, width / (double)height);
            gl.UseProgram(_program);
            fixed (float* m = mvp)
            {
                gl.UniformMatrix4fv(_mvpLocation, 1, false, m);
            }

            gl.BindVertexArray(_vao);
            gl.DrawElements(Triangles, _indexCount, UnsignedInt, IntPtr.Zero);
        }

        _frames++;
        if (_clock.ElapsedMilliseconds >= 1000)
        {
            Fps = _frames * 1000.0 / _clock.ElapsedMilliseconds;
            _frames = 0;
            _clock.Restart();
        }

        RequestNextFrameRendering(); // desenho contínuo, para medir quadros por segundo
    }

    private static unsafe void Upload(GlInterface gl, int buffer, int attribute, ReadOnlySpan<float> data)
    {
        gl.BindBuffer(ArrayBuffer, buffer);
        fixed (float* p = data)
        {
            gl.BufferData(ArrayBuffer, (IntPtr)(data.Length * 4), (IntPtr)p, DynamicDraw);
        }

        gl.VertexAttribPointer(attribute, 3, Float, 0, 12, IntPtr.Zero);
        gl.EnableVertexAttribArray(attribute);
    }

    /// <summary>Projeção perspectiva × câmera em órbita (Z para cima), em ordem de coluna (OpenGL).</summary>
    private static float[] Mvp(double azDeg, double elDeg, double distance, float[] target, double aspect)
    {
        double az = azDeg * Math.PI / 180, el = elDeg * Math.PI / 180;
        double ex = target[0] + distance * Math.Sin(az) * Math.Cos(el);
        double ey = target[1] - distance * Math.Cos(az) * Math.Cos(el);
        double ez = target[2] + distance * Math.Sin(el);

        // Eixos da câmera
        double fx = target[0] - ex, fy = target[1] - ey, fz = target[2] - ez;
        double fl = Math.Sqrt(fx * fx + fy * fy + fz * fz);
        fx /= fl; fy /= fl; fz /= fl;
        double sx = fy * 1 - fz * 0, sy = fz * 0 - fx * 1, sz = fx * 0 - fy * 0; // f × up(0,0,1)
        double sl = Math.Sqrt(sx * sx + sy * sy + sz * sz);
        if (sl < 1e-9) { sx = 1; sy = 0; sz = 0; sl = 1; }
        sx /= sl; sy /= sl; sz /= sl;
        double ux = sy * fz - sz * fy, uy = sz * fx - sx * fz, uz = sx * fy - sy * fx;

        double[] view =
        [
            sx, ux, -fx, 0,
            sy, uy, -fy, 0,
            sz, uz, -fz, 0,
            -(sx * ex + sy * ey + sz * ez), -(ux * ex + uy * ey + uz * ez), fx * ex + fy * ey + fz * ez, 1,
        ];

        double near = distance / 50, far = distance * 50;
        double f = 1 / Math.Tan(35 * Math.PI / 180 / 2);
        double[] proj =
        [
            f / aspect, 0, 0, 0,
            0, f, 0, 0,
            0, 0, (far + near) / (near - far), -1,
            0, 0, 2 * far * near / (near - far), 0,
        ];

        var result = new float[16];
        for (int c = 0; c < 4; c++)
        {
            for (int r = 0; r < 4; r++)
            {
                double sum = 0;
                for (int k = 0; k < 4; k++)
                {
                    sum += proj[k * 4 + r] * view[c * 4 + k];
                }

                result[c * 4 + r] = (float)sum;
            }
        }

        return result;
    }
}
