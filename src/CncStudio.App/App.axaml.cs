using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace CncStudio.App;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        // UI-01: Inter (fonts:Inter) para a interface do CAM; sem ela, o CAM cai na fonte do sistema
        try
        {
            Avalonia.Media.FontManager.Current.AddFontCollection(new Avalonia.Fonts.Inter.InterFontCollection());
        }
        catch (System.ArgumentException)
        {
            // já registrada (ex.: AppBuilder.WithInterFont)
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Arquivo passado na linha de comando (duplo clique num .nc associado ao CncStudio)
            desktop.MainWindow = new MainWindow(desktop.Args ?? []);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
