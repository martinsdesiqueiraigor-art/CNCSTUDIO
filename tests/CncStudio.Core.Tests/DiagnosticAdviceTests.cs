using CncStudio.Core.Dialects;
using CncStudio.Core.Parsing;
using Xunit;

namespace CncStudio.Core.Tests;

public class DiagnosticAdviceTests
{
    [Theory]
    [InlineData("Movimento de corte sem avanço (F) definido.", "avanço (F)")]
    [InlineData("Mais de um código de movimento (G00–G03) no mesmo bloco.", "só um G00")]
    [InlineData("Arco (G02/G03) sem centro (I/J) nem raio (R/CR=).", "centro")]
    [InlineData("Endereço X repetido no mesmo bloco.", "repetida")]
    public void KnownMessagesGetSpecificAdvice(string message, string expected)
    {
        Assert.Contains(expected, DiagnosticAdvice.For(message, DiagnosticSeverity.Warning));
    }

    [Fact]
    public void UnknownMessagesGetGenericAdviceBySeverity()
    {
        Assert.Contains("alarme", DiagnosticAdvice.For("mensagem nova qualquer", DiagnosticSeverity.Error));
        Assert.Contains("Confira", DiagnosticAdvice.For("mensagem nova qualquer", DiagnosticSeverity.Warning));
    }

    [Fact]
    public void EveryRealDiagnosticFromTheAnalyzerHasAdvice()
    {
        ProgramAnalysis a = ProgramAnalyzer.Analyze("G01 X10.\nG01 G02 X5. Y5. F100.\nG04\nX5. X6.\n", Dialect.Fanuc);
        Assert.NotEmpty(a.Diagnostics);
        Assert.All(a.Diagnostics, d => Assert.False(string.IsNullOrWhiteSpace(DiagnosticAdvice.For(d.Message, d.Severity))));
    }
}
