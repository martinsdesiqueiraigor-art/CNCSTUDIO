using CncStudio.Core.Assist;

namespace CncStudio.Core.Tests;

public class QuoteTests
{
    private static QuoteInput Base() => new(TimeSpan.FromMinutes(10), 4, 10, 120, SetupHours: 1, MaterialPerPart: 8, ToolWearPerPart: 2, MarginPercent: 25, TaxPercent: 0);

    [Fact]
    public void Tempo_de_ciclo_soma_trocas_folga_e_carga()
    {
        // (600 s + 4 × 6 s) × 1,10 + 30 s = 716,4 s
        TimeSpan t = QuoteCalculator.CycleTime(Base());
        Assert.Equal(716.4, t.TotalSeconds, 3);
    }

    [Fact]
    public void Preco_soma_as_parcelas_e_aplica_margem()
    {
        (QuoteResult? r, string? error) = QuoteCalculator.Calculate(Base());
        Assert.Null(error);
        double machine = 716.4 / 3600 * 120; // 23,88
        Assert.Equal(machine, r!.MachineCostPerPart, 6);
        Assert.Equal(12, r.SetupCostPerPart, 6); // 1 h × 120 / 10 peças
        double cost = machine + 12 + 8 + 2;
        Assert.Equal(cost, r.CostPerPart, 6);
        Assert.Equal(cost * 1.25, r.PricePerPart, 6);
        Assert.Equal(cost * 1.25 * 10, r.Total, 6);
        Assert.Equal(0, r.TaxPerPart, 9);
    }

    [Fact]
    public void Imposto_incide_sobre_o_preco_de_venda()
    {
        (QuoteResult? r, _) = QuoteCalculator.Calculate(Base() with { TaxPercent = 6 });
        double beforeTax = r!.CostPerPart + r.MarginPerPart;
        Assert.Equal(beforeTax / 0.94, r.PricePerPart, 6);
        Assert.Equal(r.PricePerPart * 0.06, r.TaxPerPart, 6);
    }

    [Fact]
    public void Preparo_dilui_no_lote()
    {
        (QuoteResult? one, _) = QuoteCalculator.Calculate(Base() with { Quantity = 1 });
        (QuoteResult? many, _) = QuoteCalculator.Calculate(Base() with { Quantity = 100 });
        Assert.Equal(120, one!.SetupCostPerPart, 6);
        Assert.Equal(1.2, many!.SetupCostPerPart, 6);
        Assert.True(many.PricePerPart < one.PricePerPart);
    }

    [Theory]
    [InlineData(0, 120, 0)]
    [InlineData(10, -1, 0)]
    [InlineData(10, 120, 100)]
    [InlineData(10, double.NaN, 0)]
    public void Dados_invalidos_nao_geram_orcamento(int quantity, double rate, double tax)
    {
        (QuoteResult? r, string? error) = QuoteCalculator.Calculate(Base() with { Quantity = quantity, MachineRatePerHour = rate, TaxPercent = tax });
        Assert.Null(r);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Texto_da_proposta_diz_que_e_estimativa_e_nao_promete_prazo()
    {
        (QuoteResult? r, _) = QuoteCalculator.Calculate(Base());
        string text = r!.ToText("Flange");
        Assert.Contains("estimativa", text);
        Assert.Contains("Flange", text);
        Assert.Contains("11 min 56 s", text);
        Assert.DoesNotContain("prazo", text);
    }
}
