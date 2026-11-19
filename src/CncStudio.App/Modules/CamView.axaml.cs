using Avalonia.Controls;
using Avalonia.Platform.Storage;
using CncStudio.App.Settings;
using CncStudio.Core.Cam;

namespace CncStudio.App.Modules;

/// <summary>
/// Casillas CAM no desktop: seletor Fresa/Torno (Trabalho fresa e Trabalho torno) e, no menu Mais, Torno rápido, Perfil 4/5
/// eixos e Modelo 3D (UX-04c). As abas do Trabalho fresa vêm do <see cref="JobView.TabStrip"/>.
/// Todo o cálculo é do Core (<c>Core/Cam</c>); aqui só se troca de aba e se repassam as ligações com o app.
/// </summary>
public partial class CamView : UserControl
{
    /// <summary>Abre o seletor de arquivo STL e devolve nome e bytes (null se cancelou).</summary>
    public Func<Task<(string Name, byte[] Data)?>>? PickStl
    {
        get => ModelPanel.PickStl;
        set => ModelPanel.PickStl = value;
    }

    /// <summary>Abre o seletor de arquivo DXF e devolve nome e texto (null se cancelou).</summary>
    public Func<Task<(string Name, string Text)?>>? PickDxf
    {
        get => _pickDxf;
        set
        {
            _pickDxf = value;
            LathePanel.PickDxf = value;
            JobPanel.PickDxf = value;
            LatheJobPanel.PickDxf = value;
        }
    }

    private Func<Task<(string Name, string Text)?>>? _pickDxf;

    /// <summary>Escolhe o STEP do bruto da fresa e devolve o caminho no computador (null se cancelou).</summary>
    /// <summary>M3D-1: escolhe o STEP da peça do Trabalho fresa (caminho no computador).</summary>
    public Func<Task<string?>>? PickPartStep
    {
        get => JobPanel.PickPartStep;
        set => JobPanel.PickPartStep = value;
    }

    public Func<Task<string?>>? PickStockStep
    {
        get => JobPanel.PickStockStep;
        set => JobPanel.PickStockStep = value;
    }

    /// <summary>Contornos e bruto do Trabalho de fresa (para o Assist).</summary>
    public IReadOnlyList<Contour> JobContours => JobPanel.Contours;

    /// <summary>Contornos de desenho para o Assist por DXF (sem os guardados pelas operações do 3D).</summary>
    public IReadOnlyList<Contour> JobPlanContours => JobPanel.PlanContours;

    public JobStock? JobStockInfo => JobPanel.Stock;

    /// <summary>STEP-OPS S4: a peça STEP lida no Trabalho como desenho do Assist; null = sem peça STEP lida.</summary>
    public CncStudio.Core.Assist.StepPlanDrawing? JobStepPlan => JobPanel.StepPlan;

    /// <summary>Põe no Trabalho fresa os contornos do STEP e as operações do plano do Assist.</summary>
    public void ApplyStepPlan(IReadOnlyList<Contour> contours, IReadOnlyList<CamOperationSpec> operations)
    {
        JobPanel.AddStepPlan(contours, operations);
        ShowMachine(job: true);
    }

    /// <summary>Fixação do Trabalho fresa (fonte única; o Assist lê daqui).</summary>
    public JobFixture? JobFixtureInfo => JobPanel.Fixture;

    /// <summary>Põe as operações sugeridas no Trabalho de fresa e mostra essa aba.</summary>
    public void ApplyPlan(IEnumerable<CamOperationSpec> operations)
    {
        // o plano numerou só os contornos de desenho: volta para o índice deles no trabalho
        IReadOnlyList<int> map = JobPanel.PlanContourMap;
        JobPanel.AddOperations(operations.Select(o => o with
        {
            ContourIndex = o.ContourIndex >= 0 && o.ContourIndex < map.Count ? map[o.ContourIndex] : o.ContourIndex,
            ContourIndices = o.ContourIndices?.Select(c => c >= 0 && c < map.Count ? map[c] : c).ToList(),
        }));
        ShowMachine(job: true);
    }

