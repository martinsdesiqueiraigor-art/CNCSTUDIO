using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CncStudio.App.Graphics;
using CncStudio.Core;
using CncStudio.Core.Dialects;
using CncStudio.Core.Scene;
using CncStudio.Core.Setup;
using CncStudio.Core.Simulation;
using CncStudio.Core.Trajectory;

namespace CncStudio.App.Modules;

/// <summary>
/// Vista 3D do Trabalho: simula o programa gerado até o fim e mostra o material já cortado,
/// com a mesma cena 3D do Code. Não promete colisão (simulação de 3 eixos, grade de alturas).
/// </summary>
public sealed class JobSceneView : Grid
{
    private readonly GlSceneView _gl = new();
    private readonly TextBlock _status = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        Opacity = 0.7,
        Margin = new Thickness(24),
    };

    private int _generation;
    private bool _fitted;
    private bool _failed;

    public JobSceneView()
    {
        Children.Add(_gl);
        Children.Add(_status);
        _gl.Failed += (_, message) =>
        {
            _failed = true;
            _gl.IsVisible = false;
            _status.Text = "3D pela placa de vídeo indisponível (" + message + "). Use a vista 2D.";
            _status.IsVisible = true;
        };
    }

    /// <summary>Simula o programa e mostra o resultado (segundo plano; chamadas novas descartam as velhas).</summary>
    public async void Render(string program, Dialect dialect)
    {
        int generation = ++_generation;
        if (_failed)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(program))
        {
            _gl.IsVisible = false;
            _status.Text = "Sem programa para mostrar. Corrija os avisos da árvore.";
            _status.IsVisible = true;
            return;
        }

        _status.Text = "Simulando…";
        _status.IsVisible = true;
        try
        {
            SceneSnapshot? snapshot = await Task.Run(() => Simulate(program, dialect));
            if (generation != _generation)
            {
                return; // chegou uma versão mais nova
            }

            if (snapshot is null)
            {
                _status.Text = "Não foi possível montar a vista 3D deste programa.";
                return;
            }

            _status.IsVisible = false;
            _gl.IsVisible = true;
            _gl.Show(snapshot);
            if (!_fitted)
            {
                _fitted = true;
                // espera o controle ter tamanho antes de enquadrar
                Dispatcher.UIThread.Post(() => _gl.Fit(SceneView.Isometric), DispatcherPriority.Background);
            }
        }
        catch (Exception ex)
        {
            if (generation == _generation)
            {
                _status.Text = "Falha na vista 3D: " + ex.Message;
                _status.IsVisible = true;
            }
        }
    }

    /// <summary>Enquadra de novo (vista pronta).</summary>
    public void Fit(SceneView view) => _gl.Fit(view);

    private static SceneSnapshot? Simulate(string program, Dialect dialect)
    {
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(program, dialect);
        ToolPath path = analysis.ToolPath;
        if (path.Moves.Count == 0)
        {
            return null;
        }

        MaterialSimulationParts parts = SimulationFactory.Create(analysis, null, program, SimulationFactory.DesktopCells);
        parts.Simulation.RunToEnd();
        SimulationScene scene;
        if (parts.Bar is { } bar)
        {
            scene = SimulationScene.ForLathe(bar, path);
        }
        else if (parts.Voxels is { } voxels)
        {
            scene = SimulationScene.ForVoxels(voxels, path, parts.Profiles);
        }
        else if (parts.Map is { } map)
        {
            scene = SimulationScene.ForMill(map, path, parts.Profiles);
        }
        else
        {
            return null;
        }

        scene.Refresh();
        scene.SetTool(parts.Simulation.CurrentTool, parts.Simulation.ToolPosition, parts.Simulation.CurrentMoveIndex);
        scene.SetCurrentMove(parts.Simulation.CurrentMoveIndex);
        return scene.Snapshot();
    }
}
