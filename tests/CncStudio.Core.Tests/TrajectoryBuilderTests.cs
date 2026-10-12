using CncStudio.Core.Dialects;
using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

public class TrajectoryBuilderTests
{
    private const double Tolerance = 1e-6;

    private static ToolPath BuildFanuc(params string[] lines) =>
        TrajectoryBuilder.Build(GCodeParser.Parse(string.Join("\n", lines), Dialect.Fanuc));

    private static double DistanceXY(Point3 p, double cx, double cy) =>
        Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));

    [Fact]
    public void MovimentosLinearesPartemDaOrigem()
    {
        ToolPath path = BuildFanuc(
            "G00 X10 Y5",
            "G01 Z-2 F100",
            "X20");

        Assert.Equal(3, path.Segments.Count);
        Assert.Equal(Point3.Origin, path.Segments[0].Start);
        Assert.True(path.Segments[0].IsRapid);
        Assert.Equal(new Point3(10, 5, -2), path.Segments[1].End);
        Assert.Equal(new Point3(20, 5, -2), path.Segments[2].End);
        Assert.Equal(100.0, path.Segments[2].Feed);
        Assert.Equal(3, path.Segments[2].LineNumber);
    }

    [Fact]
    public void MovimentoNuloNaoGeraSegmento()
    {
        ToolPath path = BuildFanuc(
            "G00 X10",
            "G00 X10");

        Assert.Single(path.Segments);
    }

    [Fact]
    public void QuartoDeCirculoAntiHorario()
    {
        ToolPath path = BuildFanuc(
            "G00 X10 Y0",
            "G03 X0 Y10 I-10 J0 F100");

        var arc = path.Segments.Where(s => s.Motion == MotionMode.CounterClockwiseArc).ToList();
        Assert.True(arc.Count > 4);
        Assert.Equal(new Point3(0, 10, 0), arc[^1].End);

        // Todos os pontos no raio 10 e no primeiro quadrante (caminho curto)
        foreach (Segment s in arc)
        {
            Assert.Equal(10, DistanceXY(s.End, 0, 0), 6);
            Assert.True(s.End.X >= -Tolerance && s.End.Y >= -Tolerance);
        }
    }

    [Fact]
    public void MesmosPontosEmHorarioFazemOCaminhoLongo()
    {
        ToolPath path = BuildFanuc(
            "G00 X10 Y0",
            "G02 X0 Y10 I-10 J0 F100");

        // 270° no sentido horário: passa por (0,-10) e (-10,0)
        double minY = path.Segments.Min(s => s.End.Y);
        double minX = path.Segments.Min(s => s.End.X);
        Assert.InRange(minY, -10.0 - 1e-9, -10.0 + 0.005 + 1e-9);
        Assert.InRange(minX, -10.0 - 1e-9, -10.0 + 0.005 + 1e-9);
    }

    [Fact]
    public void CirculoCompletoSoComIJ()
    {
        ToolPath path = BuildFanuc(
            "G00 X15 Y10",
            "G02 I-5 F100");

        var arc = path.Segments.Where(s => s.LineNumber == 2).ToList();
        Assert.Equal(new Point3(15, 10, 0), arc[^1].End);
        // Lado oposto do círculo de centro (10,10). Os vértices da interpolação podem não cair
        // exatamente em 180°, então aceitamos o erro de corda padrão (0,005 mm).
        Assert.InRange(arc.Min(s => s.End.X), 5.0 - 1e-9, 5.0 + 0.005 + 1e-9);
    }

    [Fact]
    public void HeliceInterpolaZ()
    {
        ToolPath path = BuildFanuc(
            "G00 X10 Y0 Z0",
            "G03 X-10 Y0 Z-4 I-10 J0 F100");

        var arc = path.Segments.Where(s => s.LineNumber == 2).ToList();
        Assert.Equal(-4, arc[^1].End.Z, 9);

        // No meio do arco (topo, Y=10) a profundidade é a metade
        Segment middle = arc.MinBy(s => Math.Abs(s.End.X))!;
        Assert.Equal(-2, middle.End.Z, 1);
    }

    [Fact]
    public void RaiosDiferentesGeramAviso()
    {
        ToolPath path = BuildFanuc(
            "G00 X10 Y0",
            "G03 X0 Y10.5 I-10 J0 F100");

        ParseDiagnostic warning = Assert.Single(path.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Equal(2, warning.LineNumber);
        Assert.Equal(new Point3(0, 10.5, 0), path.Segments[^1].End);
    }

    [Fact]
    public void IntervaloDeSegmentosPorLinha()
    {
        ToolPath path = BuildFanuc(
            "G00 X10 Y0",
            "(COMENTARIO)",
            "G03 X0 Y10 I-10 J0 F100",
            "G01 X-5");

        (int start, int count) = path.GetSegmentRange(3);
        Assert.Equal(1, start);
        Assert.True(count > 1);
        Assert.True(path.Segments.Skip(start).Take(count).All(s => s.LineNumber == 3));

        Assert.Equal(0, path.GetSegmentRange(2).Count);
        Assert.Equal(1, path.GetSegmentRange(4).Count);
    }

    [Fact]
    public void EnvelopeDeCorteIgnoraRapidos()
    {
        ToolPath path = BuildFanuc(
            "G00 X-50 Y-50 Z10",
            "G00 X0 Y0",
            "G01 Z-1 F100",
            "X30 Y20");

        Assert.Equal(-50, path.Bounds.Min.X, 9);
        Assert.Equal(0, path.CuttingBounds.Min.X, 9);
        Assert.Equal(30, path.CuttingBounds.Max.X, 9);
        Assert.Equal(-1, path.CuttingBounds.Min.Z, 9);
    }
}
