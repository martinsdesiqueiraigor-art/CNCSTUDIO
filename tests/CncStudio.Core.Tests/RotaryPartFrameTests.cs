using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Scene;

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
}
