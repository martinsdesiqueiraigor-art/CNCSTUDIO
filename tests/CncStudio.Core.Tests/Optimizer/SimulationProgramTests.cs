using CncStudio.Core.Optimizer;
using Xunit;
using static CncStudio.Core.Tests.Optimizer.Fixtures;

namespace CncStudio.Core.Tests.Optimizer;

public class SimulationProgramTests
{
    [Theory]
    [InlineData("A-fusion360-flange.nc")]
    [InlineData("B-mastercam-suporte.nc")]
    [InlineData("C-pos-compacto-placa.nc")]
    [InlineData("D-acabamento-3d-cilindro.nc")]
    [InlineData("F-furacao-ciclos.nc")]
    [InlineData("G-bolsoes-concentricos.nc")]
    [InlineData("H-faceamento.nc")]
    [InlineData("I-helices-bucha.nc")]
    public void LoadableProgramReproducesTheWholeOriginal(string name)
    {
        var lines = File.ReadAllLines(Path.Combine("otimizador-fixtures", name));
        var r = Run(lines);
        Assert.Empty(r.SimulationBlockers);
        var sim = r.SimulationProgram!.TrimEnd('\n').Split('\n');
        Assert.Equal("%", sim[0]);
        Assert.StartsWith("O", sim[1]);
        Assert.Equal("(SOMENTE SIMULADOR - NAO USAR EM MAQUINA CNC)", sim[3]);
        Assert.DoesNotContain(sim, l => l.StartsWith("=====") || l.StartsWith("GCODEOPTIMIZER"));
        Assert.Equal("%", sim[^1]);
        Assert.Equal(r.Proposals.Count(p => p.Accepted), sim.Count(l => l.EndsWith("] DO1")));
        Assert.Equal(r.ConvertedLines + 4, sim.Length);

        // Independent re-check from the test: whole file expanded vs whole original.
        var original = NcTokenizer.Tokenize(lines).Where(b => b.HasCode || b.IsDelimiter).ToArray();
        Assert.True(EquivalenceChecker.Check(Calc, NcState.Unknown, original, r.SimulationProgram).Match);
        var tampered = r.SimulationProgram.Replace("#100 = #100 + 1", "#100 = #100 + 2");
        Assert.False(EquivalenceChecker.Check(Calc, NcState.Unknown, original, tampered).Match);
    }

    [Fact]
    public void HeaderGoesFirstWhenThereIsNoProgramNumber()
    {
        var r = Run(ZLevels(6).Skip(2));
        Assert.StartsWith("(GCODEOPTIMIZER", r.SimulationProgram);
    }

    [Fact]
    public void NoAcceptedProposalMeansNoSimulationFile()
    {
        var r = Run(File.ReadAllLines("otimizador-fixtures/E-compensacao-g41-tampa.nc"));
        Assert.Null(r.SimulationProgram);
        Assert.Contains("Nenhuma proposta aceita", Assert.Single(r.SimulationBlockers));
    }

    [Fact]
    public void ProgramWithMacroSyntaxIsWithheld()
    {
        var r = Run(ZLevels(6).Prepend("#500 = 1"));
        Assert.Contains(r.Proposals, p => p.Accepted);
        Assert.Null(r.SimulationProgram);
        Assert.Contains("nao pode ser conferido inteiro", Assert.Single(r.SimulationBlockers));
    }
}
