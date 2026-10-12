using CncStudio.Core.Dialects;
using CncStudio.Core.Editing;

namespace CncStudio.Core.Tests;

public class TextSearchTests
{
    private const string Program = "N10 G01 X10 Y5\nN20 X100 Y10.5\nN30 x10 (X10 no comentário)\n";

    [Fact]
    public void BuscaSimplesIgnoraMaiusculasPorPadrao()
    {
        Assert.Equal(4, TextSearch.FindAll(Program, "x10", new SearchOptions()).Count); // X10, X100, x10, X10
        Assert.Equal(1, TextSearch.FindAll(Program, "x10", new SearchOptions(MatchCase: true)).Count);
    }

    [Fact]
    public void PalavraInteiraNoSentidoDoGCode()
    {
        var whole = new SearchOptions(WholeWord: true);
        Assert.Equal(3, TextSearch.FindAll(Program, "X10", whole).Count);       // não acha X100
        Assert.Single(TextSearch.FindAll("G1X10 G10", "G1", whole));            // G1 colado no X conta; G10 não
        Assert.Empty(TextSearch.FindAll("Y10.5", "Y10", whole));                // Y10.5 é outro número
        Assert.Single(TextSearch.FindAll("Y10.5", "10.5", whole));
    }

    [Fact]
    public void ExpressaoRegularComGrupoNaSubstituicao()
    {
        var regex = new SearchOptions(UseRegex: true);
        (string text, int count) = TextSearch.ReplaceAll("N10 X5\nN20 X7", @"X(\d+)", "X-$1", regex);
        Assert.Equal(2, count);
        Assert.Equal("N10 X-5\nN20 X-7", text);
    }

    [Fact]
    public void TextoSimplesNaoInterpretaCaracteresEspeciais()
    {
        Assert.Single(TextSearch.FindAll("#1=[#2+1]", "[#2+1]", new SearchOptions()));
        (string text, int count) = TextSearch.ReplaceAll("A$1 A$1", "$1", "$2", new SearchOptions());
        Assert.Equal("A$2 A$2", text);
        Assert.Equal(2, count);
    }

    [Fact]
    public void ExpressaoInvalidaDaMensagem()
    {
        Assert.Null(TextSearch.BuildRegex("X(", new SearchOptions(UseRegex: true), out string? error));
        Assert.Contains("inválida", error);
        Assert.Empty(TextSearch.FindAll("X(", "X(", new SearchOptions(UseRegex: true)));
    }

    [Fact]
    public void ProximoEAnteriorDaoAVolta()
    {
        string text = "X1 X1 X1";
        Assert.Equal(3, TextSearch.FindNext(text, "X1", new SearchOptions(), 1)!.Value.Offset);
        Assert.Equal(0, TextSearch.FindNext(text, "X1", new SearchOptions(), 7)!.Value.Offset);       // volta ao início
        Assert.Equal(6, TextSearch.FindNext(text, "X1", new SearchOptions(), 0, backwards: true)!.Value.Offset);
        Assert.Null(TextSearch.FindNext(text, "Y", new SearchOptions(), 0));
    }
}

public class NcTextFunctionsTests
{
    [Fact]
    public void RemoveEspacosEntreEnderecosMasNaoEmComentarios()
    {
        string text = "  N10 G01 X10 Y5  (COMENTARIO COM ESPACO)  \r\nN20 X20";
        Assert.Equal("N10G01X10Y5 (COMENTARIO COM ESPACO)\r\nN20X20", NcTextTools.RemoveSpaces(text, Dialect.Fanuc));
    }

    [Fact]
    public void RemoveEspacosPreservaPalavrasChave()
    {
        Assert.Equal("IF [#1 GT 10] GOTO 20", NcTextTools.RemoveSpaces("IF [#1 GT 10] GOTO 20", Dialect.Fanuc));
        Assert.Equal("IF R1>R2 GOTOF FIM", NcTextTools.RemoveSpaces("IF R1>R2 GOTOF FIM", Dialect.Siemens));
        Assert.Equal("G1X10 CR=5", NcTextTools.RemoveSpaces("G1 X10 CR=5", Dialect.Siemens));
    }

    [Fact]
    public void InsereEspacosEntrePalavrasColadas()
    {
        Assert.Equal("N10 G01 X10 Y-5.5 (A)", NcTextTools.InsertSpaces("N10G01X10Y-5.5(A)", Dialect.Fanuc));
        Assert.Equal("G01 X10,R2", NcTextTools.InsertSpaces("G01X10,R2", Dialect.Fanuc));
        Assert.Equal("N10 G01 X10", NcTextTools.InsertSpaces("N10 G01 X10", Dialect.Fanuc)); // já espaçado: igual
    }

    [Fact]
    public void RemoveComentariosEApagaLinhasQueEramSoComentario()
    {
        string text = "(CABECALHO)\nG01 X10 (CORTE)\n\nG00 Z5";
        Assert.Equal("G01 X10\n\nG00 Z5", NcTextTools.RemoveComments(text, Dialect.Fanuc));

        string siemens = "; contorno\nG1 X10 ; corte\nMSG(\"NAO E COMENTARIO\")";
        Assert.Equal("G1 X10\nMSG(\"NAO E COMENTARIO\")", NcTextTools.RemoveComments(siemens, Dialect.Siemens));
    }
}
