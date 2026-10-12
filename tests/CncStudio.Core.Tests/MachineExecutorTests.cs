using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

/// <summary>Estado modal, coordenadas, zeros de peça, ferramentas e avisos do executor.</summary>
public class MachineExecutorTests
{
    private static ToolPath Fanuc(string program, MachineSettings? settings = null) =>
        MachineExecutor.Execute(GCodeParser.Parse(program, Dialect.Fanuc), settings);

    private static ToolPath Siemens(string program, MachineSettings? settings = null) =>
        MachineExecutor.Execute(GCodeParser.Parse(program, Dialect.Siemens), settings);

    private static void AssertPoint(Point3 expected, Point3 actual)
    {
        Assert.Equal(expected.X, actual.X, 6);
        Assert.Equal(expected.Y, actual.Y, 6);
        Assert.Equal(expected.Z, actual.Z, 6);
    }

    [Fact]
    public void MovimentoEAvancoSaoModais()
    {
        ToolPath path = Fanuc("G01 X10 F100\nX20\nG00 X0");

        Assert.Equal(MotionMode.Linear, path.Moves[1].Motion);
        Assert.Equal(100.0, path.Moves[1].Feed);
        Assert.Equal(MotionMode.Rapid, path.Moves[2].Motion);
        Assert.Empty(path.Diagnostics);
    }

    [Fact]
    public void IncrementalG91SomaNaPosicaoAtual()
    {
        ToolPath path = Fanuc("G90 G00 X10 Y10\nG91 G01 X5 Y-2 F100\nX5\nG90 X0");

        AssertPoint(new Point3(15, 8, 0), path.Moves[1].End);
        AssertPoint(new Point3(20, 8, 0), path.Moves[2].End);
        AssertPoint(new Point3(0, 8, 0), path.Moves[3].End);
    }

    [Fact]
    public void PolegadasG20ConvertemParaMm()
    {
        ToolPath path = Fanuc("G20 G01 X1. F10.");

        AssertPoint(new Point3(25.4, 0, 0), path.Moves[0].End);
        Assert.Equal(254.0, path.Moves[0].Feed, 6);
    }

    [Fact]
    public void ZeroDePecaG55DeslocaODesenho()
    {
        var settings = new MachineSettings
        {
            WorkOffsets = new Dictionary<int, Point3> { [55] = new Point3(100, 50, 0) },
        };

        ToolPath path = Fanuc("G54 G00 X10 Y0\nG55 X10 Y0", settings);

        AssertPoint(new Point3(10, 0, 0), path.Moves[0].End);
        AssertPoint(new Point3(110, 50, 0), path.Moves[1].End);
    }

    [Fact]
    public void Siemens_TransDeslocaSemMover()
    {
        ToolPath path = Siemens("TRANS X100 Y-7\nG0 X0 Y0\nTRANS\nG0 X0 Y0");

        Assert.Equal(2, path.Moves.Count);
        AssertPoint(new Point3(100, -7, 0), path.Moves[0].End);
        AssertPoint(new Point3(0, 0, 0), path.Moves[1].End);
    }

    [Fact]
    public void G53EG04NaoGeramMovimento()
    {
        ToolPath path = Fanuc("G00 X10\nG53 G00 Z-110\nG04 X2.\nG01 X20 F100");

        Assert.Equal(2, path.Moves.Count);
        AssertPoint(new Point3(20, 0, 0), path.Moves[1].End);
    }

    [Fact]
    public void FanucInteirosEmMilesimosQuandoConfigurado()
    {
        var settings = new MachineSettings { FanucIntegers = FanucIntegerMode.Thousandths };

        ToolPath path = Fanuc("G01 X10 Y10. F100", settings);

        AssertPoint(new Point3(0.010, 10, 0), path.Moves[0].End);
    }

    [Fact]
    public void ArcoPorRaioPositivoFazOMenorCaminho()
    {
        ToolPath path = Fanuc("G00 X10 Y0\nG03 X0 Y10 R10 F100");

        Move arc = path.Moves[1];
        Assert.Equal(Math.PI / 2, arc.Sweep, 6);
        Assert.Equal(0, arc.CenterU, 6);
        Assert.Equal(0, arc.CenterV, 6);
    }

    [Fact]
    public void ArcoPorRaioNegativoFazOMaiorCaminho()
    {
        ToolPath path = Fanuc("G00 X10 Y0\nG03 X0 Y10 R-10 F100");

        Assert.Equal(1.5 * Math.PI, path.Moves[1].Sweep, 6);
    }

    [Fact]
    public void Siemens_ArcoPorCr()
    {
        ToolPath path = Siemens("G0 X3 Y66\nG3 X-3 Y66 CR=3 F800");

        Move arc = path.Moves[1];
        Assert.Equal(Math.PI, Math.Abs(arc.Sweep), 6);
        Assert.Equal(0, arc.CenterU, 6);
        Assert.Equal(66, arc.CenterV, 6);
        Assert.Empty(path.Diagnostics);
    }

    [Fact]
    public void ArcoNoPlanoG18UsaIK()
    {
        ToolPath path = Fanuc("G18 G00 X10 Z0\nG02 X0 Z10 I-10 K0 F100");

        Move arc = path.Moves[1];
        Assert.Equal(WorkPlane.ZX, arc.Plane);
        AssertPoint(new Point3(0, 0, 10), arc.End);
        Point3 middle = arc.PointAt(0.5);
        Assert.Equal(0, middle.Y, 6); // arco fica no plano XZ
        Assert.Equal(10, Math.Sqrt(middle.X * middle.X + middle.Z * middle.Z), 6);
    }

    [Fact]
    public void FerramentaAtivaVemDoM06()
    {
        ToolPath path = Fanuc("T3 M06\nG01 X10 F100\nT5\nX20\nM06\nX30");

        Assert.Equal(3, path.Moves[0].Tool);
        Assert.Equal(3, path.Moves[1].Tool); // T5 só selecionou
        Assert.Equal(5, path.Moves[2].Tool);
        Assert.Equal(new[] { 3, 5 }, path.Tools);
    }

    [Fact]
    public void BlocosDepoisDoM30GeramUmAviso()
    {
        ToolPath path = Fanuc("G01 X10 F100\nM30\nG00 X99\nG00 X98");

        Assert.Single(path.Moves);
        ParseDiagnostic warning = Assert.Single(path.Diagnostics);
        Assert.Equal(3, warning.LineNumber);
    }

    [Fact]
    public void CorteSemAvancoGeraAviso()
    {
        ToolPath path = Fanuc("G01 X10");

        Assert.Contains(path.Diagnostics, d => d.Message.Contains("avanço"));
    }

    [Fact]
    public void RapidoAbaixoDeZeroGeraAviso()
    {
        ToolPath path = Fanuc("G01 Z-5 F100\nG00 X50");

        Assert.Contains(path.Diagnostics, d => d.Message.Contains("colisão"));
    }

    [Fact]
    public void ArcoSemCentroNemRaioEhErro()
    {
        ToolPath path = Fanuc("G02 X10 Y10 F100");

        Assert.Contains(path.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ComandosComunsSaoAceitosSemAviso()
    {
        ToolPath path = Fanuc("G17 G21 G40 G49 G80 G90 G94\nT1 M06\nG54\nS3000 M03\nG43 Z50. H1\nM08\nM05\nM09");

        Assert.Empty(path.Diagnostics);
    }
}
