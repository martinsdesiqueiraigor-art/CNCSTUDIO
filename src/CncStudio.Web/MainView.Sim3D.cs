using Avalonia.Controls;
using Avalonia.Threading;
using CncStudio.Core.Execution;
using CncStudio.Core.Scene;
using CncStudio.Core.Simulation;

namespace CncStudio.Web;

/// <summary>
/// Simulação 3D na web (Fase 1): remoção de material com o mesmo Core do desktop, simulada em
/// fatias de tempo (a tela não trava) e desenhada pelo three.js. Acompanha os passos da
/// simulação 2D: o material fica sempre removido até o passo atual.
/// </summary>
public partial class MainView
{
    private Scene3DView? _scene3DView;
    private MaterialSimulationParts? _material;
    private SimulationDriver? _driver;
    private SimulationScene? _scene;
    private DispatcherTimer? _frame3D;
    private int _collisionsShown;
    private int _collisionsReported;
    private bool _view3D;
    private bool _loading3D;
    private bool _technical;
    private bool _showTool3D = true;
    private bool _quarterSection = true;

    /// <summary>Torno: peça com 1/4 removido (mostra o perfil e o furo por dentro).</summary>
    private void SetQuarterSection(bool on)
    {
        _quarterSection = on;
        if (_scene is not null)
        {
            _scene.QuarterSection = on;
            _scene3DView?.Sync();
        }
    }
    private SceneView _sceneView = SceneView.Isometric;

    /// <summary>Linha de ajuda sobre o limite da simulação (sem prometer colisão garantida).</summary>
    private const string Limit3D = "3D em 3 eixos: rebaixos laterais e 4/5 eixos não aparecem; não substitui a conferência na máquina.";

    private void Wire3D()
    {
        View3DButton.Click += async (_, _) => await Toggle3DAsync(View3DButton.IsChecked == true);
        GraphHost.SizeChanged += (_, _) => ApplyPlotsLayout();
        Backplot.SizeChanged += (_, _) =>
        {
            if (_refit2D)
            {
                _refit2D = false;
                Backplot.Fit();
            }
        };
    }

    private async Task Toggle3DAsync(bool on)
    {
        if (_loading3D)
        {
            return;
        }

        if (on && _scene3DView is null)
        {
            _loading3D = true;
            SetStatus("Carregando o 3D…");
            try
            {
                await Scene3DInterop.LoadAsync();
            }
            catch (Exception ex)
            {
                View3DButton.IsChecked = false;
                SetStatus("Não foi possível abrir o 3D neste navegador: " + ex.Message);
                return;
            }
            finally
            {
                _loading3D = false;
            }

            _scene3DView = new Scene3DView
            {
                LightTheme = Stored("cc.theme") == "light",
                LightBackground = Stored("cc.bg3d") == "1",
                ShowRapids = _showRapids3D,
                ShowPath = _showPath3D,
            };
            Scene3DSlot.Children.Add(_scene3DView);
            _frame3D = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _frame3D.Tick += (_, _) => Frame3D();
        }

        _view3D = on;
        View3DButton.IsChecked = on;
        ApplyPlotsLayout();
        UpdateTimeChip();
        IsoViewButton.Content = on ? "Iso" : "2,5D";
        UpdateViewButtons();
        Store("cc.view3d", on ? "1" : "0");
        if (on)
        {
            Rebuild3D();
            _frame3D?.Start();
            SetStatus(Limit3D);
        }
        else
        {
            _frame3D?.Stop();
            RapidsButton.IsVisible = false;
            SpeedText.IsVisible = true;
        }
    }

    /// <summary>Área do gráfico com espaço para 2D e 3D lado a lado (PC).</summary>
    private const double SideBySideWidth = 620;

    private string _plotsColumns = "*,0,0";
    private bool _refit2D;

    /// <summary>
    /// PC: 2D e 3D lado a lado, com divisor ajustável. Celular (ou área estreita): só um, alternando
    /// pelo botão 3D.
    /// </summary>
    private void ApplyPlotsLayout()
    {
        bool side = _view3D && GraphHost.Bounds.Width >= SideBySideWidth;
        string columns = !_view3D ? "*,0,0" : side ? "*,6,*" : "0,0,*";
        if (columns != _plotsColumns)
        {
            // Só ao mudar de modo (assim o divisor arrastado pela pessoa fica onde ela deixou)
            _plotsColumns = columns;
            PlotsGrid.ColumnDefinitions = new ColumnDefinitions(columns);
            // O 2D vai mudar de largura: enquadra de novo quando o novo tamanho chegar
            _refit2D = true;
        }

        Backplot.IsVisible = !_view3D || side;
        PlotsSplitter.IsVisible = side;
        Scene3DSlot.IsVisible = _view3D;
        UpdateTimeChip();
    }

