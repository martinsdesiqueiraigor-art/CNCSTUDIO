using CncStudio.Core.Cam;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

public class LatheThreadCamTests
{
    private static readonly LatheTool Tool = new(3, "ROSCA 60");
    private static readonly MachineSettings Settings = new() { Machine = MachineKind.Lathe };

    private static (ToolPath Path, string Text) Run(ThreadingParams p, CamDialect dialect)
    {
        LatheResult r = LatheOperations.Threading(Tool, 5, 20, p);
        Assert.True(r.Ok, r.Error);
        string text = LathePost.Generate([r.Operation!], new LathePostOptions(dialect));
        ToolPath path = MachineExecutor.Execute(GCodeParser.Parse(text, dialect == CamDialect.Fanuc ? Dialect.Fanuc : Dialect.Siemens), Settings);
        Assert.True(path.Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error),
            string.Join("\n", path.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)) + "\n" + text);
        return (path, text);
    }

    private static List<Move> CutMoves(ToolPath path) => path.Moves.Where(m => !m.IsRapid).ToList();

    [Theory]
    [InlineData(CamDialect.Fanuc)]
    [InlineData(CamDialect.Siemens)]
    public void RoscaExternaChegaNaRaizENuncaPassaDela(CamDialect dialect)
    {
        var p = new ThreadingParams(false, 20, 1.5, 0, -18);
        var (path, text) = Run(p, dialect);
        double root = 10 - 0.6134 * 1.5;
        List<Move> cuts = CutMoves(path);
        Assert.NotEmpty(cuts);
        Assert.Equal(root, cuts.Min(m => Math.Min(m.Start.X, m.End.X)), 3);
        Assert.True(cuts.Max(m => Math.Max(m.Start.X, m.End.X)) <= 10 + 1e-6, text);
        // Corre de Z = 3 (entrada) até Z = -19.5 (saída)
        Assert.Equal(-19.5, cuts.Min(m => Math.Min(m.Start.Z, m.End.Z)), 3);
        Assert.True(cuts.Count >= 6);
    }

    [Theory]
    [InlineData(CamDialect.Fanuc)]
    [InlineData(CamDialect.Siemens)]
    public void RoscaInternaFicaEntreFuroERaiz(CamDialect dialect)
    {
        var p = new ThreadingParams(true, 24, 3, -2, -20);
        var (path, text) = Run(p, dialect);
        double crest = 12 - 0.5413 * 3;
        List<Move> cuts = CutMoves(path);
        Assert.NotEmpty(cuts);
        Assert.Equal(12, cuts.Max(m => Math.Max(m.Start.X, m.End.X)), 3);
        Assert.True(cuts.Min(m => Math.Min(m.Start.X, m.End.X)) >= crest - 1e-6, text);
    }

    [Fact]
    public void PostFanucUsaG76EGiroFixo()
    {
        var (_, text) = Run(new ThreadingParams(false, 20, 1.5, 0, -18, Rpm: 500), CamDialect.Fanuc);
        Assert.Contains("G97 S500 M3", text);
        Assert.Contains("G76 P020060", text);
        Assert.Contains("F1.5", text);
        Assert.DoesNotContain("G96", text);
    }

    [Fact]
    public void PostSiemensUsaCycle97()
    {
        var (_, text) = Run(new ThreadingParams(false, 20, 1.5, 0, -18), CamDialect.Siemens);
        Assert.Contains("CYCLE97(1.5", text);
    }

    [Fact]
    public void VoltaAoGiroConstanteDepoisDaRosca()
    {
        LatheResult turn = LatheOperations.Facing(Tool, 5, 20, 40, 0, -1, 1, 0.2);
        LatheResult thread = LatheOperations.Threading(Tool, 5, 20, new ThreadingParams(false, 20, 1.5, 0, -18));
        string text = LathePost.Generate([thread.Operation!, turn.Operation!], new LathePostOptions(CamDialect.Fanuc));
        Assert.Contains("G97 S600 M3", text);
        Assert.Contains("G96 S", text);
    }

    [Fact]
    public void RecusaParametrosInvalidos()
    {
        Assert.False(LatheOperations.Threading(Tool, 5, 20, new ThreadingParams(false, 20, 0, 0, -18)).Ok);
        Assert.False(LatheOperations.Threading(Tool, 5, 20, new ThreadingParams(false, 20, 1.5, -18, 0)).Ok);
        Assert.False(LatheOperations.Threading(Tool, 5, 20, new ThreadingParams(false, 20, 1.5, 0, -18, Rpm: 0)).Ok);
        Assert.False(LatheOperations.Threading(Tool, 5, 20, new ThreadingParams(true, 2, 3, 0, -10)).Ok);
    }
}
