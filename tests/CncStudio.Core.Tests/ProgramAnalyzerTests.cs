using CncStudio.Core.Dialects;
using CncStudio.Core.Parsing;

namespace CncStudio.Core.Tests;

public class ProgramAnalyzerTests
{
    [Fact]
    public void JuntaDiagnosticosDoParserEDaTrajetoriaEmOrdemDeLinha()
    {
        const string program = """
            G00 X10 Y0
            G03 X0 Y10.5 I-10 J0 F100
            G91 G01 X1
            """;

        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(program, Dialect.Fanuc);

        Assert.Equal(2, analysis.Diagnostics.Count);
        Assert.Equal(2, analysis.Diagnostics[0].LineNumber); // aviso de raio (trajetória)
        Assert.Equal(3, analysis.Diagnostics[1].LineNumber); // erro G91 (parser)
        Assert.Equal(1, analysis.ErrorCount);
        Assert.Equal(1, analysis.WarningCount);
        Assert.Equal(3, analysis.BlockCount);
        Assert.Equal(3, analysis.ToolPath.Moves.Count);
    }

    [Fact]
    public void CancelamentoInterrompeAAnalise()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            ProgramAnalyzer.Analyze("G01 X1", Dialect.Fanuc, cancellation.Token));
    }
}
