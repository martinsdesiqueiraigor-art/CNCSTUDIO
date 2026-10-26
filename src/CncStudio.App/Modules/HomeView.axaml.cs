using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace CncStudio.App.Modules;

/// <summary>Tela inicial da plataforma no desktop, no mesmo estilo da web: cartões com ilustração e cor por produto.</summary>
public partial class HomeView : UserControl
{
    /// <summary>Avisa qual módulo disponível a pessoa escolheu.</summary>
    public event Action<PlatformModule>? ModuleChosen;

    public HomeView()
    {
        InitializeComponent();
        Logo.Source = PlatformModules.Logo();
        // Borda suave em volta da logo, como na web (máscara radial)
        Logo.OpacityMask = new RadialGradientBrush
        {
            GradientStops = [new GradientStop(Colors.Black, 0.64), new GradientStop(Colors.Transparent, 0.78)],
        };
        foreach (PlatformModule module in PlatformModules.All)
        {
            Cards.Items.Add(Card(module.Id, module.Name, module.Description, module.Color, module.Available ? "" : "Em breve", module.Available, () => ModuleChosen?.Invoke(module)));
        }

        foreach (WebModule web in PlatformModules.Web)
        {
            bool soon = web.Url.Length == 0;
            WebCards.Items.Add(Card(web.Id, web.Name, web.Description, web.Color, soon ? "Em breve" : "↗ web", !soon, () => PlatformModules.OpenWeb(web.Url)));
        }
    }

    private static Control Card(string id, string name, string text, string color, string badge, bool enabled, Action click)
    {
        var art = new Image { Source = PlatformModules.Art(id), Width = 84, Height = 84, HorizontalAlignment = HorizontalAlignment.Center };
        var title = new TextBlock { Text = name.Replace("Casillas ", "Casillas\n"), FontWeight = FontWeight.Bold, FontSize = 14, TextAlignment = TextAlignment.Center, Foreground = PlatformModules.Brush(color), TextWrapping = TextWrapping.Wrap };
        var sub = new TextBlock { Text = text, FontSize = 11, Opacity = 0.7, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var stack = new StackPanel { Spacing = 6 };
        stack.Children.Add(art);
        stack.Children.Add(title);
        stack.Children.Add(sub);

        var grid = new Grid();
        grid.Children.Add(stack);
        if (badge.Length > 0)
        {
            grid.Children.Add(new Border
            {
                Background = PlatformModules.Brush("#3B2F12"),
                BorderBrush = PlatformModules.Brush("#7A6122"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(7, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = badge, FontSize = 10, FontWeight = FontWeight.Bold, Foreground = PlatformModules.Brush("#F3C969") },
            });
        }

        var card = new Border
        {
            Width = 190,
            MinHeight = 190,
            Margin = new Thickness(8),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(2),
            BorderBrush = PlatformModules.Brush(color),
            Background = PlatformModules.Brush("#131a22"),
            Opacity = enabled ? 1 : 0.6,
            Cursor = enabled ? new Cursor(StandardCursorType.Hand) : Cursor.Default,
            Child = grid,
        };
        if (enabled)
        {
            card.PointerEntered += (_, _) => card.Background = PlatformModules.Brush(color, 0x26);
            card.PointerExited += (_, _) => card.Background = PlatformModules.Brush("#131a22");
            card.PointerPressed += (_, _) => click();
        }

        return card;
    }
}