    /// <summary>Abre um texto numa aba nova do editor (texto, nome sugerido).</summary>
    public Action<string, string>? OpenInEditor
    {
        get => _openInEditor;
        set
        {
            _openInEditor = value;
            LathePanel.OpenInEditor = value;
            JobPanel.OpenInEditor = value;
            LatheJobPanel.OpenInEditor = value;
            ModelPanel.OpenInEditor = value;
        }
    }

    private Action<string, string>? _openInEditor;

    /// <summary>CAM-13: os Trabalhos fresa e torno (TORNO-T7) enviam o programa com o pacote do CAM.</summary>
    public Action<string, string, CncStudio.Core.Cam.CamHandoff>? OpenWithPackage
    {
        get => JobPanel.OpenWithPackage;
        set
        {
            JobPanel.OpenWithPackage = value;
            LatheJobPanel.OpenWithPackage = value;
        }
    }

    public Func<string, Task>? CopyText
    {
        get => _copyText;
        set
        {
            _copyText = value;
            LathePanel.CopyText = value;
            JobPanel.CopyText = value;
            LatheJobPanel.CopyText = value;
        }
    }

    private Func<string, Task>? _copyText;

    /// <summary>Larguras guardadas da árvore e do painel do Trabalho (fresa e torno usam as mesmas; 0 = padrão).</summary>
    public (double Tree, double Props) JobPanes
    {
        set
        {
            JobPanel.SetPanes(value.Tree, value.Props);
            LatheJobPanel.SetPanes(value.Tree, value.Props);
        }
    }

    /// <summary>Escolhas guardadas dos chips do 3D do Trabalho fresa (texto do config).</summary>
    public string JobViewOptions
    {
        set => JobPanel.ViewOptions = value;
    }

    /// <summary>Guarda as escolhas dos chips do 3D do Trabalho fresa.</summary>
    public Action<string>? SaveJobViewOptions
    {
        get => JobPanel.SaveViewOptions;
        set => JobPanel.SaveViewOptions = value;
    }

    /// <summary>Guarda as larguras depois de arrastar uma divisória do Trabalho (fresa ou torno); a outra tela acompanha.</summary>
    public Action<double, double>? SaveJobPanes
    {
        get => _saveJobPanes;
        set
        {
            _saveJobPanes = value;
            JobPanel.SavePanes = (tree, props) =>
            {
                LatheJobPanel.SetPanes(tree, props);
                value?.Invoke(tree, props);
            };
            LatheJobPanel.SavePanes = (tree, props) =>
            {
                JobPanel.SetPanes(tree, props);
                value?.Invoke(tree, props);
            };
        }
    }

    private Action<double, double>? _saveJobPanes;

    /// <summary>Seletor de trabalho salvo (.casillas-job) da tela Trabalho.</summary>
    public Func<Task<(string Name, string Text)?>>? PickJob
    {
        get => _pickJob;
        set
        {
            // UI-06: o CAM abre o seletor ele mesmo para saber o caminho (lista de recentes); sem janela, usa o do app
            _pickJob = value;
            JobPanel.PickJob = LatheJobPanel.PickJob = PickJobWithPath;
        }
    }

    private Func<Task<(string Name, string Text)?>>? _pickJob;

    private static readonly FilePickerFileType JobFileType = new("Trabalho Casillas CAM") { Patterns = ["*.casillas-job", "*.json"] };

