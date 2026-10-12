using Avalonia.Controls;

namespace CncStudio.App.Dialogs;

/// <summary>Pede um número de linha. Fecha com a linha (base 1) ou null.</summary>
public partial class GoToLineDialog : Window
{
    // Construtor sem parâmetros exigido pelo carregador de XAML
    public GoToLineDialog()
        : this(1, 1)
    {
    }

    public GoToLineDialog(int currentLine, int lineCount)
    {
        InitializeComponent();
        RangeText.Text = $"Linha (1 a {lineCount}):";
        LineInput.Maximum = lineCount;
        LineInput.Value = currentLine;

        OkButton.Click += (_, _) => Close((int?)(int)(LineInput.Value ?? 1));
        CancelButton.Click += (_, _) => Close(null);
        Opened += (_, _) => LineInput.Focus();
    }
}
