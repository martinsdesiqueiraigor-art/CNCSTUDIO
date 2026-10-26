using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using CncStudio.App.Modules;

namespace CncStudio.App;

/// <summary>Barra lateral de módulos da plataforma, tela Início e tema CMYK (PLAT-06, etapa 1).</summary>
public partial class MainWindow
{
    private readonly Dictionary<string, ToggleButton> _railButtons = [];
    private string _currentModule = "code";

    private void InitModules()
    {
        AddRailButton("inicio", "Início", "Tela inicial da plataforma Casillas", true);
        foreach (PlatformModule module in PlatformModules.All)
        {
            AddRailButton(module.Id, ShortName(module.Name), module.Available ? module.Description : module.Name + ": em breve no desktop (já existe na versão web)", module.Available);
        }

        Home.ModuleChosen += module => ShowModule(module.Id);
        CmykButton.IsChecked = _settings.Cmyk;
        CmykButton.IsCheckedChanged += (_, _) => ApplyCmyk(CmykButton.IsChecked == true);
        ApplyCmyk(_settings.Cmyk);
        ShowModule("code");
    }

    private static string ShortName(string name) => name.Replace("Casillas ", "");

    private void AddRailButton(string id, string label, string tip, bool enabled)
    {
        var button = new ToggleButton
        {
            Content = new TextBlock { Text = label, FontSize = 11, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center },
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            MinHeight = 52,
            Padding = new Thickness(2, 6),
            IsEnabled = enabled,
        };
        button.Classes.Add("tool");
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => ShowModule(id);
        _railButtons[id] = button;
        ModuleRail.Children.Add(button);
    }

    /// <summary>Mostra o módulo escolhido: o Início cobre o Code; o Code volta intacto (abas, editor e simulação continuam).</summary>
    private void ShowModule(string id)
    {
        _currentModule = id;
        Home.IsVisible = id == "inicio";
        foreach ((string key, ToggleButton button) in _railButtons)
        {
            button.IsChecked = key == id;
        }
    }

    /// <summary>Tema CMYK opcional; o visual atual continua padrão. Só muda as cores do tema escuro.</summary>
    private void ApplyCmyk(bool on)
    {
        _settings.Cmyk = on;
        if (Application.Current?.Resources.ThemeDictionaries.TryGetValue(Avalonia.Styling.ThemeVariant.Dark, out var found) == true
            && found is ResourceDictionary dark)
        {
            foreach ((string key, string normal, string cmyk) in CmykPalette.DarkKeys)
            {
                dark[key] = new SolidColorBrush(Color.Parse(on ? cmyk : normal));
            }
        }
    }
}
