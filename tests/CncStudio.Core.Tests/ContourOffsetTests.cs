using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

public class ContourOffsetTests
{
    [Fact]
    public void AreaEPerimetroDoRetangulo()
    {
        Contour rect = Contour.Rectangle(0, 0, 20, 10);
        Assert.True(rect.IsClosed);
        Assert.True(rect.IsCounterClockwise);
        Assert.Equal(200, rect.SignedArea, 6);
        Assert.Equal(60, rect.Perimeter, 6);
        Assert.Equal(-200, rect.Reversed().SignedArea, 6);
    }

    [Fact]
    public void AreaDoCirculo()
    {
        Contour c = Contour.Circle(new Point2(5, 5), 10);
        Assert.True(c.IsClosed);
        Assert.Equal(Math.PI * 100, c.SignedArea, 6);
        Assert.Equal(2 * Math.PI * 10, c.Perimeter, 6);
    }

    [Fact]
    public void RetanguloPorForaGanhaCantosArredondados()
    {
        OffsetResult r = Contour.Rectangle(0, 0, 20, 10).OffsetOutside(2);
        Assert.True(r.Ok, r.Error);
        // área = A + P·d + π·d²
        Assert.Equal(200 + (60 * 2) + (Math.PI * 4), r.Contour!.SignedArea, 4);
        Assert.Equal(8, r.Contour.Segments.Count); // 4 retas + 4 arcos
    }

    [Fact]
    public void RetanguloPorDentroEncolhe()
    {
        OffsetResult r = Contour.Rectangle(0, 0, 20, 10).OffsetOutside(-2);
        Assert.True(r.Ok, r.Error);
        Assert.Equal(16 * 6, r.Contour!.SignedArea, 6);
        Assert.Equal(4, r.Contour.Segments.Count);
    }

    [Fact]
    public void FuncionaNosDoisSentidosDePercurso()
    {
        Contour cw = Contour.Rectangle(0, 0, 20, 10).Reversed();
        OffsetResult inside = cw.OffsetOutside(-2);
        Assert.True(inside.Ok, inside.Error);
        Assert.Equal(-96, inside.Contour!.SignedArea, 6);
    }

    [Fact]
    public void CirculoPorDentroEPorFora()
    {
        Contour c = Contour.Circle(new Point2(0, 0), 10);
        OffsetResult inside = c.OffsetOutside(-3);
        OffsetResult outside = c.OffsetOutside(3);
        Assert.True(inside.Ok, inside.Error);
        Assert.True(outside.Ok, outside.Error);
        Assert.Equal(Math.PI * 49, inside.Contour!.SignedArea, 4);
        Assert.Equal(Math.PI * 169, outside.Contour!.SignedArea, 4);
    }

    [Fact]
    public void FormaEmLComCantoInternoPorFora()
    {
        Contour l = Contour.FromPoints([new(0, 0), new(20, 0), new(20, 10), new(10, 10), new(10, 20), new(0, 20)]);
        Assert.Equal(300, l.SignedArea, 6);
        OffsetResult r = l.OffsetOutside(1);
        Assert.True(r.Ok, r.Error);
        // A + P·d + 5 cantos externos de 90° (π·d²/4 cada) − quadrado do canto interno (d²)
        Assert.Equal(300 + 80 + (5 * Math.PI / 4) - 1, r.Contour!.SignedArea, 4);
    }

    [Fact]
    public void FormaEmLPorDentro()
    {
        Contour l = Contour.FromPoints([new(0, 0), new(20, 0), new(20, 10), new(10, 10), new(10, 20), new(0, 20)]);
        OffsetResult r = l.OffsetOutside(-1);
        Assert.True(r.Ok, r.Error);
        // por dentro: faixas P·d, +d² em cada canto convexo (5) que se sobrepõe, −π·d²/4 no canto reflexo (arco)
        Assert.Equal(300 - 80 + 5 - (Math.PI / 4), r.Contour!.SignedArea, 4);
    }

