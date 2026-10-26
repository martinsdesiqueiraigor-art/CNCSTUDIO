using Avalonia.Controls;
using Avalonia.Threading;
using CncStudio.Core.Panel;

namespace CncStudio.Web;

public partial class MainView
{
    private ControlPanelView? _panelView;
    private bool _panelWanted = true;
    private bool _nativeHidden;   // um menu do app está aberto: elementos do navegador ficam escondidos

    /// <summary>Painel de controle sobre o gráfico: carrega o módulo e põe a camada sobre a área do 2D/3D.</summary>
    private async Task InitControlPanelAsync()
    {
        _panelWanted = Stored("cc.panel") != "0";
        try
        {
            await ControlPanelInterop.LoadAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine("painel: erro ao carregar " + ex.Message);
            return; // sem o painel o resto do app funciona normalmente
        }

        _panelView = new ControlPanelView();
        Grid.SetColumn(_panelView, 0);
        Grid.SetColumnSpan(_panelView, 3);
        PlotsGrid.Children.Add(_panelView);
        // Fica por cima do 3D (elemento do navegador criado depois) e dos gestos do gráfico
        foreach (Control host in new Control[] { StatsHost, LibraryHost, CompareHost })
        {
            host.PropertyChanged += (_, e) =>
            {
                if (e.Property == Control.IsVisibleProperty)
                {
                    RefreshNativeLayers();
                }
            };
        }

        RefreshNativeLayers();
        RefreshControlPanel();
    }

    /// <summary>Liga ou desliga o painel (menu Ver); guardado no aparelho.</summary>
    private void SetPanelWanted(bool wanted)
    {
        _panelWanted = wanted;
        Store("cc.panel", wanted ? "1" : "0");
        RefreshNativeLayers();
    }

    /// <summary>
    /// Elementos do navegador (3D e painel) ficam por cima de tudo que o Avalonia desenha: somem enquanto
    /// um menu está aberto ou uma tela cobre o gráfico, e voltam depois.
    /// </summary>
    private void SetNativeHidden(bool hidden)
    {
        _nativeHidden = hidden;
        RefreshNativeLayers();
    }

    private void RefreshNativeLayers()
    {
        bool covered = StatsHost.IsVisible || LibraryHost.IsVisible || CompareHost.IsVisible;
        Scene3DSlot.IsVisible = _view3D && !_nativeHidden && !CompareHost.IsVisible;
        if (_panelView is { } panel)
        {
            panel.IsVisible = _panelWanted && !_nativeHidden && !covered;
        }
    }

    /// <summary>Manda ao painel o estado do ponto atual da simulação (posição, falta, G ativos...).</summary>
    private void RefreshControlPanel()
    {
        if (_panelView is null)
        {
            return;
        }

        _panelView.SetState(_analysis is { } a ? ControlPanel.At(a.ToolPath, TargetMoves(), CurrentDialect)?.ToJson() : null);
    }
}
