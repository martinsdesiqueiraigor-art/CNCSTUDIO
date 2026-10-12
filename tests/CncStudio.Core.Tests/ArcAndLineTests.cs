using CncStudio.Core.Execution;
using CncStudio.Core.Dialects;
using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

public class ArcAndLineTests
{
    private const double ChordTolerance = 0.005;

    private static ToolPath BuildFanuc(params string[] lines) =>
        MachineExecutor.Execute(GCodeParser.Parse(string.Join("\n", lines), Dialect.Fanuc));

    /// <summary>Pontos do movimento (inclui o inicial), como o backplot e o simulador vão usar.</summary>
    private static List<Point3> Polyline(Move move)
    {
        var points = new List<Point3> { move.Start };
        move.AppendPolyline(points, ChordTolerance);
        return points;
    }

    private static double DistanceXY(Point3 p, double cx, double cy) =>
        Math.Sqrt((p.X - cx) * (p.X - cx) + (p.Y - cy) * (p.Y - cy));

    [Fact]
    public void MovimentosLinearesPartemDaOrigem()
    {
        ToolPath path = BuildFanuc(
            "G00 X10 Y5",
            "G01 Z-2 F100",
            "X20");

        Assert.Equal(3, path.Moves.Count);
        Assert.Equal(Point3.Origin, path.Moves[0].Start);
        Assert.True(path.Moves[0].IsRapid);
        Assert.Equal(new Point3(10, 5, -2), path.Moves[1].End);
        Assert.Equal(new Point3(20, 5, -2), path.Moves[2].End);
        Assert.Equal(100.0, path.Moves[2].Feed);
        Assert.Equal(3, path.Moves[2].LineNumber);
        Assert.Equal(10.0, path.Moves[2].Length, 9);
    }

    [Fact]
    public void MovimentoNuloNaoGeraMovimento()
    {
        ToolPath path = BuildFanuc(
            "G00 X10",
            "G00 X10");

        Assert.Single(path.Moves);
    }

    [Fact]
    public void ArcoEhGuardadoComoUmUnicoMovimento()
    {
        ToolPath path = BuildFanuc(
            "G00 X10 Y0",
            "G03 X0 Y10 I-10 J0 F100");

        Assert.Equal(2, path.Moves.Count);
        Move arc = path.Moves[1];
        Assert.True(arc.IsArc);
        Assert.Equal(Math.PI / 2, arc.Sweep, 9);
        Assert.Equal(Math.PI * 10 / 2, arc.Length, 9);
    }

    [Fact]
    public void QuartoDeCirculoAntiHorario()
    {
        ToolPath path = BuildFanuc(
            "G00 X10 Y0",
            "G03 X0 Y10 I-10 J0 F100");

        Move arc = path.Moves[1];
        List<Point3> points = Polyline(arc);
        Assert.True(points.Count > 4);
        Assert.Equal(new Point3(0, 10, 0), points[^1]);

        // Todos os pontos no raio 10 e no primeiro quadrante (caminho curto)
        foreach (Point3 p in points)
        {
            Assert.Equal(10, DistanceXY(p, 0, 0), 6);
            Assert.True(p.X >= -1e-9 && p.Y >= -1e-9);
        }

        Point3 middle = arc.PointAt(0.5);
        Assert.Equal(Math.Sqrt(50), middle.X, 9);
        Assert.Equal(Math.Sqrt(50), middle.Y, 9);
    }

    [Fact]
    public void MesmosPontosEmHorarioFazemOCaminhoLongo()
    {
        ToolPath path = BuildFanuc(
            "G00 X10 Y0",
            "G02 X0 Y10 I-10 J0 F100");

        // 270° no sentido horário: passa por (0,-10) e (-10,0)
        Assert.Equal(-1.5 * Math.PI, path.Moves[1].Sweep, 9);
        Assert.Equal(-10, path.CuttingBounds.Min.X, 9);
        Assert.Equal(-10, path.CuttingBounds.Min.Y, 9);
    }

    [Fact]
    public void CirculoCompletoSoComIJ()
    {
        ToolPath path = BuildFanuc(
            "G00 X15 Y10",
            "G02 I-5 F100");

        Move circle = path.Moves[1];
        Assert.Equal(-2 * Math.PI, circle.Sweep, 9);
        Assert.Equal(new Point3(15, 10, 0), Polyline(circle)[^1]);

        // Envelope exato pelos pontos extremos do círculo de centro (10,10) e raio 5
        Assert.Equal(5, path.CuttingBounds.Min.X, 9);
        Assert.Equal(15, path.CuttingBounds.Max.X, 9);
        Assert.Equal(5, path.CuttingBounds.Min.Y, 9);
        Assert.Equal(15, path.CuttingBounds.Max.Y, 9);
    }

    [Fact]
    public void HeliceInterpolaZ()
    {
        ToolPath path = BuildFanuc(
            "G00 X10 Y0 Z0",
            "G03 X-10 Y0 Z-4 I-10 J0 F100");

        Move helix = path.Moves[1];
        Assert.Equal(-4, Polyline(helix)[^1].Z, 9);
        Assert.Equal(-2, helix.PointAt(0.5).Z, 9);
    }

    [Fact]
    public void ToleranciaMaiorGeraMenosPontos()
    {
        Move circle = BuildFanuc("G00 X50 Y0", "G03 I-50 F100").Moves[1];

        Assert.True(circle.ArcStepCount(0.1) < circle.ArcStepCount(0.005));
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
        Assert.Equal(new Point3(0, 10.5, 0), Polyline(path.Moves[^1])[^1]);
    }

    [Fact]
    public void BuscaDoMovimentoPelaLinha()
    {
        ToolPath path = BuildFanuc(
            "G00 X10 Y0",
            "(COMENTARIO)",
            "G03 X0 Y10 I-10 J0 F100",
            "G01 X-5");

        Assert.Equal(new[] { 0 }, path.MovesForLine(1));
        Assert.Empty(path.MovesForLine(2));
        Assert.Equal(new[] { 1 }, path.MovesForLine(3));
        Assert.Equal(new[] { 2 }, path.MovesForLine(4));
        Assert.Empty(path.MovesForLine(99));
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
