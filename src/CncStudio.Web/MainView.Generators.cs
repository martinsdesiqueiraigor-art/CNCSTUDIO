using System.Text.Json.Nodes;
using CncStudio.Core.Generators;

namespace CncStudio.Web;

public partial class MainView
{
    private const string GeneratorsKey = "cc.generators.last";
    private bool _openingGenerators;

    /// <summary>
    /// Geradores de programa (Macro): formulário em HTML, programa calculado pelo Core. O resultado entra no
    /// editor como programa novo e, se a pessoa pediu, abre direto no 3D (a prévia é a simulação do editor).
    /// </summary>
    private async Task OpenGeneratorsAsync()
    {
        if (_openingGenerators) return;
        _openingGenerators = true;
        try
        {
            StopSimulation();
            string? picked = await WebInterop.OpenGeneratorsAsync(
                GeneratorsJson.Catalog(),
                GeneratorsJson.Generate,
                Stored(GeneratorsKey) ?? "{}");
            if (picked is null || JsonNode.Parse(picked) is not JsonObject result) return;

            string id = (string?)result["id"] ?? "";
            string mode = (string?)result["mode"] ?? "plain";
            string text = (string?)result["text"] ?? "";
            if (text.Length == 0) return;

            Store(GeneratorsKey, GeneratorsJson.RememberLast(Stored(GeneratorsKey), id, mode, result["values"]));
            string name = (id == "bhc" ? "BHC_FURACAO_CIRCULO" : id == "pocket" ? "BOLSAO_RETANGULAR" : "GERADO") + ".nc";
            LoadProgram(name, text.Replace("\r\n", "\n"));
            _dirty = true;
            UpdateTitle();
            SetStatus("Programa gerado (rascunho): confira antes de usar na máquina.");
            if ((bool?)result["view3d"] == true)
            {
                await SetModeAsync(PlotMode.ThreeD, startAtBeginning: true);
            }
        }
        finally
        {
            _openingGenerators = false;
        }
    }
}