    [Fact]
    public void DeslocamentoGrandeDemaisDaErro()
    {
        OffsetResult r = Contour.Rectangle(0, 0, 20, 10).OffsetOutside(-6);
        Assert.False(r.Ok);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void ArcoMenorQueOFerramentaDaErro()
    {
        OffsetResult r = Contour.Circle(new Point2(0, 0), 3).OffsetOutside(-4);
        Assert.False(r.Ok);
    }

    [Fact]
    public void ContornoAbertoNaoDesloca()
    {
        var open = new Contour([Segment2.Line(new Point2(0, 0), new Point2(10, 0)), Segment2.Line(new Point2(10, 0), new Point2(10, 10))]);
        Assert.False(open.IsClosed);
        Assert.False(open.OffsetOutside(1).Ok);
    }

    [Fact]
    public void DistanciaZeroDevolveOMesmo()
    {
        Contour rect = Contour.Rectangle(0, 0, 5, 5);
        OffsetResult r = rect.OffsetOutside(0);
        Assert.True(r.Ok);
        Assert.Same(rect, r.Contour);
    }

    [Fact]
    public void CantoComArcoPorForaAumentaORaio()
    {
        // quadrado 20×20 com cantos de raio 3: por fora 2 → arcos de raio 5
        var segs = new List<Segment2>
        {
            Segment2.Line(new Point2(3, 0), new Point2(17, 0)),
            Segment2.Arc(new Point2(17, 0), new Point2(20, 3), new Point2(17, 3), ccw: true),
            Segment2.Line(new Point2(20, 3), new Point2(20, 17)),
            Segment2.Arc(new Point2(20, 17), new Point2(17, 20), new Point2(17, 17), ccw: true),
            Segment2.Line(new Point2(17, 20), new Point2(3, 20)),
            Segment2.Arc(new Point2(3, 20), new Point2(0, 17), new Point2(3, 17), ccw: true),
            Segment2.Line(new Point2(0, 17), new Point2(0, 3)),
            Segment2.Arc(new Point2(0, 3), new Point2(3, 0), new Point2(3, 3), ccw: true),
        };
        var rounded = new Contour(segs);
        Assert.True(rounded.IsClosed);
        OffsetResult outside = rounded.OffsetOutside(2);
        Assert.True(outside.Ok, outside.Error);
        Assert.All(outside.Contour!.Segments.Where(s => s.IsArc), s => Assert.Equal(5, s.Radius, 6));
        // por dentro 2: arcos de raio 1
        OffsetResult inside = rounded.OffsetOutside(-2);
        Assert.True(inside.Ok, inside.Error);
        Assert.All(inside.Contour!.Segments.Where(s => s.IsArc), s => Assert.Equal(1, s.Radius, 6));
        // por dentro 4: arco some
        Assert.False(rounded.OffsetOutside(-4).Ok);
    }
}

public class ToolpathViewTests
{
    [Fact]
    public void BoundsDoContorno()
    {
        (double minX, double minY, double maxX, double maxY) = Contour.Rectangle(2, 3, 20, 10).BoundsXY();
        Assert.Equal((2, 3, 22, 13), (minX, minY, maxX, maxY));
        (double cx0, double cy0, double cx1, double cy1) = Contour.Circle(new Point2(0, 0), 5).BoundsXY();
        Assert.Equal(-5, cx0, 2);
        Assert.Equal(5, cx1, 2);
        Assert.Equal(-5, cy0, 2);
        Assert.Equal(5, cy1, 2);
    }

    [Fact]
    public void PrevisaoMostraSoOCorteECortaNoRapido()
    {
        var tool = new CamTool(1, 10, 3000, 500, 200);
        var lv = new CamLevels(50, 2, 0, -3, 3);
        CamOperation op = CamOperations.Contour(Contour.Rectangle(0, 0, 40, 20), tool, lv, new ContourParams(ContourSide.Outside)).Operation!;
        List<List<Point2>> lines = ToolpathView.CuttingPolylines(op);
        Assert.NotEmpty(lines);
        // nenhum ponto cortante entra no retângulo
        Assert.All(lines.SelectMany(l => l), p => Assert.False(p.X > 0.01 && p.X < 39.99 && p.Y > 0.01 && p.Y < 19.99));
    }

    [Fact]
    public void PrevisaoDeFuracaoMarcaCadaFuro()
    {
        var tool = new CamTool(1, 6, 3000, 500, 200);
        CamOperation op = CamOperations.Drilling(tool, 50, [new(10, 10), new(30, 10)], DrillCycleKind.Drill, 0, -5, 2).Operation!;
        Assert.Equal(4, ToolpathView.CuttingPolylines(op).Count);
    }
}
