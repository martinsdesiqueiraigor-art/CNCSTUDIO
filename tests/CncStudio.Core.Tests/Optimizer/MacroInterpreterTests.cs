using CncStudio.Core.Optimizer;
using Xunit;

namespace CncStudio.Core.Tests.Optimizer;

public class MacroInterpreterTests
{
    [Fact]
    public void ExpandsLoopWithExpressions()
    {
        var e = MacroInterpreter.Expand("(T)\n#100 = 0\nWHILE [#100 LT 3] DO1\n#103 = #100 * -1.5\nG01 Z[-1.0+#103] X10\n#100 = #100 + 1\nEND1");
        Assert.True(e.Ok, e.Error);
        Assert.Equal(new[] { "G01 Z-1.0 X10", "G01 Z-2.5 X10", "G01 Z-4.0 X10" }, e.Text);
        Assert.True(e.Blocks[0].Get('Z')!.HasDecimalPoint);
        Assert.False(e.Blocks[0].Get('X')!.HasDecimalPoint);
    }

    [Fact]
    public void PrecedenceAndBrackets()
    {
        var e = MacroInterpreter.Expand("#1 = 2 + 3 * 4\n#2 = [2 + 3] * 4\n#3 = -#1 / 2\nX#1 Y#2 Z#3");
        Assert.True(e.Ok, e.Error);
        Assert.Equal("X14.0 Y20.0 Z-7.0", e.Text[0]);
    }

    [Theory]
    [InlineData("#1=0\nWHILE [#1 LT 2] DO4\n#1=#1+1\nEND4", "DO1, DO2 ou DO3")]
    [InlineData("#1=0\nWHILE [#1 LT 2] DO1\nWHILE [#1 LT 2] DO1\nEND1\nEND1", "aninhamento")]
    [InlineData("#1=0\nWHILE [#1 LT 2] DO1\n#1=#1+1", "WHILE sem END")]
    [InlineData("IF [#1 EQ 0] GOTO 10", "IF/GOTO")]
    [InlineData("X#5", "vazia")]
    [InlineData("M99", "M99/G65/G66")]
    [InlineData("G65 P9010 A1.", "M99/G65/G66")]
    [InlineData("#1=1/0", "divisão")]
    [InlineData("#1000=1", "variável inválida")]
    [InlineData("#1=0\nWHILE [#1 GE 0] DO1\nG01 X1.\nEND1", "excede")]
    public void RejectsUnsupportedOrUnsafeMacro(string fragment, string expected)
    {
        var e = MacroInterpreter.Expand(fragment);
        Assert.False(e.Ok);
        Assert.Contains(expected, e.Error);
    }
}