    /// <summary>Lista dos G00 que encostaram no material (botão vermelho na barra da simulação).</summary>
    private void UpdateRapidsButton()
    {
        IReadOnlyList<int> hits = _view3D && _material is not null ? _material.Simulation.RapidCollisions : [];
        RapidsButton.IsVisible = hits.Count > 0;
        // Celular estreito: o texto da velocidade cede lugar ao botão (o − e o + continuam)
        SpeedText.IsVisible = hits.Count == 0 || GraphHost.Bounds.Width >= 470;
        if (hits.Count == 0 || _analysis is not { } analysis)
        {
            return;
        }

        RapidsButton.Content = $"⚠ {hits.Count}";
        var list = new StackPanel { Spacing = 2, Margin = new Avalonia.Thickness(4) };
        list.Children.Add(new TextBlock { Text = "G00 que encostaram no material", FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Avalonia.Thickness(4, 2, 4, 6) });
        foreach (int move in hits.Take(30))
        {
            int line = analysis.ToolPath.Moves[move].LineNumber;
            string code = line >= 1 && line <= Editor.Document.LineCount
                ? Editor.Document.GetText(Editor.Document.GetLineByNumber(line)).Trim()
                : "";
            var item = new Button
            {
                Content = $"Linha {line}: {(code.Length > 40 ? code[..40] + "…" : code)}",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                MinHeight = 36,
            };
            item.Click += (_, _) =>
            {
                RapidsButton.Flyout?.Hide();
                GoToRapid(move, line);
            };
            list.Children.Add(item);
        }

        RapidsButton.Flyout = new Flyout { Content = new ScrollViewer { Content = list, MaxHeight = 360 } };
    }

    /// <summary>Leva o editor à linha do G00 e a simulação ao passo em que ele acontece.</summary>
    private void GoToRapid(int move, int line)
    {
        StopSimulation();
        int step = Array.FindIndex(_stepEnds, end => end > move);
        if (step >= 0)
        {
            SetStep(step + 1);
        }

        GoToLine(line);
    }

    /// <summary>Torno no 2D: só o plano ZX. No 3D, fresa e torno usam as câmeras prontas.</summary>
    private void UpdateViewButtons()
    {
        bool lathe = _analysis?.ToolPath.Machine == MachineKind.Lathe;
        LatheLabel.IsVisible = lathe && !_view3D;
        MillViews.IsVisible = !lathe || _view3D;
    }

    /// <summary>Recria a simulação 3D para o programa analisado (programa, setup ou tela mudaram).</summary>
    private void Rebuild3D()
    {
        if (!_view3D || _scene3DView is null)
        {
            return;
        }

        _material = null;
        _driver = null;
        _scene = null;
        _collisionsShown = 0;
        _collisionsReported = 0;
        RapidsButton.IsVisible = false;
        SpeedText.IsVisible = true;
        if (_analysis is not { } analysis || analysis.ToolPath.Moves.Count == 0)
        {
            _scene3DView.SetScene(null);
            return;
        }

        int cells = SimulationFactory.CellsFor(GraphHost.Bounds.Width, GraphHost.Bounds.Height);
        _material = SimulationFactory.Create(analysis, _machiningSetup, Editor.Text, cells);
        ScenePalette palette = _technical ? ScenePalette.Technical : ScenePalette.Realistic;
        _scene = _material.Bar is { } bar
            ? SimulationScene.ForLathe(bar, analysis.ToolPath, _machiningSetup, palette)
            : SimulationScene.ForMill(_material.Map!, analysis.ToolPath, _material.Profiles, _machiningSetup, palette);
        _scene.QuarterSection = _quarterSection;
        ApplyToolVisibility();
        _driver = new SimulationDriver(_material.Simulation) { Target = TargetMoves() };
        _scene3DView.SetScene(_scene);
        _sceneView = SceneView.Isometric;
    }

