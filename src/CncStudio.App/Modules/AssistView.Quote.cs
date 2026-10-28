using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CncStudio.App.Settings;
using CncStudio.Core.Assist;
using CncStudio.Core.Statistics;

namespace CncStudio.App.Modules;

/// <summary>Aba Orçamento do Assist: tempo de ciclo do programa aberto no Code e preço por peça. Estimativa.</summary>
public sealed partial class AssistView
{
    private readonly Dictionary<string, TextBox> _quoteBoxes = [];
    private readonly TextBlock _quoteProgram = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock _quoteResult = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private readonly TextBox _partName = new() { Text = "", Watermark = "nome da peça (opcional)", MinHeight = 26 };
    private readonly TextBox _company = new() { Watermark = "seu nome e contato (cabeçalho da proposta)", MinHeight = 26 };
    private string _lastProposal = "";
    private string _lastHtml = "";

    /// <summary>Valores guardados do orçamento (nas configurações).</summary>
    public Func<QuoteDefaults>? QuoteSource { get; set; }

    public Action? SaveQuote { get; set; }

    /// <summary>Tempo do programa simulado no Code, trocas de ferramenta e nome; null se não há programa analisado.</summary>
    public Func<(TimeSpan Time, int ToolChanges, string Name)?>? ProgramSource { get; set; }

    public Func<string, Task>? CopyText { get; set; }

    /// <summary>Salva a proposta em HTML (nome sugerido, texto); abra no navegador e imprima ou salve como PDF.</summary>
    public Func<string, string, Task>? SaveProposal { get; set; }

