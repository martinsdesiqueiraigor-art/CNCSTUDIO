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

    [Fact]
    public void Proposta_html_mostra_preco_escapa_texto_e_esconde_custos_internos()
    {
        (QuoteResult? r, _) = QuoteCalculator.Calculate(Base());
        string html = r!.ToHtml("Flange <A&B>", "Usinagem X\nTel 19 9999", new DateOnly(2026, 10, 28));
        Assert.Contains("Flange &lt;A&amp;B&gt;", html);
        Assert.Contains("Usinagem X<br>Tel 19 9999", html);
        Assert.Contains("28/10/2026", html);
        Assert.Contains("R$ " + r.PricePerPart.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")), html);
        Assert.DoesNotContain("Margem", html);
        Assert.DoesNotContain("Hora-máquina", html);
        Assert.DoesNotContain("<script", html);
    }
}

public class QuoteLimitsTests
{
    private static QuoteInput Base(int qty = 10, double rate = 120) =>
        new(TimeSpan.FromMinutes(10), 4, qty, rate, SetupHours: 1, MaterialPerPart: 8, ToolWearPerPart: 2, MarginPercent: 25);

    [Theory]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    [InlineData(1_000_000, 1_000_000)]
    public void QuantidadeInteiraValidaPassa(double typed, int expected)
    {
        (int q, string? e) = QuoteInput.ParseQuantity(typed);
        Assert.Null(e);
        Assert.Equal(expected, q);
    }

    [Theory]
    [InlineData(2.5)]               // não arredonda para 2
    [InlineData(0.4)]               // não vira 0 nem 1
    [InlineData(0)]
    [InlineData(-3)]
    [InlineData(1_000_001)]
    [InlineData(1e300)]             // o cast (int) de um valor assim é indefinido
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void QuantidadeInvalidaEhRecusadaSemArredondar(double typed)
    {
        (int q, string? e) = QuoteInput.ParseQuantity(typed);
        Assert.Equal(0, q);
        Assert.False(string.IsNullOrWhiteSpace(e));
    }

    [Fact]
    public void LoteAcimaDoMaximoEhRecusadoNoCalculo()
    {
        (QuoteResult? r, string? e) = QuoteCalculator.Calculate(Base(qty: QuoteInput.MaxQuantity + 1));
        Assert.Null(r);
        Assert.NotNull(e);
    }

    [Theory]
    [InlineData(1e308)]             // hora-máquina que estoura o preço
    [InlineData(1e300)]             // finito, mas absurdo: recusado por plausibilidade
    [InlineData(100_001)]
    [InlineData(double.MaxValue)]
    public void ValorEnormeNaoViraPrecoInfinito(double rate)
    {
        (QuoteResult? r, string? e) = QuoteCalculator.Calculate(Base(rate: rate));
        Assert.Null(r);
        Assert.False(string.IsNullOrWhiteSpace(e));
    }

    [Fact]
    public void TempoDeTrocaEnormeNaoLancaExcecao()
    {
        var input = Base() with { ToolChangeSeconds = 1e300 };
        (QuoteResult? r, string? e) = QuoteCalculator.Calculate(input);
        Assert.Null(r);
        Assert.False(string.IsNullOrWhiteSpace(e));
    }

    [Fact]
    public void ImpostoMuitoPertoDe100NaoEstoura()
    {
        var input = Base() with { TaxPercent = 99.99999999999999, MaterialPerPart = 100_000_000 };
        (QuoteResult? r, string? e) = QuoteCalculator.Calculate(input);
        Assert.True(r is null ? !string.IsNullOrWhiteSpace(e) : double.IsFinite(r.PricePerPart) && double.IsFinite(r.Total));
    }

    [Fact]
    public void ValoresNoLimiteAindaCalculam()
    {
        var input = Base(rate: 100_000) with { MaterialPerPart = 100_000_000, SetupHours = 10_000, MarginPercent = 1_000, TaxPercent = 99.9 };
        (QuoteResult? r, string? e) = QuoteCalculator.Calculate(input);
        Assert.Null(e);
        Assert.True(double.IsFinite(r!.Total));
    }

    [Fact]
    public void ResultadoSempreFinitoQuandoHaResultado()
    {
        (QuoteResult? r, string? e) = QuoteCalculator.Calculate(Base());
        Assert.Null(e);
        Assert.True(double.IsFinite(r!.PricePerPart) && double.IsFinite(r.Total) && r.PricePerPart > 0);
    }
}
