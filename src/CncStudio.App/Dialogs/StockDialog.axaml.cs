using Avalonia.Controls;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Setup;
using CncStudio.Core.Simulation;

namespace CncStudio.App.Dialogs;

/// <summary>Escolha do bruto: resultado do diálogo (Stock null = automático).</summary>
public sealed record StockChoice(Stock? Stock);

/// <summary>Bruto do programa: automático ou informado (bloco na fresa, barra no torno).</summary>
public partial class StockDialog : Window
{
    private readonly MachineKind _machine;
    private readonly Bounds3 _cutting;
    private readonly string _programText;

    // Construtor sem parâmetros exigido pelo carregador de XAML
    public StockDialog()
        : this(null, MachineKind.Mill, Bounds3.Empty, "")
    {
    }

    public StockDialog(Stock? current, MachineKind machine, Bounds3 cutting, string programText)
    {
        InitializeComponent();
        _machine = machine;
        _cutting = cutting;
        _programText = programText;

        bool lathe = machine == MachineKind.Lathe;
        Title = lathe ? "Bruto — torno" : "Bruto — fresa";
        MillPanel.IsVisible = !lathe;
        LathePanel.IsVisible = lathe;

        AutoOption.IsChecked = current is null;
        ManualOption.IsChecked = current is not null;
        Fill(current ?? Suggested());

        AutoOption.IsCheckedChanged += (_, _) => UpdateEnabled();
        FromCommentButton.Click += (_, _) => ReadFromComment();
        FitPathButton.Click += (_, _) =>
        {
            Fill(FitToPath());
            ManualOption.IsChecked = true;
        };
        OkButton.Click += (_, _) => Save();
        CancelButton.Click += (_, _) => Close(null);
        UpdateEnabled();
    }

    private void UpdateEnabled()
    {
        bool manual = ManualOption.IsChecked == true;
        MillPanel.IsEnabled = manual;
        LathePanel.IsEnabled = manual;
    }

    /// <summary>Sugestão inicial: do cabeçalho, se houver; senão, a trajetória.</summary>
    private Stock Suggested() => FromProgramComments() ?? FitToPath();

    private Stock? FromProgramComments() => SetupBinding.FromProgramComments(_programText, _machine, _cutting);

    private void ReadFromComment()
    {
        if (FromProgramComments() is { } stock)
        {
            Fill(stock);
            ManualOption.IsChecked = true;
            HintText.Text = _machine == MachineKind.Mill
                ? "Tamanho lido do comentário. A posição é um palpite (canto no zero ou centrado no corte): confira."
                : "Tamanho lido do comentário, com a face em Z0: ajuste se o bruto passa da face.";
        }
        else
        {
            HintText.Text = "Nenhum comentário de bruto encontrado. Exemplos: (BRUTO 100 X 80 X 20), (BRUTO D50 X 70).";
        }
    }

    private Stock FitToPath() => SetupBinding.FitToPath(_machine, _cutting);

    private void Fill(Stock stock)
    {
        if (_machine == MachineKind.Lathe)
        {
            DiameterInput.Value = (decimal)stock.Diameter;
            LengthInput.Value = (decimal)stock.Length;
            FaceInput.Value = (decimal)stock.MaxZ;
            return;
        }

        MinXInput.Value = (decimal)stock.MinX;
        MaxXInput.Value = (decimal)stock.MaxX;
        MinYInput.Value = (decimal)stock.MinY;
        MaxYInput.Value = (decimal)stock.MaxY;
        MinZInput.Value = (decimal)stock.MinZ;
        MaxZInput.Value = (decimal)stock.MaxZ;
    }

    private void Save()
    {
        if (AutoOption.IsChecked == true)
        {
            Close(new StockChoice(null));
            return;
        }

        double V(NumericUpDown input) => (double)(input.Value ?? 0);
        Stock stock = _machine == MachineKind.Lathe
            ? Stock.Bar(V(DiameterInput), V(LengthInput), V(FaceInput))
            : Stock.Block(V(MinXInput), V(MaxXInput), V(MinYInput), V(MaxYInput), V(MinZInput), V(MaxZInput));

        if (!stock.IsValid)
        {
            HintText.Text = "Medidas inválidas: o máximo de cada eixo precisa ser maior que o mínimo.";
            return;
        }

        Close(new StockChoice(stock));
    }
}
