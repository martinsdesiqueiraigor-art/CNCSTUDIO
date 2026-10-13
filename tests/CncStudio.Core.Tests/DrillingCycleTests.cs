using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

public class DrillingCycleTests
{
    private static ToolPath Fanuc(string program) =>
        MachineExecutor.Execute(GCodeParser.Parse(program, Dialect.Fanuc));

    private static ToolPath Siemens(string program) =>
        MachineExecutor.Execute(GCodeParser.Parse(program, Dialect.Siemens));

    /// <summary>Profundidade mínima alcançada em cada posição XY (um valor por furo).</summary>
    private static List<(double X, double Y, double Bottom)> Holes(ToolPath path) =>
        path.Moves
            .Where(m => m.Motion == MotionMode.Linear)
            .GroupBy(m => (Math.Round(m.End.X, 3), Math.Round(m.End.Y, 3)))
            .Select(g => (g.Key.Item1, g.Key.Item2, g.Min(m => m.End.Z)))
            .ToList();

    [Fact]
    public void G81FuraCadaPosicaoERetornaAoNivelInicial()
    {
        ToolPath path = Fanuc("""
            G90 G00 X0 Y0 Z50
            G98 G81 X10 Y10 R2 Z-5 F100
            X20
            X30 Y20
            G80
            G00 X0 Y0
            """);

        var holes = Holes(path);
        Assert.Equal(3, holes.Count);
        Assert.All(holes, h => Assert.Equal(-5, h.Bottom, 6));

        // Depois do terceiro furo, volta ao nível inicial (Z50, G98)
        Move lastRetract = path.Moves.Last(m => m.LineNumber == 4);
        Assert.Equal(50, lastRetract.End.Z, 6);

        // Depois do G80, G00 não fura
        Assert.Equal(MotionMode.Rapid, path.Moves[^1].Motion);
        Assert.Empty(path.Diagnostics);
    }

    [Fact]
    public void G99RetornaAoPlanoR()
    {
        ToolPath path = Fanuc("G00 Z50\nG99 G81 X10 Y0 R3 Z-5 F100");

        Assert.Equal(3, path.Moves[^1].End.Z, 6);
    }

    [Fact]
    public void G83FuraEmBicadas()
    {
        ToolPath path = Fanuc("G00 Z20\nG99 G83 X0 Y0 R2 Z-10 Q4 F100");

        var feeds = path.Moves.Where(m => m.Motion == MotionMode.Linear).Select(m => Math.Round(m.End.Z, 3)).ToList();
        Assert.Equal(new[] { -2.0, -6.0, -10.0 }, feeds); // 2 → -2 → -6 → -10
        Assert.Equal(2, path.Moves[^1].End.Z, 6);
    }

    [Fact]
    public void G73RecuaSoAFolgaEntreBicadas()
    {
        ToolPath path = Fanuc("G00 Z20\nG99 G73 X0 Y0 R2 Z-10 Q4 F100");

        var feeds = path.Moves.Where(m => m.Motion == MotionMode.Linear).Select(m => Math.Round(m.End.Z, 3)).ToList();
        Assert.Equal(new[] { -2.0, -6.0, -10.0 }, feeds);

        // Entre bicadas não volta ao R (só sobe a folga)
        double highestBetween = path.Moves.Skip(2).Take(path.Moves.Count - 3).Max(m => m.End.Z);
        Assert.True(highestBetween < 2, $"subiu até {highestBetween}");
        Assert.Empty(path.Diagnostics);
    }

    [Theory]
    [InlineData("G84")]
    [InlineData("G85")]
    public void G84EG85SobemEmAvanco(string code)
    {
        ToolPath path = Fanuc($"G00 Z20\nG98 {code} X0 Y0 R3 Z-12 F100");

        var cut = path.Moves.Where(m => m.Motion == MotionMode.Linear).ToList();
        Assert.Equal(2, cut.Count);
        Assert.Equal(-12, cut[0].End.Z, 6);
        Assert.Equal(3, cut[1].End.Z, 6);                // sobe em avanço até R
        Assert.Equal(20, path.Moves[^1].End.Z, 6);        // G98: completa em rápido
        Assert.Equal(MotionMode.Rapid, path.Moves[^1].Motion);
        Assert.Empty(path.Diagnostics);
    }

    [Fact]
    public void G89EsperaNoFundo()
    {
        ToolPath path = Fanuc("G00 Z20\nG99 G89 X0 Y0 R2 Z-5 P800 F100");

        Assert.Equal(0.8, Assert.Single(path.Dwells).Seconds, 6);
        Assert.Equal(2, path.Moves[^1].End.Z, 6);
    }

    [Fact]
    public void G91ComKRepeteOsFurosComPasso()
    {
        ToolPath path = Fanuc("G00 X0 Y0 Z10\nG91 G99 G81 X10 R-8 Z-5 K3 F100");

        var holes = Holes(path);
        Assert.Equal(new[] { 10.0, 20.0, 30.0 }, holes.Select(h => h.X));
        Assert.All(holes, h => Assert.Equal(-3, h.Bottom, 6)); // R: 10-8 = 2; fundo: 2-5 = -3
    }

    [Fact]
    public void Siemens_Cycle81NaPosicaoAtual()
    {
        ToolPath path = Siemens("G0 X10 Y20 Z50\nCYCLE81(10,0,2,-5)");

        var holes = Holes(path);
        var hole = Assert.Single(holes);
        Assert.Equal(10, hole.X, 6);
        Assert.Equal(-5, hole.Bottom, 6);
        Assert.Equal(10, path.Moves[^1].End.Z, 6); // RTP
    }

    [Fact]
    public void Siemens_McallFuraEmCadaPosicao()
    {
        ToolPath path = Siemens("""
            G0 Z50
            MCALL CYCLE81(10,0,2,,8)
            G0 X10 Y10
            X20
            MCALL
            G0 X30
            """);

        var holes = Holes(path);
        Assert.Equal(2, holes.Count);
        Assert.All(holes, h => Assert.Equal(-8, h.Bottom, 6)); // DPR=8 a partir de RFP=0
    }

    [Fact]
    public void Siemens_Cycle83ComDegressao()
    {
        // RTP=10, RFP=0, SDIS=1, DP=-12, DPR vazio, FDEP vazio, FDPR=5, DAM=1, ..., VARI=1
        ToolPath path = Siemens("G0 X0 Y0 Z20\nCYCLE83(10,0,1,-12,,,5,1,,,,1)");

        var depths = path.Moves.Where(m => m.Motion == MotionMode.Linear && m.End.Z < 0)
            .Select(m => Math.Round(m.End.Z, 3)).ToList();
        Assert.Equal(new[] { -5.0, -9.0, -12.0 }, depths); // bicadas de 5, 4, 3
    }

    [Fact]
    public void CicloSemRouZEhErro()
    {
        ToolPath path = Fanuc("G81 X10 Y10 F100");

        Assert.Contains(path.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }
}
