using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;
using OcctNet.Wrapper;

namespace CncStudio.Step;

/// <summary>Resultado da leitura de um STEP: a malha (em mm, com os triângulos orientados) ou o erro em português.</summary>
public sealed record StepResult(TriangleMesh? Mesh, string? Error, MeshOrientationResult? Orientation = null)
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
            File.WriteAllBytes(path, data);
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

            MeshOrientationResult oriented = MeshOrientation.MakeConsistent(new TriangleMesh(triangles));
            return new StepResult(oriented.Mesh, null, oriented);
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
}
