using CncStudio.Core.Cam;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

public class LatheGrooveCamTests
{
    private static readonly LatheTool Tool = new(4, "CANAL 3MM");
    private static readonly MachineSettings Settings = new() { Machine = MachineKind.Lathe };

    private static (List<Move> Cuts, string Text) Run(GroovingParams p, CamDialect dialect)
    {
        LatheResult r = LatheOperations.Grooving(Tool, 50, 40, p);
        Assert.True(r.Ok, r.Error);
        string text = LathePost.Generate([r.Operation!], new LathePostOptions(dialect));
        ToolPath path = MachineExecutor.Execute(GCodeParser.Parse(text, dialect == CamDialect.Fanuc ? Dialect.Fanuc : Dialect.Siemens), Settings);
        Assert.True(path.Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error),
            string.Join("\n", path.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message)) + "\n" + text);
        return (path.Moves.Where(m => !m.IsRapid).ToList(), text);
    }

    /// <summary>Faixas de Z cobertas pela ferramenta (aresta de maior Z = Z do programa) em pontos com raio na altura do fundo ou mais fundo.</summary>
    private static bool Covers(List<Move> cuts, double toolWidth, double zFrom, double zTo, Func<double, bool> deepEnough)
    {
        var intervals = new List<(double Lo, double Hi)>();
        foreach (Move m in cuts)
        {
            for (int i = 0; i <= 100; i++)
            {
                double t = i / 100.0;
                double x = m.Start.X + (m.End.X - m.Start.X) * t, zz = m.Start.Z + (m.End.Z - m.Start.Z) * t;
                if (deepEnough(x))
                {
                    intervals.Add((zz - toolWidth, zz));
                }
            }
        }

        double z = zFrom;                       // cobre de zFrom (alto) descendo até zTo
        foreach (var iv in intervals.OrderByDescending(i => i.Hi))
        {
            if (iv.Hi >= z - 1e-6 && iv.Lo < z)
            {
                z = iv.Lo;
            }
        }

        return z <= zTo + 1e-6;
    }

    [Theory]
    [InlineData(CamDialect.Fanuc)]
    [InlineData(CamDialect.Siemens)]
    public void CanalExternoChegaNoFundoSemSairDasParedes(CamDialect dialect)
    {
        var p = new GroovingParams(false, 30, 22, -20, 8, 3);
        var (cuts, text) = Run(p, dialect);
        Assert.Equal(11, cuts.Min(m => Math.Min(m.Start.X, m.End.X)), 3);          // fundo Ø22, nunca abaixo
        double zTop = cuts.Where(m => Math.Min(m.Start.X, m.End.X) < 14.99).Max(m => Math.Max(m.Start.Z, m.End.Z));
        double zBottom = cuts.Where(m => Math.Min(m.Start.X, m.End.X) < 14.99).Min(m => Math.Min(m.Start.Z, m.End.Z));
        Assert.Equal(-20, zTop, 3);                // aresta alta na parede de cima
        Assert.Equal(-28, zBottom - 3, 3);         // aresta baixa (Z − largura) na parede de baixo
        Assert.True(Covers(cuts, 3, -20, -28, x => x <= 11.0 + 1e-6), text);
    }

    [Theory]
    [InlineData(CamDialect.Fanuc)]
    [InlineData(CamDialect.Siemens)]
    public void CanalInternoVaiParaForaDoFuro(CamDialect dialect)
    {
        var p = new GroovingParams(true, 20, 26, -15, 6, 3);
        var (cuts, text) = Run(p, dialect);
        Assert.Equal(13, cuts.Max(m => Math.Max(m.Start.X, m.End.X)), 3);
        Assert.True(Covers(cuts, 3, -15, -21, x => x >= 13.0 - 1e-6), text);
    }

    [Fact]
    public void LarguraIgualAFerramentaFazUmMergulho()
    {
        var (cuts, _) = Run(new GroovingParams(false, 30, 24, -10, 3, 3), CamDialect.Fanuc);
        Assert.Equal(12, cuts.Min(m => Math.Min(m.Start.X, m.End.X)), 3);
        Assert.All(cuts, m => Assert.True(m.Start.Z <= -10 + 1e-6 && m.End.Z <= -10 + 1e-6 && m.Start.Z >= -10 - 1e-6));
    }

    [Fact]
    public void RecusaCanalMaisEstreitoQueAFerramentaOuFundoErrado()
    {
        Assert.False(LatheOperations.Grooving(Tool, 50, 40, new GroovingParams(false, 30, 22, -20, 2, 3)).Ok);
        Assert.False(LatheOperations.Grooving(Tool, 50, 40, new GroovingParams(false, 30, 34, -20, 8, 3)).Ok);
        Assert.False(LatheOperations.Grooving(Tool, 50, 40, new GroovingParams(true, 30, 22, -20, 8, 3)).Ok);
    }
}
