using CncStudio.Core.Dialects;
using CncStudio.Core.Editing;

namespace CncStudio.Core.Tests;

public class NcTextToolsTests
{
    [Fact]
    public void RenumeraTodosOsBlocosEPulaComentariosEMarcadores()
    {
        const string program = "%\nO1001 (TESTE)\n(COMENTARIO)\nN5 G00 X0\nG01 X10 F100\n\nM30\n%";

        string result = NcTextTools.RenumberBlocks(program, Dialect.Fanuc, new RenumberOptions(10, 10));

        Assert.Equal("%\nO1001 (TESTE)\n(COMENTARIO)\nN10 G00 X0\nN20 G01 X10 F100\n\nN30 M30\n%", result);
    }

    [Fact]
    public void RenumeraSoOsQueJaTemNumero()
    {
        const string program = "N100 G00 X0\nG01 X10\nN7 M30";

        string result = NcTextTools.RenumberBlocks(program, Dialect.Fanuc,
            new RenumberOptions(1, 1, RenumberMode.ExistingOnly));

        Assert.Equal("N1 G00 X0\nG01 X10\nN2 M30", result);
    }

    [Fact]
    public void RemoveNumeros()
    {
        string result = NcTextTools.RenumberBlocks("N10 G00 X0\n/N20 M30", Dialect.Fanuc,
            new RenumberOptions(Mode: RenumberMode.Remove));

        Assert.Equal("G00 X0\n/M30", result);
    }

    [Fact]
    public void PreservaQuebraDeLinhaWindowsEBlocoOpcional()
    {
        string result = NcTextTools.RenumberBlocks("G00 X0\r\n/G01 X1\r\n", Dialect.Fanuc, new RenumberOptions(5, 5));

        Assert.Equal("N5 G00 X0\r\n/N10 G01 X1\r\n", result);
    }

    [Fact]
    public void SiemensNumeraCiclosENaoNumeraComentario()
    {
        string result = NcTextTools.RenumberBlocks("; inicio\nCYCLE81(10,0,2,-5)\nG1 X=5", Dialect.Siemens,
            new RenumberOptions(10, 10));

        Assert.Equal("; inicio\nN10 CYCLE81(10,0,2,-5)\nN20 G1 X=5", result);
    }

    [Fact]
    public void MaiusculasPreservamComentarios()
    {
        Assert.Equal("G01 X10 (corte leve)", NcTextTools.ToUpperOutsideComments("g01 x10 (corte leve)", Dialect.Fanuc));
        Assert.Equal("G1 X=5 ; comentário", NcTextTools.ToUpperOutsideComments("g1 x=5 ; comentário", Dialect.Siemens));
    }
}