    /// <summary>Seletor do trabalho que guarda o caminho nos recentes (fresa ou torno pelo formato do arquivo).</summary>
    private async Task<(string Name, string Text)?> PickJobWithPath()
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanOpen: true } storage)
        {
            return _pickJob is null ? null : await _pickJob();
        }

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Abrir trabalho do CAM", AllowMultiple = false, FileTypeFilter = [JobFileType],
        });
        if (files.Count == 0)
        {
            return null;
        }

        await using Stream stream = await files[0].OpenReadAsync();
        using var reader = new StreamReader(stream);
        string text = await reader.ReadToEndAsync();
        if (files[0].TryGetLocalPath() is { } path)
        {
            CamRecent.Add(path, IsLatheJob(text));
        }

        return (files[0].Name, text);
    }

    /// <summary>Trabalho de torno pelo formato do arquivo (o mesmo nome de formato que o Core grava).</summary>
    private static bool IsLatheJob(string text) => text.Contains("\"casillas-job-torno\"", StringComparison.Ordinal);

    /// <summary>Salva o trabalho num arquivo escolhido (nome sugerido, texto).</summary>
    public Func<string, string, Task>? SaveJobFile
    {
        get => _saveJobFile;
        set
        {
            // UI-06: como no Abrir, o CAM salva pelo próprio seletor para guardar o caminho nos recentes
            _saveJobFile = value;
            JobPanel.SaveJobFile = LatheJobPanel.SaveJobFile = SaveJobWithPath;
        }
    }

    private Func<string, string, Task>? _saveJobFile;

    private async Task SaveJobWithPath(string suggested, string text)
    {
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { CanSave: true } storage)
        {
            if (_saveJobFile is not null)
            {
                await _saveJobFile(suggested, text);
            }

            return;
        }

        IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Salvar trabalho do CAM", SuggestedFileName = suggested, DefaultExtension = "casillas-job",
            FileTypeChoices = [new FilePickerFileType("Trabalho Casillas CAM") { Patterns = ["*.casillas-job"] }],
        });
        if (file is null)
        {
            return;
        }

        await using (Stream stream = await file.OpenWriteAsync())
        {
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(text);
        }

        if (file.TryGetLocalPath() is { } path)
        {
            CamRecent.Add(path, IsLatheJob(text));
        }
    }

    /// <summary>Tela inicial: abre um trabalho (fresa ou torno pelo formato) e mostra a tela dele; null = abriu.</summary>
    private void OpenJobText(string text, string? path)
    {
        bool lathe = IsLatheJob(text);
        ShowMachine(job: !lathe, latheJob: lathe);
        string? error = lathe ? LatheJobPanel.OpenText(text) : JobPanel.OpenText(text);
        if (error is null && path is not null)
        {
            CamRecent.Add(path, lathe);
        }
    }

    private async Task HomeOpenJob()
    {
        if (await PickJobWithPath() is { } file)
        {
            // o caminho já foi para os recentes no seletor
            OpenJobText(file.Text, null);
        }
    }

    private void HomeOpenRecent(CamRecentItem item)
    {
        string text;
        try
        {
            text = File.ReadAllText(item.Path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            HomePanel.Refresh();
            return;
        }

        OpenJobText(text, item.Path);
    }

    public CamView()
    {
        InitializeComponent();
        CamColors.ApplyControls(Resources);
        HeaderSlot.Content = JobPanel.TabStrip;
        HomeTab.Click += (_, _) => ShowMachine(home: true);
        HomePanel.MillJob = () => ShowMachine(job: true);
        HomePanel.LatheJob = () => ShowMachine(latheJob: true);
        HomePanel.OpenJob = () => _ = HomeOpenJob();
        HomePanel.OpenRecent = HomeOpenRecent;
        HomePanel.OpenDrawing = () =>
        {
            ShowMachine(job: true);
            _ = JobPanel.RunFileAction("abrir-dxf");
        };
        HomePanel.OpenProfiles = () => ShowMachine(profile: true);
        HomePanel.OpenBank = () => ShowMachine(bank: true);
        HomePanel.ActiveMachine = () => MachinePanel.ActiveSource?.Invoke() ?? "";
        JobPanel.PickJob = LatheJobPanel.PickJob = PickJobWithPath;
        JobPanel.SaveJobFile = LatheJobPanel.SaveJobFile = SaveJobWithPath;
        LatheTab.Click += (_, _) => ShowMachine(lathe: true);
        MachineTab.Click += (_, _) => ShowMachine(profile: true);
        JobTab.Click += (_, _) => ShowMachine(job: true);
        LatheJobTab.Click += (_, _) => ShowMachine(latheJob: true);
        ModelTab.Click += (_, _) => ShowMachine(model: true);
        ToolBankTab.Click += (_, _) => ShowMachine(bank: true);
        JobPanel.MachineSource = () => MachinePanel.ProfilesSource?.Invoke().FirstOrDefault(m => m.Name == MachinePanel.ActiveSource?.Invoke());
        LatheJobPanel.MachineSource = JobPanel.MachineSource;
        // cartão Máquina da Preparação (UX-04f PR 2): os mesmos perfis e o mesmo ativo da tela Perfil 4/5 eixos
        JobPanel.ProfilesSource = () => MachinePanel.ProfilesSource?.Invoke() ?? [];
        JobPanel.ActiveMachineName = () => MachinePanel.ActiveSource?.Invoke() ?? "";
        JobPanel.SetActiveMachine = name => MachinePanel.SetActive?.Invoke(name);
        JobPanel.SaveMachines = () => MachinePanel.Save?.Invoke();
        JobPanel.OpenMachineProfiles = () => ShowMachine(profile: true);
        // TORNO-T1: o cartão Máquina do Trabalho torno usa os mesmos perfis e o mesmo perfil ativo
        LatheJobPanel.ProfilesSource = JobPanel.ProfilesSource;
        LatheJobPanel.ActiveMachineName = JobPanel.ActiveMachineName;
        LatheJobPanel.SetActiveMachine = JobPanel.SetActiveMachine;
        LatheJobPanel.SaveMachines = JobPanel.SaveMachines;
        LatheJobPanel.OpenMachineProfiles = JobPanel.OpenMachineProfiles;
        // UI-06: o CAM abre na tela inicial
        ShowMachine(home: true);
    }

    /// <summary>Ao abrir o módulo: marca o programa do Trabalho fresa como desatualizado se o perfil da máquina mudou.</summary>
    public void CheckJobFresh() => JobPanel.CheckFresh();

    /// <summary>Ao abrir o módulo: o banco do Code pode ter mudado; o Trabalho fresa refaz o aviso de diâmetro.</summary>
    public void RefreshBank()
    {
        JobPanel.RefreshBank();
        LatheJobPanel.RefreshBank();
    }

    /// <summary>Trabalho do torno (UX-04h D1: banco ativo do torno).</summary>
    public LatheJobView LatheJob => LatheJobPanel;

    /// <summary>Trabalho fresa (para ligar o banco ativo, UX-04h B2).</summary>
    public JobView MillJob => JobPanel;

    /// <summary>Tela do banco de ferramentas (UX-04h B1).</summary>
    public ToolBankView ToolBanks => ToolBankPanel;

    private void ShowMachine(bool lathe = false, bool profile = false, bool job = false, bool latheJob = false, bool model = false, bool bank = false, bool home = false)
    {
        HomeTab.IsChecked = home;
        HomePanel.IsVisible = home;
        if (home)
        {
            HomePanel.Refresh();
        }

        JobTab.IsChecked = job;
        LatheJobTab.IsChecked = latheJob;
        LathePanel.IsVisible = lathe;
        MachinePanel.IsVisible = profile;
        JobPanel.IsVisible = job;
        LatheJobPanel.IsVisible = latheJob;
        ModelPanel.IsVisible = model;
        ToolBankTab.IsChecked = bank;
        ToolBankPanel.IsVisible = bank;
        // as abas Preparação/Operações/Código NC são do Trabalho fresa e do Trabalho torno; nas telas do Mais, o nome dela aparece no topo
        HeaderSlot.Content = latheJob ? LatheJobPanel.TabStrip : JobPanel.TabStrip;
        HeaderSlot.IsVisible = job || latheJob;
        OtherTitle.Text = home ? "" : lathe ? "Torno rápido" : profile ? "Perfil 4/5 eixos" : model ? "Modelo 3D (STL/STEP)" : bank ? "Banco de ferramentas" : "";
        if (profile)
        {
            MachinePanel.Refresh();
        }

        if (job)
        {
            JobPanel.CheckFresh(); // o perfil pode ter mudado na aba Máquina
        }
    }

    /// <summary>Perfis de máquina 4/5 eixos guardados nas configurações.</summary>
    public Func<List<MachineProfile>>? MachineProfilesSource
    {
        get => MachinePanel.ProfilesSource;
        set => MachinePanel.ProfilesSource = value;
    }

    public Func<string>? ActiveMachineSource
    {
        get => MachinePanel.ActiveSource;
        set => MachinePanel.ActiveSource = value;
    }

    public Action<string>? SetActiveMachine
    {
        get => MachinePanel.SetActive;
        set => MachinePanel.SetActive = value;
    }

    public Action? SaveMachines
    {
        get => MachinePanel.Save;
        set => MachinePanel.Save = value;
    }
}
