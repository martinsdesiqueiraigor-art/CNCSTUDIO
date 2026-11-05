using System.Text;
using System.Text.RegularExpressions;
using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;
using OcctNet.Wrapper;

namespace CncStudio.Step;

/// <summary>
/// Resultado da leitura de um STEP: a malha (em mm, com os triângulos orientados) ou o erro em português.
/// <paramref name="Collapsed"/>: triângulos colapsados (dois vértices no mesmo ponto) descartados da triangulação.
/// <paramref name="Notice"/>: aviso curto para a tela sobre o que foi lido (superfícies ignoradas, malha facetada).
/// </summary>
public sealed record StepResult(TriangleMesh? Mesh, string? Error, MeshOrientationResult? Orientation = null, int Collapsed = 0, string? Notice = null)
{
    public bool Ok => Mesh is not null;
}

/// <summary>
/// Lê um arquivo STEP (AP203/AP214/AP242) com o OpenCASCADE e entrega uma malha de triângulos pronta para o CAM 3D
/// (a mesma <see cref="TriangleMesh"/> do STL). O OpenCASCADE converte a unidade do arquivo para milímetros.
/// Só a geometria: não há faces, arestas, cores nem montagens (vários corpos viram uma malha só).
/// </summary>
public static class StepMeshReader
{
    /// <summary>
    /// Lê o STEP dado em bytes. <paramref name="linearDeflection"/> é o desvio máximo da malha em relação à superfície
    /// (mm); <paramref name="angularDeflection"/>, o desvio angular (rad).
    /// </summary>
    public static StepResult Read(byte[] data, double linearDeflection = 0.05, double angularDeflection = 0.2)
    {
        if (data.Length == 0)
        {
            return new StepResult(null, "O arquivo STEP está vazio.");
        }

        if (linearDeflection <= 0 || angularDeflection <= 0)
        {
            return new StepResult(null, "O desvio da malha precisa ser maior que zero.");
        }

        // A biblioteca lê de um caminho de arquivo
        string path = Path.Combine(Path.GetTempPath(), "casillas-" + Guid.NewGuid().ToString("N") + ".step");
        try
        {
            File.WriteAllBytes(path, PrepareGeometry(data, out string? notice));
            using OcctShape shape = OcctShape.ImportStep(path);
            if (shape.IsNull)
            {
                return new StepResult(null, "O STEP não tem nenhum sólido ou superfície que o Casillas consiga ler.");
            }

            OcctMesh mesh = shape.Triangulate(linearDeflection, angularDeflection);
            if (mesh.TriangleCount == 0)
            {
                return new StepResult(null, "O STEP foi lido, mas não gerou triângulos (só curvas ou pontos?).");
            }

            var triangles = new List<(Point3, Point3, Point3)>(mesh.TriangleCount);
            IReadOnlyList<OcctMeshVertex> v = mesh.Vertices;
            IReadOnlyList<int> ix = mesh.TriangleIndices;
            Point3 P(int i) => new(v[i].X, v[i].Y, v[i].Z);
            for (int i = 0; i + 2 < ix.Count; i += 3)
            {
                triangles.Add((P(ix[i]), P(ix[i + 1]), P(ix[i + 2])));
            }

            // O OpenCASCADE às vezes gera triângulos com dois vértices no mesmo ponto: sem área, mas faziam a
            // malha fechada parecer aberta e em pedaços. Saem antes de orientar (diagnóstico CAM-08-T2)
            TriangleMesh clean = new TriangleMesh(triangles).WithoutCollapsed(out int collapsed);
            if (clean.TriangleCount == 0)
            {
                return new StepResult(null, "O STEP foi lido, mas não gerou triângulos (só curvas ou pontos?).");
            }

            MeshOrientationResult oriented = MeshOrientation.MakeConsistent(clean);
            return new StepResult(oriented.Mesh, null, oriented, collapsed, notice);
        }
        catch (OcctException)
        {
            return new StepResult(null, "Não consegui ler este STEP: o arquivo está corrompido ou não é um STEP válido.");
        }
        catch (Exception e) when (e is DllNotFoundException or BadImageFormatException or TypeInitializationException or EntryPointNotFoundException)
        {
            return new StepResult(null, "A biblioteca de leitura de STEP não carregou neste computador. Exporte o modelo como STL.");
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
                // arquivo temporário: o sistema limpa depois
            }
        }
    }

    // Sólidos B-rep do STEP (o que a peça é de verdade)
    private static readonly Regex Solid = new(@"\b(MANIFOLD_SOLID_BREP|BREP_WITH_VOIDS|FACETED_BREP)\s*\(", RegexOptions.CultureInvariant);

    // Modelos de superfície: "#12=SHELL_BASED_SURFACE_MODEL(" → 12
    private static readonly Regex SurfaceModel = new(@"#(\d+)\s*=\s*SHELL_BASED_SURFACE_MODEL\s*\(", RegexOptions.CultureInvariant);

    // Lista de itens de uma representação de forma: "#5=MANIFOLD_SURFACE_SHAPE_REPRESENTATION('nome',(#1,#2),#3)"
    private static readonly Regex RepresentationItems = new(
        @"(#\d+\s*=\s*[A-Z_]*SHAPE_REPRESENTATION\s*\(\s*'(?:[^']|'')*'\s*,\s*\()([^()]*)(\))", RegexOptions.CultureInvariant);

    // Malha facetada (AP242 tesselado), sem B-rep
    private static readonly Regex Faceted = new(
        @"\b(TESSELLATED_SOLID|TESSELLATED_SHELL|COMPLEX_TRIANGULATED_FACE|TRIANGULATED_FACE|COMPLEX_TRIANGULATED_SURFACE_SET|TRIANGULATED_SURFACE_SET)\s*\(",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// Escolhe o que o OpenCASCADE vai ler. Muitos STEP (ex.: os do NIST com PMI) trazem, além do sólido, modelos de
    /// superfície auxiliares (SHELL_BASED_SURFACE_MODEL com OPEN_SHELL: planos e geometria de apoio das cotas).
    /// O OpenCASCADE junta tudo numa malha só, que fica aberta, com caixa e volume errados (CAM-08-FIX).
    /// Por isso: com pelo menos 1 sólido, as superfícies saem das listas de itens das representações e só o sólido é
    /// lido. Sem sólido, o arquivo segue como veio (superfícies ou malha facetada, com aviso).
    /// </summary>
    internal static byte[] PrepareGeometry(byte[] data, out string? notice)
    {
        notice = null;

        // Latin-1 devolve os mesmos bytes ao gravar: o arquivo só muda onde a lista de itens foi editada
        string text = Encoding.Latin1.GetString(data);
        if (!Solid.IsMatch(text))
        {
            if (Faceted.IsMatch(text))
            {
                notice = "O arquivo só tem malha facetada, sem sólido: volume aproximado.";
            }

            return data;
        }

        var surfaces = new HashSet<string>(SurfaceModel.Matches(text).Select(m => "#" + m.Groups[1].Value));
        if (surfaces.Count == 0)
        {
            return data;
        }

        var ignored = new HashSet<string>();
        string edited = RepresentationItems.Replace(text, m =>
        {
            string[] items = m.Groups[2].Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            string[] kept = items.Where(s => !surfaces.Contains(s)).ToArray();
            if (kept.Length == items.Length)
            {
                return m.Value;
            }

            ignored.UnionWith(items.Except(kept));
            return m.Groups[1].Value + string.Join(",", kept) + m.Groups[3].Value;
        });

        if (ignored.Count == 0)
        {
            return data;
        }

        notice = ignored.Count == 1
            ? "Ignorei 1 superfície auxiliar; usei só o sólido."
            : $"Ignorei {ignored.Count} superfícies auxiliares; usei só o sólido.";
        return Encoding.Latin1.GetBytes(edited);
    }
}
