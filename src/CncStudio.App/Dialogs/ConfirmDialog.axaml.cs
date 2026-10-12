using Avalonia.Controls;

namespace CncStudio.App.Dialogs;

/// <summary>Pergunta simples com botões. Fecha com o índice do botão escolhido (null = fechou a janela).</summary>
public partial class ConfirmDialog : Window
{
    // Construtor sem parâmetros exigido pelo carregador de XAML
    public ConfirmDialog()
        : this("", ["OK"])
    {
    }

    public ConfirmDialog(string message, IReadOnlyList<string> buttons, int defaultButton = 0, int? cancelButton = null)
    {
        InitializeComponent();
        MessageText.Text = message;
        for (int i = 0; i < buttons.Count; i++)
        {
            int index = i;
            var button = new Button
            {
                Content = buttons[i],
                IsDefault = i == defaultButton,
                IsCancel = i == cancelButton,
                MinWidth = 90,
            };
            button.Click += (_, _) => Close((int?)index);
            ButtonsPanel.Children.Add(button);
        }
    }
}
