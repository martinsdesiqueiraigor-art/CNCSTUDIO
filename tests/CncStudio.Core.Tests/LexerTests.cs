using CncStudio.Core.Dialects;
using CncStudio.Core.Lexing;

namespace CncStudio.Core.Tests;

public class LexerTests
{
    [Fact]
    public void Fanuc_LinhaComNumeroDeBlocoPalavrasEComentario()
    {
        const string line = "N10 G01 X10.5 Y-3. (CORTE) F200";

        List<Token> tokens = GCodeLexer.TokenizeLine(line, Dialect.Fanuc);

        Assert.Equal(
            new[] { TokenKind.BlockNumber, TokenKind.Word, TokenKind.Word, TokenKind.Word, TokenKind.Comment, TokenKind.Word },
            tokens.Select(t => t.Kind));
        Assert.Equal(10.0, tokens[0].Value!.Value);
        Assert.Equal('X', tokens[2].Letter);
        Assert.Equal(10.5, tokens[2].Value!.Value);
        Assert.Equal(-3.0, tokens[3].Value!.Value);
        Assert.Equal("(CORTE)", tokens[4].GetText(line));
    }

    [Fact]
    public void Fanuc_PalavrasSemEspacos()
    {
        List<Token> tokens = GCodeLexer.TokenizeLine("G01X10Y20", Dialect.Fanuc);

        Assert.Equal(new[] { 'G', 'X', 'Y' }, tokens.Select(t => t.Letter));
        Assert.Equal(new[] { 1.0, 10.0, 20.0 }, tokens.Select(t => t.Value!.Value));
    }

    [Fact]
    public void Fanuc_LetraMinusculaViraMaiuscula()
    {
        List<Token> tokens = GCodeLexer.TokenizeLine("g1 x5", Dialect.Fanuc);

        Assert.Equal(new[] { 'G', 'X' }, tokens.Select(t => t.Letter));
    }

    [Fact]
    public void Fanuc_ProgramaOEMarcadorPercentual()
    {
        Assert.Equal(TokenKind.ProgramMarker, GCodeLexer.TokenizeLine("%", Dialect.Fanuc)[0].Kind);

        List<Token> tokens = GCodeLexer.TokenizeLine("O1001 (TESTE)", Dialect.Fanuc);
        Assert.Equal(TokenKind.ProgramMarker, tokens[0].Kind);
        Assert.Equal(TokenKind.Comment, tokens[1].Kind);
    }

    [Fact]
    public void Fanuc_ComentarioSemFecharVaiAteOFimDaLinha()
    {
        const string line = "G00 X1 (SEM FECHAR";

        List<Token> tokens = GCodeLexer.TokenizeLine(line, Dialect.Fanuc);

        Assert.Equal(TokenKind.Comment, tokens[^1].Kind);
        Assert.Equal("(SEM FECHAR", tokens[^1].GetText(line));
    }

    [Fact]
    public void Siemens_ComentarioComPontoEVirgula()
    {
        const string line = "G1 X10 ; comentario G01 X99";

        List<Token> tokens = GCodeLexer.TokenizeLine(line, Dialect.Siemens);

        Assert.Equal(3, tokens.Count);
        Assert.Equal(TokenKind.Comment, tokens[2].Kind);
        Assert.Equal(7, tokens[2].Column);
    }

    [Fact]
    public void Siemens_EnderecoComIgual()
    {
        List<Token> tokens = GCodeLexer.TokenizeLine("X=-10.5 Y=2", Dialect.Siemens);

        Assert.Equal(new[] { TokenKind.Word, TokenKind.Word }, tokens.Select(t => t.Kind));
        Assert.Equal(-10.5, tokens[0].Value!.Value);
        Assert.Equal(7, tokens[0].Length);
    }

    [Fact]
    public void Siemens_CicloComParentesesEhUmaUnicaPalavraChave()
    {
        const string line = "CYCLE81(10,0,2,-5)";

        List<Token> tokens = GCodeLexer.TokenizeLine(line, Dialect.Siemens);

        Token token = Assert.Single(tokens);
        Assert.Equal(TokenKind.Keyword, token.Kind);
        Assert.Equal(line.Length, token.Length);
    }

    [Fact]
    public void Siemens_RaioCrComValor()
    {
        List<Token> tokens = GCodeLexer.TokenizeLine("G2 X10 Y0 CR=5", Dialect.Siemens);

        Assert.Equal(TokenKind.Keyword, tokens[^1].Kind);
        Assert.Equal(5.0, tokens[^1].Value!.Value);
    }

    [Fact]
    public void Siemens_CabecalhoDoPrograma()
    {
        List<Token> tokens = GCodeLexer.TokenizeLine("%_N_EXEMPLO_MPF", Dialect.Siemens);

        Assert.Equal(TokenKind.ProgramMarker, Assert.Single(tokens).Kind);
    }

    [Fact]
    public void CaractereDesconhecidoGeraTokenUnknown()
    {
        List<Token> tokens = GCodeLexer.TokenizeLine("G01 X10 $", Dialect.Fanuc);

        Assert.Equal(TokenKind.Unknown, tokens[^1].Kind);
        Assert.Equal(8, tokens[^1].Column);
    }

    [Theory]
    [InlineData("10", 10)]
    [InlineData("10.", 10)]
    [InlineData(".5", 0.5)]
    [InlineData("-.5", -0.5)]
    [InlineData("+3.25", 3.25)]
    [InlineData("-0.001", -0.001)]
    public void FormatosDeNumero(string number, double expected)
    {
        Token token = Assert.Single(GCodeLexer.TokenizeLine("X" + number, Dialect.Fanuc));

        Assert.Equal(TokenKind.Word, token.Kind);
        Assert.Equal(expected, token.Value!.Value, 9);
    }

    [Fact]
    public void LetraSemNumeroEhDesconhecida()
    {
        List<Token> tokens = GCodeLexer.TokenizeLine("G01 X Y10", Dialect.Fanuc);

        Assert.Equal(TokenKind.Unknown, tokens[1].Kind);
        Assert.Equal(TokenKind.Word, tokens[2].Kind);
    }
}