    private void AddQuoteTab()
    {
        var form = new StackPanel { Spacing = 6, MaxWidth = 620 };
        form.Children.Add(new TextBlock { Text = "O tempo vem do programa aberto no Code (simulação). Os valores em R$ são seus: ficam só neste computador.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.8 });
        form.Children.Add(_quoteProgram);
        form.Children.Add(_partName);
        form.Children.Add(_company);
        Add(form, "qty", "Quantidade do lote", "peças", d => d.Quantity);
        Add(form, "rate", "Hora-máquina", "R$/h", d => d.MachineRatePerHour);
        Add(form, "setup", "Preparo (setup)", "h", d => d.SetupHours);
        Add(form, "tc", "Troca de ferramenta", "s", d => d.ToolChangeSeconds);
        Add(form, "load", "Carga e descarga da peça", "s", d => d.LoadUnloadSeconds);
        Add(form, "safety", "Folga sobre o tempo simulado", "%", d => d.SafetyPercent);
        Add(form, "mat", "Material por peça", "R$", d => d.MaterialPerPart);
        Add(form, "wear", "Desgaste de ferramenta por peça", "R$", d => d.ToolWearPerPart);
        Add(form, "margin", "Margem", "%", d => d.MarginPercent);
        Add(form, "tax", "Impostos sobre a venda", "%", d => d.TaxPercent);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        Button calc = new() { Content = "Calcular", Padding = new Thickness(16, 6) };
        Button copy = new() { Content = "Copiar proposta", Padding = new Thickness(16, 6) };
        calc.Click += (_, _) => CalculateQuote();
        copy.Click += async (_, _) =>
        {
            if (_lastProposal.Length > 0 && CopyText is not null)
            {
                await CopyText(_lastProposal);
                _quoteResult.Text += "\n(proposta copiada)";
            }
        };
        Button file = new() { Content = "Salvar proposta (HTML)…", Padding = new Thickness(16, 6) };
        file.Click += async (_, _) =>
        {
            if (_lastHtml.Length > 0 && SaveProposal is not null)
            {
                await SaveProposal("proposta.html", _lastHtml);
                _quoteResult.Text += "\n(abra o arquivo no navegador e use Imprimir › Salvar como PDF)";
            }
        };
        buttons.Children.Add(calc);
        buttons.Children.Add(copy);
        buttons.Children.Add(file);
        form.Children.Add(buttons);
        form.Children.Add(_quoteResult);
        var tab = new TabItem { Header = "Orçamento", Content = new Border { Padding = new Thickness(0, 10), Child = form } };
        tab.PropertyChanged += (_, e) =>
        {
            if (e.Property == TabItem.IsSelectedProperty && tab.IsSelected)
            {
                LoadQuote();
            }
        };
        _tabs.Items.Add(tab);
    }

    private void Add(StackPanel form, string key, string label, string unit, Func<QuoteDefaults, double> get)
    {
        var box = new TextBox { MinHeight = 26 };
        _quoteBoxes[key] = box;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("260,*,60") };
        var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        var u = new TextBlock { Text = unit, Opacity = 0.65, FontSize = 11, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(box, 1);
        Grid.SetColumn(u, 2);
        row.Children.Add(l);
        row.Children.Add(box);
        row.Children.Add(u);
        form.Children.Add(row);
        _quoteGetters[key] = get;
    }

    private readonly Dictionary<string, Func<QuoteDefaults, double>> _quoteGetters = [];

    private void LoadQuote()
    {
        QuoteDefaults d = QuoteSource?.Invoke() ?? new QuoteDefaults();
        foreach ((string key, Func<QuoteDefaults, double> get) in _quoteGetters)
        {
            _quoteBoxes[key].Text = get(d).ToString("0.##", Br);
        }

        _company.Text = d.Company;
        (TimeSpan Time, int ToolChanges, string Name)? p = ProgramSource?.Invoke();
        _quoteProgram.Text = p is { } v
            ? $"Programa: {v.Name} · tempo simulado {DurationFormat.Format(v.Time, Br)} · {v.ToolChanges} troca(s) de ferramenta"
            : "Nenhum programa analisado no Code. Abra um programa no Code e volte aqui.";
        if (p is { } prog && string.IsNullOrEmpty(_partName.Text))
        {
            _partName.Text = Path.GetFileNameWithoutExtension(prog.Name);
        }
    }

    private double Q(string key) =>
        double.TryParse((_quoteBoxes[key].Text ?? "").Trim().Replace('.', ','), NumberStyles.Float, Br, out double d) ? d : double.NaN;

    private void CalculateQuote()
    {
        _lastProposal = "";
        _lastHtml = "";
        if (ProgramSource?.Invoke() is not { } p || p.Time <= TimeSpan.Zero)
        {
            _quoteResult.Text = "Abra no Code um programa com movimentos de corte para calcular o tempo.";
            return;
        }

        var input = new QuoteInput(p.Time, p.ToolChanges, (int)Math.Round(Q("qty")), Q("rate"), Q("setup"), Q("tc"), Q("load"), Q("safety"), Q("mat"), Q("wear"), Q("margin"), Q("tax"));
        (QuoteResult? r, string? error) = QuoteCalculator.Calculate(input);
        if (r is null)
        {
            _quoteResult.Text = error is not null && error.Contains("NaN", StringComparison.Ordinal) ? "Confira os números digitados." : error ?? "Confira os números digitados.";
            return;
        }

        QuoteDefaults d = QuoteSource?.Invoke() ?? new QuoteDefaults();
        d.Quantity = input.Quantity;
        d.MachineRatePerHour = input.MachineRatePerHour;
        d.SetupHours = input.SetupHours;
        d.ToolChangeSeconds = input.ToolChangeSeconds;
        d.LoadUnloadSeconds = input.LoadUnloadSeconds;
        d.SafetyPercent = input.SafetyPercent;
        d.MaterialPerPart = input.MaterialPerPart;
        d.ToolWearPerPart = input.ToolWearPerPart;
        d.MarginPercent = input.MarginPercent;
        d.TaxPercent = input.TaxPercent;
        d.Company = _company.Text ?? "";
        SaveQuote?.Invoke();

        string M(double v) => "R$ " + v.ToString("N2", Br);
        _lastProposal = r.ToText(_partName.Text ?? "");
        _lastHtml = r.ToHtml(_partName.Text ?? "", _company.Text ?? "");
        _quoteResult.Text = string.Join("\n",
            $"Tempo de ciclo estimado: {DurationFormat.Format(r.CycleTime, Br)} por peça",
            $"Máquina {M(r.MachineCostPerPart)} · Preparo {M(r.SetupCostPerPart)} · Material {M(r.MaterialPerPart)} · Ferramenta {M(r.ToolWearPerPart)}",
            $"Custo por peça {M(r.CostPerPart)} · Margem {M(r.MarginPerPart)} · Impostos {M(r.TaxPerPart)}",
            $"Preço por peça: {M(r.PricePerPart)}   ·   Total do lote ({r.Quantity}): {M(r.Total)}",
            "Estimativa: confira com a primeira peça cortada.");
    }
}
