using CncStudio.App.Settings;
using CncStudio.App.Modules;
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
        Cam.ToolBanks.Load(folder, loaded, _settings.ToolBankNotes);
        Cam.ToolBanks.BanksChanged += banks =>
        {
            _settings.Banks = banks;
            StartAnalysis(); // o simulador lê os diâmetros do banco
            Cam.RefreshBank(); // seletor do banco ativo, escolha e avisos do Trabalho fresa
        };

        // UX-04h B2: o Trabalho fresa escolhe a ferramenta no banco ativo (guardado só no config.json, nunca no .casillas-job)
        JobView job = Cam.MillJob;
        job.BanksSource = () => _settings.Banks;
        job.ActiveBankId = () => _settings.ActiveMillBankId;
        job.SetActiveBank = id =>
        {
            _settings.ActiveMillBankId = id;
            _settings.Save();
        };
        job.SaveBank = bank => Cam.ToolBanks.SaveFromJob(bank);
        job.RefreshBank();
        Cam.ToolBanks.NotesDismissed += () =>
        {
            _settings.ToolBankNotes.Clear();
            _settings.Save();
        };
    }
}
