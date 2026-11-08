using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using CncStudio.App.Modules;
using CncStudio.Core.Cam;
using CncStudio.Core.Execution;

namespace CncStudio.App;

/// <summary>
/// CAM-13 PR 2: o Code usa o pacote do CAM. As regras ("quem vale" e os avisos) estão no Core (<see cref="CamHandoffCode"/>); aqui só a
/// ligação com a aba, o salvar e a barra de avisos. O texto do G-code nunca muda e a tabela geral (ferramentas e zeros) não é alterada.
/// </summary>
public partial class MainWindow
{
    /// <summary>
    /// Liga o pacote à aba: o que veio do "Enviar ao Code" (memória) ou o arquivo ao lado do programa aberto do disco. Com pacote, o
    /// dialeto vem dele e o bruto do CAM preenche o setup só se o setup não tem bruto. Sem pacote, nada muda.
    /// </summary>
    private static void AttachCamPackage(OpenProgram document, string? path, CamHandoff? package)
    {
        HandoffReadResult read = package is not null ? new HandoffReadResult(package, null)
            : path is not null ? CamHandoffFile.Read(path)
            : new HandoffReadResult(null, null);
        document.CamPackage = read.Package;
        document.CamReadWarning = read.Warning;
        if (read.Package is { } p)
        {
            document.Dialect = CamHandoffCode.DialectOf(p);
            CamHandoffCode.FillStock(document.Setup, p);
        }
    }

    /// <summary>Grava o pacote da aba ao lado do programa salvo (nome inteiro + .casillas-cam). Sem pacote: nada.</summary>
    private void SaveCamPackage(string programPath)
    {
        if (_active.CamPackage is not { } p)
        {
            return;
        }

        try
        {
            CamHandoffFile.Write(programPath, p);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AnalysisStatus.Text = $"Programa salvo, mas os dados do CAM não foram gravados ao lado: {ex.Message}";
        }
    }

    /// <summary>Configuração da análise: tabela geral com o Ø do CAM na frente (só para este programa). O setup entra depois, por cima.</summary>
    private MachineSettings MachineForProgram(string? folder) =>
        CamHandoffCode.ApplyDiameters(_settings.ToMachineSettings(folder), _active.CamPackage);

    /// <summary>T cujo Ø neste programa veio do CAM (etiqueta "do CAM" nas estatísticas): o setup do programa não o redefine.</summary>
    private bool ToolFromCam(int tool) =>
        CamHandoffCode.Diameters(_active.CamPackage).ContainsKey(tool) && _active.Setup.ToolOf(tool) is null;

    private IReadOnlyList<HandoffNotice> CamNotices()
    {
        MachineSettings table = _settings.ToMachineSettings(null);
        Func<int, double> tableOf = table.ToolDiameterOf ?? (_ => 0);
        return CamHandoffCode.Notices(_active.CamPackage, _active.CamReadWarning, _active.Setup, Editor.Text, tableOf, table.WorkOffsets);
    }

    /// <summary>Texto dos avisos de certos tipos (para o diálogo do bruto); null se nenhum vale agora.</summary>
    private string? CamNoteFor(params HandoffNoticeKind[] kinds)
    {
        string[] texts = CamNotices().Where(n => kinds.Contains(n.Kind)).Select(n => n.Text).ToArray();
        return texts.Length == 0 ? null : string.Join("\n", texts);
    }

    /// <summary>Barra de avisos do CAM acima do gráfico: refeita a cada análise (texto, setup e tabelas). Aviso fixo, sem fechar.</summary>
    private void RefreshCamNotes()
    {
        CamNotes.Children.Clear();
        foreach (HandoffNotice n in CamNotices())
        {
            Border note = UiKit.StatusNote(n.Text, "pending");
            note.Padding = new Thickness(8, 4);
            if (n.Kind == HandoffNoticeKind.StockDiffers)
            {
                var use = new Button { Content = "Usar o do CAM", Padding = new Thickness(10, 3), VerticalAlignment = VerticalAlignment.Center };
                use.Click += (_, _) => UseCamStock();
                var row = new DockPanel();
                DockPanel.SetDock(use, Dock.Right);
                use.Margin = new Thickness(8, 0, 0, 0);
                TextBlock text = (TextBlock)note.Child!;
                note.Child = row;
                row.Children.Add(use);
                row.Children.Add(text);
            }

            CamNotes.Children.Add(note);
        }

        CamNotes.IsVisible = CamNotes.Children.Count > 0;
    }

    /// <summary>"Usar o do CAM": troca o bruto informado pelo do CAM (só quando o usuário pede).</summary>
    private void UseCamStock()
    {
        if (CamHandoffCode.ToSetupStock(_active.CamPackage?.Stock) is not { } cam)
        {
            return;
        }

        Core.Setup.MachiningSetup setup = _active.Setup.Clone();
        setup.Stock = cam;
        _active.Setup = setup;
        _settings.SetSetup(SetupKey, setup);
        _settings.Save();
        StartAnalysis();
    }
}
