using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Dialects;
using CncStudio.Core.Parsing;
using CncStudio.Core.Scene;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

public class RotaryPartFrameTests
{
    private static readonly RotaryPartFrame Frame = new(AY: 0, AZ: -25, AReversed: false, BX: 0, BZ: -25, BReversed: false);

    [Fact]
    public void SemGiroNaoMuda()
    {
        var p = new Point3(3, 4, 5);
        Assert.Equal(p, Frame.ToPart(p, Point3.Origin, default));
    }

    [Fact]
    public void Giro90EmAColocaOCorteNaFaceCerta()
    {
        // Eixo de giro a 25 abaixo do zero; face girada 90°: o corte feito a 25 acima do eixo (Z=0 na máquina)
        // cai na lateral da peça (Y = ±25, Z = -25 no sistema da peça)
        Point3 r = Frame.ToPart(new Point3(10, 0, 0), Point3.Origin, new RotaryAngles(90, 0, 0));
        Assert.Equal(10, r.X, 6);
        Assert.Equal(25, Math.Abs(r.Y), 6);
        Assert.Equal(-25, r.Z, 6);
    }

    [Fact]
    public void SentidoInvertidoEspelhaEIdaEVoltaFecha()
    {
        var rev = Frame with { AReversed = true };
        Point3 a = Frame.ToPart(new Point3(0, 7, 3), Point3.Origin, new RotaryAngles(30, 0, 0));
        Point3 b = rev.ToPart(new Point3(0, 7, 3), Point3.Origin, new RotaryAngles(-30, 0, 0));
        Assert.Equal(a.Y, b.Y, 6);
        Assert.Equal(a.Z, b.Z, 6);
        // Distância ao eixo se conserva
        Assert.Equal(Math.Sqrt(7 * 7 + 28 * 28), Math.Sqrt(a.Y * a.Y + (a.Z + 25) * (a.Z + 25)), 6);
    }

    private static Point3 Apply(double[] m, Point3 p) => new(
        (m[0] * p.X) + (m[4] * p.Y) + (m[8] * p.Z) + m[12],
        (m[1] * p.X) + (m[5] * p.Y) + (m[9] * p.Z) + m[13],
        (m[2] * p.X) + (m[6] * p.Y) + (m[10] * p.Z) + m[14]);

    [Theory]
    [InlineData(90, 0)]
    [InlineData(0, 90)]
    [InlineData(30, 45)]
    [InlineData(-20, 70)]
    public void MatrizDaMesaEOContrarioDeToPart(double a, double b)
    {
        var frame = new RotaryPartFrame(AY: 5, AZ: -25, AReversed: false, BX: 8, BZ: -25, BReversed: true);
        var offset = new Point3(100, 50, 10);
        var angles = new RotaryAngles(a, b, 0);
        var part = new Point3(113, 61, -7);
        Point3 machine = Apply(frame.ToMachineMatrix(offset, angles), part);
        Point3 back = frame.ToPart(machine, offset, angles);
        Assert.Equal(part.X, back.X, 6);
        Assert.Equal(part.Y, back.Y, 6);
        Assert.Equal(part.Z, back.Z, 6);
    }

    [Fact]
    public void SemGiroAMatrizEIdentidade()
    {
        Assert.Equal(RotaryPartFrame.Identity(), Frame.ToMachineMatrix(Point3.Origin, default));
    }

    [Fact]
    public void LimitesDoPercursoUsamAPecaParada()
    {
        // Corte a 25 acima do eixo com A90: na peça cai na lateral (|Y| = 25), não a 25 de altura em Z
        const string text = "G21 G90 G54\nG00 A90.\nG00 X0 Y0 Z0\nG01 X20 F100";
        ToolPath path = MachineExecutor.Execute(GCodeParser.Parse(text, Dialect.Fanuc), new MachineSettings { PartFrame = Frame });
        Assert.Equal(25, Math.Abs(path.CuttingBounds.Min.Y), 6);
        Assert.Equal(25, Math.Abs(path.CuttingBounds.Max.Y), 6);
        Assert.Equal(-25, path.CuttingBounds.Max.Z, 6);
    }
}
