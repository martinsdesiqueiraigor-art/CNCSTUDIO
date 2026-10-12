using CncStudio.Core.Dialects;
using CncStudio.Core.Parsing;

namespace CncStudio.Core.Tests;

public class ParserTests
{
    private static ParseResult ParseFanuc(params string[] lines) =>
        GCodeParser.Parse(string.Join("\n", lines), Dialect.Fanuc);

    [Fact]
    public void MovimentoEAvancoSaoModais()
    {
        ParseResult result = ParseFanuc(
            "G01 X10 F100",
            "X20",
            "G00 X0");

        Assert.Equal(3, result.Blocks.Count);
        Assert.Equal(MotionMode.Linear, result.Blocks[1].Motion);
        Assert.Equal(100.0, result.Blocks[1].Feed);
        Assert.Equal(20.0, result.Blocks[1].X!.Value);
        Assert.Null(result.Blocks[1].Y);
        Assert.Equal(MotionMode.Rapid, result.Blocks[2].Motion);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void MovimentoInicialEhRapido()
    {
        ParseResult result = ParseFanuc("X5 Y5");

        Assert.Equal(MotionMode.Rapid, Assert.Single(result.Blocks).Motion);
    }

    [Fact]
    public void LinhasDeComentarioNaoGeramBlocoMasContamNaNumeracao()
    {
        ParseResult result = ParseFanuc(
            "%",
            "(CABECALHO)",
            "",
            "N100 G01 X1 F50");

        Block block = Assert.Single(result.Blocks);
        Assert.Equal(4, block.LineNumber);
        Assert.Equal(100, block.BlockNumber!.Value);
    }

    [Fact]
    public void SpindleLigaEDesliga()
    {
        ParseResult result = ParseFanuc(
            "S3000 M03",
            "G01 X10 F200",
            "M05");

        Assert.Equal(SpindleState.Clockwise, result.Blocks[0].Spindle);
        Assert.Equal(3000.0, result.Blocks[1].SpindleSpeed);
        Assert.Equal(SpindleState.Clockwise, result.Blocks[1].Spindle);
        Assert.Equal(SpindleState.Off, result.Blocks[2].Spindle);
    }

    [Fact]
    public void ArcoGuardaCentroIJ()
    {
        Block block = Assert.Single(ParseFanuc("G03 X0 Y10 I-10 J0").Blocks);

        Assert.Equal(MotionMode.CounterClockwiseArc, block.Motion);
        Assert.Equal(-10.0, block.I!.Value);
        Assert.Equal(0.0, block.J!.Value);
        Assert.True(block.HasMotion);
    }

    [Fact]
    public void ArcoSoComIJEhMovimento_CirculoCompletoFanuc()
    {
        Block block = Assert.Single(ParseFanuc("G02 I-5").Blocks);

        Assert.True(block.HasMotion);
    }

    [Fact]
    public void ArcoSemIJGeraErro()
    {
        ParseResult result = ParseFanuc("G02 X10 Y10");

        ParseDiagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal(1, error.LineNumber);
    }

    [Fact]
    public void G91GeraErroComPosicaoDoToken()
    {
        ParseResult result = ParseFanuc(
            "G00 X0",
            "G91 G01 X5");

        ParseDiagnostic error = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, error.Severity);
        Assert.Equal(2, error.LineNumber);
        Assert.Equal(0, error.Column);
        Assert.Equal(3, error.Length);
        Assert.True(result.HasErrors);
    }

    [Fact]
    public void DoisCodigosDeMovimentoNoMesmoBlocoGeramErro()
    {
        ParseResult result = ParseFanuc("G00 G01 X5");

        Assert.Contains(result.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void EixoRepetidoGeraErro()
    {
        ParseResult result = ParseFanuc("G01 X5 X6");

        Assert.Contains(result.Diagnostics, d => d.Message.Contains("repetido"));
    }

    [Fact]
    public void CodigosComunsSaoAceitosSemAviso()
    {
        ParseResult result = ParseFanuc(
            "G21 G17 G40 G49 G80 G90",
            "T1 M06",
            "G54",
            "M08");

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void CodigoNaoSuportadoGeraAviso()
    {
        ParseResult result = ParseFanuc("G28 X0 Y0");

        ParseDiagnostic warning = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public void BlocosDepoisDoM30SaoIgnoradosComUmAviso()
    {
        ParseResult result = ParseFanuc(
            "G01 X10 F100",
            "M30",
            "G00 X99",
            "G00 X98");

        Assert.Equal(2, result.Blocks.Count);
        Assert.True(result.Blocks[1].EndsProgram);
        ParseDiagnostic warning = Assert.Single(result.Diagnostics);
        Assert.Equal(3, warning.LineNumber);
    }

    [Fact]
    public void Siemens_ProgramaBasico()
    {
        const string program = """
            %_N_TESTE_MPF
            ;$PATH=/_N_MPF_DIR
            N10 G17 G71 G90 ; preparacao
            N20 T1 D1
            N30 M6
            N40 S2000 M3
            N50 G0 X=10 Y=5
            N60 G1 Z-2 F=150
            N70 G2 X20 Y5 I5 J0
            N80 M30
            """;

        ParseResult result = GCodeParser.Parse(program, Dialect.Siemens);

        Assert.Empty(result.Diagnostics);
        Block arc = result.Blocks.Single(b => b.BlockNumber == 70);
        Assert.Equal(MotionMode.ClockwiseArc, arc.Motion);
        Assert.Equal(150.0, arc.Feed);
        Assert.True(result.Blocks[^1].EndsProgram);
    }

    [Fact]
    public void Siemens_CicloGeraAvisoENaoBloco()
    {
        ParseResult result = GCodeParser.Parse("CYCLE81(10,0,2,-5)", Dialect.Siemens);

        Assert.Empty(result.Blocks);
        ParseDiagnostic warning = Assert.Single(result.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
        Assert.Contains("CYCLE81", warning.Message);
    }

    [Fact]
    public void Siemens_RaioCrAindaNaoSuportado()
    {
        ParseResult result = GCodeParser.Parse("G2 X10 Y0 CR=5", Dialect.Siemens);

        Assert.Contains(result.Diagnostics, d => d.Message.Contains("CR"));
        Assert.True(result.HasErrors); // arco sem I/J
    }
}
