using CncStudio.Core.Dialects;
using CncStudio.Core.Generators;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;
using Xunit;

namespace CncStudio.Core.Tests;

public class GeneratorTests
{
    private static IReadOnlyDictionary<string, double> With(ProgramGenerator g, params (string Id, double Value)[] changes)
    {
        var values = g.Defaults.ToDictionary(p => p.Key, p => p.Value);
        foreach (var (id, value) in changes)
        {
            values[id] = value;
        }

        return values;
    }

    private static ProgramAnalysis Analyze(string text)
    {
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(text, Dialect.Fanuc);
        Assert.DoesNotContain(analysis.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return analysis;
    }

    [Fact]
    public void CatalogListsTheTwoGenerators()
    {
        Assert.Equal(["bhc", "pocket"], Generators.Generators.All.Select(g => g.Id));
        Assert.NotNull(Generators.Generators.ById("bhc"));
        Assert.Null(Generators.Generators.ById("nao-existe"));
    }

    [Theory]
    [InlineData(GeneratorOutput.Plain)]
    [InlineData(GeneratorOutput.MacroCall)]
    public void BhcDrillsSixHolesOnTheCircle(GeneratorOutput output)
    {
        var g = new BhcGenerator();
        GeneratorResult result = g.Generate(With(g, ("X", 50), ("Y", 40)), output);
        Assert.True(result.Ok, string.Join("; ", result.Errors));

        ProgramAnalysis analysis = Analyze(result.Program);
        var bottoms = analysis.ToolPath.Moves.Where(m => m.End.Z < -11.9).Select(m => (Math.Round(m.End.X, 2), Math.Round(m.End.Y, 2))).Distinct().ToList();
        Assert.Equal(6, bottoms.Count);
        foreach (var (x, y) in bottoms)
        {
            Assert.Equal(20, Math.Sqrt((x - 50) * (x - 50) + (y - 40) * (y - 40)), 1);
        }

        Assert.Contains((70.0, 40.0), bottoms);
        Assert.Equal(-12, analysis.ToolPath.Moves.Min(m => m.End.Z), 6);
    }

    [Fact]
    public void BhcPlainAndMacroDrillTheSameHoles()
    {
        var g = new BhcGenerator();
        var values = With(g, ("X", 12.5), ("Y", -7), ("N", 5), ("A", 30), ("Q", 0));
        List<(double X, double Y)> Holes(GeneratorOutput o) =>
            [.. Analyze(g.Generate(values, o).Program).ToolPath.Moves.Where(m => m.End.Z < -11.9).Select(m => (m.End.X, m.End.Y)).DistinctBy(p => (Math.Round(p.X, 1), Math.Round(p.Y, 1)))
                .OrderBy(p => Math.Atan2(p.Y + 7, p.X - 12.5))];

        List<(double X, double Y)> plain = Holes(GeneratorOutput.Plain), macro = Holes(GeneratorOutput.MacroCall);
        Assert.Equal(5, plain.Count);
        Assert.Equal(5, macro.Count);
        for (int i = 0; i < 5; i++)
        {
            // o programa pronto escreve 3 casas; a macro calcula sem arredondar
            Assert.InRange(Math.Abs(plain[i].X - macro[i].X), 0, 0.01);
            Assert.InRange(Math.Abs(plain[i].Y - macro[i].Y), 0, 0.01);
        }
    }

    [Fact]
    public void BhcUsesG81WithoutPeckAndG83WithPeck()
    {
        var g = new BhcGenerator();
        Assert.Contains("G81 ", g.Generate(With(g, ("Q", 0)), GeneratorOutput.Plain).Program);
        string withPeck = g.Generate(With(g, ("Q", 3)), GeneratorOutput.Plain).Program;
        Assert.Contains("G83 ", withPeck);
        Assert.Contains("Q3.", withPeck);
    }

    [Theory]
    [InlineData(GeneratorOutput.Plain)]
    [InlineData(GeneratorOutput.MacroCall)]
    public void PocketCutsEveryLayerAndEveryRowAndStaysInsideTheWalls(GeneratorOutput output)
    {
        var g = new PocketGenerator();
        GeneratorResult result = g.Generate(With(g, ("X", 50), ("Y", 40)), output);
        Assert.True(result.Ok, string.Join("; ", result.Errors));

        ProgramAnalysis analysis = Analyze(result.Program);
        var cuts = analysis.ToolPath.Moves.Where(m => !m.IsRapid && m.End.Z < -0.001).ToList();
        Assert.Equal(-4, cuts.Min(m => m.End.Z), 6);
        // 4 passes de 1 mm; linhas em Y 35.2, 40.2 e 44.8 (a última encostada no limite)
        Assert.Equal([35.2, 40.2, 44.8], cuts.Select(m => Math.Round(m.End.Y, 2)).Distinct().Order().ToArray());
        // centro da fresa respeita as paredes (X 36.2..63.8 → 50 ± (15 − 5 − 0,2)... )
        Assert.InRange(cuts.Min(m => m.End.X), 40.2 - 1e-6, 40.2 + 1e-6);
        Assert.InRange(cuts.Max(m => m.End.X), 59.8 - 1e-6, 59.8 + 1e-6);
        Assert.Equal(4, cuts.Select(m => Math.Round(m.End.Z, 3)).Distinct().Count());
    }

    [Fact]
    public void PocketPlainAndMacroMakeTheSameCuttingMoves()
    {
        var g = new PocketGenerator();
        var values = With(g, ("X", -10), ("Y", 5), ("I", 41), ("J", 27), ("Z", -3.5), ("Q", 1.5), ("D", 8), ("U", 0.3));
        List<(double, double, double)> Cuts(GeneratorOutput o) =>
            [.. Analyze(g.Generate(values, o).Program).ToolPath.Moves.Where(m => !m.IsRapid).Select(m => (Math.Round(m.End.X, 3), Math.Round(m.End.Y, 3), Math.Round(m.End.Z, 3)))];

        List<(double, double, double)> plain = Cuts(GeneratorOutput.Plain);
        Assert.NotEmpty(plain);
        Assert.Equal(plain, Cuts(GeneratorOutput.MacroCall));
        Assert.Equal(-3.5, plain.Min(c => c.Item3), 6);
    }

    [Fact]
    public void InvalidValuesGiveReadableErrorsAndNoProgram()
    {
        var g = new PocketGenerator();
        GeneratorResult small = g.Generate(With(g, ("I", 10), ("D", 10)), GeneratorOutput.Plain);
        Assert.False(small.Ok);
        Assert.Equal("", small.Program);
        Assert.Contains(small.Errors, e => e.StartsWith("Comprimento:") && e.Contains("fresa"));

        var b = new BhcGenerator();
        Assert.Contains(b.Generate(With(b, ("N", 1)), GeneratorOutput.Plain).Errors, e => e.StartsWith("Número de furos:"));
        Assert.Contains(b.Generate(With(b, ("N", 5.5)), GeneratorOutput.Plain).Errors, e => e.Contains("inteiro"));
        Assert.Contains(b.Generate(With(b, ("Z", 3)), GeneratorOutput.Plain).Errors, e => e.StartsWith("Fundo:"));
        Assert.Contains(b.Generate(With(b, ("Q", 20), ("Z", -12)), GeneratorOutput.Plain).Errors, e => e.Contains("Picada"));
    }

    [Fact]
    public void FanucNumbersAlwaysCarryADecimalPoint()
    {
        var g = new BhcGenerator();
        string program = g.Generate(g.Defaults, GeneratorOutput.Plain).Program;
        Assert.Contains("Z-12.", program);
        Assert.Contains("R2.", program);
        Assert.DoesNotContain("-0.", program.Replace("-0.5", ""));
    }
}

public class GeneratorsJsonTests
{
    [Fact]
    public void CatalogListsGeneratorsWithTheirFields()
    {
        var catalog = System.Text.Json.Nodes.JsonNode.Parse(GeneratorsJson.Catalog())!.AsArray();
        Assert.Equal(2, catalog.Count);
        Assert.Equal("bhc", (string?)catalog[0]!["id"]);
        Assert.Contains(catalog[0]!["params"]!.AsArray(), p => (string?)p!["id"] == "N" && (bool)p["integer"]!);
    }

