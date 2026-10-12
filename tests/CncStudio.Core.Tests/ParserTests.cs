using CncStudio.Core.Dialects;
using CncStudio.Core.Parsing;

namespace CncStudio.Core.Tests;

/// <summary>O parser só cuida da sintaxe: palavras, palavras-chave, rótulos e erros de escrita.</summary>
public class ParserTests
{
    [Fact]
    public void BlocoGuardaPalavrasNaOrdemENumeroDaLinha()
    {
        ParseResult result = GCodeParser.Parse("%\n(CABECALHO)\n\nN100 G01 X10. Y5 F200", Dialect.Fanuc);

        NcBlock block = Assert.Single(result.Blocks);
        Assert.Equal(4, block.LineNumber);
        Assert.Equal(100, block.BlockNumber!.Value);
        Assert.Equal(new[] { 'G', 'X', 'Y', 'F' }, block.Words.Select(w => w.Letter));
        Assert.True(block.Find('X')!.Value.HasDecimalPoint);
        Assert.False(block.Find('Y')!.Value.HasDecimalPoint);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void LinhasSoComComentarioNaoGeramBloco()
    {
        ParseResult result = GCodeParser.Parse("(SO COMENTARIO)\n%\nO1001 (PROGRAMA)", Dialect.Fanuc);

        Assert.Empty(result.Blocks);
    }

    [Fact]
    public void TextoNaoReconhecidoEhErroComPosicao()
    {
        ParseResult result = GCodeParser.Parse("G00 X0\nG01 X10 $", Dialect.Fanuc);

        ParseDiagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal(2, error.LineNumber);
        Assert.Equal(8, error.Column);
    }

    [Fact]
    public void Siemens_CicloViraStatementComArgumentos()
    {
        NcBlock block = Assert.Single(GCodeParser.Parse("MCALL CYCLE81(10,0,2,-5)", Dialect.Siemens).Blocks);

        Assert.Equal(2, block.Statements.Count);
        Assert.Equal("MCALL", block.Statements[0].Name);
        Statement cycle = block.Statements[1];
        Assert.Equal("CYCLE81", cycle.Name);
        Assert.Equal("10,0,2,-5", cycle.Arguments);
    }

    [Fact]
    public void Siemens_RotuloERaioCr()
    {
        ParseResult result = GCodeParser.Parse("VOLTA:\nG3 X-3. Y66. CR=3", Dialect.Siemens);

        Assert.Equal(StatementKind.Label, result.Blocks[0].Statements[0].Kind);
        Assert.Equal("VOLTA", result.Blocks[0].Statements[0].Name);
        Statement radius = result.Blocks[1].Statements[0];
        Assert.Equal("CR", radius.Name);
        Assert.Equal(3.0, radius.Value!.Value);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Fanuc_MacrosSaoReconhecidasSemErro()
    {
        const string program = """
            #1=48;
            WHILE[#1 GE 0] DO2;
            #7=#1*COS[#2];
            G1 X#7 Y#8 F800;
            IF [#20 GT #12] GOTO260
            X[#1] ,R[#8]
            END2;
            """;

        ParseResult result = GCodeParser.Parse(program, Dialect.Fanuc);

        Assert.Empty(result.Diagnostics);
        Assert.Equal(StatementKind.Expression, result.Blocks[0].Statements[0].Kind);
        Assert.Equal("WHILE", result.Blocks[1].Statements[0].Name);
        Assert.Equal("#1 GE 0", result.Blocks[1].Statements[0].Arguments);
        Assert.Equal(2, result.Blocks[3].Statements.Count); // X#7 e Y#8
        Assert.Equal(",R", result.Blocks[5].Statements[1].Name);
    }

    [Fact]
    public void Siemens_ExemploChavetaNaoTemErroDeSintaxe()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "samples", "macros", "CHAVETA.mpf");

        ParseResult result = GCodeParser.Parse(File.ReadAllText(path), Dialect.Siemens);

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Fanuc_ExemploEspiralNaoTemErroDeSintaxe()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "samples", "macros", "ESPIRAL_WHILE.nc");

        ParseResult result = GCodeParser.Parse(File.ReadAllText(path), Dialect.Fanuc);

        Assert.Empty(result.Diagnostics);
    }
}
