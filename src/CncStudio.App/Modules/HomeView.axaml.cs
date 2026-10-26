using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace CncStudio.App.Modules;

/// <summary>Tela inicial da plataforma no desktop: um cartão por módulo, disponível ou "em breve".</summary>
public partial class HomeView : UserControl
{
    /// <summary>Avisa qual módulo disponível a pessoa escolheu.</summary>
    public event Action<PlatformModule>? ModuleChosen;

    public HomeView()
    {
        InitializeComponent();
        foreach (PlatformModule module in PlatformModules.All)
        {
            Cards.Items.Add(BuildCard(module));
        }
    }

    private Control BuildCard(PlatformModule module)
    {
        var title = new TextBlock { Text = module.Name, FontWeight = FontWeight.SemiBold, FontSize = 16, TextWrapping = TextWrapping.Wrap };
        var text = new TextBlock { Text = module.Description, FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };
        var status = new TextBlock
        {
            Text = module.Available ? "Disponível" : "Em breve no desktop",
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Foreground = module.Available ? Brushes.MediumSeaGreen : Brushes.Goldenrod,
        };
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(title);
        stack.Children.Add(text);
        stack.Children.Add(status);

        var button = new Button
        {
            Content = stack,
            Width = 250,
            MinHeight = 110,
            Margin = new Thickness(8),
            Padding = new Thickness(14),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top,
            IsEnabled = module.Available,
        };
        button.Click += (_, _) => ModuleChosen?.Invoke(module);
        return button;
    }
}
