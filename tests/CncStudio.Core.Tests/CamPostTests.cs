using CncStudio.Core.Cam;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

public class CamPostTests
{
    private static readonly CamTool Tool10 = new(1, 10, 3000, 500, 200);
    private static readonly CamTool Tool6 = new(2, 6, 4000, 400, 150);
    private static readonly CamLevels Levels = new(SafeZ: 50, ClearanceZ: 2, TopZ: 0, BottomZ: -6, StepDown: 3);

    private static ToolPath Simulate(string program, CamDialect dialect, double toolDiameter = 0)
    {
        MachineSettings settings = toolDiameter > 0 ? new MachineSettings { ToolDiameterOf = _ => toolDiameter } : new MachineSettings();
        return MachineExecutor.Execute(GCodeParser.Parse(program, dialect == CamDialect.Fanuc ? Dialect.Fanuc : Dialect.Siemens), settings);
    }

    private static string Post(CamDialect dialect, params CamOperation[] ops) =>
        CamPost.Generate(ops, new PostOptions(dialect));

    private static void AssertNoErrors(ToolPath path, string program) =>
        Assert.True(path.Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error), string.Join("\n", path.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)) + "\n" + program);

    /// <summary>O percurso simulado de corte precisa ser igual ao gerado, movimento a movimento.</summary>
    private static void AssertSameCuts(CamOperation op, ToolPath path)
    {
        var expected = op.Moves.Where(m => m.Kind != MoveKind.Rapid).ToList();
        var actual = path.Moves.Where(m => !m.IsRapid).ToList();
        Assert.Equal(expected.Count, actual.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].X, actual[i].End.X, 2);
            Assert.Equal(expected[i].Y, actual[i].End.Y, 2);
            Assert.Equal(expected[i].Z, actual[i].End.Z, 2);
            Assert.Equal(expected[i].IsArc, actual[i].IsArc);
            if (expected[i].IsArc)
            {
                Assert.Equal(expected[i].Kind == MoveKind.ArcCcw ? MotionMode.CounterClockwiseArc : MotionMode.ClockwiseArc, actual[i].Motion);
                Assert.Equal(expected[i].Center.X, actual[i].CenterU, 2);
                Assert.Equal(expected[i].Center.Y, actual[i].CenterV, 2);
            }
        }
    }

    [Theory]
    [InlineData(CamDialect.Fanuc)]
    [InlineData(CamDialect.Siemens)]
    public void FaceamentoSimuladoIgualAoGerado(CamDialect dialect)
    {
        CamOperation op = CamOperations.Facing(Tool10, Levels, 0, 0, 60, 40, 70).Operation!;
        string program = Post(dialect, op);
        ToolPath path = Simulate(program, dialect);
        AssertNoErrors(path, program);
        AssertSameCuts(op, path);
        Assert.Equal(-6, path.CuttingBounds.Min.Z, 3);
    }

    [Theory]
    [InlineData(CamDialect.Fanuc)]
    [InlineData(CamDialect.Siemens)]
    public void ContornoComArcosSimuladoIgualAoGerado(CamDialect dialect)
    {
        CamOperation op = CamOperations.Contour(Contour.Rectangle(0, 0, 40, 20), Tool10, Levels, new ContourParams(ContourSide.Outside)).Operation!;
        string program = Post(dialect, op);
        ToolPath path = Simulate(program, dialect);
        AssertNoErrors(path, program);
        AssertSameCuts(op, path);
    }

    [Theory]
    [InlineData(CamDialect.Fanuc)]
    [InlineData(CamDialect.Siemens)]
    public void CirculoCompletoEHeliceFuncionam(CamDialect dialect)
    {
        CamOperation op = CamOperations.Pocket(Contour.Circle(new Point2(0, 0), 20), Tool10, Levels, new PocketParams()).Operation!;
        string program = Post(dialect, op);
        ToolPath path = Simulate(program, dialect);
        AssertNoErrors(path, program);
        AssertSameCuts(op, path);
    }

    [Fact]
    public void CompensacaoFanucDesloncaOPercursoParaFora()
    {
        CamOperation op = CamOperations.Contour(Contour.Rectangle(0, 0, 40, 20), Tool10, Levels, new ContourParams(ContourSide.Outside, ControllerComp: true, LeadRadius: 8)).Operation!;
        string program = Post(CamDialect.Fanuc, op);
        Assert.Contains("G41 D1", program);
        Assert.Contains("G40", program);
        ToolPath path = Simulate(program, CamDialect.Fanuc, toolDiameter: 10);
        AssertNoErrors(path, program);

        // no fundo, nenhum ponto cortante entra no retângulo e o percurso encosta a 5 mm dele
        double min = double.MaxValue;
        foreach (Move m in path.Moves.Where(m => !m.IsRapid && Math.Abs(m.End.Z + 6) < 1e-6))
        {
            double dx = Math.Max(Math.Max(0 - m.End.X, m.End.X - 40), 0);
            double dy = Math.Max(Math.Max(0 - m.End.Y, m.End.Y - 20), 0);
            min = Math.Min(min, Math.Sqrt((dx * dx) + (dy * dy)));
        }

        Assert.True(min >= 5 - 0.05, $"a ferramenta entrou na peça (distância mínima {min:F3})");
        Assert.True(min <= 5 + 0.05, $"a compensação foi para o lado errado ou não aplicou (distância mínima {min:F3})");
    }

    [Fact]
    public void CompensacaoSiemensUsaG41SemD()
    {
        CamOperation op = CamOperations.Contour(Contour.Rectangle(0, 0, 40, 20), Tool10, Levels, new ContourParams(ContourSide.Outside, ControllerComp: true, LeadRadius: 8)).Operation!;
        string program = Post(CamDialect.Siemens, op);
        Assert.Contains("G41", program);
        Assert.DoesNotContain("G41 D", program);
        Assert.Contains("D1", program);
        AssertNoErrors(Simulate(program, CamDialect.Siemens, 10), program);
    }

    [Fact]
    public void FuracaoFanucUsaCicloFixo()
    {
        Point2[] pts = [new(10, 10), new(40, 10), new(25, 30)];
        CamOperation op = CamOperations.Drilling(Tool6, 50, pts, DrillCycleKind.Peck, 0, -12, 2, peck: 4).Operation!;
        string program = Post(CamDialect.Fanuc, op);
        Assert.Contains("G83", program);
        Assert.Contains("Q4.", program);
        Assert.Contains("G80", program);
        ToolPath path = Simulate(program, CamDialect.Fanuc);
        AssertNoErrors(path, program);
        Assert.Equal(-12, path.CuttingBounds.Min.Z, 3);
        foreach (Point2 p in pts)
        {
            Assert.Contains(path.Moves, m => !m.IsRapid && Math.Abs(m.End.X - p.X) < 1e-3 && Math.Abs(m.End.Y - p.Y) < 1e-3 && Math.Abs(m.End.Z + 12) < 1e-3);
        }
    }

    [Theory]
    [InlineData(DrillCycleKind.Drill)]
    [InlineData(DrillCycleKind.Dwell)]
    [InlineData(DrillCycleKind.Peck)]
    [InlineData(DrillCycleKind.ChipBreak)]
    public void FuracaoSiemensEFanucTodosOsCiclosSimulam(DrillCycleKind kind)
    {
        Point2[] pts = [new(10, 10), new(40, 10)];
        CamOperation op = CamOperations.Drilling(Tool6, 50, pts, kind, 0, -12, 2, peck: 4, dwellSeconds: 0.5).Operation!;
        foreach (CamDialect dialect in new[] { CamDialect.Fanuc, CamDialect.Siemens })
        {
            string program = Post(dialect, op);
            ToolPath path = Simulate(program, dialect);
            AssertNoErrors(path, program);
            Assert.Equal(-12, path.CuttingBounds.Min.Z, 3);
            foreach (Point2 p in pts)
            {
                Assert.Contains(path.Moves, m => Math.Abs(m.End.X - p.X) < 1e-3 && Math.Abs(m.End.Y - p.Y) < 1e-3 && Math.Abs(m.End.Z + 12) < 1e-3);
            }
        }
    }

    [Fact]
    public void ProgramaComVariasOperacoesTrocaDeFerramenta()
    {
        CamOperation face = CamOperations.Facing(Tool10, Levels, 0, 0, 60, 40, 70).Operation!;
        CamOperation pocket = CamOperations.Pocket(Contour.Rectangle(10, 10, 40, 20), Tool6, Levels, new PocketParams()).Operation!;
        CamOperation holes = CamOperations.Drilling(Tool6, 50, [new(15, 15)], DrillCycleKind.Drill, 0, -6, 2).Operation!;
        foreach (CamDialect dialect in new[] { CamDialect.Fanuc, CamDialect.Siemens })
        {
            string program = Post(dialect, face, pocket, holes);
            ToolPath path = Simulate(program, dialect);
            AssertNoErrors(path, program);
            Assert.Equal(new[] { 1, 2 }, path.Moves.Select(m => m.Tool).Where(t => t > 0).Distinct().OrderBy(t => t).ToArray());
            Assert.EndsWith("M30\n", dialect == CamDialect.Siemens ? program : program.Replace("%\n", string.Empty));
            Assert.Equal(2, program.Split('\n').Count(l => l.StartsWith("T", StringComparison.Ordinal))); // só 2 trocas (a 3ª operação usa a mesma ferramenta)
        }
    }

    [Fact]
    public void FanucUsaPontoDecimalESiemensNao()
    {
        CamOperation op = CamOperations.Facing(Tool10, Levels, 0, 0, 60, 40, 70).Operation!;
        string fanuc = Post(CamDialect.Fanuc, op);
        string siemens = Post(CamDialect.Siemens, op);
        Assert.Contains("Z50.", fanuc);
        Assert.Contains("Z50", siemens);
        Assert.DoesNotContain("Z50.", siemens);
        Assert.StartsWith("%\nO1001", fanuc);
        Assert.StartsWith("; ", siemens);
    }

    [Fact]
    public void NomeComParentesesNaoQuebraOComentario()
    {
        CamOperation op = CamOperations.Facing(Tool10, Levels, 0, 0, 60, 40, 70).Operation!;
        string program = CamPost.Generate([op], new PostOptions(CamDialect.Fanuc, ProgramName: "Peça (teste); x"));
        AssertNoErrors(Simulate(program, CamDialect.Fanuc), program);
    }

    [Fact]
    public void TodoMovimentoDeCorteTemAvancoNoTexto()
    {
        CamOperation op = CamOperations.Contour(Contour.Rectangle(0, 0, 40, 20), Tool10, Levels, new ContourParams(ContourSide.Outside)).Operation!;
        foreach (CamDialect dialect in new[] { CamDialect.Fanuc, CamDialect.Siemens })
        {
            string program = Post(dialect, op);
            string[] lines = program.Split('\n');
            int firstCut = Array.FindIndex(lines, l => l.StartsWith("G1", StringComparison.Ordinal));
            Assert.True(firstCut >= 0);
            Assert.Contains("F200", lines[firstCut]); // descida em Z
            Assert.Contains(lines, l => l.Contains("F500")); // avanço no plano, escrito quando muda
            Assert.All(path(program), l => Assert.DoesNotContain("F" + double.NaN, l));
        }

        static IEnumerable<string> path(string text) => text.Split('\n');
    }
}
