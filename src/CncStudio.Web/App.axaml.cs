using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace CncStudio.Web;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // No navegador só existe uma "janela": a página inteira
        if (ApplicationLifetime is ISingleViewApplicationLifetime single)
        {
            single.MainView = new MainView();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
