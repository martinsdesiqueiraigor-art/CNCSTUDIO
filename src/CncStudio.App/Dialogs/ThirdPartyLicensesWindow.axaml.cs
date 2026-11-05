using Avalonia.Controls;

namespace CncStudio.App.Dialogs;

/// <summary>Janela "Licenças de terceiros": texto do aviso embutido, só leitura, com rolagem.</summary>
public partial class ThirdPartyLicensesWindow : Window
{
    public ThirdPartyLicensesWindow()
    {
        InitializeComponent();
        // URLs como texto simples: o azul padrão do link fica ilegível no tema escuro
        NoticesText.Options.EnableHyperlinks = false;
        NoticesText.Options.EnableEmailHyperlinks = false;
        NoticesText.Text = ThirdPartyNotices.Load();
        CloseButton.Click += (_, _) => Close();
    }
}
