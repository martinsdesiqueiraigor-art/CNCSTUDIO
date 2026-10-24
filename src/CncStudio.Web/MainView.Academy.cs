using System.Text.Json.Nodes;
using CncStudio.Core.Academy;

namespace CncStudio.Web;

public partial class MainView
{
    private bool _openingAcademy;

    /// <summary>Módulo escolhido na tela inicial da plataforma (ou por link #academy / #macro).</summary>
    private async Task OpenPlatformModuleAsync(string id)
    {
        try
        {
            if (id == "academy") await OpenAcademyAsync();
            else if (id == "macro") await OpenGeneratorsAsync();
        }
        catch (Exception ex)
        {
            SetStatus("Não foi possível abrir o módulo: " + ex.Message);
        }
    }


    /// <summary>
    /// Academy: a tela em HTML (trilhas, aula, quiz, teste final) pede tudo ao Core. Se a pessoa abrir o programa de exemplo
    /// de uma aula, ele entra no editor (e, se pedido, no 3D). O progresso fica no aparelho.
    /// </summary>
    private async Task OpenAcademyAsync()
    {
        if (_openingAcademy) return;
        _openingAcademy = true;
        try
        {
            StopSimulation();
            string? picked = await WebInterop.OpenAcademyAsync(AcademyJson.Dispatch);
            if (picked is null || JsonNode.Parse(picked) is not JsonObject result) return;

            string text = (string?)result["text"] ?? "";
            if (text.Length == 0) return;

            string name = (string?)result["name"] ?? "ACADEMY.nc";
            LoadProgram(name, text.Replace("\r\n", "\n"));
            SetStatus("Programa de exemplo da Academy (rascunho): simule antes de rodar na máquina.");
            if ((bool?)result["view3d"] == true)
            {
                await SetModeAsync(PlotMode.ThreeD, startAtBeginning: true);
            }
        }
        finally
        {
            _openingAcademy = false;
        }
    }
}
