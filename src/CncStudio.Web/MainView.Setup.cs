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
            draft.Stock ??= SuggestedSetupStock(); // com comentário BRUTO, a tela troca pelo do programa ("Do programa")
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

            // WEB-02: "Outro bruto" com comentário BRUTO no programa: aviso já ao salvar
            if (StockOverrideNote() is { } note)
            {
                SetStatus("⚠ " + note);
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

    /// <summary>WEB-02: "Outro bruto" escolhido com comentário BRUTO no programa (null = a simulação segue o programa).</summary>
    private string? StockOverrideNote() => _analysis is { } a
        ? SetupBinding.OverrideNote(_machiningSetup, a.ToolPath.Machine, Editor.Text, a.ToolPath.CuttingBounds)
        : null;

    /// <summary>
    /// WEB-02: o setup que a simulação, o desenho e a folha usam: o bruto do comentário BRUTO, na posição do programa, manda
    /// (a não ser "Outro bruto"). O setup salvo não muda.
    /// </summary>
    private MachiningSetup SimSetup => _analysis is { } a
        ? SetupBinding.ProgramFirst(_machiningSetup, a.ToolPath.Machine, Editor.Text, a.ToolPath.CuttingBounds)
        : _machiningSetup;

    /// <summary>
    /// WEB-01 S1/S2: contexto para a tela do setup: máquina do programa e o bruto do comentário (para sugerir e comparar).
    /// </summary>
    private string SetupContextJson()
    {
        MachineKind machine = _analysis?.ToolPath.Machine ?? MachineKind.Mill;
        Bounds3 cutting = _analysis?.ToolPath.CuttingBounds ?? Bounds3.Empty;
        SetupStock? program = ProgramStock.Read(Editor.Text, machine, cutting);
        var o = new System.Text.Json.Nodes.JsonObject
        {
            ["machine"] = machine == MachineKind.Lathe ? "torno" : "fresa",
            ["fromProgram"] = _machiningSetup.Stock is not { OverridesProgram: true },
        };
        if (program is not null)
        {
            o["programStock"] = System.Text.Json.Nodes.JsonNode.Parse(SetupJson.Serialize(new MachiningSetup { Stock = program }))!["Stock"]!.DeepClone();
            o["programStockText"] = DescribeProgramStock(program, machine);
        }

        return o.ToJsonString();
    }

    // "100 × 80 × 20", "Ø50 × 30", "Ø50 / Ø30 × 30" (fresa); torno como a simulação descreve
    private static string DescribeProgramStock(SetupStock s, MachineKind machine)
    {
        static string N(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture).Replace('.', ',');
        if (machine == MachineKind.Lathe && s.ToStock(machine) is { } bar)
        {
            return SetupBinding.Describe(bar);
        }

        return s.Kind switch
        {
            SetupStockKind.Round => $"Ø{N(s.Diameter)} × {N(s.Height)}",
            SetupStockKind.Tube => $"Ø{N(s.Diameter)} / Ø{N(s.InnerDiameter)} × {N(s.Height)}",
            _ => $"{N(s.Width)} × {N(s.Depth)} × {N(s.Height)}",
        };
    }

    /// <summary>Ferramentas e bruto mudaram: refaz a trajetória (compensação G41/G42) mantendo a vista.</summary>
    private void OnMachiningSetupChanged() => Analyze(keepView: true);
}
