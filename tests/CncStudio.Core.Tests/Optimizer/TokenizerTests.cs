using CncStudio.Core.Optimizer;
using Xunit;

namespace CncStudio.Core.Tests.Optimizer;

public class TokenizerTests
{
    [Fact]
    public void CompactCodeIsSplitIntoWords()
    {
        var b = NcTokenizer.Tokenize(1, "G0G90G54X0Y-1.5");
        Assert.Null(b.Unsupported);
        Assert.Equal(new[] { "G0", "G90", "G54", "X0", "Y-1.5" }, b.Words.Select(w => w.Text));
    }

    [Fact]
    public void CommentsSemicolonNumbersAndDelimiterAreKept()
    {
        var b = NcTokenizer.Tokenize(3, "N100 G01 X1. (PASS 1) Y2 ; FIM");
        Assert.Equal("PASS 1 FIM", b.Comment);
        Assert.Equal(4, b.Words.Count);
        Assert.True(b.Get('X')!.HasDecimalPoint);
        Assert.False(b.Get('Y')!.HasDecimalPoint);
        Assert.True(NcTokenizer.Tokenize(1, "%").IsDelimiter);
        Assert.Null(NcTokenizer.Tokenize(1, "O1000").Unsupported);
    }

    [Theory]
    [InlineData("#100=1")]
    [InlineData("G01 X#1")]
    [InlineData("G01 X[#1+2]")]
    [InlineData("/G01 X1.")]
    [InlineData("G01 X1. (SEM FECHAR")]
    [InlineData("WHILE [#1 LT 2] DO1")]
    [InlineData("G01 X1. X2.")]
    [InlineData("G01 X1.2.3")]
    public void UnsupportedTextIsMarkedNotDropped(string line) =>
        Assert.NotNull(NcTokenizer.Tokenize(1, line).Unsupported);
}
