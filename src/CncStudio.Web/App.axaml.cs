using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace CncStudio.Web;

public sealed class App : Application
{
    /// <summary>Abre a tela de teste do 3D no lugar do app (URL com ?poc3d).</summary>
    public static bool ProofOfConcept3D { get; set; }

    /// <summary>Caminho inicial da tela de teste: "b" (three.js), "a" (OpenGL) ou "cpu".</summary>
    public static string ProofOfConcept3DStart { get; set; } = "b";

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // No navegador só existe uma "janela": a página inteira
        if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            single.MainView = ProofOfConcept3D ? new Poc.Poc3DView() : new MainView();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
