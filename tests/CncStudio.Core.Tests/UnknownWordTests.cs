using CncStudio.Core;
using CncStudio.Core.Dialects;
using CncStudio.Core.Parsing;

namespace CncStudio.Core.Tests;

/// <summary>Palavras que o comando não conhece: no Fanuc é alarme (erro); no Siemens pode ser variável/subprograma (aviso).</summary>
public class UnknownWordTests
{
    [Fact]
    public void Fanuc_UnknownWord_IsError()
    {
        ProgramAnalysis a = ProgramAnalyzer.Analyze("G21 G90\nKXNSOS\nG01 X10 F100\n", Dialect.Fanuc);

        ParseDiagnostic d = Assert.Single(a.Diagnostics, x => x.LineNumber == 2);
        Assert.Equal(DiagnosticSeverity.Error, d.Severity);
        Assert.Contains("KXNSOS", d.Message);
    }

    [Fact]
    public void Siemens_UnknownWord_IsWarning()
    {
        ProgramAnalysis a = ProgramAnalyzer.Analyze("G71 G90\nKXNSOS\nG1 X10 F100\n", Dialect.Siemens);

        Assert.DoesNotContain(a.Diagnostics, x => x.Severity == DiagnosticSeverity.Error);
    }
}
