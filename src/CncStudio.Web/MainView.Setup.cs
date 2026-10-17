using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Setup;

namespace CncStudio.Web;

public partial class MainView
{
    private MachiningSetup _machiningSetup = MachiningSetup.CreateDefault("");
    private bool _editingSetup;

    // A chave mantém o prefixo "v1" para não perder setups já salvos neste aparelho;
    // a versão do formato fica dentro do próprio JSON (SchemaVersion) e é migrada na leitura.
    private string SetupKey => "cc.setup.v1." + Uri.EscapeDataString(_libraryPath ?? _fileName);

    private void LoadMachiningSetup()
    {
        var key = _libraryPath ?? _fileName;
        (MachiningSetup setup, string? warning) = SetupJson.LoadOrDefault(Stored(SetupKey), key);
        _machiningSetup = setup;
        Backplot.SetMachiningSetup(setup);
        if (warning is not null) SetStatus(warning);
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
            string? edited = await WebInterop.EditMachiningSetupAsync(SetupJson.Serialize(draft));
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
            SetStatus("Setup salvo neste aparelho para " + parsed.ProjectKey);
        }
        finally
        {
            _editingSetup = false;
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
