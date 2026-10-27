using CncStudio.Core.Cam;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;
using CncStudio.Core.Scene;

namespace CncStudio.Core.Tests;

public class RotaryWrapTests
{
    private static readonly CamTool Tool = new(1, 1, 8000, 200, 60);

    private static MachineProfile Machine(double min = -720, double max = 720)
    {
        MachineProfile m = MachineProfile.FourAxisTableA();
        m.Axes[0].MinAngle = min;
        m.Axes[0].MaxAngle = max;
        m.Axes[0].PivotY = 10;
        m.Axes[0].PivotZ = -20;
        return m;
    }

    private static CamOperation Text(MachineProfile m, double depth = 0.3)
    {
        var strokes = RotaryWrap.TextStrokes("AB1", 6);
        OperationResult r = RotaryWrap.Engrave("Gravação", Tool, m, "A", strokes, new WrapSetup(Diameter: 40, StartAxial: 5, Depth: depth, PassDepth: 0.15));
        Assert.True(r.Ok, r.Error);
        return r.Operation!;
    }

    [Fact]
    public void ZFicaNaSuperficieMenosProfundidadeEXNaLinhaDeCentro()
    {
        CamOperation op = Text(Machine());
        double top = -20 + 20; // centro do eixo + raio
        Assert.All(op.Moves.Where(m => m.Kind == MoveKind.Feed), m =>
        {
            Assert.InRange(m.Z, top - 0.3 - 1e-9, top - 0.15 + 1e-9);
            Assert.Equal(10, m.Y, 9); // fica sobre o centro do eixo A
        });
        Assert.All(op.Moves, m => Assert.Equal("A", m.RotaryLetter));
    }

    [Fact]
    public void PontosNaPecaParadaFicamNoCilindroComOComprimentoDeArcoCerto()
    {
        MachineProfile m = Machine();
        CamOperation op = Text(m, depth: 0.3);
        var frame = RotaryPartFrame.From(m)!;
        foreach (ToolMove mv in op.Moves.Where(x => x.Kind == MoveKind.Feed))
        {
            // volta o giro e confere o raio (distância ao eixo) = 20 - profundidade do passe
            Point3 part = frame.ToPart(new Point3(mv.X, mv.Y, mv.Z), Point3.Origin, new RotaryAngles(mv.RotaryAngle, 0, 0));
            double r = Math.Sqrt(((part.Y - 10) * (part.Y - 10)) + ((part.Z + 20) * (part.Z + 20)));
            Assert.InRange(r, 19.7 - 1e-6, 19.85 + 1e-6);
        }

        // Altura da letra (6 mm) vira 6/20 rad ≈ 17,19° ao redor
        double span = op.Moves.Max(x => x.RotaryAngle) - op.Moves.Min(x => x.RotaryAngle);
        Assert.InRange(span, 17.18, 17.2);
    }

    [Fact]
    public void AvancoCorrigidoFazASuperficieAndarNoAvancoPedido()
    {
        MachineProfile m = Machine();
        var strokes = new List<IReadOnlyList<(double U, double V)>> { new List<(double U, double V)> { (0, 0), (10, 0), (10, 5) } };
        OperationResult r = RotaryWrap.Engrave("t", Tool, m, "A", strokes, new WrapSetup(40, SurfaceFeed: 300, PassDepth: 1, Depth: 1));
        ToolMove[] feeds = r.Operation!.Moves.Where(x => x.Kind == MoveKind.Feed).Skip(1).ToArray();
        Assert.Equal(300, feeds[0].Feed, 6);              // só ao longo do eixo: igual
        double degrees = 5 / 20.0 * 180 / Math.PI;       // 14,32° por 5 mm de arco
        Assert.Equal(300 * degrees / 5, feeds[1].Feed, 4); // só girando: graus por minuto
    }

    [Fact]
    public void ForaDoCursoOuSemEixoAVoltaErro()
    {
        var strokes = RotaryWrap.TextStrokes("CASILLAS", 6);
        Assert.False(RotaryWrap.Engrave("t", Tool, Machine(-5, 5), "A", strokes, new WrapSetup(40)).Ok);
        Assert.False(RotaryWrap.Engrave("t", Tool, MachineProfile.ThreeAxis(), "A", strokes, new WrapSetup(40)).Ok);
        Assert.False(RotaryWrap.Engrave("t", Tool, Machine(), "A", [], new WrapSetup(40)).Ok);
    }

    [Theory]
    [InlineData(CamDialect.Fanuc)]
    [InlineData(CamDialect.Siemens)]
    public void ProgramaSimulaSemErroEOSiemensUsaFgroup(CamDialect d)
    {
        string text = CamPost.Generate([Text(Machine())], new PostOptions(d));
        var path = MachineExecutor.Execute(GCodeParser.Parse(text, d == CamDialect.Fanuc ? Dialect.Fanuc : Dialect.Siemens), new MachineSettings());
        Assert.True(path.Diagnostics.All(x => x.Severity != DiagnosticSeverity.Error), text);
        Assert.Equal(d == CamDialect.Siemens, text.Contains("FGROUP(X,Y,Z,A)"));
        Assert.Contains("A", text);
    }
}
