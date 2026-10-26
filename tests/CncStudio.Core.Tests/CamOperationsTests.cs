using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

public class CamOperationsTests
{
    private static readonly CamTool Tool10 = new(1, 10, 3000, 500, 200);
    private static readonly CamTool Tool6 = new(2, 6, 4000, 400, 150);
    private static readonly CamLevels Levels = new(SafeZ: 50, ClearanceZ: 2, TopZ: 0, BottomZ: -6, StepDown: 3);

    private static IEnumerable<ToolMove> Cuts(CamOperation op) => op.Moves.Where(m => m.Kind != MoveKind.Rapid);

    /// <summary>Todo arco precisa ter o mesmo raio no começo e no fim (percurso coerente).</summary>
    private static void AssertArcsValid(CamOperation op)
    {
        ToolMove? prev = null;
        foreach (ToolMove m in op.Moves)
        {
            if (m.IsArc && prev is { } p)
            {
                Assert.Equal(p.XY.DistanceTo(m.Center), m.XY.DistanceTo(m.Center), 4);
            }

            prev = m;
        }
    }

    /// <summary>Distância do ponto ao retângulo (positiva fora, negativa dentro).</summary>
    private static double SignedDistToRect(Point2 p, double x, double y, double w, double h)
    {
        double dx = Math.Max(Math.Max(x - p.X, p.X - (x + w)), 0);
        double dy = Math.Max(Math.Max(y - p.Y, p.Y - (y + h)), 0);
        if (dx > 0 || dy > 0)
        {
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        return -Math.Min(Math.Min(p.X - x, x + w - p.X), Math.Min(p.Y - y, y + h - p.Y));
    }

    [Fact]
    public void FaceamentoRespeitaProfundidadeELimites()
    {
        OperationResult r = CamOperations.Facing(Tool10, Levels, 0, 0, 60, 40, 70);
        Assert.True(r.Ok, r.Error);
        CamOperation op = r.Operation!;
        Assert.Equal(-6, Cuts(op).Min(m => m.Z), 9);
        Assert.Equal(2, Cuts(op).Select(m => Math.Round(m.Z, 6)).Distinct().Count()); // dois passes de 3 mm
        Assert.All(Cuts(op), m => Assert.InRange(m.Y, 5 - 1e-9, 35 + 1e-9)); // aresta da fresa rente à peça
        Assert.True(Cuts(op).Min(m => m.X) < -5 && Cuts(op).Max(m => m.X) > 65); // entra e sai por fora
        Assert.Equal(50, op.Moves[^1].Z);
    }

    [Fact]
    public void FaceamentoCobreAPeca()
    {
        CamOperation op = CamOperations.Facing(Tool10, Levels, 0, 0, 60, 40, 70).Operation!;
        double[] rows = Cuts(op).Where(m => Math.Abs(m.Z + 6) < 1e-9).Select(m => m.Y).Distinct().OrderBy(v => v).ToArray();
        for (int i = 1; i < rows.Length; i++)
        {
            Assert.True(rows[i] - rows[i - 1] <= 7 + 1e-9, "linhas mais afastadas que o passo lateral");
        }

        Assert.Equal(5, rows[0], 9);
        Assert.Equal(35, rows[^1], 9);
    }

    [Fact]
    public void FaceamentoComFerramentaMaiorQueAPecaAvisa()
    {
        OperationResult r = CamOperations.Facing(Tool10, Levels, 0, 0, 60, 8, 70);
        Assert.True(r.Ok);
        Assert.NotEmpty(r.Operation!.Warnings);
    }

    [Fact]
    public void ContornoPorForaFicaFora()
    {
        Contour rect = Contour.Rectangle(0, 0, 40, 20);
        OperationResult r = CamOperations.Contour(rect, Tool10, Levels, new ContourParams(ContourSide.Outside));
        Assert.True(r.Ok, r.Error);
        CamOperation op = r.Operation!;
        AssertArcsValid(op);
        Assert.Equal(-6, Cuts(op).Min(m => m.Z), 9);
        foreach (ToolMove m in Cuts(op).Where(m => Math.Abs(m.Z + 6) < 1e-9))
        {
            Assert.True(SignedDistToRect(m.XY, 0, 0, 40, 20) >= 5 - 1e-6, $"ferramenta invadiu a peça em {m.X},{m.Y}");
        }
    }

    [Fact]
    public void ContornoPorForaConcordanteGiraNoSentidoHorario()
    {
        CamOperation op = CamOperations.Contour(Contour.Rectangle(0, 0, 40, 20), Tool10, Levels, new ContourParams(ContourSide.Outside)).Operation!;
        var pts = Cuts(op).Where(m => Math.Abs(m.Z + 6) < 1e-9).Select(m => m.XY).ToList();
        double area = 0;
        for (int i = 0; i < pts.Count; i++)
        {
            area += pts[i].Cross(pts[(i + 1) % pts.Count]) / 2;
        }

        Assert.True(area < 0, "concordante por fora deve girar no sentido horário");
    }

    [Fact]
    public void ContornoPorDentroFicaDentro()
    {
        OperationResult r = CamOperations.Contour(Contour.Rectangle(0, 0, 40, 20), Tool6, Levels, new ContourParams(ContourSide.Inside));
        Assert.True(r.Ok, r.Error);
        AssertArcsValid(r.Operation!);
        foreach (ToolMove m in Cuts(r.Operation!).Where(m => Math.Abs(m.Z + 6) < 1e-9))
        {
            Assert.True(SignedDistToRect(m.XY, 0, 0, 40, 20) <= -3 + 1e-6, $"ferramenta cortou fora do desenho em {m.X},{m.Y}");
        }

        Assert.Contains(r.Operation!.Warnings, w => w.Contains("pré-furo"));
    }

    [Fact]
    public void SobremetalAfastaOPercurso()
    {
        CamOperation op = CamOperations.Contour(Contour.Rectangle(0, 0, 40, 20), Tool10, Levels, new ContourParams(ContourSide.Outside, Allowance: 0.5)).Operation!;
        double min = Cuts(op).Where(m => Math.Abs(m.Z + 6) < 1e-9).Min(m => SignedDistToRect(m.XY, 0, 0, 40, 20));
        Assert.Equal(5.5, min, 6);
    }

    [Fact]
    public void CompensacaoNoControleSegueODesenho()
    {
        OperationResult r = CamOperations.Contour(Contour.Rectangle(0, 0, 40, 20), Tool10, Levels, new ContourParams(ContourSide.Outside, ControllerComp: true, LeadRadius: 8));
        Assert.True(r.Ok, r.Error);
        CamOperation op = r.Operation!;
        AssertArcsValid(op);
        Assert.Equal(2, op.Moves.Count(m => m.Comp == CutterComp.Left)); // um por passe
        Assert.Equal(2, op.Moves.Count(m => m.Comp == CutterComp.Off));
        // o percurso do contorno (retas) está sobre o retângulo do desenho
        var lines = op.Moves.Where(m => m.Kind == MoveKind.Feed && Math.Abs(m.Z + 6) < 1e-9 && m.Comp is null && Math.Abs(m.Feed - 500) < 1e-9).ToList();
        Assert.NotEmpty(lines);
        Assert.All(lines, m => Assert.Equal(0, Math.Abs(SignedDistToRect(m.XY, 0, 0, 40, 20)), 6));
    }

    [Fact]
    public void CompensacaoComConvencionalUsaDireita()
    {
        CamOperation op = CamOperations.Contour(Contour.Rectangle(0, 0, 40, 20), Tool10, Levels, new ContourParams(ContourSide.Outside, Climb: false, ControllerComp: true, LeadRadius: 8)).Operation!;
        Assert.Contains(op.Moves, m => m.Comp == CutterComp.Right);
        Assert.DoesNotContain(op.Moves, m => m.Comp == CutterComp.Left);
    }

    [Fact]
    public void RaioDeEntradaPequenoComCompensacaoDaErro()
    {
        OperationResult r = CamOperations.Contour(Contour.Rectangle(0, 0, 40, 20), Tool10, Levels, new ContourParams(ContourSide.Outside, ControllerComp: true, LeadRadius: 4));
        Assert.False(r.Ok);
    }

    [Fact]
    public void ContornoPorDentroMaiorQueOFuroDaErro()
    {
        OperationResult r = CamOperations.Contour(Contour.Rectangle(0, 0, 40, 8), Tool10, Levels, new ContourParams(ContourSide.Inside));
        Assert.False(r.Ok);
        Assert.NotNull(r.Error);
    }

    [Fact]
    public void ContornoComArcosFicaCoerente()
    {
        OperationResult r = CamOperations.Contour(Contour.Circle(new Point2(10, 10), 15), Tool10, Levels, new ContourParams(ContourSide.Outside));
        Assert.True(r.Ok, r.Error);
        AssertArcsValid(r.Operation!);
        double max = Cuts(r.Operation!).Where(m => Math.Abs(m.Z + 6) < 1e-9).Min(m => m.XY.DistanceTo(new Point2(10, 10)));
        Assert.True(max >= 20 - 1e-6);
    }

    [Fact]
    public void BolsaoFicaDentroECobre()
    {
        OperationResult r = CamOperations.Pocket(Contour.Rectangle(0, 0, 40, 30), Tool6, Levels, new PocketParams(StepOverPercent: 50));
        Assert.True(r.Ok, r.Error);
        CamOperation op = r.Operation!;
        AssertArcsValid(op);
        Assert.Equal(-6, Cuts(op).Min(m => m.Z), 9);
        Assert.All(Cuts(op), m => Assert.True(SignedDistToRect(m.XY, 0, 0, 40, 30) <= -3 + 1e-6));

        // cobertura: todo ponto da região do centro da fresa está a menos de um raio de algum percurso no fundo
        var deep = new List<(Point2 A, Point2 B)>();
        ToolMove? prev = null;
        foreach (ToolMove m in op.Moves)
        {
            if (prev is { } p && m.Kind == MoveKind.Feed && Math.Abs(m.Z + 6) < 1e-9 && Math.Abs(p.Z + 6) < 1e-9)
            {
                deep.Add((p.XY, m.XY));
            }

            prev = m;
        }

        for (double x = 3; x <= 37; x += 1)
        {
            for (double y = 3; y <= 27; y += 1)
            {
                double best = deep.Min(s => DistToSegment(new Point2(x, y), s.A, s.B));
                Assert.True(best <= 3 + 1e-6, $"ponto {x},{y} sem corte (distância {best:F2})");
            }
        }
    }

    private static double DistToSegment(Point2 p, Point2 a, Point2 b)
    {
        Point2 d = b - a;
        double len2 = d.Dot(d);
        double t = len2 < 1e-18 ? 0 : Math.Clamp((p - a).Dot(d) / len2, 0, 1);
        return p.DistanceTo(a + (d * t));
    }

    [Fact]
    public void BolsaoCircularUsaHelice()
    {
        OperationResult r = CamOperations.Pocket(Contour.Circle(new Point2(0, 0), 20), Tool10, Levels, new PocketParams());
        Assert.True(r.Ok, r.Error);
        AssertArcsValid(r.Operation!);
        // há arco com Z diferente do anterior (rampa helicoidal)
        ToolMove? prev = null;
        bool helix = false;
        foreach (ToolMove m in r.Operation!.Moves)
        {
            if (m.IsArc && prev is { } p && Math.Abs(p.Z - m.Z) > 1e-6)
            {
                helix = true;
            }

            prev = m;
        }

        Assert.True(helix);
    }

    [Fact]
    public void BolsaoComAcabamentoTerminaNaParede()
    {
        OperationResult r = CamOperations.Pocket(Contour.Rectangle(0, 0, 40, 30), Tool6, Levels, new PocketParams(StepOverPercent: 60, FinishAllowance: 0.3, LeadRadius: 2));
        Assert.True(r.Ok, r.Error);
        AssertArcsValid(r.Operation!);
        double wall = Cuts(r.Operation!).Where(m => Math.Abs(m.Z + 6) < 1e-9).Max(m => -SignedDistToRect(m.XY, 0, 0, 40, 30) < 3 + 1e-6 ? 1 : 0);
        Assert.Equal(1, wall); // algum ponto de acabamento encosta na parede (distância = raio)
        Assert.All(Cuts(r.Operation!), m => Assert.True(SignedDistToRect(m.XY, 0, 0, 40, 30) <= -3 + 1e-6));
    }

    [Fact]
    public void BolsaoPequenoDemaisDaErro()
    {
        Assert.False(CamOperations.Pocket(Contour.Rectangle(0, 0, 5, 5), Tool10, Levels, new PocketParams()).Ok);
    }

    [Fact]
    public void FuracaoOrdenaPeloMaisProximo()
    {
        Point2[] pts = [new(100, 0), new(10, 0), new(50, 0), new(30, 0)];
        OperationResult r = CamOperations.Drilling(Tool6, 50, pts, DrillCycleKind.Peck, 0, -12, 2, peck: 4);
        Assert.True(r.Ok, r.Error);
        Assert.Equal([10.0, 30.0, 50.0, 100.0], r.Operation!.Drill!.Points.Select(p => p.X).ToArray());
        Assert.Equal(4, r.Operation.Drill.Peck);
    }

    [Fact]
    public void FuracaoValidaParametros()
    {
        Point2[] pts = [new(0, 0)];
        Assert.False(CamOperations.Drilling(Tool6, 50, pts, DrillCycleKind.Peck, 0, -12, 2, peck: 0).Ok);
        Assert.False(CamOperations.Drilling(Tool6, 50, pts, DrillCycleKind.Drill, 0, 1, 2).Ok);
        Assert.False(CamOperations.Drilling(Tool6, 50, pts, DrillCycleKind.Dwell, 0, -5, 2).Ok);
        Assert.False(CamOperations.Drilling(Tool6, 50, [], DrillCycleKind.Drill, 0, -5, 2).Ok);
        Assert.True(CamOperations.Drilling(Tool6, 50, pts, DrillCycleKind.Dwell, 0, -5, 2, dwellSeconds: 0.5).Ok);
    }

    [Fact]
    public void FurosVemDosCirculosDoDesenho()
    {
        DxfImportResult dxf = DxfImporter.Parse("0\nSECTION\n2\nENTITIES\n0\nCIRCLE\n10\n10\n20\n20\n40\n3\n0\nCIRCLE\n10\n40\n20\n20\n40\n4\n0\nLINE\n10\n0\n20\n0\n11\n5\n21\n0\n0\nENDSEC\n");
        IReadOnlyList<DrillHole> holes = CamOperations.HolesFromCircles(dxf.Contours);
        Assert.Equal(2, holes.Count);
        Assert.Equal([6.0, 8.0], holes.Select(h => h.Diameter).OrderBy(d => d).ToArray());
    }

    [Fact]
    public void NiveisInvalidosSaoRecusados()
    {
        Assert.False(CamOperations.Facing(Tool10, Levels with { StepDown = 0 }, 0, 0, 10, 10, 70).Ok);
        Assert.False(CamOperations.Contour(Contour.Rectangle(0, 0, 40, 20), Tool10, Levels with { BottomZ = 1 }, new ContourParams(ContourSide.Outside)).Ok);
        Assert.False(CamOperations.Contour(Contour.Rectangle(0, 0, 40, 20), Tool10 with { Diameter = 0 }, Levels, new ContourParams(ContourSide.Outside)).Ok);
    }

    [Fact]
    public void ProfundidadesDividemIgualSemPassarDoPasse()
    {
        var lv = new CamLevels(50, 2, 0, -10, 4);
        IReadOnlyList<double> d = lv.Depths();
        Assert.Equal(3, d.Count);
        Assert.Equal(-10, d[^1], 9);
        Assert.Equal(-10.0 / 3, d[0], 9);
    }
}
