using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using CncStudio.Core.Geometry;

namespace CncStudio.Core.Cam3D;

/// <summary>Resultado da leitura de um STL.</summary>
/// <param name="Mesh">Modelo (null se deu erro).</param>
/// <param name="Error">Mensagem em português quando não foi possível ler.</param>
/// <param name="Binary">Formato encontrado.</param>
/// <param name="Skipped">Triângulos descartados (degenerados ou com número inválido).</param>
public sealed record StlResult(TriangleMesh? Mesh, string? Error, bool Binary, int Skipped)
{
    public bool Ok => Mesh is not null;
}

/// <summary>
/// Lê STL binário e em texto (ASCII). Não depende de pacote. O STL não guarda a unidade: o
/// resultado vem como está e o usuário escolhe a escala (<see cref="TriangleMesh.Scaled"/>).
/// </summary>
public static class StlReader
{
    /// <summary>Limite de triângulos aceito (protege a memória).</summary>
    public const int MaxTriangles = 5_000_000;

    public static StlResult Read(byte[] data)
    {
        if (data.Length < 15)
        {
            return Fail("O arquivo é pequeno demais para ser um STL.");
        }

        // Binário: 80 bytes de cabeçalho + contagem + 50 bytes por triângulo (o texto também pode começar com "solid")
        if (data.Length >= 84)
        {
            uint count = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(80, 4));
            if (84L + (50L * count) == data.Length)
            {
                return ReadBinary(data, (int)Math.Min(count, int.MaxValue));
            }
        }

        string head = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 200)).TrimStart();
        if (head.StartsWith("solid", StringComparison.OrdinalIgnoreCase))
        {
            return ReadAscii(Encoding.UTF8.GetString(data));
        }

        return Fail("Não parece um STL (nem binário nem texto). Exporte de novo em STL.");
    }

    private static StlResult ReadBinary(byte[] data, int count)
    {
        if (count == 0)
        {
            return Fail("O STL não tem triângulos.");
        }

        if (count > MaxTriangles)
        {
            return Fail($"O STL tem {count} triângulos; o limite é {MaxTriangles}. Reduza a malha ao exportar.");
        }

        var triangles = new List<(Point3, Point3, Point3)>(count);
        int skipped = 0;
        for (int i = 0; i < count; i++)
        {
            int at = 84 + (i * 50) + 12; // pula a normal
            Point3 a = Vertex(data, at), b = Vertex(data, at + 12), c = Vertex(data, at + 24);
            if (!Valid(a) || !Valid(b) || !Valid(c) || Degenerate(a, b, c))
            {
                skipped++;
                continue;
            }

            triangles.Add((a, b, c));
        }

        return Finish(triangles, true, skipped);
    }

    private static Point3 Vertex(byte[] d, int at) => new(
        BitConverter.ToSingle(d, at), BitConverter.ToSingle(d, at + 4), BitConverter.ToSingle(d, at + 8));

    private static StlResult ReadAscii(string text)
    {
        var triangles = new List<(Point3, Point3, Point3)>();
        var current = new List<Point3>(3);
        int skipped = 0;
        foreach (string raw in text.Split('\n'))
        {
            string[] parts = raw.Split([' ', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }

            if (parts[0].Equals("vertex", StringComparison.OrdinalIgnoreCase))
            {
                if (parts.Length < 4
                    || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
                    || !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double y)
                    || !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double z))
                {
                    return Fail("Número inválido numa linha \"vertex\" do STL.");
                }

                current.Add(new Point3(x, y, z));
            }
            else if (parts[0].Equals("endfacet", StringComparison.OrdinalIgnoreCase))
            {
                if (current.Count == 3 && Valid(current[0]) && Valid(current[1]) && Valid(current[2]) && !Degenerate(current[0], current[1], current[2]))
                {
                    triangles.Add((current[0], current[1], current[2]));
                }
                else
                {
                    skipped++;
                }

                current.Clear();
                if (triangles.Count > MaxTriangles)
                {
                    return Fail($"O STL tem mais de {MaxTriangles} triângulos. Reduza a malha ao exportar.");
                }
            }
        }

        return Finish(triangles, false, skipped);
    }

    private static StlResult Finish(List<(Point3, Point3, Point3)> triangles, bool binary, int skipped) =>
        triangles.Count == 0
            ? Fail("O STL não tem triângulos válidos.")
            : new StlResult(new TriangleMesh(triangles), null, binary, skipped);

    private static bool Valid(Point3 p) =>
        double.IsFinite(p.X) && double.IsFinite(p.Y) && double.IsFinite(p.Z) && Math.Abs(p.X) < 1e7 && Math.Abs(p.Y) < 1e7 && Math.Abs(p.Z) < 1e7;

    private static bool Degenerate(Point3 a, Point3 b, Point3 c)
    {
        Point3 u = b - a, v = c - a;
        double cx = (u.Y * v.Z) - (u.Z * v.Y), cy = (u.Z * v.X) - (u.X * v.Z), cz = (u.X * v.Y) - (u.Y * v.X);
        return (cx * cx) + (cy * cy) + (cz * cz) < 1e-18;
    }

    private static StlResult Fail(string message) => new(null, message, false, 0);
}
