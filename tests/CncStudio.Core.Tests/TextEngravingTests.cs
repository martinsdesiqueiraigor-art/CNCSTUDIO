using CncStudio.Core.Dialects;
using CncStudio.Core.Generators;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>Gerador "Texto (gravação)": letras em traço único.</summary>
public class TextEngravingTests
{
    private static readonly TextEngravingGenerator G = new();

    private static IReadOnlyDictionary<string, double> With(params (string Id, double Value)[] changes)
    {
        var values = G.Defaults.ToDictionary(p => p.Key, p => p.Value);
        foreach (var (id, value) in changes)
        {
            values[id] = value;
        }

        return values;
    }

    private static GeneratorResult Make(string text, params (string Id, double Value)[] changes) =>
        G.Generate(With(changes), new Dictionary<string, string> { ["TX"] = text }, GeneratorOutput.Plain);

    private static List<Move> Cuts(string text, params (string Id, double Value)[] changes)
    {
        GeneratorResult r = Make(text, changes);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        ProgramAnalysis a = ProgramAnalyzer.Analyze(r.Program, Dialect.Fanuc);
        Assert.DoesNotContain(a.Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
        return [.. a.ToolPath.Moves.Where(m => !m.IsRapid && m.End.Z < 0 && Math.Abs(m.End.Z - m.Start.Z) < 1e-9)];
    }

    [Fact]
    public void DefaultsGenerateProgramWithoutErrors()
    {
        GeneratorResult r = G.Generate(G.Defaults, GeneratorOutput.Plain);
        Assert.True(r.Ok, string.Join("; ", r.Errors));
        Assert.Contains("CASILLAS", r.Program);
        Assert.StartsWith("%", r.Program);
        Assert.False(G.SupportsMacro);
    }

    [Fact]
    public void EveryCharacterOfTheFontCanBeEngraved()
    {
        Assert.NotEmpty(Cuts(StrokeFont.Supported.Replace(" ", "")));
    }

    [Fact]
    public void LettersStayInsideTheirHeight()
    {
        var moves = Cuts("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", ("X", 0), ("Y", 0), ("H", 10), ("N", 0));
        Assert.All(moves, m => Assert.InRange(m.End.Y, -0.05, 10.05));
        Assert.True(moves.Min(m => m.End.X) >= -0.05);
    }

    [Fact]
    public void HeightScalesTheText()
    {
        double small = Cuts("HO", ("H", 5)).Max(m => m.End.Y);
        double big = Cuts("HO", ("H", 20)).Max(m => m.End.Y);
        Assert.Equal(4, big / small, 1);
    }

    [Fact]
    public void AlignmentMovesTheTextAroundTheOrigin()
    {
        var left = Cuts("TESTE", ("N", 0));
        var center = Cuts("TESTE", ("N", 1));
        var right = Cuts("TESTE", ("N", 2));
        Assert.True(left.Min(m => m.End.X) >= -0.05);
        Assert.True(right.Max(m => m.End.X) <= 0.05);
        double mid = (center.Min(m => m.End.X) + center.Max(m => m.End.X)) / 2;
        Assert.InRange(Math.Abs(mid), 0, 2.0);
    }

    [Fact]
    public void AngleRotatesTheText()
    {
        var up = Cuts("IIIII", ("A", 90));
        Assert.True(up.Max(m => m.End.Y) - up.Min(m => m.End.Y) > 15);
        Assert.True(up.Max(m => m.End.X) - up.Min(m => m.End.X) < 12);
    }

    [Fact]
    public void DepthIsReachedInPassesAndNeverAboveStock()
    {
        GeneratorResult r = Make("I", ("W", 2), ("Z", -1), ("Q", 0.5));
        Assert.True(r.Ok);
        ProgramAnalysis a = ProgramAnalyzer.Analyze(r.Program, Dialect.Fanuc);
        var cutZ = a.ToolPath.Moves.Where(m => !m.IsRapid).Select(m => Math.Round(m.End.Z, 3)).Distinct().ToList();
        Assert.Equal(-1, cutZ.Min());
        Assert.Contains(1.0, cutZ);
        Assert.All(a.ToolPath.Moves.Where(m => !m.IsRapid), m => Assert.True(m.End.Z <= 2.0 + 0.01 || m.Start.Z >= m.End.Z));
    }

    [Fact]
    public void AccentsAndLowercaseAreNormalized()
    {
        Assert.Equal(Make("ACAO").Program.Replace("ACAO", "X"), Make("ação").Program.Replace("AÇÃO", "X").Replace("ACAO", "X"));
        Assert.Contains("(TEXTO: ACAO)", Make("ação").Program);
    }

    [Fact]
    public void ParenthesesInTextDoNotBreakTheComment()
    {
        GeneratorResult r = Make("(A)");
        Assert.True(r.Ok);
        Assert.DoesNotContain("(TEXTO: (", r.Program);
        Assert.DoesNotContain(ProgramAnalyzer.Analyze(r.Program, Dialect.Fanuc).Diagnostics, d => d.Severity == DiagnosticSeverity.Error);
    }

    [Fact]
    public void ErrorsForEmptyUnsupportedAndTooLongText()
    {
        Assert.Contains(Make("   ").Errors, e => e.StartsWith("Texto:"));
        Assert.Contains(Make("A@B").Errors, e => e.Contains("\"@\""));
        Assert.Contains(Make(new string('A', 61)).Errors, e => e.Contains("no máximo"));
    }

    [Fact]
    public void RuleDepthMustBeBelowStart()
    {
        Assert.False(Make("A", ("W", -5), ("Z", -3)).Ok);
    }

    [Fact]
    public void JsonBridgeAcceptsTextAndReportsNoMacro()
    {
        string ok = GeneratorsJson.Generate("text", """{"TX":"Oi","X":0,"Y":0,"H":10,"L":100,"E":20,"A":0,"N":0,"K":0,"M":0,"W":0,"Z":-0.3,"Q":0.3,"R":60,"F":200,"S":8000,"T":1}""", "macro");
        var node = System.Text.Json.Nodes.JsonNode.Parse(ok)!;
        Assert.True((bool)node["ok"]!, ok);
        Assert.DoesNotContain("G65", (string)node["program"]!);
        var catalog = System.Text.Json.Nodes.JsonNode.Parse(GeneratorsJson.Catalog())!.AsArray();
        var entry = catalog.Single(e => (string)e!["id"]! == "text")!;
        Assert.False((bool)entry["macro"]!);
        Assert.Equal("TX", (string)entry["texts"]![0]!["id"]!);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ArcTextStaysOnTheRing(int side)
    {
        double radius = 50, h = 8;
        var moves = Cuts("CASILLAS 2026", ("X", 10), ("Y", 20), ("K", radius), ("M", side), ("H", h), ("N", 1), ("A", side == 0 ? 90 : 270));
        var dist = moves.Select(m => Math.Sqrt(Math.Pow(m.End.X - 10, 2) + Math.Pow(m.End.Y - 20, 2))).ToList();
        double lo = side == 0 ? radius : radius - h, hi = side == 0 ? radius + h : radius;
        Assert.All(dist, d => Assert.InRange(d, lo - 0.05, hi + 0.05));
        // meio do texto fica na direção pedida (em cima ou embaixo)
        double midX = (moves.Min(m => m.End.X) + moves.Max(m => m.End.X)) / 2;
        Assert.InRange(midX, 10 - 8, 10 + 8);
        Assert.True(side == 0 ? moves.Max(m => m.End.Y) > 20 + radius : moves.Min(m => m.End.Y) < 20 - radius + h);
    }

    [Fact]
    public void ArcTextCentersOnTheGivenAngle()
    {
        var moves = Cuts("OOOOO", ("K", 40), ("A", 0), ("N", 1));
        Assert.InRange((moves.Min(m => m.End.Y) + moves.Max(m => m.End.Y)) / 2, -3, 3);
        Assert.True(moves.Min(m => m.End.X) > 30);
    }

    [Fact]
    public void ArcInsideNeedsHeightBelowRadius()
    {
        Assert.Contains(Make("A", ("K", 5), ("M", 1), ("H", 10)).Errors, e => e.Contains("menor que o raio"));
    }
}
