using Avalonia.Controls;
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
    public Func<Task<string?>>? PickStockStep
    {
        get => JobPanel.PickStockStep;
        set => JobPanel.PickStockStep = value;
    }

    /// <summary>Contornos e bruto do Trabalho de fresa (para o Assist).</summary>
    public IReadOnlyList<Contour> JobContours => JobPanel.Contours;

    public JobStock? JobStockInfo => JobPanel.Stock;

    /// <summary>Fixação do Trabalho fresa (fonte única; o Assist lê daqui).</summary>
    public JobFixture? JobFixtureInfo => JobPanel.Fixture;

    /// <summary>Põe as operações sugeridas no Trabalho de fresa e mostra essa aba.</summary>
    public void ApplyPlan(IEnumerable<CamOperationSpec> operations)
    {
        JobPanel.AddOperations(operations);
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

    /// <summary>CAM-13: o Trabalho fresa envia o programa com o pacote do CAM (só a fresa; o torno segue sem pacote).</summary>
    public Action<string, string, CncStudio.Core.Cam.CamHandoff>? OpenWithPackage
    {
        get => JobPanel.OpenWithPackage;
        set => JobPanel.OpenWithPackage = value;
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

    /// <summary>Tabela de ferramentas do Code (a mesma do simulador); o CAM lê e grava nela.</summary>
    public Func<List<ToolEntry>>? ToolsSource
    {
        get => _toolsSource;
        set
        {
            _toolsSource = value;
            JobPanel.ToolsSource = value;
            LatheJobPanel.ToolsSource = value;
        }
    }

    private Func<List<ToolEntry>>? _toolsSource;

    /// <summary>Grava as configurações (depois de mudar o banco de ferramentas).</summary>
    public Action? SaveTools
    {
        get => _saveTools;
        set
        {
            _saveTools = value;
            JobPanel.SaveTools = value;
            LatheJobPanel.SaveTools = value;
        }
    }

    private Action? _saveTools;

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
        get => JobPanel.PickJob;
        set
        {
            JobPanel.PickJob = value;
            LatheJobPanel.PickJob = value;
        }
    }

    /// <summary>Salva o trabalho num arquivo escolhido (nome sugerido, texto).</summary>
    public Func<string, string, Task>? SaveJobFile
    {
        get => JobPanel.SaveJobFile;
        set
        {
            JobPanel.SaveJobFile = value;
            LatheJobPanel.SaveJobFile = value;
        }
    }

    public CamView()
    {
        InitializeComponent();
        HeaderSlot.Content = JobPanel.TabStrip;
        LatheTab.Click += (_, _) => ShowMachine(lathe: true);
        MachineTab.Click += (_, _) => ShowMachine(profile: true);
        JobTab.Click += (_, _) => ShowMachine(job: true);
        LatheJobTab.Click += (_, _) => ShowMachine(latheJob: true);
        ModelTab.Click += (_, _) => ShowMachine(model: true);
        JobPanel.MachineSource = () => MachinePanel.ProfilesSource?.Invoke().FirstOrDefault(m => m.Name == MachinePanel.ActiveSource?.Invoke());
        LatheJobPanel.MachineSource = JobPanel.MachineSource;
        // cartão Máquina da Preparação (UX-04f PR 2): os mesmos perfis e o mesmo ativo da tela Perfil 4/5 eixos
        JobPanel.ProfilesSource = () => MachinePanel.ProfilesSource?.Invoke() ?? [];
        JobPanel.ActiveMachineName = () => MachinePanel.ActiveSource?.Invoke() ?? "";
        JobPanel.SetActiveMachine = name => MachinePanel.SetActive?.Invoke(name);
        JobPanel.SaveMachines = () => MachinePanel.Save?.Invoke();
        JobPanel.OpenMachineProfiles = () => ShowMachine(profile: true);
    }

    /// <summary>Ao abrir o módulo: marca o programa do Trabalho fresa como desatualizado se o perfil da máquina mudou.</summary>
    public void CheckJobFresh() => JobPanel.CheckFresh();

    /// <summary>Ao abrir o módulo: o banco do Code pode ter mudado; o Trabalho fresa refaz o aviso de diâmetro.</summary>
    public void RefreshBank() => JobPanel.RefreshBank();

    private void ShowMachine(bool lathe = false, bool profile = false, bool job = false, bool latheJob = false, bool model = false)
    {
        JobTab.IsChecked = job;
        LatheJobTab.IsChecked = latheJob;
        LathePanel.IsVisible = lathe;
        MachinePanel.IsVisible = profile;
        JobPanel.IsVisible = job;
        LatheJobPanel.IsVisible = latheJob;
        ModelPanel.IsVisible = model;
        // as abas Preparação/Operações/Código NC são do Trabalho fresa e do Trabalho torno; nas telas do Mais, o nome dela aparece no topo
        HeaderSlot.Content = latheJob ? LatheJobPanel.TabStrip : JobPanel.TabStrip;
        HeaderSlot.IsVisible = job || latheJob;
        OtherTitle.Text = lathe ? "Torno rápido" : profile ? "Perfil 4/5 eixos" : model ? "Modelo 3D (STL/STEP)" : "";
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
