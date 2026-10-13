using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Layout;
using Avalonia.Media;

namespace CncStudio.Web;

/// <summary>Item da biblioteca (arquivo ou pasta).</summary>
public sealed record LibraryEntry(string Path, bool IsFolder, int Size, string When)
{
    public string Name => Path.Contains('/') ? Path[(Path.LastIndexOf('/') + 1)..] : Path;

    public string Parent => Path.Contains('/') ? Path[..Path.LastIndexOf('/')] : "";
}

/// <summary>
/// Biblioteca de programas guardada no próprio aparelho (IndexedDB do navegador):
/// pastas, salvar aqui, importar vários arquivos, renomear, excluir e compartilhar.
/// </summary>
public partial class LibraryPanel : UserControl
{
    private static readonly Geometry FolderIcon = StreamGeometry.Parse("M0,2 C0,1 1,0 2,0 H6 L8,2 H16 C17,2 18,3 18,4 V12 C18,13 17,14 16,14 H2 C1,14 0,13 0,12 Z");
    private static readonly Geometry FileIcon = StreamGeometry.Parse("M0,0 H9 L13,4 V17 H0 Z M9,0 V4 H13 M3,8 H10 M3,11 H10 M3,14 H7");

    private readonly NativeFileButton _import;
    private List<LibraryEntry> _entries = [];
    private string _folder = "";

    public LibraryPanel()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Closed?.Invoke(this, EventArgs.Empty);
        UpButton.Click += async (_, _) =>
        {
            _folder = _folder.Contains('/') ? _folder[.._folder.LastIndexOf('/')] : "";
            await RefreshAsync();
        };
        NewFolderButton.Click += async (_, _) => await NewFolderAsync();
        SaveHereButton.Click += async (_, _) => await SaveHereAsync();
        ShareFolderButton.Click += async (_, _) => await ShareAsync(_folder, isFolder: true);

