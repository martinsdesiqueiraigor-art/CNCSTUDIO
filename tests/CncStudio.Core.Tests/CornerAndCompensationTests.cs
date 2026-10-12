using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

public class CornerAndCompensationTests
{
    private static ToolPath Fanuc(string program, double toolDiameter = 10) =>
        MachineExecutor.Execute(GCodeParser.Parse(program, Dialect.Fanuc),
            new MachineSettings { ToolDiameterOf = _ => toolDiameter });

    private static void AssertXY(double x, double y, Point3 p)
    {
        Assert.Equal(x, p.X, 6);
        Assert.Equal(y, p.Y, 6);
    }

    // ───────── Cantos ─────────

    [Fact]
    public void ArredondamentoDeCanto()
    {
        ToolPath path = Fanuc("G01 X0 Y0 F100\nX50 ,R5\nY30");

        // Reta até 45, arco R5 com centro (45,5), reta a partir de (50,5)
        Assert.Equal(3, path.Moves.Count);
        AssertXY(45, 0, path.Moves[0].End);
        Move arc = path.Moves[1];
        Assert.Equal(MotionMode.CounterClockwiseArc, arc.Motion);
        Assert.Equal(45, arc.CenterU, 6);
        Assert.Equal(5, arc.CenterV, 6);
        Assert.Equal(Math.PI / 2, arc.Sweep, 6);
        AssertXY(50, 5, path.Moves[2].Start);
        AssertXY(50, 30, path.Moves[2].End);
    }

    [Fact]
    public void ChanfroDeCanto()
    {
        ToolPath path = Fanuc("G01 X0 Y0 F100\nX50 ,C5\nY30");

        AssertXY(45, 0, path.Moves[0].End);
        Assert.Equal(MotionMode.Linear, path.Moves[1].Motion);
        AssertXY(50, 5, path.Moves[1].End);
    }

    [Fact]
    public void CantoComExpressao()
    {
        ToolPath path = Fanuc("#8=4\nG01 X0 Y0 F100\nX50 ,R[#8]\nY30");

        AssertXY(46, 0, path.Moves[0].End);
    }

    // ───────── Compensação ─────────

    [Fact]
    public void G41EmContornoExternoGanhaArcosNosCantos()
    {
        // Quadrado percorrido no sentido horário com a ferramenta à esquerda = por fora
        ToolPath path = Fanuc("""
            G00 X-20 Y-20
            G41 G01 X0 Y0 F100
            Y30
            X50
            Y0
            X0
            G40 G00 X-20 Y-20
            """);

        // Entrada vai direto ao início compensado do primeiro lado (x = −5)
        AssertXY(-5, 0, path.Moves[1].End);

        // Lado esquerdo compensado: x = −5
        Move left = path.Moves[2];
        AssertXY(-5, 0, left.Start);
        AssertXY(-5, 30, left.End);

        // Canto externo (0,30): arco R5 até (0,35)
        Move corner = path.Moves[3];
        Assert.True(corner.IsArc);
        Assert.Equal(0, corner.CenterU, 6);
        Assert.Equal(30, corner.CenterV, 6);
        AssertXY(0, 35, corner.End);

        // Lado de cima: y = 35
        AssertXY(50, 35, path.Moves[4].End);

        // Saída volta ao ponto programado
        AssertXY(-20, -20, path.Moves[^1].End);
        Assert.DoesNotContain(path.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void G41EmContornoInternoAparaOsCantos()
    {
        // Quadrado no sentido anti-horário com a ferramenta à esquerda = por dentro
        ToolPath path = Fanuc("""
            G00 X25 Y15
            G41 G01 X0 Y0 F100
            X50
            Y30
            X0
            Y0
            G40 G01 X25 Y15
            """);

        // Cantos internos: interseções em (5,5), (45,5), (45,25), (5,25)
        var ends = path.Moves.Select(m => (Math.Round(m.End.X, 6), Math.Round(m.End.Y, 6))).ToList();
        Assert.Contains((45.0, 5.0), ends);
        Assert.Contains((45.0, 25.0), ends);
        Assert.Contains((5.0, 25.0), ends);
        Assert.DoesNotContain(path.Moves, m => m.IsArc); // sem arcos em cantos internos
    }

    [Fact]
    public void G42InverteOLado()
    {
        ToolPath path = Fanuc("G00 X0 Y-10\nG42 G01 X0 Y0 F100\nY30\nG40 X0 Y40");

        // Subindo em Y com a ferramenta à direita: x = +5
        AssertXY(5, 0, path.Moves[1].End);
        AssertXY(5, 30, path.Moves[2].End);
    }

    [Fact]
    public void ArcoCompensadoMudaORaio()
    {
        // Círculo R20 anti-horário com G41: ferramenta por dentro → raio 15
        ToolPath path = Fanuc("G00 X0 Y0\nG41 G01 X20 Y0 F100\nG03 I-20 J0\nG40 G01 X0 Y0");

        Move arc = path.Moves.First(m => m.IsArc);
        Assert.Equal(15, arc.StartRadius, 6);
        Assert.Equal(2 * Math.PI, arc.Sweep, 6);
    }

    [Fact]
    public void FerramentaMaiorQueOArcoEhErro()
    {
        ToolPath path = Fanuc("G00 X0 Y0\nG41 G01 X3 Y0 F100\nG03 I-3 J0\nG40 G01 X0 Y0", toolDiameter: 10);

        Assert.Contains(path.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("raio"));
    }

    [Fact]
    public void SemDiametroDaFerramentaMantemOProgramadoComAviso()
    {
        ToolPath path = MachineExecutor.Execute(GCodeParser.Parse("G41 G01 X10 Y0 F100\nY10\nG40 X0", Dialect.Fanuc));

        AssertXY(10, 10, path.Moves[1].End);
        Assert.Contains(path.Diagnostics, d => d.Message.Contains("diâmetro"));
    }

    [Fact]
    public void Siemens_ChavetaComFresaD4()
    {
        string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "macros", "CHAVETA.mpf"));
        ToolPath path = MachineExecutor.Execute(GCodeParser.Parse(text, Dialect.Siemens),
            new MachineSettings { ToolDiameterOf = _ => 4 });

        Assert.DoesNotContain(path.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

        // Rasgo programado em x = ±3; com fresa Ø4 à esquerda, o centro anda em x = ±1
        var cuts = path.Moves.Where(m => m.Motion == MotionMode.Linear && Math.Abs(m.End.Y - m.Start.Y) > 10).ToList();
        Assert.NotEmpty(cuts);
        Assert.All(cuts, m => Assert.Equal(1, Math.Abs(m.End.X), 6));
    }
}
