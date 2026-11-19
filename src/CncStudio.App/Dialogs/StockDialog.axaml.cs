using Avalonia.Controls;
using Avalonia.Layout;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Setup;
using CncStudio.Core.Simulation;

namespace CncStudio.App.Dialogs;

/// <summary>
/// Escolha do bruto: resultado do diálogo. Stock null = automático (sem comentário) ou "Do programa" (com comentário BRUTO).
/// "Outro bruto" com comentário volta marcado (<see cref="SetupStock.OverridesProgram"/>).
/// </summary>
public sealed record StockChoice(SetupStock? Stock)
{
    /// <summary>Nada foi mexido no diálogo (CAM-13: salvar assim não troca o bruto do CAM).</summary>
    public bool Unchanged { get; init; }
}

/// <summary>
/// DESK-01: bruto do programa no Code desktop, igual ao setup do Code web. Com comentário (BRUTO ...) no programa: "Do programa"
/// (a simulação segue o comentário, na posição do zero) ou "Outro bruto" (troca de propósito, com aviso). Sem comentário:
/// "Automático" (pela trajetória) ou "Informado". Fresa: bloco, redondo, tubo e sextavado (cilindro e tubo em pé ou deitados).
/// Torno: barra redonda, tubo, quadrada ou retangular e sextavada, com a face em Z. Campo vazio não salva (nada suposto).
/// </summary>
public partial class StockDialog : Window
{
    private readonly MachineKind _machine;
    private readonly Bounds3 _cutting;
    private readonly string _programText;
    private readonly SetupStock? _fromProgram;
    private readonly SetupStock _work;
    private readonly RadioButton _first = new();
    private readonly RadioButton _second = new();
    private readonly ComboBox _kind = new() { MinWidth = 260 };
    private readonly ComboBox _axis = new() { MinWidth = 260 };
    private readonly Grid _fields = new() { ColumnDefinitions = new ColumnDefinitions("220,160,40") };
    private readonly TextBlock _hint = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12, Opacity = 0.85 };
    private readonly StackPanel _camRow = new() { Spacing = 6, IsVisible = false };
    private readonly Button _fit = new() { Content = "Ajustar à trajetória" };
    private readonly List<NumericUpDown> _inputs = [];
    private bool _edited;
    private bool _building;

    private static readonly SetupStockKind[] MillKinds = [SetupStockKind.Prism, SetupStockKind.Round, SetupStockKind.Tube, SetupStockKind.Hexagon];
    private static readonly SetupStockKind[] LatheKinds = [SetupStockKind.Round, SetupStockKind.Tube, SetupStockKind.Prism, SetupStockKind.Hexagon];

    // Construtor sem parâmetros exigido pelo carregador de XAML
    public StockDialog()
        : this(null, MachineKind.Mill, Bounds3.Empty, "")
    {
    }

    public StockDialog(SetupStock? current, MachineKind machine, Bounds3 cutting, string programText, bool fromCam = false, string? camNote = null)
    {
        InitializeComponent();
        _machine = machine;
        _cutting = cutting;
        _programText = programText;
        _fromProgram = ProgramStock.Read(programText, machine, cutting);
        bool lathe = machine == MachineKind.Lathe;
        Title = lathe ? "Bruto — torno" : "Bruto — fresa";

        bool followProgram = _fromProgram is not null && current is not { OverridesProgram: true };
        // "Do programa" com o mesmo bruto já no setup (o do CAM, com a posição exata): mostra o do setup
        bool keepCurrent = current is not null && (!followProgram || SetupBinding.SameShape(current, _fromProgram!));
        _work = (keepCurrent ? Dup(current!) : null) ?? (_fromProgram is { } fp ? Dup(fp) : null) ?? SetupBinding.FromStock(FitToPath());
        if (lathe)
        {
            (_work.X, _work.Y, _work.Axis) = (0, 0, SetupAxis.Z);
        }

        Root.Children.Add(_camRow);
        string comment = _fromProgram is { } p ? Describe(p) : "";
        _first.Content = _fromProgram is not null ? $"Do programa (BRUTO {comment}): a simulação segue o comentário" : "Automático (calculado pela trajetória, topo em Z0)";
        _second.Content = _fromProgram is not null ? "Outro bruto (troca de propósito: a simulação mostra um aviso)" : "Informado";
        _first.GroupName = _second.GroupName = "StockMode";
        _first.IsChecked = _fromProgram is not null ? followProgram : current is null;
        _second.IsChecked = !_first.IsChecked;
        Root.Children.Add(_first);
        Root.Children.Add(_second);

        SetupStockKind[] kinds = lathe ? LatheKinds : MillKinds;
        _kind.ItemsSource = lathe
            ? new[] { "Barra redonda", "Tubo", "Barra quadrada ou retangular", "Barra sextavada" }
            : new[] { "Bloco (prisma)", "Redondo maciço", "Tubo", "Sextavado (em pé)" };
        _kind.SelectedIndex = Math.Max(0, Array.IndexOf(kinds, _work.Kind));
        _axis.ItemsSource = new[] { "Z (em pé)", "X (deitado)", "Y (deitado)" };
        _axis.SelectedIndex = _work.Axis switch { SetupAxis.X or SetupAxis.Auto => 1, SetupAxis.Y => 2, _ => 0 };
        Root.Children.Add(Row("Tipo de bruto", _kind));
        var axisRow = Row("Eixo do cilindro", _axis);
        Root.Children.Add(axisRow);
        Root.Children.Add(_fields);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        buttons.Children.Add(_fit);
        Root.Children.Add(buttons);
        Root.Children.Add(_hint);

        var ok = new Button { Content = "Salvar", IsDefault = true };
        var cancel = new Button { Content = "Cancelar", IsCancel = true };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8 };
        actions.Children.Add(ok);
        actions.Children.Add(cancel);
        Root.Children.Add(actions);

        _kind.SelectionChanged += (_, _) =>
        {
            if (_building)
            {
                return;
            }

            _work.Kind = kinds[Math.Max(0, _kind.SelectedIndex)];
            if (_work.Kind is SetupStockKind.Prism or SetupStockKind.Hexagon || lathe)
            {
                _work.Axis = SetupAxis.Z;
            }

            Changed();
        };
        _axis.SelectionChanged += (_, _) =>
        {
            if (_building)
            {
                return;
            }

            SetupAxis next = _axis.SelectedIndex switch { 1 => SetupAxis.X, 2 => SetupAxis.Y, _ => SetupAxis.Z };
            // em pé ↔ deitado: o Z passa de base a centro (e volta) para o bruto não pular de lugar
            double r = _work.Diameter / 2;
            if (_work.Axis == SetupAxis.Z && next != SetupAxis.Z)
            {
                _work.Z += r;
            }
            else if (_work.Axis != SetupAxis.Z && next == SetupAxis.Z)
            {
                _work.Z -= r;
            }

            _work.Axis = next;
            Changed();
        };
        _first.IsCheckedChanged += (_, _) =>
        {
            if (_first.IsChecked == true && _fromProgram is not null)
            {
                Copy(_fromProgram, _work);
            }

            Changed();
        };
        _fit.Click += (_, _) =>
        {
            SetupStock fit = SetupBinding.FromStock(FitToPath());
            Copy(fit, _work);
            _second.IsChecked = true;
            _hint.Text = "Preenchido com os limites do corte + 2 mm: confira.";
            Changed();
        };
        ok.Click += (_, _) => Save();
        cancel.Click += (_, _) => Close(null);

        void Changed()
        {
            _edited = true;
            BuildFields(axisRow);
        }

        BuildFields(axisRow);
        _edited = false;
        ShowCam(fromCam, camNote);
    }

    private bool Lathe => _machine == MachineKind.Lathe;

    private Stock FitToPath() => SetupBinding.FitToPath(_machine, _cutting);

    private static SetupStock Dup(SetupStock s)
    {
        var d = new SetupStock { Material = s.Material };
        Copy(s, d);
        return d;
    }

    private static void Copy(SetupStock from, SetupStock to)
    {
        (to.Kind, to.X, to.Y, to.Z, to.Width, to.Depth, to.Height, to.Diameter, to.InnerDiameter, to.Axis) =
            (from.Kind, from.X, from.Y, from.Z, from.Width, from.Depth, from.Height, from.Diameter, from.InnerDiameter, from.Axis);
    }

    private static Control Row(string label, Control input)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*") };
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(input, 1);
        g.Children.Add(input);
        return g;
    }

    /// <summary>Campos da forma escolhida, ligados ao bruto em edição (apagados em "Do programa" e "Automático").</summary>
    private void BuildFields(Control axisRow)
    {
        _building = true;
        SetupStockKind[] kinds = Lathe ? LatheKinds : MillKinds;
        _kind.SelectedIndex = Math.Max(0, Array.IndexOf(kinds, _work.Kind));
        _axis.SelectedIndex = _work.Axis switch { SetupAxis.X or SetupAxis.Auto => 1, SetupAxis.Y => 2, _ => 0 };
        _building = false;

        bool editable = _second.IsChecked == true;
        _kind.IsEnabled = editable;
        _axis.IsEnabled = editable;
        _fit.IsEnabled = !Lathe || editable;
        axisRow.IsVisible = !Lathe && _work.IsRound;

        _fields.Children.Clear();
        _fields.RowDefinitions.Clear();
        _inputs.Clear();
        SetupStock s = _work;
        bool lying = !Lathe && s.IsLying;
        var rows = new List<(string Label, Func<double> Get, Action<double> Set)>();
        switch (s.Kind)
        {
            case SetupStockKind.Prism:
                rows.Add((Lathe ? "Largura da barra" : "Comprimento X", () => s.Width, v => s.Width = v));
                rows.Add((Lathe ? "Altura da barra" : "Largura Y", () => s.Depth, v => s.Depth = v));
                break;
            case SetupStockKind.Hexagon:
                rows.Add(("Medida entre faces (chave)", () => s.Width, v => s.Width = v));
                break;
            default:
                rows.Add(("Diâmetro externo", () => s.Diameter, v => s.Diameter = v));
                if (s.Kind == SetupStockKind.Tube)
                {
                    rows.Add(("Diâmetro interno", () => s.InnerDiameter, v => s.InnerDiameter = v));
                }

                break;
        }

        rows.Add((Lathe ? "Comprimento" : s.Kind == SetupStockKind.Prism ? "Espessura Z" : lying ? "Comprimento" : "Altura", () => s.Height, v =>
        {
            double top = s.Top;
            s.Height = v;
            if (Lathe)
            {
                s.Z = top - v; // a face fica no lugar
            }
        }));

        if (Lathe)
        {
            rows.Add(("Face da barra em Z", () => s.Top, v => s.Z = v - s.Height));
        }
        else if (s.Kind == SetupStockKind.Prism)
        {
            rows.Add(("Canto X mínimo", () => s.X, v => s.X = v));
            rows.Add(("Canto Y mínimo", () => s.Y, v => s.Y = v));
            rows.Add(("Topo Z", () => s.Top, v => s.Z = v - s.Height));
        }
        else if (lying)
        {
            bool alongX = s.Axis is SetupAxis.X or SetupAxis.Auto;
            rows.Add((alongX ? "Início em X" : "Centro X", () => s.X, v => s.X = v));
            rows.Add((alongX ? "Centro Y" : "Início em Y", () => s.Y, v => s.Y = v));
            rows.Add(("Centro Z (eixo)", () => s.Z, v => s.Z = v));
        }
        else
        {
            rows.Add(("Centro X", () => s.X, v => s.X = v));
            rows.Add(("Centro Y", () => s.Y, v => s.Y = v));
            rows.Add(("Topo Z", () => s.Top, v => s.Z = v - s.Height));
        }

        for (int i = 0; i < rows.Count; i++)
        {
            (string label, Func<double> get, Action<double> set) = rows[i];
            _fields.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
            var input = new NumericUpDown
            {
                Value = (decimal)Math.Round(get(), 3), FormatString = "0.###", Increment = 1, Width = 150, Margin = new Avalonia.Thickness(0, 2),
                HorizontalAlignment = HorizontalAlignment.Left, IsEnabled = editable,
            };
            input.ValueChanged += (_, e) =>
            {
                if (e.NewValue is { } v)
                {
                    set((double)v);
                    _edited = true;
                }
            };
            var unit = new TextBlock { Text = "mm", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, Margin = new Avalonia.Thickness(6, 0, 0, 0) };
            Grid.SetRow(text, i);
            Grid.SetRow(input, i);
            Grid.SetColumn(input, 1);
            Grid.SetRow(unit, i);
            Grid.SetColumn(unit, 2);
            _fields.Children.Add(text);
            _fields.Children.Add(input);
            _fields.Children.Add(unit);
            _inputs.Add(input);
        }

        if (!editable)
        {
            _hint.Text = _fromProgram is not null
                ? "A simulação usa o bruto do comentário do programa, na posição do zero do programa. Para trocar, escolha \"Outro bruto\"."
                : "A simulação calcula o bruto pela trajetória (topo em Z0). Para dar as medidas, escolha \"Informado\".";
        }
        else if (_fromProgram is not null)
        {
            _hint.Text = $"Outro bruto: a simulação não segue o comentário (BRUTO {Describe(_fromProgram)}) e mostra um aviso fixo.";
        }
        else
        {
            _hint.Text = "";
        }
    }

    /// <summary>"100 × 80 × 20", "Ø50 × 30", "Ø50 / Ø30 × 30", "sextavado 60 × 100".</summary>
    private static string Describe(SetupStock s)
    {
        static string N(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');
        return s.Kind switch
        {
            SetupStockKind.Round => $"Ø{N(s.Diameter)} × {N(s.Height)}",
            SetupStockKind.Tube => $"Ø{N(s.Diameter)} / Ø{N(s.InnerDiameter)} × {N(s.Height)}",
            SetupStockKind.Hexagon => $"sextavado {N(s.Width)} × {N(s.Height)}",
            _ => $"{N(s.Width)} × {N(s.Depth)} × {N(s.Height)}",
        };
    }

    private void Save()
    {
        if (_first.IsChecked == true)
        {
            Close(new StockChoice(null) { Unchanged = !_edited });
            return;
        }

        if (_inputs.Any(i => i.Value is null))
        {
            _hint.Text = "Preencha todos os campos (nada é suposto).";
            return;
        }

        SetupStock stock = Dup(_work);
        stock.OverridesProgram = _fromProgram is not null ? true : null;
        if (stock.Validate() is { } error)
        {
            _hint.Text = error;
            return;
        }

        Close(new StockChoice(stock) { Unchanged = !_edited });
    }

    /// <summary>
    /// CAM-13: etiqueta "do CAM" (o bruto veio do pacote do CAM) e o aviso do CAM sobre o bruto, se houver. Mexer em qualquer valor tira a
    /// etiqueta e marca o bruto como editado.
    /// </summary>
    private void ShowCam(bool fromCam, string? camNote)
    {
        _camRow.IsVisible = fromCam || camNote is not null;
        if (fromCam)
        {
            var tag = CncStudio.App.Modules.UiKit.StateTag(("pending", "do CAM"));
            tag.HorizontalAlignment = HorizontalAlignment.Left;
            ToolTip.SetTip(tag, "Bruto que veio do CAM, no referencial do programa. Continua editável: editar tira a etiqueta.");
            _camRow.Children.Add(tag);
        }

        if (camNote is not null)
        {
            _camRow.Children.Add(CncStudio.App.Modules.UiKit.StatusNote(camNote, "pending"));
        }
    }
}
