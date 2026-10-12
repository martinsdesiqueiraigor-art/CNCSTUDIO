using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Parsing;
using CncStudio.Core.Statistics;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

public class StatisticsTests
{
    private const double Rapid = 10_000; // mm/min

    private static ToolPath Run(string program, Dialect dialect = Dialect.Fanuc) =>
        MachineExecutor.Execute(GCodeParser.Parse(program, dialect));

    private static ProgramStatistics Stats(string program, Dialect dialect = Dialect.Fanuc) =>
        ProgramStatistics.Compute(Run(program, dialect), Rapid);

    [Fact]
    public void TempoDeCorteEDeRapidoUsamAvancoEVelocidadeDeRapido()
    {
        ProgramStatistics stats = Stats("""
            G90 G00 X100
            G01 X200 F1000
            """);

        Assert.Equal(100, stats.RapidDistance, 6);
        Assert.Equal(100, stats.CuttingDistance, 6);
        Assert.Equal(0.6, stats.RapidTime.TotalSeconds, 3);   // 100 mm a 10.000 mm/min
        Assert.Equal(6.0, stats.CuttingTime.TotalSeconds, 3); // 100 mm a 1.000 mm/min
        Assert.Equal(6.6, stats.TotalTime.TotalSeconds, 3);
        Assert.Equal(2, stats.MoveCount);
    }

    [Fact]
    public void ArcoUsaOComprimentoDoArcoENaoACorda()
    {
        ProgramStatistics stats = Stats("""
            G90 G00 X10 Y0
            G02 X-10 Y0 I-10 J0 F600
            """);

        Assert.Equal(Math.PI * 10, stats.CuttingDistance, 6);
        Assert.Equal(Math.PI * 10 / 600 * 60, stats.CuttingTime.TotalSeconds, 3);
    }

    [Fact]
    public void SeparaPorFerramentaNaOrdemDeUso()
    {
        ProgramStatistics stats = Stats("""
            T2 M06
            G90 G00 X10
            G01 Z-2 F100
            G01 X20 F100
            T1 M06
            G00 Z5
            G00 X30
            G01 X40 F200
            G01 X50 F400
            """);

        Assert.Equal([2, 1], stats.Tools.Select(t => t.Tool));
        Assert.Equal(1, stats.ToolChanges);

        ToolStatistics t2 = stats.Tools[0];
        Assert.Equal(12, t2.CuttingDistance, 6);
        Assert.Equal(12.0 / 100 * 60, t2.CuttingTime.TotalSeconds, 3);
        Assert.Equal(-2, t2.MinZ!.Value, 6);
        Assert.Equal(2, t2.FirstLine);

        ToolStatistics t1 = stats.Tools[1];
        Assert.Equal(20, t1.CuttingDistance, 6);
        Assert.Equal(200, t1.MinFeed, 6);
        Assert.Equal(400, t1.MaxFeed, 6);
        Assert.Equal(10.0 / 200 * 60 + 10.0 / 400 * 60, t1.CuttingTime.TotalSeconds, 3);

        Assert.Equal(stats.CuttingTime, t1.CuttingTime + t2.CuttingTime);
    }

    [Fact]
    public void G04FanucSomaSegundosEMilissegundos()
    {
        ToolPath path = Run("""
            G90 G00 X10
            G04 X1.5
            G04 P2000
            G00 X20
            """);

        Assert.Equal(2, path.Dwells.Count);
        Assert.Equal(20, path.Moves[^1].End.X, 6); // X do G04 não é movimento

        ProgramStatistics stats = ProgramStatistics.Compute(path, Rapid);
        Assert.Equal(3.5, stats.DwellTime.TotalSeconds, 3);
    }

    [Fact]
    public void G4SiemensEmSegundosERotacoesNaoMudaOAvanco()
    {
        ToolPath path = Run("""
            S600 M3
            G1 X10 F100
            G4 F2
            G4 S10
            G1 X20
            """, Dialect.Siemens);

        Assert.Equal(100, path.Moves[^1].Feed, 6); // F do G4 é tempo, não avanço

        ProgramStatistics stats = ProgramStatistics.Compute(path, Rapid);
        Assert.Equal(3.0, stats.DwellTime.TotalSeconds, 3); // 2 s + 10 rotações a 600 rpm (1 s)
    }

    [Fact]
    public void G82EsperaNoFundoDeCadaFuro()
    {
        ProgramStatistics stats = Stats("""
            G90 G00 X0 Y0 Z50
            G98 G82 X10 Y10 R2 Z-5 P500 F100
            X20
            X30
            G80
            G81 X40 R2 Z-5 F100
            G80
            """);

        Assert.Equal(1.5, stats.DwellTime.TotalSeconds, 3); // 3 furos × 0,5 s; o G81 não espera
    }

    [Fact]
    public void Cycle82SiemensUsaODtb()
    {
        ProgramStatistics stats = Stats("""
            G0 X10 Y10 Z50
            CYCLE82(50, 0, 2, -5, , 0.8)
            """, Dialect.Siemens);

        Assert.Equal(0.8, stats.DwellTime.TotalSeconds, 3);
    }

    [Fact]
    public void CorteSemAvancoEContadoMasSemTempo()
    {
        ProgramStatistics stats = Stats("""
            G90 G01 X10
            """);

        Assert.Equal(1, stats.MovesWithoutFeed);
        Assert.Equal(10, stats.CuttingDistance, 6);
        Assert.Equal(TimeSpan.Zero, stats.CuttingTime);
    }

    [Fact]
    public void AnalisadorUsaAVelocidadeDeRapidoDaMaquina()
    {
        var settings = new MachineSettings { RapidRate = 6000 };
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze("G90 G00 X100", Dialect.Fanuc, settings);

        Assert.Equal(1.0, analysis.Statistics.RapidTime.TotalSeconds, 3);
        Assert.Equal(6000, analysis.Statistics.RapidRate);
    }

    [Fact]
    public void VelocidadeDeRapidoInvalidaEhRejeitada()
    {
        ToolPath path = Run("G00 X10");
        Assert.Throws<ArgumentOutOfRangeException>(() => ProgramStatistics.Compute(path, 0));
    }

    [Fact]
    public void ProgramaVazioTemTudoZerado()
    {
        ProgramStatistics stats = Stats("");

        Assert.Equal(TimeSpan.Zero, stats.TotalTime);
        Assert.Empty(stats.Tools);
        Assert.Equal(0, stats.ToolChanges);
    }

    [Fact]
    public void EspiralWhileTemTempoCoerente()
    {
        string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "macros", "ESPIRAL_WHILE.nc"));
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(text, Dialect.Fanuc);
        ProgramStatistics stats = analysis.Statistics;

        Assert.True(stats.CuttingDistance > 0);
        Assert.True(stats.CuttingTime > TimeSpan.Zero);
        Assert.Equal(analysis.ToolPath.Moves.Count, stats.Tools.Sum(t => t.MoveCount));
    }
}

public class DurationFormatTests
{
    private static readonly System.Globalization.CultureInfo PtBr = new("pt-BR");

    [Theory]
    [InlineData(0, "0,0 s")]
    [InlineData(4.24, "4,2 s")]
    [InlineData(59.96, "1min 00s")]
    [InlineData(725, "12min 05s")]
    [InlineData(3723, "1h 02min 03s")]
    public void FormataSegundosMinutosEHoras(double seconds, string expected)
    {
        Assert.Equal(expected, DurationFormat.Format(TimeSpan.FromSeconds(seconds), PtBr));
    }
}
