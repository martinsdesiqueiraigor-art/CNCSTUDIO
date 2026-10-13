using System.Globalization;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Reports;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

public class SetupSheetTests
{
    private const string Mill = """
        %
        O1001 (FLANGE TESTE)
        (MATERIAL SAE 1045)
        (BRUTO 100 X 80 X 20)
        G21 G90 G54
        T1 M06 (FRESA TOPO D10)
        S3000 M03
        G00 X0 Y0 Z5
        G01 Z-2 F200
        X50 F600
        G00 Z5
        T2 M06 (BROCA D6 <HSS>)
        G55
        G00 X10 Y10
        G81 Z-15 R2 F100
        G80
        M30
        %
        """;

    private static (SetupSheet Sheet, ProgramAnalysis Analysis) Build(string text, Dialect dialect = Dialect.Fanuc,
        Func<int, (double, string)?>? table = null)
    {
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(text, dialect);
        SetupSheet sheet = SetupSheetBuilder.Build(text, analysis, "FLANGE.nc", @"C:\CNC\FLANGE.nc",
            table ?? (t => t == 1 ? (10.0, "Fresa tabela") : null), "100 × 80 × 20 mm", new DateTime(2026, 10, 12, 9, 30, 0));
        return (sheet, analysis);
    }

    [Fact]
    public void LeAsInformacoesDoProgramaAtual()
    {
        (SetupSheet sheet, _) = Build(Mill);

        Assert.Equal("1001", sheet.ProgramNumber);
        Assert.Equal(["FLANGE TESTE", "MATERIAL SAE 1045", "BRUTO 100 X 80 X 20"], sheet.HeaderComments);
        Assert.Equal(["G54", "G55"], sheet.WorkOffsets);
        Assert.Equal(MachineKind.Mill, sheet.Machine);
        Assert.Equal("100 × 80 × 20 mm", sheet.Stock);
    }

    [Fact]
    public void ListaFerramentasComDescricaoDoProgramaETempo()
    {
        (SetupSheet sheet, ProgramAnalysis analysis) = Build(Mill);

        Assert.Equal([1, 2], sheet.Tools.Select(t => t.Number));
        SetupTool t1 = sheet.Tools[0];
        Assert.Equal("FRESA TOPO D10", t1.Description);   // comentário do programa tem prioridade
        Assert.Equal(10, t1.Diameter);
        Assert.Equal(-2, t1.Reach);
        SetupTool t2 = sheet.Tools[1];
        Assert.Equal("BROCA D6 <HSS>", t2.Description);
        Assert.Null(t2.Diameter);
        Assert.Equal(-15, t2.Reach);

        // Tempo por ferramenta soma o total
        Assert.Equal(analysis.Statistics.TotalTime.TotalSeconds, sheet.Tools.Sum(t => t.Statistics.TotalTime.TotalSeconds), 6);
        Assert.Equal(1, sheet.Tools.Sum(t => t.Share), 6);
    }

    [Fact]
    public void HtmlTemTabelaDeFerramentasTotalEDesenho()
    {
        (SetupSheet sheet, ProgramAnalysis analysis) = Build(Mill);
        List<PathPolyline> figures = PathPolylineBuilder.Build(analysis.ToolPath.Moves, 0.01);
        string html = SetupSheetHtml.Render(sheet, figures, new CultureInfo("pt-BR"));

        Assert.Contains("FOLHA DE SETUP", html);
        Assert.Contains("1001 — FLANGE.nc", html);
        Assert.Contains("FRESA TOPO D10", html);
        Assert.Contains("BROCA D6 &lt;HSS&gt;", html);       // texto do programa é escapado
        Assert.Contains("Total (2 ferramenta(s))", html);
        Assert.Contains(DurationFormatFor(analysis), html);
        Assert.Equal(2, CountOf(html, "<svg"));               // fresa: superior e frontal
        Assert.Contains("<polyline", html);
    }

    [Fact]
    public void TornoMostraDiametroEUmDesenho()
    {
        string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "samples", "torno", "EXEMPLO_TORNO.nc"));
        (SetupSheet sheet, ProgramAnalysis analysis) = Build(text, table: _ => null);
        string html = SetupSheetHtml.Render(sheet, PathPolylineBuilder.Build(analysis.ToolPath.Moves, 0.01), new CultureInfo("pt-BR"));

        Assert.Equal(MachineKind.Lathe, sheet.Machine);
        Assert.Equal("DESBASTE EXTERNO R0.8", sheet.Tools.Single(t => t.Number == 1).Description);
        Assert.Equal(16, sheet.Tools.Single(t => t.Number == 1).Reach!.Value, 3); // menor Ø de corte
        Assert.Contains("Menor Ø", html);
        Assert.Equal(1, CountOf(html, "<svg"));
    }

    [Fact]
    public void OperacaoNaLinhaDoTECodigoNaLinhaDeBaixo()
    {
        const string text = """
            %
            O1234 (PADRAO DE FERRAMENTA)
            G21 G18 G99
            T0101 (DESBASTE EXTERNO)
            (WNML 2525-WNMG 1608)
            G96 S180 M03
            G00 X40 Z2
            G01 Z-20 F0.2
            T0202 (ACABAMENTO)
            G00 X38 Z2
            G01 Z-20 F0.1
            M30
            """;
        (SetupSheet sheet, ProgramAnalysis analysis) = Build(text, table: _ => null);

        SetupTool t1 = sheet.Tools.Single(t => t.Number == 1);
        Assert.Equal("DESBASTE EXTERNO", t1.Description);
        Assert.Equal("WNML 2525-WNMG 1608", t1.Code);
        Assert.Null(sheet.Tools.Single(t => t.Number == 2).Code); // linha de baixo não é só comentário

        string html = SetupSheetHtml.Render(sheet, PathPolylineBuilder.Build(analysis.ToolPath.Moves, 0.01),
            new CultureInfo("pt-BR"), printHint: false, watermark: "VERSÃO DE AVALIAÇÃO");
        Assert.Contains("WNML 2525-WNMG 1608", html);
        Assert.Contains("class=\"wm\"", html); // marca d'água (texto vai codificado em HTML)
        Assert.Contains("Casillas Code", html);
        Assert.DoesNotContain("Ctrl+P", html);
    }

    private static string DurationFormatFor(ProgramAnalysis analysis) =>
        CncStudio.Core.Statistics.DurationFormat.Format(analysis.Statistics.TotalTime, new CultureInfo("pt-BR"));

    private static int CountOf(string text, string piece)
    {
        int count = 0;
        for (int i = text.IndexOf(piece, StringComparison.Ordinal); i >= 0; i = text.IndexOf(piece, i + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
