using System.Globalization;

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
    /// <summary>Mensagem do primeiro dado inválido, ou null se está tudo certo.</summary>
    public string? Validate()
    {
        if (ProgramTime < TimeSpan.Zero || ToolChanges < 0)
        {
            return "Tempo do programa ou trocas de ferramenta inválidos.";
        }

        if (Quantity < 1)
        {
            return "A quantidade precisa ser de pelo menos 1 peça.";
        }

        (string Name, double Value)[] nonNegative =
        [
            ("Hora-máquina", MachineRatePerHour), ("Preparo", SetupHours), ("Tempo de troca de ferramenta", ToolChangeSeconds),
            ("Carga e descarga", LoadUnloadSeconds), ("Folga de segurança", SafetyPercent), ("Material", MaterialPerPart),
            ("Desgaste de ferramenta", ToolWearPerPart), ("Margem", MarginPercent), ("Impostos", TaxPercent),
        ];
        foreach ((string name, double value) in nonNegative)
        {
            if (!double.IsFinite(value) || value < 0)
            {
                return $"\"{name}\" precisa ser um número maior ou igual a zero.";
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

    private static string Cycle(TimeSpan t) => t.TotalMinutes >= 1 ? $"{(int)t.TotalMinutes} min {t.Seconds:00} s" : $"{t.TotalSeconds:0.#} s";
}

/// <summary>Cálculo de tempo de ciclo e preço. Estimativa simples: confira com a primeira peça cortada.</summary>
public static class QuoteCalculator
{
    /// <summary>Tempo de ciclo por peça: programa + trocas de ferramenta, com folga, mais carga e descarga.</summary>
    public static TimeSpan CycleTime(QuoteInput i)
    {
        double seconds = (i.ProgramTime.TotalSeconds + i.ToolChanges * i.ToolChangeSeconds) * (1 + i.SafetyPercent / 100) + i.LoadUnloadSeconds;
        return TimeSpan.FromSeconds(seconds);
    }

    public static (QuoteResult? Result, string? Error) Calculate(QuoteInput i)
    {
        if (i.Validate() is { } error)
        {
            return (null, error);
        }

        TimeSpan cycle = CycleTime(i);
        double machine = cycle.TotalHours * i.MachineRatePerHour;
        double setup = i.SetupHours * i.MachineRatePerHour / i.Quantity;
        double cost = machine + setup + i.MaterialPerPart + i.ToolWearPerPart;
        double margin = cost * i.MarginPercent / 100;
        double beforeTax = cost + margin;
        double price = beforeTax / (1 - i.TaxPercent / 100);
        return (new QuoteResult(cycle, machine, setup, i.MaterialPerPart, i.ToolWearPerPart, cost, margin, price - beforeTax, price, price * i.Quantity, i.Quantity), null);
    }
}
