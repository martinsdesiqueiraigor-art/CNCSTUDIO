using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Setup;
using System.Text.Json;

namespace CncStudio.Web;

public partial class MainView
{
    private MachiningSetup _machiningSetup = MachiningSetup.CreateDefault("");
    private bool _editingSetup;

    // A chave mantém o prefixo "v1" para não perder setups já salvos neste aparelho;
    // a versão do formato fica dentro do próprio JSON (SchemaVersion) e é migrada na leitura.
    private string SetupKey => "cc.setup.v1." + Uri.EscapeDataString(_libraryPath ?? _fileName);

    // "Minhas ferramentas": lista guardada neste aparelho; programa sem setup salvo começa com ela.
    private const string MyToolsKey = "cc.mytools.v1";

    private void LoadMachiningSetup()
    {
        var key = _libraryPath ?? _fileName;
        (MachiningSetup setup, string? warning, bool fromMine) = SetupJson.LoadOrDefault(Stored(SetupKey), key, Stored(MyToolsKey));
        _machiningSetup = setup;
        Backplot.SetMachiningSetup(setup);
        if (warning is not null) SetStatus(warning);
        else if (fromMine) SetStatus($"Programa sem setup: usando minhas ferramentas ({setup.Tools.Count}). Confira no Setup.");
    }

    /// <summary>
    /// Editor de setup em HTML (o mesmo no PC e no celular). Recebe e devolve o JSON do modelo
    /// do Core; a validação final é sempre a do Core.
    /// </summary>
    private async Task EditMachiningSetupAsync()
    {
        if (_editingSetup) return;
        _editingSetup = true;
        try
        {
            MachiningSetup draft = _machiningSetup.Clone();
            draft.Stock ??= SuggestedSetupStock();
            string? edited = await WebInterop.EditMachiningSetupAsync(SetupJson.Serialize(draft), Stored(MyToolsKey) is not null);
            if (edited is null) return;
            if (SetupJson.Parse(edited).Setup is not { } parsed)
            {
                SetStatus("Setup inválido: confira os valores.");
                return;
            }

            parsed.ProjectKey = _libraryPath ?? _fileName;
            Store(SetupKey, SetupJson.Serialize(parsed));
            _machiningSetup = parsed;
            Backplot.SetMachiningSetup(parsed);
            OnMachiningSetupChanged();
            if (SaveAsMyTools(edited))
            {
                Store(MyToolsKey, SetupJson.SerializeMyTools(parsed));
                SetStatus($"Setup salvo para {parsed.ProjectKey}; {parsed.Tools.Count} ferramenta(s) guardada(s) como minhas ferramentas.");
            }
            else
            {
                SetStatus("Setup salvo neste aparelho para " + parsed.ProjectKey);
            }
        }
        finally
        {
            _editingSetup = false;
        }
    }

    /// <summary>O editor marca "SaveAsMyTools" quando a caixa "Guardar como minhas ferramentas" está ligada.</summary>
    private static bool SaveAsMyTools(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("SaveAsMyTools", out JsonElement v) && v.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Bruto proposto quando o setup ainda não tem um (o usuário confere antes de salvar):
    /// do comentário (BRUTO ...), se houver; senão, o que envolve a trajetória.
    /// </summary>
    private SetupStock SuggestedSetupStock()
    {
        MachineKind machine = _analysis?.ToolPath.Machine ?? MachineKind.Mill;
        Bounds3 cutting = _analysis?.ToolPath.CuttingBounds ?? Bounds3.Empty;
        return SetupBinding.FromStock(SetupBinding.SuggestStock(machine, Editor.Text, cutting));
    }

    /// <summary>Ferramentas e bruto mudaram: refaz a trajetória (compensação G41/G42) mantendo a vista.</summary>
    private void OnMachiningSetupChanged() => Analyze(keepView: true);
}
