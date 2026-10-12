using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Parsing;
using CncStudio.Core.Statistics;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

public class LatheTests
{
    private static readonly MachineSettings Lathe = new() { Machine = MachineKind.Lathe };

    private static ToolPath Fanuc(string program, MachineSettings? settings = null) =>
        MachineExecutor.Execute(GCodeParser.Parse(program, Dialect.Fanuc), settings ?? Lathe);

    private static ToolPath Siemens(string program, MachineSettings? settings = null) =>
        MachineExecutor.Execute(GCodeParser.Parse(program, Dialect.Siemens), settings ?? Lathe);

    private static string Sample(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", name));

    private static void AssertNoErrors(ToolPath path) =>
        Assert.DoesNotContain(path.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);

    // ───────────────────────── Detecção ─────────────────────────

    [Theory]
    [InlineData("torno/EXEMPLO_TORNO.nc", Dialect.Fanuc, MachineKind.Lathe)]
    [InlineData("torno/EXEMPLO_TORNO.mpf", Dialect.Siemens, MachineKind.Lathe)]
    [InlineData("EXEMPLO.nc", Dialect.Fanuc, MachineKind.Mill)]
    [InlineData("EXEMPLO.mpf", Dialect.Siemens, MachineKind.Mill)]
    [InlineData("macros/CHAVETA.mpf", Dialect.Siemens, MachineKind.Mill)]
    public void DetectaTornoOuFresaPeloPrograma(string file, Dialect dialect, MachineKind expected)
    {
        ParseResult parse = GCodeParser.Parse(Sample(file), dialect);
        Assert.Equal(expected, MachineDetector.Detect(parse));
    }

    [Fact]
    public void FresaComYNaoEhTornoMesmoComG96()
    {
        ParseResult parse = GCodeParser.Parse("G96 S100\nG01 X10 Y10 F100", Dialect.Fanuc);
        Assert.Equal(MachineKind.Mill, MachineDetector.Detect(parse));
    }

    // ───────────────────────── Coordenadas ─────────────────────────

    [Fact]
    public void XEmDiametroViraRaioEUWSaoIncrementais()
    {
        ToolPath path = Fanuc("""
            G00 X50 Z2
            G01 X40 Z-10 F0.2
            U-10 W-5
            """);

        Assert.Equal(25, path.Moves[0].End.X, 6);  // X50 = raio 25
        Assert.Equal(20, path.Moves[1].End.X, 6);
        Assert.Equal(-10, path.Moves[1].End.Z, 6);
        Assert.Equal(15, path.Moves[2].End.X, 6);  // U-10 (diâmetro) = −5 no raio
        Assert.Equal(-15, path.Moves[2].End.Z, 6);
        Assert.Equal(0, path.Moves[2].End.Y, 6);
    }

    [Fact]
    public void DiamofDoSiemensUsaXEmRaio()
    {
        ToolPath path = Siemens("""
            DIAMOF
            G0 X30 Z0
            DIAMON
            G0 X30
            """);

        Assert.Equal(30, path.Moves[0].End.X, 6);
        Assert.Equal(15, path.Moves[1].End.X, 6);
    }

    [Fact]
    public void ArcoNoPlanoZXComRaio()
    {
        ToolPath path = Fanuc("""
            G00 X20 Z-20
            G02 X30 Z-25 R5 F0.1
            """);

        Move arc = path.Moves[1];
        Assert.True(arc.IsArc);
        Assert.Equal(Math.PI * 5 / 2, arc.Length, 6); // quarto de círculo de raio 5
        Assert.Equal(15, arc.End.X, 6);
    }

    [Fact]
    public void FerramentaT0101EhAFerramenta1SemM06()
    {
        ToolPath path = Fanuc("""
            T0101
            G00 X50 Z2
            T0303
            G00 X60
            """);

        Assert.Equal(1, path.Moves[0].Tool);
        Assert.Equal(3, path.Moves[1].Tool);
    }

    // ───────────────────────── Avanço e rotação ─────────────────────────

    [Fact]
    public void AvancoPorRotacaoUsaARotacao()
    {
        ToolPath path = Fanuc("""
            G97 S1000 M03
            G00 X40 Z2
            G99 G01 Z-10 F0.2
            G98 G01 Z-20 F100
            """);

        Assert.Equal(200, path.Moves[1].Feed, 6); // 0,2 mm/rot × 1000 rpm
        Assert.Equal(100, path.Moves[2].Feed, 6); // G98: mm/min
    }

    [Fact]
    public void VelocidadeDeCorteConstanteDependeDoDiametroELimite()
    {
        ToolPath path = Fanuc("""
            G50 S2000
            G96 S100 M03
            G00 X20 Z2
            G01 Z-10 F0.1
            G00 X2
            G01 Z-20
            """);

        double rpmAt20 = 100 * 1000 / (Math.PI * 20);
        Assert.Equal(0.1 * rpmAt20, path.Moves[1].Feed, 3);
        Assert.Equal(0.1 * 2000, path.Moves[3].Feed, 3); // Ø2 passaria de 2000 rpm: limitado pelo G50 S
    }

    [Fact]
    public void AvancoPorRotacaoSemRotacaoAvisa()
    {
        ToolPath path = Fanuc("G00 X40 Z2\nG01 Z-10 F0.2");
        Assert.Contains(path.Diagnostics, d => d.Message.Contains("sem rotação"));
    }

    // ───────────────────────── G90 / G94 / G92 ─────────────────────────

    [Fact]
    public void G90RepeteOCicloAcadaNovoX()
    {
        ToolPath path = Fanuc("""
            G97 S1000
            G00 X52 Z2
            G90 X46 Z-30 F0.2
            X42
            G00 X100
            """);

        // 1 aproximação + 2 ciclos × 4 movimentos + 1 saída
        Assert.Equal(10, path.Moves.Count);
        Move firstCut = path.Moves[2];
        Assert.Equal(MotionMode.Linear, firstCut.Motion);
        Assert.Equal(23, firstCut.End.X, 6);
        Assert.Equal(-30, firstCut.End.Z, 6);
        Assert.Equal(21, path.Moves[6].End.X, 6);   // X42: Z-30 mantido do ciclo anterior
        Assert.Equal(-30, path.Moves[6].End.Z, 6);
        Assert.Equal(26, path.Moves[8].End.X, 6);   // volta ao ponto inicial
        Assert.Equal(2, path.Moves[8].End.Z, 6);
    }

    [Fact]
    public void G94FaceiaEmX()
    {
        ToolPath path = Fanuc("""
            G97 S1000
            G00 X52 Z2
            G94 X10 Z-2 F0.2
            """);

        Assert.Equal(-2, path.Moves[1].End.Z, 6); // rápido em Z
        Assert.Equal(5, path.Moves[2].End.X, 6);  // corte em X até Ø10
        Assert.Equal(MotionMode.Linear, path.Moves[2].Motion);
    }

    [Fact]
    public void G92RoscaUsaPassoPorRotacao()
    {
        ToolPath path = Fanuc("""
            G97 S500
            G98
            G00 X22 Z5
            G92 X19 Z-20 F1.5
            X18.5
            """);

        Move thread = path.Moves[2];
        Assert.Equal(1.5 * 500, thread.Feed, 6); // mesmo em G98 a rosca usa o passo por rotação
        Assert.Equal(9.5, thread.End.X, 6);
        Assert.Equal(9.25, path.Moves[6].End.X, 6);
    }

    // ───────────────────────── G71 / G70 ─────────────────────────

    private const string G71Program = """
        G97 S1000
        G00 X52 Z2
        G71 U2 R0.5
        G71 P10 Q20 U0 W0 F0.2
        N10 G00 X20
        G01 Z-20 F0.1
        X40
        Z-40
        N20 X52
        G70 P10 Q20
        G00 X100 Z100
        """;

    [Fact]
    public void G71DesbastaEmPassesSemInvadirOPerfil()
    {
        ToolPath path = Fanuc(G71Program);
        AssertNoErrors(path);

        // Passes de desbaste (linha 4): cortes em Z nos níveis 24, 22, ..., 12 (raio)
        var cuts = path.Moves
            .Where(m => m.LineNumber == 4 && m.Motion == MotionMode.Linear && Math.Abs(m.Start.X - m.End.X) < 1e-9)
            .ToList();
        double[] levels = cuts.Select(m => Math.Round(m.End.X, 6)).Distinct().ToArray();
        Assert.Equal([24.0, 22, 20, 18, 16, 14, 12], levels.Take(7));

        // Nenhum corte entra na peça: até Z-20 o raio mínimo é 10; até Z-40, 20
        foreach (Move m in path.Moves.Where(m => m.LineNumber == 4))
        {
            double r = Math.Min(m.Start.X, m.End.X);
            double z = Math.Min(m.Start.Z, m.End.Z);
            Assert.True(z >= -40 - 1e-6, $"Z {z} além do perfil");
            Assert.True(r >= (z < -20 - 1e-6 ? 20 : 10) - 1e-6, $"raio {r} dentro do perfil em Z {z}");
        }
    }

    [Fact]
    public void G71PulaOPerfilEG70FazOAcabamento()
    {
        ToolPath path = Fanuc(G71Program);

        // Linhas do perfil (5 a 9) só aparecem no G70, com o avanço do perfil (F0.1 × 1000)
        var finishing = path.Moves.Where(m => m.LineNumber is >= 5 and <= 9).ToList();
        Assert.NotEmpty(finishing);
        Assert.Contains(finishing, m => m.LineNumber == 6 && Math.Abs(m.Feed - 100) < 1e-6);
        int firstFinish = path.Moves.ToList().FindIndex(m => m.LineNumber is >= 5 and <= 9);
        Assert.True(path.Moves.Take(firstFinish).All(m => m.LineNumber == 4 || m.LineNumber == 2));

        // Depois do G70, volta ao ponto inicial (X52 Z2) antes do G00 X100 Z100
        Move last = path.Moves[^1];
        Assert.Equal(11, last.LineNumber);
        Assert.Equal(26, last.Start.X, 6);
        Assert.Equal(2, last.Start.Z, 6);
    }

    [Fact]
    public void G71ComSobremetalDeixaMaterial()
    {
        ToolPath path = Fanuc(G71Program.Replace("U0 W0", "U0.4 W0.1"));
        AssertNoErrors(path);

        double minRadius = path.Moves.Where(m => m.LineNumber == 4 && !m.IsRapid).Min(m => Math.Min(m.Start.X, m.End.X));
        Assert.Equal(10.2, minRadius, 6); // Δu 0,4 no diâmetro = 0,2 no raio
    }

    [Fact]
    public void G71SemBlocoDoPerfilDaErro()
    {
        ToolPath path = Fanuc("G00 X52 Z2\nG71 U2 R0.5\nG71 P10 Q20 U0 W0 F0.2\nN10 G00 X20\nG01 Z-10");
        Assert.Contains(path.Diagnostics, d => d.Severity == DiagnosticSeverity.Error && d.Message.Contains("N20"));
    }

    [Fact]
    public void G72DesbastaEmPassesDeFace()
    {
        ToolPath path = Fanuc("""
            G97 S1000
            G00 X52 Z2
            G72 W2 R0.5
            G72 P10 Q20 U0 W0 F0.2
            N10 G00 Z-10
            G01 X20 F0.1
            Z0
            N20 X0
            G00 X100 Z100
            """);

        AssertNoErrors(path);

        // Cortes em X (face) em Z = 0, −2, ..., −8; nenhum abaixo de Z−10 nem dentro do Ø20 abaixo de Z0
        var cuts = path.Moves.Where(m => m.LineNumber == 4 && m.Motion == MotionMode.Linear && Math.Abs(m.Start.Z - m.End.Z) < 1e-9).ToList();
        Assert.NotEmpty(cuts);
        Assert.All(cuts, c => Assert.True(c.End.Z >= -10 - 1e-6));
        Assert.All(cuts.Where(c => c.End.Z < -1e-6), c => Assert.True(c.End.X >= 10 - 1e-6));
    }

    // ───────────────────────── G76 ─────────────────────────

    [Fact]
    public void ProfundidadesDoG76CrescemComRaizQuadrada()
    {
        List<double> depths = MachineExecutor.ThreadDepths(0.92, 0.3, 0.05, 0.02, 1);

        Assert.Equal(0.3, depths[0], 6);
        Assert.Equal(0.3 * Math.Sqrt(2), depths[1], 6);
        Assert.Equal(0.92, depths[^1], 6);
        Assert.Equal(0.9, depths[^2], 6); // último de desbaste: altura − sobremetal
        for (int i = 1; i < depths.Count; i++)
        {
            Assert.True(depths[i] >= depths[i - 1]);
        }
    }

    [Fact]
    public void G76GeraUmPasseDeRoscaPorProfundidade()
    {
        ToolPath path = Fanuc("""
            G97 S800
            G00 X22 Z5
            G76 P020060 Q50 R0.02
            G76 X18.16 Z-18 P920 Q300 F1.5
            """);

        AssertNoErrors(path);
        int expected = MachineExecutor.ThreadDepths(0.92, 0.3, 0.05, 0.02, 2).Count;
        var threads = path.Moves.Where(m => m.LineNumber == 4 && m.Motion == MotionMode.Linear).ToList();
        Assert.Equal(expected, threads.Count);
        Assert.All(threads, t => Assert.Equal(1.5 * 800, t.Feed, 6));
        Assert.Equal(9.08, threads[^1].End.X, 6); // raiz Ø18,16
        Assert.Equal(-18, threads[^1].End.Z, 6);
        Assert.Equal(9.08 + 0.92 - 0.3, threads[0].End.X, 6);
    }

    // ───────────────────────── Siemens ─────────────────────────

    [Fact]
    public void Cycle95ComRotulosDesbastaEAcaba()
    {
        ToolPath path = Siemens("""
            G95 G97 S1000
            G0 X52 Z2
            CYCLE95("INICIO:FIM", 2, 0, 0, , 0.2, 0.1, 0.1, 9, , , 1)
            G0 X100 Z100
            M30
            INICIO:
            G1 X20 Z0
            Z-20
            X40
            Z-40
            X52
            FIM:
            """);

        AssertNoErrors(path);
        var cycle = path.Moves.Where(m => m.LineNumber == 3).ToList();
        Assert.Contains(cycle, m => !m.IsRapid && Math.Abs(m.End.X - 24) < 1e-9); // 1º passe a raio 24
        Assert.All(cycle.Where(m => !m.IsRapid), m =>
        {
            double z = Math.Min(m.Start.Z, m.End.Z);
            double r = Math.Min(m.Start.X, m.End.X);
            Assert.True(z >= -40 - 1e-6);
            Assert.True(r >= (z < -20 - 1e-6 ? 20 : 10) - 1e-6, $"raio {r} em Z {z}");
        });

        // Acabamento (FF3 = 0,1 mm/rot): segue o contorno (com as linhas dele), raio 10 entre Z0 e Z-20
        Assert.Contains(path.Moves, m => m.LineNumber == 8 && !m.IsRapid && Math.Abs(m.End.X - 10) < 1e-9 && Math.Abs(m.End.Z + 20) < 1e-9 && Math.Abs(m.Feed - 100) < 1e-6);

        // Não há aviso de código inalcançável: o contorno foi usado pelo ciclo
        Assert.DoesNotContain(path.Diagnostics, d => d.Message.Contains("nunca"));
    }

    [Fact]
    public void Cycle95ComSubprogramaExterno()
    {
        var settings = Lathe with
        {
            LoadSubprogram = name => name == "KONTUR" ? "G1 X20 Z0\nZ-20\nX40\nM17" : null,
        };

        ToolPath path = Siemens("""
            G95 G97 S1000
            G0 X44 Z2
            CYCLE95("KONTUR", 2, 0, 0, , 0.2, 0.1, 0.1, 1)
            """, settings);

        AssertNoErrors(path);
        Assert.Contains(path.Moves, m => !m.IsRapid && Math.Abs(m.End.X - 20) < 1e-9);
    }

    [Fact]
    public void Cycle97FazNrcPassesMaisAcabamentoEVazio()
    {
        ToolPath path = Siemens("""
            G95 G97 S800
            G0 X24 Z5
            CYCLE97(1.5, , 0, -18, 20, 20, 3, 2, 0.92, 0.05, 30, 0, 6, 1, 3, 1)
            """);

        AssertNoErrors(path);
        var threads = path.Moves.Where(m => m.LineNumber == 3 && m.Motion == MotionMode.Linear).ToList();
        Assert.Equal(6 + 1 + 1, threads.Count); // NRC + acabamento (FAL) + NID
        Assert.Equal(10 - 0.92, threads[^1].End.X, 6);
        Assert.Equal(-18 - 2, threads[^1].End.Z, 6); // FPL + ROP
        Assert.Equal(1.5 * 800, threads[0].Feed, 6);
    }

    [Fact]
    public void G33ComPassoK()
    {
        ToolPath path = Siemens("""
            G97 S600
            G0 X19 Z5
            G33 Z-20 K1.5
            """);

        Assert.Equal(1.5 * 600, path.Moves[^1].Feed, 6);
    }

    [Fact]
    public void LimsLimitaARotacaoNoG96()
    {
        ToolPath path = Siemens("""
            G96 S100 LIMS=1000
            G0 X10 Z2
            G1 Z-10 F0.1
            """);

        Assert.Equal(0.1 * 1000, path.Moves[^1].Feed, 3); // Ø10 pediria 3183 rpm
    }

    // ───────────────────────── Exemplos ─────────────────────────

    [Theory]
    [InlineData("torno/EXEMPLO_TORNO.nc", Dialect.Fanuc)]
    [InlineData("torno/EXEMPLO_TORNO.mpf", Dialect.Siemens)]
    public void ExemplosDeTornoRodamSemErros(string file, Dialect dialect)
    {
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(Sample(file), dialect);

        Assert.Equal(MachineKind.Lathe, analysis.ToolPath.Machine);
        Assert.Equal(0, analysis.ErrorCount);
        Assert.True(analysis.ToolPath.Moves.Count > 50);
        Assert.All(analysis.ToolPath.Moves, m => Assert.Equal(0, m.End.Y, 9));

        ProgramStatistics stats = analysis.Statistics;
        Assert.True(stats.CuttingTime > TimeSpan.Zero);
        Assert.Equal(0, stats.MovesWithoutFeed);
        Assert.Equal([1, 3], stats.Tools.Where(t => t.Tool != 0).Select(t => t.Tool));
    }
}
