using CncStudio.Core.Dialects;
using CncStudio.Core.Editing;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

public class GCodeHelpTests
{
    private static string? At(string line, string piece, Dialect dialect = Dialect.Fanuc, MachineKind machine = MachineKind.Mill) =>
        GCodeHelp.Describe(line, line.IndexOf(piece, StringComparison.Ordinal), dialect, machine);

    [Fact]
    public void ExplicaCodigosGeM()
    {
        Assert.StartsWith("G01", At("N10 G01 X10 F100", "G01"));
        Assert.StartsWith("M30", At("M30", "M30"));
        Assert.StartsWith("N —", At("N10 G01", "N10"));
    }

    [Fact]
    public void MesmoCodigoMudaNoTorno()
    {
        Assert.Contains("absolutas", At("G90 X10", "G90"));
        Assert.Contains("torneamento", At("G90 X10", "G90", machine: MachineKind.Lathe));
        Assert.Contains("Desbaste longitudinal", At("G71 U2 R0.5", "G71", machine: MachineKind.Lathe));
        Assert.Contains("diâmetro", At("G00 X50", "X50", machine: MachineKind.Lathe));
    }

    [Fact]
    public void ExplicaCiclosSiemensComParametros()
    {
        string? help = At("CYCLE83(10, 0, 2, -30, , -5)", "CYCLE83", Dialect.Siemens);
        Assert.Contains("FDEP", help);
        Assert.Contains("Furação profunda", help);
        Assert.Contains("KONTUR", At("CYCLE95(\"KONTUR\", 2)", "CYCLE95", Dialect.Siemens));
    }

    [Fact]
    public void ExplicaVariaveisEPalavrasChave()
    {
        Assert.Contains("Alarme", At("#3000=1 (FALTA FERRAMENTA)", "#3000"));
        Assert.Contains("argumento X", At("G00 X#24", "#24"));
        Assert.Contains("WHILE", At("WHILE [#1 LT 10] DO1", "WHILE"));
        Assert.Contains("Fim do laço", At("END1", "END1"));
        Assert.Contains("Parâmetro", At("X=R1+5", "R1", Dialect.Siemens));
        Assert.Contains("Seno", At("#1=SIN[#2]", "SIN"));
        Assert.Contains("Rótulo", At("VOLTA:", "VOLTA", Dialect.Siemens));
    }

    [Fact]
    public void ComentarioEEspacoNaoTemExplicacao()
    {
        Assert.Null(At("G01 X10 (CORTE G01)", "CORTE"));
        Assert.Null(GCodeHelp.Describe("G01  X10", 4, Dialect.Fanuc));
    }

    [Fact]
    public void AvisaOQueNaoEhSuportado()
    {
        Assert.Contains("Não suportado", At("G68 X0 Y0 R45", "G68"));
        Assert.Contains("Não suportado", At("CYCLE800()", "CYCLE800", Dialect.Siemens));
    }
}

public class PathHitTestTests
{
    private static readonly Func<Point3, (double, double)> Top = p => (p.X, p.Y);

    private static ToolPath Fanuc(string program) => MachineExecutor.Execute(GCodeParser.Parse(program, Dialect.Fanuc));

    [Fact]
    public void AchaOMovimentoMaisProximoDentroDaDistancia()
    {
        ToolPath path = Fanuc("G01 X50 F100\nY50\nG03 X0 Y50 R25"); // G03: semicírculo por cima (Y75)
        Assert.Equal(0, PathHitTest.NearestMove(path.Moves, Top, 25, 0.5, 2, 0.01));    // reta em Y0
        Assert.Equal(1, PathHitTest.NearestMove(path.Moves, Top, 49.5, 30, 2, 0.01));   // reta em X50
        Assert.Equal(2, PathHitTest.NearestMove(path.Moves, Top, 25, 75, 1, 0.01));     // topo do arco
        Assert.Equal(-1, PathHitTest.NearestMove(path.Moves, Top, 25, 25, 2, 0.01));    // meio: nada perto
    }

    [Fact]
    public void PrefereCorteQuandoORapidoPassaPorCima()
    {
        ToolPath path = Fanuc("G00 X50 Y0\nG01 X0 F100");
        Assert.Equal(1, PathHitTest.NearestMove(path.Moves, Top, 25, 0, 1, 0.01));
    }

    [Fact]
    public void EncaixaNoPontoProgramadoMaisProximo()
    {
        ToolPath path = Fanuc("G00 X10 Y10 Z5\nG01 Z-2 F100\nX40");
        Point3? point = PathHitTest.NearestPoint(path.Moves, Top, 39, 11, 3);
        Assert.Equal(new Point3(40, 10, -2), point);
        Assert.Null(PathHitTest.NearestPoint(path.Moves, Top, 25, 30, 3));
    }
}

public class TravelLimitsTests
{
    [Fact]
    public void AvisaOPrimeiroMovimentoQuePassaDoLimite()
    {
        string program = "G00 X10 Y10 Z5\nG01 Z-30 F100\nX600\nX-5\nX700";
        var settings = new MachineSettings { TravelLimits = new TravelLimits(MaxX: 500, MinZ: -25) };
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(program, Dialect.Fanuc, settings);

        ParseDiagnostic x = Assert.Single(analysis.Diagnostics, d => d.Message.Contains("X+"));
        Assert.Equal(3, x.LineNumber);
        Assert.Contains("600", x.Message);
        Assert.Contains("mais 2", x.Message); // X-5 (sai de X600) e X700

        ParseDiagnostic z = Assert.Single(analysis.Diagnostics, d => d.Message.Contains("Z−"));
        Assert.Equal(2, z.LineNumber);
        Assert.DoesNotContain(analysis.Diagnostics, d => d.Message.Contains("X−"));
    }

    [Fact]
    public void ArcoQuePassaDoLimiteNoMeioTambemConta()
    {
        string program = "G00 X0 Y0\nG02 X20 Y0 R10 F100"; // passa por Y=+10
        var settings = new MachineSettings { TravelLimits = new TravelLimits(MaxY: 5) };
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(program, Dialect.Fanuc, settings);
        Assert.Contains(analysis.Diagnostics, d => d.Message.Contains("Y+"));
    }

    [Fact]
    public void NoTornoOLimiteDeXEhEmDiametro()
    {
        string program = "G97 S500\nG00 X60 Z2\nG01 Z-10 F0.2";
        var settings = new MachineSettings { Machine = MachineKind.Lathe, TravelLimits = new TravelLimits(MaxX: 50) };
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(program, Dialect.Fanuc, settings);
        Assert.Contains(analysis.Diagnostics, d => d.Message.Contains("X = 60"));
    }

    [Fact]
    public void SemLimitesNaoAvisa()
    {
        var settings = new MachineSettings { TravelLimits = new TravelLimits() };
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze("G00 X9999", Dialect.Fanuc, settings);
        Assert.Empty(analysis.Diagnostics);
    }
}
