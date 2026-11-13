using CncStudio.App.Settings;
using CncStudio.Core.Cam;

namespace CncStudio.App;

/// <summary>Banco de ferramentas (UX-04h B1): migração única da tabela antiga, leitura da pasta e a tela no CAM.</summary>
public partial class MainWindow
{
    private void InitToolBanks()
    {
        string folder = AppSettings.ToolBankFolderPath;
        if (ToolBankStore.MigrateOnce(folder, _settings.ToolBanksMigrated, _settings.LegacyToolRows()) is { } done)
        {
            // a tabela fica como está no config.json; só a marca e a lista do que não entrou mudam
            _settings.ToolBankNotes = [.. done.Migration.Warnings, .. done.SaveErrors];
            _settings.ToolBanksMigrated = done.SaveErrors.Count < 2; // nada gravado: tenta de novo na próxima abertura
            _settings.Save();
        }

        ToolBankFolder loaded = ToolBankStore.Load(folder);
        _settings.Banks = [.. loaded.Banks.Select(b => b.Bank)];
    }
}
