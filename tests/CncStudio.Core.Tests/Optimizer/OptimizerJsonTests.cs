using System.Text.Json.Nodes;
using CncStudio.Core.Optimizer;

namespace CncStudio.Core.Tests.Optimizer;

public class OptimizerJsonTests
{
    private static readonly string Program = File.ReadAllText(Path.Combine("otimizador-fixtures", "A-fusion360-flange.nc"));

    [Fact]
    public void AnalyzeDevolveResumoPropostasESimulacao()
    {
        JsonObject r = JsonNode.Parse(OptimizerJson.Dispatch("analyze", new JsonObject { ["text"] = Program, ["profile"] = "calculadora" }.ToJsonString()))!.AsObject();
        Assert.True((bool)r["ok"]!);
        Assert.False((bool)r["machineReady"]!);
        Assert.True((int)r["accepted"]! > 0);
        Assert.True((int)r["savedLines"]! > 0);
        Assert.NotNull(r["simulationProgram"]);
        Assert.Contains("somente para simulador", (string)r["warning"]!, StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(r["proposals"]!.AsArray());
    }

    [Fact]
    public void ProgramaVazioOuGrandeDemaisDaErroEmVezDeFalhar()
    {
        Assert.False((bool)JsonNode.Parse(OptimizerJson.Dispatch("analyze", "{\"text\":\"\"}"))!["ok"]!);
        string big = string.Join("\n", Enumerable.Repeat("G01 X1", OptimizerJson.MaxLines + 1));
        JsonObject r = JsonNode.Parse(OptimizerJson.Dispatch("analyze", new JsonObject { ["text"] = big }.ToJsonString()))!.AsObject();
        Assert.False((bool)r["ok"]!);
        Assert.Contains("50 mil", (string)r["error"]!);
        Assert.False((bool)JsonNode.Parse(OptimizerJson.Dispatch("nada", "{}"))!["ok"]!);
    }

    [Fact]
    public void AnonimizarTrocaComentariosMantendoAsLinhas()
    {
        JsonObject r = JsonNode.Parse(OptimizerJson.Dispatch("anonymize", new JsonObject { ["text"] = "(CLIENTE SECRETO)\nG01 X1 F100\nM30" }.ToJsonString()))!.AsObject();
        string text = (string)r["text"]!;
        Assert.DoesNotContain("SECRETO", text);
        Assert.Equal(3, text.Split('\n').Length);
    }
}
