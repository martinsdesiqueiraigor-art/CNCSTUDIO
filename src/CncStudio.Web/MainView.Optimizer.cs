using System.Text.Json.Nodes;
using CncStudio.Core.Optimizer;

namespace CncStudio.Web;

public partial class MainView
{
    private bool _openingOptimizer;

    /// <summary>
    /// Otimizador: analisa o programa do editor e propõe laços Macro B SÓ PARA SIMULADOR. A tela em HTML pede tudo ao Core.
    /// Se a pessoa escolher abrir a versão otimizada, ela entra no editor (o arquivo original não é alterado em disco).
    /// </summary>
    private async Task OpenOptimizerAsync()
    {
        if (_openingOptimizer) return;
        string original = Editor.Text;

        _openingOptimizer = true;
        try
        {
            StopSimulation();
            string originalName = _fileName;
            string? picked = await WebInterop.OpenOptimizerAsync(OptimizerJson.Dispatch, original, originalName);
            if (picked is null || JsonNode.Parse(picked) is not JsonObject result) return;

            string text = ((string?)result["text"] ?? "").Replace("\r\n", "\n");
            if (text.Length == 0) return;

            string name = (string?)result["name"] ?? "OTIMIZADO.nc";
            string action = (string?)result["action"] ?? "editor";
            if (action == "compare")
            {
                string left = ((string?)result["original"] ?? original).Replace("\r\n", "\n");
                OpenCompare(left, (string?)result["originalName"] ?? originalName);
                CompareView.SetRight(text, name);
                SetStatus("Comparação: original à esquerda, versão otimizada (somente simulador) à direita.");
                return;
            }

            LoadProgram(name, text);
            SetStatus("Versão otimizada para SIMULADOR. Não executar na máquina. O original (" + originalName + ") não foi alterado.");
            if (action == "view3d")
            {
                await SetModeAsync(PlotMode.ThreeD, startAtBeginning: true);
            }
        }
        finally
        {
            _openingOptimizer = false;
        }
    }
}
