using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CncStudio.App.Modules;

namespace CncStudio.App;

/// <summary>Barra lateral de módulos da plataforma (ícones coloridos), tela Início e tema CMYK (PLAT-06).</summary>
public partial class MainWindow
{
    private readonly Dictionary<string, Border> _railItems = [];
    private readonly Dictionary<string, string> _railColors = [];
    private string _currentModule = "code";

    private void InitModules()
    {
        // Marca no alto da barra (a logo com os mascotes, como no cabeçalho da web)
        var brand = new StackPanel { Spacing = 0, Margin = new Thickness(0, 0, 0, 6) };
        brand.Children.Add(new Image
        {
            Source = PlatformModules.Logo(), Width = 58, Height = 58, HorizontalAlignment = HorizontalAlignment.Center,
            OpacityMask = new RadialGradientBrush { GradientStops = [new GradientStop(Colors.Black, 0.64), new GradientStop(Colors.Transparent, 0.78)] },
        });
        brand.Children.Add(new TextBlock
        {
            Text = "Casillas",
            FontSize = 11, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = PlatformModules.Brush("#F3D9A4"),
        });
        ModuleRail.Children.Add(brand);

        AddRailItem("inicio", "Início", "Tela inicial da plataforma Casillas", true, "#f3d9a4", null);
        foreach (PlatformModule module in PlatformModules.All)
        {
            AddRailItem(module.Id, module.Name.Replace("Casillas ", ""),
                module.Available ? module.Description : module.Name + ": em breve no desktop", module.Available, module.Color, () => ShowModule(module.Id));
        }

        ModuleRail.Children.Add(new Border { Height = 1, Margin = new Thickness(6, 8), Background = Brushes.Gray, Opacity = 0.4 });
        foreach (WebModule web in PlatformModules.Web)
        {
            bool soon = web.Url.Length == 0;
            string label = (web.Id == "calculadoras" ? "Calc." : web.Name.Replace("Casillas ", "")) + (soon ? "" : " ↗");
            AddRailItem("web-" + web.Id, label,
                soon ? web.Name + ": em breve" : web.Name + ": abre no navegador (precisa de internet)", !soon, web.Color,
                () => PlatformModules.OpenWeb(web.Url), small: true, artId: web.Id);
        }

        Home.ModuleChosen += module => ShowModule(module.Id);
        Optimizer.GetProgram = () => (Editor.Text, _active.DisplayName);
        Optimizer.OpenInEditor = (text, name) =>
        {
            LoadText(text, null, name);
            ShowModule("code");
        };
        Macro.OpenInEditor = (text, name) =>
        {
            LoadText(text, null, name);
            ShowModule("code");
        };
        Cam.OpenInEditor = (text, name) =>
        {
            LoadText(text, null, name);
            ShowModule("code");
        };
        Cam.ToolsSource = () => _settings.Tools;
        Cam.SaveTools = () => _settings.Save();
        Cam.MachineProfilesSource = () => _settings.MachineProfiles;
        Cam.ActiveMachineSource = () => _settings.ActiveMachineProfile;
        Cam.SetActiveMachine = name =>
        {
            _settings.ActiveMachineProfile = name;
            StartAnalysis(); // o 3D usa o giro do perfil ativo
        };
        Cam.SaveMachines = () =>
        {
            _settings.Save();
            StartAnalysis();
        };
        Cam.PickDxf = async () =>
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Abrir desenho DXF",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Desenho DXF") { Patterns = ["*.dxf", "*.DXF"] }],
            });
            if (files.Count == 0)
            {
                return null;
            }

            await using Stream stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            return (files[0].Name, await reader.ReadToEndAsync());
        };
        Assist.QuoteSource = () => _settings.Quote;
        Assist.SaveQuote = () => _settings.Save();
        Assist.ProgramSource = () => CurrentAnalysis is { } a ? (a.Statistics.TotalTime, a.Statistics.ToolChanges, _active.DisplayName) : null;
        Assist.CopyText = async text =>
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
            }
        };
        Assist.LibrarySource = () => _settings.AssistLibraryJson;
        Assist.SaveLibrary = json =>
        {
            _settings.AssistLibraryJson = json;
            _settings.Save();
        };
        Assist.PickFile = async () =>
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Abrir biblioteca do Assist",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Biblioteca Casillas Assist") { Patterns = ["*.casillas-assist", "*.json"] }],
            });
            if (files.Count == 0)
            {
                return null;
            }

            await using Stream stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            return (files[0].Name, await reader.ReadToEndAsync());
        };
        Assist.SaveFile = async (suggested, text) =>
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Salvar biblioteca do Assist",
                SuggestedFileName = suggested,
                DefaultExtension = "casillas-assist",
                FileTypeChoices = [new FilePickerFileType("Biblioteca Casillas Assist") { Patterns = ["*.casillas-assist"] }],
            });
            if (file is null)
            {
                return;
            }

            await using Stream stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(text);
        };
        Cam.PickJob = async () =>
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Abrir trabalho do CAM",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Trabalho Casillas CAM") { Patterns = ["*.casillas-job", "*.json"] }],
            });
            if (files.Count == 0)
            {
                return null;
            }

            await using Stream stream = await files[0].OpenReadAsync();
            using var reader = new StreamReader(stream);
            return (files[0].Name, await reader.ReadToEndAsync());
        };
        Cam.SaveJobFile = async (suggested, text) =>
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Salvar trabalho do CAM",
                SuggestedFileName = suggested,
                DefaultExtension = "casillas-job",
                FileTypeChoices = [new FilePickerFileType("Trabalho Casillas CAM") { Patterns = ["*.casillas-job"] }],
            });
            if (file is null)
            {
                return;
            }

            await using Stream stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await using var writer = new StreamWriter(stream);
            await writer.WriteAsync(text);
        };
        Optimizer.CopyText = Macro.CopyText = Cam.CopyText = async text =>
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(text);
            }
        };
        CmykButton.IsChecked = _settings.Cmyk;
        CmykButton.IsCheckedChanged += (_, _) => ApplyCmyk(CmykButton.IsChecked == true);
        ApplyCmyk(_settings.Cmyk);
        ShowModule("code");
    }

    private void AddRailItem(string id, string label, string tip, bool enabled, string color, Action? click, bool small = false, string? artId = null)
    {
        var stack = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
        string art = artId ?? id;
        if (id != "inicio" && PlatformModules.Art(art) is { } bitmap)
        {
            stack.Children.Add(new Image { Source = bitmap, Width = small ? 28 : 38, Height = small ? 28 : 38, HorizontalAlignment = HorizontalAlignment.Center });
        }
        else
        {
            stack.Children.Add(new PathIcon
            {
                Data = StreamGeometry.Parse("M3 11l9-8 9 8M5 10v10h5v-6h4v6h5V10"),
                Width = 26, Height = 26, Foreground = PlatformModules.Brush(color),
            });
        }

        stack.Children.Add(new TextBlock
        {
            Text = label, FontSize = small ? 9.5 : 10.5, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, Foreground = PlatformModules.Brush(color),
        });

        var item = new Border
        {
            Child = stack,
            Padding = new Thickness(2, 6),
            Margin = new Thickness(0, 1),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(2, 0, 0, 0),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.Transparent,
            Opacity = enabled ? 1 : 0.4,
            Cursor = enabled ? new Cursor(StandardCursorType.Hand) : Cursor.Default,
        };
        ToolTip.SetTip(item, tip);
        _railItems[id] = item;
        _railColors[id] = color;
        if (enabled)
        {
            item.PointerEntered += (_, _) =>
            {
                if (_currentModule != id)
                {
                    item.Background = PlatformModules.Brush(color, 0x18);
                }
            };
            item.PointerExited += (_, _) =>
            {
                if (_currentModule != id)
                {
                    item.Background = Brushes.Transparent;
                }
            };
            item.PointerPressed += (_, _) =>
            {
                if (click is null)
                {
                    ShowModule(id);
                }
                else
                {
                    click();
                }
            };
        }

        ModuleRail.Children.Add(item);
    }

    /// <summary>Mostra o módulo escolhido: o Início cobre o Code; o Code volta intacto (abas, editor e simulação continuam).</summary>
    private void ShowModule(string id)
    {
        _currentModule = id;
        Home.IsVisible = id == "inicio";
        Optimizer.IsVisible = id == "otimizador";
        Macro.IsVisible = id == "macro";
        Cam.IsVisible = id == "cam";
        Assist.IsVisible = id == "assist";
        if (id == "assist")
        {
            Assist.Reload();
        }

        if (id == "cam")
        {
            Cam.RefreshBank();
        }

        if (id == "otimizador")
        {
            Optimizer.Refresh();
        }

        foreach ((string key, Border item) in _railItems)
        {
            bool active = key == id;
            item.Background = active ? PlatformModules.Brush(_railColors[key], 0x30) : Brushes.Transparent;
            item.BorderBrush = active ? PlatformModules.Brush(_railColors[key]) : Brushes.Transparent;
        }
    }

    /// <summary>Tema CMYK opcional; o visual atual continua padrão. Só muda o tema escuro (interface, editor e código).</summary>
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

        _colorizer.Cmyk = on;
        if (on && _settings.Theme != "Light")
        {
            Editor.Background = new SolidColorBrush(Color.Parse("#0A1216"));
        }
        else
        {
            Editor.ClearValue(Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty);
        }

        Editor.TextArea.TextView.Redraw();
    }
}
