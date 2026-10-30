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

    private readonly TextBlock _cardTime = new() { Text = "—" };
    private readonly TextBlock _cardPrice = new() { Text = "—" };
    private readonly TextBlock _cardTotal = new() { Text = "—" };

    private void AddQuoteTab()
    {
        const string accent = "#FF9A3D";
        var form = new StackPanel { Spacing = 6 };
        form.Children.Add(new TextBlock { Text = "Calcule o custo da peça", FontSize = 16, FontWeight = FontWeight.SemiBold });
        form.Children.Add(new TextBlock { Text = "O tempo vem do programa aberto no Code (simulação). Os valores em R$ são seus e ficam só neste computador.", TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.8 });
        form.Children.Add(_quoteProgram);
        form.Children.Add(_partName);
        form.Children.Add(_company);
        Add(form, "qty", "lote", "Quantidade do lote", "peças", d => d.Quantity);
        Add(form, "rate", "moeda", "Hora-máquina", "R$/h", d => d.MachineRatePerHour);
        Add(form, "setup", "preparo", "Preparo (setup)", "h", d => d.SetupHours);
        Add(form, "tc", "troca", "Troca de ferramenta", "s", d => d.ToolChangeSeconds);
        Add(form, "load", "caixa", "Carga e descarga da peça", "s", d => d.LoadUnloadSeconds);
        Add(form, "safety", "relogio", "Folga sobre o tempo simulado", "%", d => d.SafetyPercent);
        Add(form, "mat", "caixa", "Material por peça", "R$", d => d.MaterialPerPart);
        Add(form, "wear", "ferramenta", "Desgaste de ferramenta por peça", "R$", d => d.ToolWearPerPart);
        Add(form, "margin", "margem", "Margem", "%", d => d.MarginPercent);
        Add(form, "tax", "imposto", "Impostos sobre a venda", "%", d => d.TaxPercent);

        Button calc = new() { Content = "Calcular", Padding = new Thickness(20, 8), FontWeight = FontWeight.SemiBold, Background = new SolidColorBrush(Color.Parse(accent)), Foreground = Brushes.Black };
        // O tema troca o fundo ao passar o mouse/clicar; mantém a cor de destaque nesses estados
        foreach (string state in new[] { "PointerOver", "Pressed", "Disabled" })
        {
            calc.Resources["ButtonBackground" + state] = calc.Background;
            calc.Resources["ButtonForeground" + state] = Brushes.Black;
        }

        Button copy = new() { Content = "Copiar proposta", Padding = new Thickness(16, 8) };
        Button file = new() { Content = "Salvar proposta (HTML)…", Padding = new Thickness(16, 8) };
        calc.Click += (_, _) => CalculateQuote();
        copy.Click += async (_, _) =>
        {
            if (_lastProposal.Length > 0 && CopyText is not null)
            {
                await CopyText(_lastProposal);
                _quoteResult.Text += "\n(proposta copiada)";
            }
        };
        file.Click += async (_, _) =>
        {
            if (_lastHtml.Length > 0 && SaveProposal is not null)
            {
                await SaveProposal("proposta.html", _lastHtml);
                _quoteResult.Text += "\n(abra o arquivo no navegador e use Imprimir › Salvar como PDF)";
            }
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        buttons.Children.Add(calc);
        buttons.Children.Add(copy);
        buttons.Children.Add(file);
        form.Children.Add(buttons);

        // Resultado ao lado do formulário: sempre à vista, sem rolar
        var result = new StackPanel { Spacing = 10 };
        result.Children.Add(new TextBlock { Text = "Resultado da estimativa", FontSize = 16, FontWeight = FontWeight.SemiBold });
        result.Children.Add(UiKit.ResultCard("Tempo de ciclo por peça", "relogio", accent, _cardTime));
        result.Children.Add(UiKit.ResultCard("Preço por peça", "moeda", "#F5C542", _cardPrice));
        result.Children.Add(UiKit.ResultCard("Total do lote", "lote", "#3DDC97", _cardTotal));
        result.Children.Add(new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(12), Child = _quoteResult }.Themed());

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24,360") };
        Grid.SetColumn(form, 0);
        Grid.SetColumn(result, 2);
        grid.Children.Add(form);
        grid.Children.Add(result);
        var tab = new TabItem { Header = "Orçamento", Content = new Border { Padding = new Thickness(0, 10), Child = grid } };
        tab.PropertyChanged += (_, e) =>
        {
            if (e.Property == TabItem.IsSelectedProperty && tab.IsSelected)
            {
                LoadQuote();
            }
        };
        _tabs.Items.Add(tab);
    }

    private void Add(StackPanel form, string key, string icon, string label, string unit, Func<QuoteDefaults, double> get)
    {
        var box = new TextBox { MinHeight = 26 };
        _quoteBoxes[key] = box;
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("28,240,*,56") };
        var ic = UiKit.Icon(icon, "#FF9A3D", 17);
        var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        var u = new TextBlock { Text = unit, Opacity = 0.65, FontSize = 11, Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(l, 1);
        Grid.SetColumn(box, 2);
        Grid.SetColumn(u, 3);
        row.Children.Add(ic);
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
        _cardTime.Text = _cardPrice.Text = _cardTotal.Text = "—";
        if (ProgramSource?.Invoke() is not { } p || p.Time <= TimeSpan.Zero)
        {
            _quoteResult.Text = "Abra no Code um programa com movimentos de corte para calcular o tempo.";
            return;
        }

        (int quantity, string? quantityError) = QuoteInput.ParseQuantity(Q("qty"));
        if (quantityError is not null)
        {
            _quoteResult.Text = quantityError;
            return;
        }

        var input = new QuoteInput(p.Time, p.ToolChanges, quantity, Q("rate"), Q("setup"), Q("tc"), Q("load"), Q("safety"), Q("mat"), Q("wear"), Q("margin"), Q("tax"));
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
        _cardTime.Text = DurationFormat.Format(r.CycleTime, Br);
        _cardPrice.Text = M(r.PricePerPart);
        _cardTotal.Text = M(r.Total);
        _quoteResult.Text = string.Join("\n",
            $"Máquina {M(r.MachineCostPerPart)} · Preparo {M(r.SetupCostPerPart)}",
            $"Material {M(r.MaterialPerPart)} · Ferramenta {M(r.ToolWearPerPart)}",
            $"Custo por peça {M(r.CostPerPart)} · Margem {M(r.MarginPerPart)} · Impostos {M(r.TaxPerPart)}",
            $"Lote de {r.Quantity} peça(s).",
            "Estimativa: confira com a primeira peça cortada.");
    }
}
