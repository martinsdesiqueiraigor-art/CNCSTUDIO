using CncStudio.Core.Dialects;
using CncStudio.Core.Editing;

namespace CncStudio.Core.Tests;

/// <summary>Renumerar sem quebrar os saltos: P/Q dos ciclos de perfil, GOTO e M99 P.</summary>
public class RenumberReferencesTests
{
    private const string Lathe = """
        G71 U1.5 R0.5
        G71 P100 Q200 U0.4 W0.1 F0.25
        N100 G00 X24.
        G01 Z-15.
        N200 X52.
        G70 P100 Q200
        """;

    [Fact]
    public void Renumerar_AtualizaPeQDoG71EdoG70()
    {
        string result = NcTextTools.RenumberBlocks(Lathe, Dialect.Fanuc, new RenumberOptions(10, 10));
        string[] lines = result.Split('\n');

        Assert.Equal("N20 G71 P30 Q50 U0.4 W0.1 F0.25", lines[1]);
        Assert.Equal("N30 G00 X24.", lines[2]);
        Assert.Equal("N50 X52.", lines[4]);
        Assert.Equal("N60 G70 P30 Q50", lines[5]);
    }

    [Fact]
    public void RemoverNumeracao_MantemOsBlocosReferenciados()
    {
        string result = NcTextTools.RenumberBlocks(Lathe + "\nN300 M30", Dialect.Fanuc, new RenumberOptions(Mode: RenumberMode.Remove));

        Assert.Contains("N100 G00 X24.", result);
        Assert.Contains("N200 X52.", result);
        Assert.DoesNotContain("N300", result);
    }

    [Fact]
    public void Renumerar_AtualizaGotoEM99P()
    {
        const string program = """
            N5 #1=0
            N7 IF[#1 GT 3] GOTO 9
            N8 #1=#1+1
            GOTO 7
            N9 M99 P8
            """;

        string result = NcTextTools.RenumberBlocks(program, Dialect.Fanuc, new RenumberOptions(10, 10));

        Assert.Contains("N40 GOTO 20", result);
        Assert.Contains("N50 M99 P30", result);
    }

    [Fact]
    public void EsperaComPontoDecimalNaoEReferencia()
    {
        // P0.5 numa linha de M99 não é número de bloco
        string result = NcTextTools.RenumberBlocks("N1 G04 P0.5\nN2 M99", Dialect.Fanuc, new RenumberOptions(10, 10));

        Assert.Equal("N10 G04 P0.5\nN20 M99", result);
    }
}
