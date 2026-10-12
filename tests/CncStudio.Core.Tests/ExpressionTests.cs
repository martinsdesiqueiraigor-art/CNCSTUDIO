using CncStudio.Core.Dialects;
using CncStudio.Core.Execution.Expressions;

namespace CncStudio.Core.Tests;

public class ExpressionTests
{
    private static double Fanuc(string text, VariableStore? vars = null) =>
        ExpressionEvaluator.Evaluate(text, Dialect.Fanuc, vars ?? new VariableStore());

    private static double Siemens(string text, VariableStore? vars = null) =>
        ExpressionEvaluator.Evaluate(text, Dialect.Siemens, vars ?? new VariableStore());

    [Theory]
    [InlineData("1+2*3", 7)]
    [InlineData("[1+2]*3", 9)]
    [InlineData("-[2+3]", -5)]
    [InlineData("10/4", 2.5)]
    [InlineData("10 MOD 3", 1)]
    [InlineData("SQRT[16]+ABS[-2]", 6)]
    [InlineData("ROUND[2.5]", 3)]
    [InlineData("FIX[2.7]", 2)]
    [InlineData("FUP[2.1]", 3)]
    [InlineData("SIN[30]", 0.5)]
    [InlineData("COS[60]", 0.5)]
    [InlineData("ATAN[1]/[1]", 45)]
    [InlineData("ATAN[-1]/[-1]", 225)]
    [InlineData("3 GT 2", 1)]
    [InlineData("3 LE 2", 0)]
    [InlineData("[1 EQ 1] AND [2 NE 2]", 0)]
    [InlineData("[1 EQ 1] OR [2 NE 2]", 1)]
    public void FanucAritmeticaFuncoesEComparacoes(string text, double expected)
    {
        Assert.Equal(expected, Fanuc(text), 9);
    }

    [Theory]
    [InlineData("(1+2)*3", 9)]
    [InlineData("7 DIV 2", 3)]
    [InlineData("7 MOD 2", 1)]
    [InlineData("POT(3)", 9)]
    [InlineData("ATAN2(1,1)", 45)]
    [InlineData("TRUNC(-2.7)", -2)]
    [InlineData("MINVAL(4,2)+MAXVAL(4,2)", 6)]
    [InlineData("(1<=2) AND NOT (3==4)", 1)]
    [InlineData("5<>5", 0)]
    [InlineData("TRUE", 1)]
    public void SiemensOperadoresEFuncoes(string text, double expected)
    {
        Assert.Equal(expected, Siemens(text), 9);
    }

    [Fact]
    public void FanucVariaveisDiretasEIndiretas()
    {
        var vars = new VariableStore();
        vars.Set("#1", 48);
        vars.Set("#3", 6);
        vars.Set("#105", 7);

        Assert.Equal(47.983333, Fanuc("#1-#3/360", vars), 5);
        Assert.Equal(7, Fanuc("#[100+5]", vars), 9);
    }

    [Fact]
    public void SiemensParametrosRENomes()
    {
        var vars = new VariableStore();
        vars.Set("R1", 0);
        vars.Set("R2", -3.1);
        vars.Set("CONTADOR", 4);

        Assert.Equal(1, Siemens("R1-R2>R3", vars) == 1 ? 1 : 0); // R3 vazio = 0
        Assert.Equal(3.1, Siemens("R1-(R1-R2)+6.2", vars), 9);
        Assert.Equal(8, Siemens("CONTADOR*2", vars), 9);
    }

    [Fact]
    public void VariavelVaziaValeZeroEAvisa()
    {
        var warnings = new List<string>();

        double value = ExpressionEvaluator.Evaluate("#7+1", Dialect.Fanuc, new VariableStore(), warnings.Add);

        Assert.Equal(1, value, 9);
        Assert.Single(warnings);
    }

    [Fact]
    public void LocaisDoFanucSaoPorNivelDeChamada()
    {
        var vars = new VariableStore();
        vars.Set("#1", 10);
        vars.Set("#500", 99);

        vars.PushLocals(new Dictionary<int, double> { [1] = 5 });
        Assert.Equal(5.0, vars.Get("#1"));
        Assert.Equal(99.0, vars.Get("#500")); // comum continua visível
        vars.PopLocals();

        Assert.Equal(10.0, vars.Get("#1"));
    }

    [Fact]
    public void DestinoDeAtribuicao()
    {
        var vars = new VariableStore();
        vars.Set("#1", 4);

        Assert.Equal("#12", ExpressionEvaluator.ResolveTarget("#12", Dialect.Fanuc, vars));
        Assert.Equal("#104", ExpressionEvaluator.ResolveTarget("#[100+#1]", Dialect.Fanuc, vars));
        Assert.Equal("R3", ExpressionEvaluator.ResolveTarget("r3", Dialect.Siemens, vars));
    }

    [Theory]
    [InlineData("1/0")]
    [InlineData("SQRT[-1]")]
    [InlineData("[1+2")]
    [InlineData("FOO[1]")]
    [InlineData("1 2")]
    public void ExpressaoInvalidaLancaErro(string text)
    {
        Assert.Throws<ExpressionException>(() => Fanuc(text));
    }
}
