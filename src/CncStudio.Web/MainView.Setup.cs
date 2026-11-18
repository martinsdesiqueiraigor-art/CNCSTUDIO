using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Setup;
using CncStudio.Core.Simulation;
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
            string? edited = await WebInterop.EditMachiningSetupAsync(SetupJson.Serialize(draft), Stored(MyToolsKey) is not null, SetupContextJson());
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

            // WEB-01 S1: o aviso de bruto diferente do comentário aparece já ao salvar
            if (StockMismatch() is { } mismatch)
            {
                SetStatus("⚠ " + mismatch);
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

    /// <summary>WEB-01 S1: bruto do setup diferente do comentário BRUTO do programa (null = iguais ou sem um dos dois).</summary>
    private string? StockMismatch() => _analysis is { } a
        ? SetupBinding.StockMismatch(_machiningSetup, a.ToolPath.Machine, Editor.Text, a.ToolPath.CuttingBounds)
        : null;

    /// <summary>
    /// WEB-01 S1/S2: contexto para a tela do setup: máquina do programa e o bruto do comentário (para sugerir e comparar).
    /// </summary>
    private string SetupContextJson()
    {
        MachineKind machine = _analysis?.ToolPath.Machine ?? MachineKind.Mill;
        Bounds3 cutting = _analysis?.ToolPath.CuttingBounds ?? Bounds3.Empty;
        Stock? comment = SetupBinding.FromProgramComments(Editor.Text, machine, cutting);
        var o = new System.Text.Json.Nodes.JsonObject
        {
            ["machine"] = machine == MachineKind.Lathe ? "torno" : "fresa",
            ["fromProgram"] = _machiningSetup.Stock is null,
        };
        if (comment is not null)
        {
            o["programStock"] = System.Text.Json.Nodes.JsonNode.Parse(SetupJson.Serialize(new MachiningSetup { Stock = SetupBinding.FromStock(comment) }))!["Stock"]!.DeepClone();
            o["programStockText"] = SetupBinding.Describe(comment);
        }

        return o.ToJsonString();
    }

    /// <summary>Ferramentas e bruto mudaram: refaz a trajetória (compensação G41/G42) mantendo a vista.</summary>
    private void OnMachiningSetupChanged() => Analyze(keepView: true);
}
