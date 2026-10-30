using System.Globalization;
using System.Net;

namespace CncStudio.Core.Assist;

/// <summary>
/// Dados do orçamento. Tempo vem da simulação do programa (<c>ProgramStatistics</c>); os valores em dinheiro são do usinador.
/// Percentuais em 0–100. Impostos incidem sobre o preço de venda (o preço sobe para que sobre a margem desejada).
/// </summary>
public sealed record QuoteInput(
    TimeSpan ProgramTime,
    int ToolChanges,
    int Quantity,
    double MachineRatePerHour,
    double SetupHours = 0,
    double ToolChangeSeconds = 6,
    double LoadUnloadSeconds = 30,
    double SafetyPercent = 10,
    double MaterialPerPart = 0,
    double ToolWearPerPart = 0,
    double MarginPercent = 20,
    double TaxPercent = 0)
{
    /// <summary>Maior lote aceito. Acima disso quase certamente é erro de digitação.</summary>
    public const int MaxQuantity = 1_000_000;

    /// <summary>
    /// Converte a quantidade digitada (número real) em peças inteiras <b>sem arredondar em silêncio</b> (auditoria A-06): 2,5 é
    /// recusado, não vira 2; NaN, infinito, zero, negativo e acima de <see cref="MaxQuantity"/> também.
    /// </summary>
    public static (int Quantity, string? Error) ParseQuantity(double typed)
    {
        if (!double.IsFinite(typed))
        {
            return (0, "Digite a quantidade do lote como um número inteiro de peças.");
        }

        if (typed != Math.Floor(typed))
        {
            return (0, $"A quantidade precisa ser um número inteiro de peças (você digitou {typed.ToString("0.###", CultureInfo.GetCultureInfo("pt-BR"))}).");
        }

        if (typed < 1)
        {
            return (0, "A quantidade precisa ser de pelo menos 1 peça.");
        }

        return typed > MaxQuantity
            ? (0, "A quantidade passa de 1 milhão de peças; confira o número digitado.")
            : ((int)typed, null);
    }

    /// <summary>Mensagem do primeiro dado inválido, ou null se está tudo certo.</summary>
    public string? Validate()
    {
        if (ProgramTime < TimeSpan.Zero || ToolChanges < 0)
        {
            return "Tempo do programa ou trocas de ferramenta inválidos.";
        }

        if (Quantity < 1 || Quantity > MaxQuantity)
        {
            return Quantity < 1 ? "A quantidade precisa ser de pelo menos 1 peça." : "A quantidade passa de 1 milhão de peças; confira o número digitado.";
        }

        // Limites de plausibilidade (A-07): acima disso é erro de digitação, e valores enormes ainda finitos viram preço absurdo
        (string Name, double Value, double Max, string Unit)[] limits =
        [
            ("Hora-máquina", MachineRatePerHour, 100_000, "R$/h"), ("Preparo", SetupHours, 10_000, "h"),
            ("Tempo de troca de ferramenta", ToolChangeSeconds, 36_000, "s"), ("Carga e descarga", LoadUnloadSeconds, 360_000, "s"),
            ("Folga de segurança", SafetyPercent, 1_000, "%"), ("Material", MaterialPerPart, 100_000_000, "R$"),
            ("Desgaste de ferramenta", ToolWearPerPart, 100_000_000, "R$"), ("Margem", MarginPercent, 1_000, "%"), ("Impostos", TaxPercent, 100, "%"),
        ];
        foreach ((string name, double value, double max, string unit) in limits)
        {
            if (!double.IsFinite(value) || value < 0)
            {
                return $"\"{name}\" precisa ser um número maior ou igual a zero.";
            }

            if (value > max && name != "Impostos")
            {
                return $"\"{name}\" passou de {max.ToString("0.##", CultureInfo.GetCultureInfo("pt-BR"))} {unit}; confira o número digitado.";
            }
        }

        return TaxPercent >= 100 ? "Impostos precisam ser menores que 100%." : null;
    }
}

/// <summary>Resultado do orçamento: tempos, cada parcela de custo e o preço. Sempre uma estimativa.</summary>
public sealed record QuoteResult(
    TimeSpan CycleTime,
    double MachineCostPerPart,
    double SetupCostPerPart,
    double MaterialPerPart,
    double ToolWearPerPart,
    double CostPerPart,
    double MarginPerPart,
    double TaxPerPart,
    double PricePerPart,
    double Total,
    int Quantity)
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    private static string Money(double v) => "R$ " + v.ToString("N2", Br);

    /// <summary>Texto para copiar como proposta (sem prometer prazo nem tempo exato).</summary>
    public string ToText(string partName)
    {
        string name = partName.Trim().Length > 0 ? partName.Trim() : "Peça";
        return string.Join("\n",
            $"Orçamento (estimativa): {name}",
            $"Quantidade: {Quantity} peça(s)",
            $"Tempo de ciclo estimado: {Cycle(CycleTime)} por peça",
            $"Valor por peça: {Money(PricePerPart)}",
            $"Total do lote: {Money(Total)}",
            "Valores estimados; sujeitos a confirmação após a primeira peça.");
    }

    /// <summary>
    /// Proposta em HTML de uma página (imprime ou "salva como PDF" pelo navegador). Mostra só o valor por peça e o total,
    /// sem os custos internos. <paramref name="company"/>: nome/contato de quem vende (opcional).
    /// </summary>
    public string ToHtml(string partName, string company = "", DateOnly? date = null)
    {
        string E(string t) => WebUtility.HtmlEncode(t);
        string name = partName.Trim().Length > 0 ? partName.Trim() : "Peça";
        string day = (date ?? DateOnly.FromDateTime(DateTime.Today)).ToString("dd/MM/yyyy", Br);
        string from = company.Trim().Length > 0 ? $"<p class=\"from\">{E(company.Trim()).Replace("\n", "<br>")}</p>" : "";
        return $$$"""
            <!doctype html>
            <html lang="pt-BR"><head><meta charset="utf-8"><title>Proposta — {{{E(name)}}}</title>
            <style>
            body{font:15px/1.5 system-ui,Segoe UI,Arial,sans-serif;color:#111;max-width:720px;margin:32px auto;padding:0 20px}
            h1{font-size:24px;margin:0 0 4px}.from{color:#444;margin:0 0 20px}.date{color:#666;margin:0 0 20px}
            table{border-collapse:collapse;width:100%;margin:12px 0}td,th{border-bottom:1px solid #ccc;padding:8px;text-align:left}
            td.n,th.n{text-align:right}.total td{font-weight:700;font-size:17px;border-top:2px solid #111}
            .note{color:#555;font-size:13px;margin-top:24px}
            @media print{body{margin:0}}
            </style></head><body>
            <h1>Proposta comercial</h1>{{{from}}}
            <p class="date">{{{day}}}</p>
            <table>
            <tr><th>Peça</th><th class="n">Quantidade</th><th class="n">Valor por peça</th><th class="n">Total</th></tr>
            <tr><td>{{{E(name)}}}</td><td class="n">{{{Quantity}}}</td><td class="n">{{{Money(PricePerPart)}}}</td><td class="n">{{{Money(Total)}}}</td></tr>
            <tr class="total"><td colspan="3">Total do lote</td><td class="n">{{{Money(Total)}}}</td></tr>
            </table>
            <p class="note">Valores estimados, sujeitos a confirmação após a primeira peça. Prazo e condições de pagamento a combinar.</p>
            </body></html>
            """;
    }

    private static string Cycle(TimeSpan t) => t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min {t.Seconds:00} s" : $"{t.TotalSeconds:0.#} s";
}

/// <summary>Cálculo de tempo de ciclo e preço. Estimativa simples: confira com a primeira peça cortada.</summary>
public static class QuoteCalculator
{
    /// <summary>Tempo de ciclo por peça: programa + trocas de ferramenta, com folga, mais carga e descarga.</summary>
    public static TimeSpan CycleTime(QuoteInput i) => TimeSpan.FromSeconds(CycleSeconds(i));

    private static double CycleSeconds(QuoteInput i) =>
        (i.ProgramTime.TotalSeconds + i.ToolChanges * i.ToolChangeSeconds) * (1 + i.SafetyPercent / 100) + i.LoadUnloadSeconds;

    public static (QuoteResult? Result, string? Error) Calculate(QuoteInput i)
    {
        if (i.Validate() is { } error)
        {
            return (null, error);
        }

        // Entradas enormes (mas finitas) podem estourar o tempo ou o dinheiro: nunca devolver infinito/NaN como preço (A-07)
        double seconds = CycleSeconds(i);
        if (!double.IsFinite(seconds) || seconds >= TimeSpan.MaxValue.TotalSeconds)
        {
            return (null, "Os números digitados geram um tempo de ciclo fora do possível; confira os valores.");
        }

        TimeSpan cycle = TimeSpan.FromSeconds(seconds);
        double machine = cycle.TotalHours * i.MachineRatePerHour;
        double setup = i.SetupHours * i.MachineRatePerHour / i.Quantity;
        double cost = machine + setup + i.MaterialPerPart + i.ToolWearPerPart;
        double margin = cost * i.MarginPercent / 100;
        double beforeTax = cost + margin;
        double price = beforeTax / (1 - i.TaxPercent / 100);
        double total = price * i.Quantity;
        if (!new[] { machine, setup, cost, margin, beforeTax, price, total }.All(double.IsFinite) || price < 0)
        {
            return (null, "Os números digitados geram um preço fora do possível (valor infinito ou inválido); confira os valores.");
        }

        return (new QuoteResult(cycle, machine, setup, i.MaterialPerPart, i.ToolWearPerPart, cost, margin, price - beforeTax, price, total, i.Quantity), null);
    }
}
