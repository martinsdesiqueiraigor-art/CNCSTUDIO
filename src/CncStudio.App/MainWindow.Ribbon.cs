using Avalonia.Controls;

namespace CncStudio.App;

/// <summary>
/// UI-C1: faixa do Code com abas (Arquivo, Editar, Simular, Saída). Cada aba mostra um grupo de botões com nome; os botões
/// são os mesmos de antes (mesmos nomes, mesmas ações e atalhos), só mudaram de lugar.
/// </summary>
public partial class MainWindow
{
    private void InitRibbon()
    {
        Button[] tabs = [TabFileButton, TabEditButton, TabSimButton, TabOutButton];
        StackPanel[] pages = [RibbonFile, RibbonEdit, RibbonSim, RibbonOut];
        void Pick(int index)
        {
            for (int k = 0; k < tabs.Length; k++)
            {
                tabs[k].Classes.Set("on", k == index);
                pages[k].IsVisible = k == index;
            }
        }

        for (int k = 0; k < tabs.Length; k++)
        {
            int index = k;
            tabs[k].Click += (_, _) => Pick(index);
        }

        Pick(0);
        // Estatísticas: alterna entre o gráfico e a aba de estatísticas (as mesmas abas de antes, embaixo da faixa)
        StatsTabButton.Click += (_, _) => ViewTabs.SelectedIndex = ViewTabs.SelectedIndex == 1 ? 0 : 1;
        CollapseEditorRibbonButton.Click += (_, _) => SetEditorCollapsed(!_editorCollapsed);
        // UI-C4: erros e avisos numa linha; a lista abre ao clicar (140 px, com a divisória de antes)
        DiagnosticsExpand.IsCheckedChanged += (_, _) => SetDiagnosticsOpen(DiagnosticsExpand.IsChecked == true);
        // UI-C2: painel do comando aberto ou recolhido, guardado nas preferências do app
        Graphics.CommandPanel.SetOpen(_settings.CodePanelOpen);
        Graphics.CommandPanel.OpenChanged += open =>
        {
            _settings.CodePanelOpen = open;
            _settings.Save();
        };
    }

    private void SetDiagnosticsOpen(bool open)
    {
        DiagnosticsList.IsVisible = open;
        DiagnosticsSplitter.IsVisible = open;
        EditorPane.RowDefinitions[3].Height = new GridLength(open ? 4 : 0);
        EditorPane.RowDefinitions[4].Height = open ? new GridLength(140) : GridLength.Auto;
        DiagnosticsExpand.Content = open ? "▾ fechar" : "▸ abrir";
    }
}
