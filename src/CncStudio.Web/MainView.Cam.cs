using System.Text.Json.Nodes;
using CncStudio.Core.Cam;

namespace CncStudio.Web;

public partial class MainView
{
    private const string CamKey = "cc.cam.last";
    private bool _openingCam;

    /// <summary>Giro do 4º eixo do programa que veio do CAM (para o 3D desenhar o percurso na peça); apagado ao abrir outro programa.</summary>
    private CncStudio.Core.Scene.RotaryPartFrame? _camPartFrame;

    /// <summary>CAM rápido: formulário em HTML, programa calculado pelo Core. O resultado entra no editor e, se pedido, abre no 3D.</summary>
    private async Task OpenCamAsync()
    {
        if (_openingCam) return;
        _openingCam = true;
        try
        {
            StopSimulation();
            string? picked = await WebInterop.OpenCamAsync(CamQuickJson.Catalog(), CamQuickJson.Generate, Stored(CamKey) ?? "{}");
            if (picked is null || JsonNode.Parse(picked) is not JsonObject result) return;

            string text = (string?)result["text"] ?? "";
            if (text.Length == 0) return;

            if (result["values"] is JsonNode values) Store(CamKey, values.ToJsonString());
            LoadProgram("CAM.nc", text.Replace("\r\n", "\n"));
            _camPartFrame = result["values"] is JsonNode vals ? CamQuickJson.PartFrameOf(vals.ToJsonString()) : null;
            _dirty = true;
            UpdateTitle();
            SetStatus("Programa do CAM (rascunho): simule e rode no ar antes de usar na máquina.");
            if ((bool?)result["view3d"] == true)
            {
                await SetModeAsync(PlotMode.ThreeD, startAtBeginning: true);
            }
        }
        finally
        {
            _openingCam = false;
        }
    }
}