        _import = new NativeFileButton(ImportButton, "import", multiple: true);
        _import.Picked += async (_, files) => await ImportAsync(files);
    }

    /// <summary>Programa aberto no editor (nome sugerido e texto), para "Salvar aqui".</summary>
    public Func<(string Name, string Text)>? CurrentProgram { get; set; }

    /// <summary>O usuário tocou num arquivo: caminho e texto.</summary>
    public event EventHandler<(string Path, string Text)>? OpenRequested;

    /// <summary>O programa aberto foi salvo neste caminho.</summary>
    public event EventHandler<string>? Saved;

    /// <summary>Mensagem curta para o rodapé.</summary>
    public event EventHandler<string>? Message;

    public event EventHandler? Closed;

    /// <summary>Atualiza a posição do campo nativo do botão "Importar" (ao abrir/fechar o painel).</summary>
    public void RefreshNativeButtons() => _import.Refresh();

    /// <summary>Abre na pasta indicada ("" = raiz) e recarrega a lista.</summary>
    public async Task ShowFolderAsync(string folder)
    {
        _folder = folder;
        await RefreshAsync();
    }

    // ───────────────────────── Lista ─────────────────────────

    public async Task RefreshAsync()
    {
        try
        {
            string? json = await WebInterop.LibListAsync();
            _entries = Parse(json);
        }
        catch (Exception ex)
        {
            _entries = [];
            Message?.Invoke(this, "Biblioteca indisponível neste navegador: " + ex.Message);
        }

        if (_folder.Length > 0 && !_entries.Any(e => e.IsFolder && e.Path == _folder))
        {
            _folder = ""; // pasta sumiu (excluída/renomeada)
        }

        PathText.Text = _folder.Length == 0 ? "Pasta principal" : "Pasta principal / " + _folder.Replace("/", " / ");
        UpButton.IsVisible = _folder.Length > 0;
        BuildRows();
    }

    private static List<LibraryEntry> Parse(string? json)
    {
        var list = new List<LibraryEntry>();
        if (string.IsNullOrEmpty(json))
        {
            return list;
        }

        using JsonDocument doc = JsonDocument.Parse(json);
        foreach (JsonElement e in doc.RootElement.EnumerateArray())
        {
            list.Add(new LibraryEntry(
                e.GetProperty("path").GetString() ?? "",
                e.GetProperty("kind").GetString() == "folder",
                e.TryGetProperty("size", out JsonElement size) ? size.GetInt32() : 0,
                e.TryGetProperty("when", out JsonElement when) ? when.GetString() ?? "" : ""));
        }

        return list;
    }

    private void BuildRows()
    {
        Rows.Children.Clear();
        List<LibraryEntry> here = _entries
            .Where(e => e.Parent == _folder)
            .OrderByDescending(e => e.IsFolder)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (here.Count == 0)
        {
            Rows.Children.Add(new TextBlock
            {
                Text = _folder.Length == 0
                    ? "A biblioteca está vazia. Use \"Salvar aqui\" para guardar o programa aberto, ou \"Importar\" para trazer arquivos do aparelho."
                    : "Pasta vazia.",
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.7,
                Margin = new Thickness(8, 16),
            });
            return;
        }

        foreach (LibraryEntry entry in here)
        {
            Rows.Children.Add(BuildRow(entry));
        }
    }

    private Control BuildRow(LibraryEntry entry)
    {
        int count = entry.IsFolder ? _entries.Count(e => !e.IsFolder && e.Path.StartsWith(entry.Path + "/", StringComparison.Ordinal)) : 0;
        string detail = entry.IsFolder
            ? (count == 1 ? "1 programa" : $"{count} programas")
            : $"{FormatSize(entry.Size)} · {entry.When}";

        var icon = new PathIcon
        {
            Data = entry.IsFolder ? FolderIcon : FileIcon,
            Width = 20,
            Height = 18,
            Foreground = entry.IsFolder ? (IBrush)new SolidColorBrush(Color.Parse("#F0883E")) : new SolidColorBrush(Color.Parse("#58A6FF")),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        texts.Children.Add(new TextBlock { Text = entry.Name, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        texts.Children.Add(new TextBlock { Text = detail, FontSize = 12, Opacity = 0.65 });

        var content = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        content.Children.Add(icon);
        content.Children.Add(texts);

        var row = new Button { Content = content };
        row.Classes.Add("row");
        row.Click += async (_, _) =>
        {
            if (entry.IsFolder)
            {
                _folder = entry.Path;
                await RefreshAsync();
            }
            else
            {
                string? text = await WebInterop.LibReadAsync(entry.Path);
                if (text is not null)
                {
                    OpenRequested?.Invoke(this, (entry.Path, text));
                }
            }
        };

        var more = new Button { Content = "•••" };
        more.Classes.Add("more");
        more.Flyout = BuildMenu(entry);

        var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(more, 1);
        line.Children.Add(row);
        line.Children.Add(more);
        return new Border
        {
            Child = line,
            BorderBrush = new SolidColorBrush(Color.Parse("#30363D")),
            BorderThickness = new Thickness(0, 0, 0, 1),
        };
    }

    private MenuFlyout BuildMenu(LibraryEntry entry)
    {
        var menu = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedRight };
        void Add(string header, Func<Task> action)
        {
            var item = new MenuItem { Header = header };
            item.Click += async (_, _) => await action();
            menu.Items.Add(item);
        }

        if (!entry.IsFolder)
        {
            Add("Abrir", async () =>
            {
                string? text = await WebInterop.LibReadAsync(entry.Path);
                if (text is not null)
                {
                    OpenRequested?.Invoke(this, (entry.Path, text));
                }
            });
        }

        Add("Renomear…", () => RenameAsync(entry));
        Add(entry.IsFolder ? "Compartilhar pasta" : "Compartilhar", () => ShareAsync(entry.Path, entry.IsFolder));
        Add("Excluir…", () => DeleteAsync(entry));
        return menu;
    }

    private static string FormatSize(int chars) => chars < 1024 ? $"{chars} B" : $"{chars / 1024.0:0.#} KB";

    // ───────────────────────── Ações ─────────────────────────

    private string Join(string name) => _folder.Length == 0 ? name : _folder + "/" + name;

    /// <summary>Nome válido: sem barras e sem espaços nas pontas.</summary>
    private static string? CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string clean = name.Trim().Replace('/', '-').Replace('\\', '-');
        return clean.Length == 0 ? null : clean;
    }

    private async Task NewFolderAsync()
    {
        string? name = CleanName(await WebInterop.PromptTextAsync("Nome da nova pasta", "", "Criar"));
        if (name is null)
        {
            return;
        }

        string path = Join(name);
        if (await WebInterop.LibExistsAsync(path))
        {
            Message?.Invoke(this, $"Já existe \"{name}\" nesta pasta.");
            return;
        }

        await WebInterop.LibMakeFolderAsync(path);
        await RefreshAsync();
    }

    /// <summary>Salva o programa aberto na pasta atual (pergunta o nome; avisa antes de substituir).</summary>
    public async Task SaveHereAsync()
    {
        if (CurrentProgram is null)
        {
            return;
        }

        (string currentName, string text) = CurrentProgram();
        string? name = CleanName(await WebInterop.PromptTextAsync("Salvar na biblioteca como", currentName, "Salvar"));
        if (name is null)
        {
            return;
        }

        string path = Join(name);
        if (await WebInterop.LibExistsAsync(path) &&
            !await WebInterop.ConfirmAsync($"\"{name}\" já existe nesta pasta. Substituir?", "Substituir", false))
        {
            return;
        }

        await WebInterop.LibWriteAsync(path, text);
        Saved?.Invoke(this, path);
        Message?.Invoke(this, "Salvo na biblioteca: " + path);
        await RefreshAsync();
    }

    private async Task ImportAsync(IReadOnlyList<PickedFile> files)
    {
        var existing = new List<PickedFile>();
        foreach (PickedFile f in files)
        {
            if (await WebInterop.LibExistsAsync(Join(CleanName(f.Name) ?? f.Name)))
            {
                existing.Add(f);
            }
        }

        bool replace = true;
        if (existing.Count > 0)
        {
            string question = existing.Count == 1
                ? $"\"{existing[0].Name}\" já existe nesta pasta. Substituir?"
                : $"{existing.Count} arquivos já existem nesta pasta. Substituir todos?";
            replace = await WebInterop.ConfirmAsync(question, "Substituir", false);
        }

        int count = 0;
        foreach (PickedFile f in files)
        {
            if (!replace && existing.Contains(f))
            {
                continue;
            }

            await WebInterop.LibWriteAsync(Join(CleanName(f.Name) ?? f.Name), f.Text);
            count++;
        }

        Message?.Invoke(this, count == 1 ? "1 programa importado." : $"{count} programas importados.");
        await RefreshAsync();
    }

    private async Task RenameAsync(LibraryEntry entry)
    {
        string? name = CleanName(await WebInterop.PromptTextAsync("Novo nome", entry.Name, "Renomear"));
        if (name is null || name == entry.Name)
        {
            return;
        }

        string target = entry.Parent.Length == 0 ? name : entry.Parent + "/" + name;
        if (await WebInterop.LibExistsAsync(target) &&
            !await WebInterop.ConfirmAsync($"\"{name}\" já existe. Substituir?", "Substituir", false))
        {
            return;
        }

        if (await WebInterop.LibExistsAsync(target))
        {
            await WebInterop.LibDeleteAsync(target);
        }

        await WebInterop.LibRenameAsync(entry.Path, target);
        await RefreshAsync();
    }

    private async Task DeleteAsync(LibraryEntry entry)
    {
        string question = entry.IsFolder
            ? $"Excluir a pasta \"{entry.Name}\" e tudo o que está dentro?"
            : $"Excluir \"{entry.Name}\"?";
        if (!await WebInterop.ConfirmAsync(question, "Excluir", true))
        {
            return;
        }

        await WebInterop.LibDeleteAsync(entry.Path);
        Message?.Invoke(this, "Excluído: " + entry.Name);
        await RefreshAsync();
    }

    /// <summary>Compartilha um arquivo, ou todos os programas de uma pasta (inclusive subpastas).</summary>
    private async Task ShareAsync(string path, bool isFolder)
    {
        List<LibraryEntry> files = isFolder
            ? _entries.Where(e => !e.IsFolder && (path.Length == 0 || e.Path.StartsWith(path + "/", StringComparison.Ordinal))).ToList()
            : _entries.Where(e => e.Path == path && !e.IsFolder).ToList();
        if (files.Count == 0)
        {
            Message?.Invoke(this, "Não há programas para compartilhar aqui.");
            return;
        }

        var items = new List<Dictionary<string, string>>();
        foreach (LibraryEntry f in files)
        {
            string? text = await WebInterop.LibReadAsync(f.Path);
            if (text is not null)
            {
                items.Add(new Dictionary<string, string> { ["name"] = f.Name, ["text"] = text });
            }
        }

        string json = JsonSerializer.Serialize(items, LibraryJson.Default.ListDictionaryStringString);
        string? result = await WebInterop.ShareFilesAsync(json, "Programas CNC — Casillas Code");
        if (result == "downloaded")
        {
            Message?.Invoke(this, "Arquivos baixados (pasta Downloads do aparelho).");
        }
    }
}

/// <summary>Serialização JSON sem reflexão (funciona com o corte de código do WebAssembly).</summary>
[System.Text.Json.Serialization.JsonSerializable(typeof(List<Dictionary<string, string>>))]
internal sealed partial class LibraryJson : System.Text.Json.Serialization.JsonSerializerContext
{
}
