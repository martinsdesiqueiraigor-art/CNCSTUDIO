using Avalonia.Controls;
using CncStudio.Core.Editing;

namespace CncStudio.App.Dialogs;

/// <summary>Janela de opções da renumeração. Fecha com as opções escolhidas ou null (cancelado).</summary>
public partial class RenumberDialog : Window
{
    public RenumberDialog()
    {
        InitializeComponent();

        RemoveOption.IsCheckedChanged += (_, _) =>
        {
            // Começo/incremento não fazem sentido ao remover
            bool enabled = RemoveOption.IsChecked != true;
            StartInput.IsEnabled = enabled;
            IncrementInput.IsEnabled = enabled;
        };

        OkButton.Click += (_, _) => Close(BuildOptions());
        CancelButton.Click += (_, _) => Close(null);
    }

    private RenumberOptions BuildOptions()
    {
        RenumberMode mode = RemoveOption.IsChecked == true ? RenumberMode.Remove
            : ExistingOption.IsChecked == true ? RenumberMode.ExistingOnly
            : RenumberMode.AllBlocks;

        return new RenumberOptions(
            (int)(StartInput.Value ?? 10),
            Math.Max(1, (int)(IncrementInput.Value ?? 10)),
            mode);
    }
}