    [Fact]
    public void GenerateReturnsProgramOrErrors()
    {
        string ok = GeneratorsJson.Generate("bhc", """{"X":0,"Y":0,"D":40,"N":6,"A":0,"Z":-12,"R":2,"Q":0,"F":90,"S":1100,"T":1}""", "plain");
        var okNode = System.Text.Json.Nodes.JsonNode.Parse(ok)!;
        Assert.True((bool)okNode["ok"]!);
        Assert.Contains("G81", (string?)okNode["program"]);

        string bad = GeneratorsJson.Generate("bhc", """{"X":null,"N":6}""", "plain");
        var badNode = System.Text.Json.Nodes.JsonNode.Parse(bad)!;
        Assert.False((bool)badNode["ok"]!);
        Assert.NotEmpty(badNode["errors"]!.AsArray());

        Assert.False((bool)System.Text.Json.Nodes.JsonNode.Parse(GeneratorsJson.Generate("x", "{}", "plain"))!["ok"]!);
        Assert.Contains("G65 P9010", (string?)System.Text.Json.Nodes.JsonNode.Parse(
            GeneratorsJson.Generate("bhc", """{"X":0,"Y":0,"D":40,"N":6,"A":0,"Z":-12,"R":2,"Q":3,"F":90,"S":1100,"T":1}""", "macro"))!["program"]);
    }

    [Fact]
    public void RememberLastKeepsValuesPerGenerator()
    {
        string first = GeneratorsJson.RememberLast(null, "bhc", "macro", System.Text.Json.Nodes.JsonNode.Parse("""{"X":5}"""));
        string second = GeneratorsJson.RememberLast(first, "pocket", "plain", System.Text.Json.Nodes.JsonNode.Parse("""{"I":30}"""));
        var node = System.Text.Json.Nodes.JsonNode.Parse(second)!;
        Assert.Equal("pocket", (string?)node["id"]);
        Assert.Equal(5, (double)node["values"]!["bhc"]!["X"]!);
        Assert.Equal(30, (double)node["values"]!["pocket"]!["I"]!);
        // JSON quebrado no armazenamento não derruba
        Assert.Equal("bhc", (string?)System.Text.Json.Nodes.JsonNode.Parse(GeneratorsJson.RememberLast("{ruim", "bhc", "plain", null))!["id"]);
    }
}