    /// <summary>Movimentos já executados no passo atual (todos quando a simulação está completa).</summary>
    private int TargetMoves()
    {
        int total = _analysis?.ToolPath.Moves.Count ?? 0;
        return _step >= _stepEnds.Length ? total : _step == 0 ? 0 : _stepEnds[_step - 1];
    }

    /// <summary>O passo mudou: o 3D passa a correr atrás do novo alvo.</summary>
    private void OnStepChanged3D()
    {
        if (_driver is not null)
        {
            _driver.Target = TargetMoves();
        }
    }

    // A cada quadro: simula um pedaço (no máximo ~12 ms) e envia ao 3D só o que mudou
    private void Frame3D()
    {
        if (!_view3D || _driver is null || _scene is null || _material is null || _scene3DView is null)
        {
            return;
        }

        if (_driver.Run(SimulationDriver.DefaultBudget))
        {
            IMaterialSimulation sim = _material.Simulation;
            _scene.Refresh();
            _scene.SetTool(sim.CurrentTool, sim.ToolPosition);
            bool currentChanged = _scene.SetCurrentMove(sim.CurrentMoveIndex);
            if (currentChanged)
            {
                _scene3DView.SyncLines();
            }

            if (sim.RapidCollisions.Count != _collisionsShown)
            {
                _collisionsShown = sim.RapidCollisions.Count;
                _scene.SetRapidCollisions(sim.RapidCollisions);
                _scene3DView.SyncLines();
                UpdateRapidsButton();
            }

            ShowSimPosition();
            if (_driver.AtTarget && sim.RapidCollisions.Count > 0 && _collisionsReported != sim.RapidCollisions.Count)
            {
                _collisionsReported = sim.RapidCollisions.Count;
                int hits = _collisionsReported;
                SetStatus($"{hits} {(hits == 1 ? "rápido (G00) encostou" : "rápidos (G00) encostaram")} no material: em vermelho no 3D.");
            }
        }

        _scene3DView.Sync();
    }

    /// <summary>Andamento do 3D para o rodapé do gráfico ("" quando em dia).</summary>
    private string Progress3D()
    {
        if (!_view3D || _driver is null || _material is null || _driver.AtTarget)
        {
            return "";
        }

        int total = Math.Max(1, _driver.Target);
        return $" · 3D {Math.Min(99, 100.0 * _material.Simulation.CurrentMoveIndex / total):0}%";
    }

    /// <summary>Botões de vista no 3D: câmeras prontas.</summary>
    private bool SetSceneView(SceneView view)
    {
        if (!_view3D || _scene3DView is null)
        {
            return false;
        }

        _sceneView = view;
        _scene3DView.Fit(view);
        return true;
    }

    private void Fit3DOr2D()
    {
        SetSceneView(_sceneView);
        if (Backplot.IsVisible)
        {
            Backplot.Fit();
        }
    }

    private bool _showRapids3D = true;
    private bool _showPath3D;   // padrão: só o trecho atual; a trajetória inteira é opcional

    private void SetPath3D(bool show)
    {
        _showPath3D = show;
        if (_scene3DView is not null)
        {
            _scene3DView.ShowPath = show;
        }
    }

    private void SetLightBackground3D(bool light)
    {
        Store("cc.bg3d", light ? "1" : "0");
        if (_scene3DView is not null)
        {
            _scene3DView.LightBackground = light;
        }
    }

    private void SetRapids3D(bool show)
    {
        _showRapids3D = show;
        if (_scene3DView is not null)
        {
            _scene3DView.ShowRapids = show;
        }
    }

    private void SetTool3D(bool show)
    {
        _showTool3D = show;
        ApplyToolVisibility();
    }

    private void ApplyToolVisibility()
    {
        if (_scene is null)
        {
            return;
        }

        foreach (SceneObject o in _scene.Objects)
        {
            if (o.Role is SceneRole.Tool or SceneRole.Holder)
            {
                o.Visible = _showTool3D;
            }
        }
    }

    /// <summary>Modo técnico: o corte pintado com a cor de cada ferramenta.</summary>
    private void SetTechnical3D(bool on)
    {
        _technical = on;
        Store("cc.technical", on ? "1" : "0");
        if (_scene is not null)
        {
            _scene.Palette = on ? ScenePalette.Technical : ScenePalette.Realistic;
            ApplyToolVisibility();
            _scene3DView?.SyncLines();
        }
    }
}
