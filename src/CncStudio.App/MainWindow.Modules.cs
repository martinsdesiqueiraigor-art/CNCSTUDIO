using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CncStudio.App.Modules;
using CncStudio.Core.Assist;
using CncStudio.Core.Setup;

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
            Source = PlatformModules.Logo(), Width = 40, Height = 40, HorizontalAlignment = HorizontalAlignment.Center,
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
        // CAM-13: o Trabalho fresa envia com o pacote do CAM (aba sem arquivo; o pacote fica na memória até salvar)
        Cam.OpenWithPackage = (text, name, package) =>
        {
            LoadText(text, null, name, package: package);
            ShowModule("code");
        };
        Statistics.FromCam = ToolFromCam;
        // larguras das divisórias do Trabalho fresa (UX-04c): preferência local do app
        Cam.JobPanes = (_settings.CamTreeWidth, _settings.CamPropsWidth);
        Cam.SaveJobPanes = (tree, props) =>
        {
            _settings.CamTreeWidth = tree;
            _settings.CamPropsWidth = props;
            _settings.Save();
        };
        // chips do 3D do Trabalho fresa (UX-04d): preferência local do app, fora do .casillas-job
        Cam.JobViewOptions = _settings.CamView3D;
        Cam.SaveJobViewOptions = text =>
        {
            _settings.CamView3D = text;
            _settings.Save();
        };
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
        Cam.PickStl = async () =>
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Abrir modelo 3D (STL ou STEP)",
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType("Modelo 3D (STL, STEP)") { Patterns = ["*.stl", "*.STL", "*.step", "*.STEP", "*.stp", "*.STP"] },
                    new FilePickerFileType("Modelo STL") { Patterns = ["*.stl", "*.STL"] },
                    new FilePickerFileType("Modelo STEP") { Patterns = ["*.step", "*.STEP", "*.stp", "*.STP"] },
                ],
            });
            if (files.Count == 0)
            {
                return null;
            }

            await using Stream stream = await files[0].OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            return (files[0].Name, memory.ToArray());
        };
        Cam.PickPartStep = async () =>
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Peça em STEP",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Modelo STEP") { Patterns = ["*.step", "*.STEP", "*.stp", "*.STP"] }],
            });

            // o trabalho guarda o caminho: só arquivo com caminho no computador serve
            return files.Count == 0 ? null : files[0].TryGetLocalPath();
        };
        Cam.PickStockStep = async () =>
        {
            IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Bruto em STEP (fundido ou forjado)",
                AllowMultiple = false,
                FileTypeFilter = [new FilePickerFileType("Modelo STEP") { Patterns = ["*.step", "*.STEP", "*.stp", "*.STP"] }],
            });

            // o trabalho guarda o caminho: só arquivo com caminho no computador serve
            return files.Count == 0 ? null : files[0].TryGetLocalPath();
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
        Assist.PlanDrawing = () => (Cam.JobPlanContours, Cam.JobStockInfo?.Thickness);
        Assist.PlanStepDrawing = () => Cam.JobStepPlan;
        Assist.ApplyStepPlan = (contours, operations) =>
        {
            Cam.ApplyStepPlan(contours, operations);
            ShowModule("cam");
        };
        Assist.PlanFixture = () => Cam.JobFixtureInfo;
        Assist.PlanMaxRpm = () => _settings.MachineProfiles.FirstOrDefault(p => p.Name == _settings.ActiveMachineProfile)?.MaxSpindleRpm;
        Assist.PlanTools = () => _settings.Tools.Select(t => new PlanTool(
            t.Number, t.Diameter, Enum.TryParse(t.Kind, out ToolKind kind) && kind == ToolKind.Drill, t.Description)).ToList();
        Assist.ApplyPlan = operations =>
        {
            Cam.ApplyPlan(operations);
            ShowModule("cam");
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
        Assist.SaveProposal = async (suggested, text) =>
        {
            IStorageFile? file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Salvar proposta",
                SuggestedFileName = suggested,
                DefaultExtension = "html",
                FileTypeChoices = [new FilePickerFileType("Página HTML") { Patterns = ["*.html"] }],
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

    /// <summary>UI-C4: ícones de linha de uma cor da barra de módulos (desenhados para o Casillas, grade 24 × 24).</summary>
    private static readonly Dictionary<string, string> RailIcons = new()
    {
        ["inicio"] = "M3 11l9-8 9 8M5 10v10h5v-6h4v6h5V10",
        ["code"] = "M8 7l-5 5 5 5M16 7l5 5-5 5M14 4l-4 16",
        ["otimizador"] = "M4 20V10M10 20V4M16 20v-8M2 20h20",
        ["macro"] = "M4 4h16v16H4zM8 9h8M8 13h5M8 17h3",
        ["cam"] = "M12 3v6M9 9h6l-1 6h-4zM6 21h12",
        ["assist"] = "M12 3a7 7 0 0 0-4 12.7V19h8v-3.3A7 7 0 0 0 12 3zM9 22h6",
        ["academy"] = "M2 9l10-5 10 5-10 5zM6 11v5c3 2 9 2 12 0v-5",
        ["loja"] = "M5 8h14l-1 12H6zM9 8V6a3 3 0 0 1 6 0v2",
        ["calculadoras"] = "M6 3h12v18H6zM8 7h8M8 12h2M12 12h2M8 16h2M12 16h2",
        ["biblioteca"] = "M4 4h4v16H4zM10 4h4v16h-4zM16 6l3-1 3 15-3 1z",
    };

    private readonly Dictionary<string, (Avalonia.Controls.Shapes.Path Icon, TextBlock Label)> _railParts = [];

    private static readonly IBrush RailMuted = new SolidColorBrush(Color.Parse("#9AA3AE"));
    private static readonly IBrush RailAccent = new SolidColorBrush(Color.Parse("#F08A24"));

    private void AddRailItem(string id, string label, string tip, bool enabled, string color, Action? click, bool small = false, string? artId = null)
    {
        // UI-C4: barra fina, ícone de linha de uma cor (cinza; laranja no módulo aberto) no lugar das ilustrações
        var stack = new StackPanel { Spacing = 2, HorizontalAlignment = HorizontalAlignment.Center };
        string key = artId ?? id;
        var icon = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(RailIcons.TryGetValue(key, out string? d) ? d : RailIcons["inicio"]),
            Stroke = RailMuted, StrokeThickness = 1.7, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
            Width = small ? 18 : 20, Height = small ? 18 : 20, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center,
        };
        stack.Children.Add(icon);
        var text = new TextBlock
        {
            Text = label, FontSize = 9, FontWeight = FontWeight.SemiBold, TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap, Foreground = RailMuted,
        };
        stack.Children.Add(text);
        _railParts[id] = (icon, text);

        var item = new Border
        {
            Child = stack,
            Padding = new Thickness(2, 5),
            Margin = new Thickness(0, 1),
            CornerRadius = new CornerRadius(8),
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
                    item.Background = new SolidColorBrush(Color.Parse("#808A96"), 0.14);
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
            Cam.CheckJobFresh();
        }

        if (id == "otimizador")
        {
            Optimizer.Refresh();
        }

        foreach ((string key, Border item) in _railItems)
        {
            bool active = key == id;
            item.Background = active ? new SolidColorBrush(Color.Parse("#F08A24"), 0.16) : Brushes.Transparent;
            item.BorderBrush = active ? RailAccent : Brushes.Transparent;
            if (_railParts.TryGetValue(key, out var parts))
            {
                parts.Icon.Stroke = active ? RailAccent : RailMuted;
                parts.Label.Foreground = active ? RailAccent : RailMuted;
            }
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
            // UI-07: fundo do editor pelo tema (grafite no escuro, branco no claro)
            Editor.Bind(Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, Editor.GetResourceObservable("EditorBackground"));
        }

        Editor.TextArea.TextView.Redraw();
    }
}
