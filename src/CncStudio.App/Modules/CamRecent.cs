using System.Text.Json;

namespace CncStudio.App.Modules;

/// <summary>Trabalho aberto ou salvo há pouco no CAM (UI-06): caminho no computador, fresa ou torno e quando.</summary>
public sealed record CamRecentItem(string Path, bool Lathe, DateTime When)
{
    /// <summary>Nome do arquivo sem a extensão.</summary>
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
}

/// <summary>
/// Lista dos trabalhos recentes da tela inicial do CAM (UI-06), num arquivo próprio ao lado do config.json
/// (<c>cam-recentes.json</c>). Guarda só caminho, tipo e data; nunca o conteúdo do trabalho. Falha de leitura ou escrita
/// não atrapalha o CAM: a lista só fica vazia.
/// </summary>
public static class CamRecent
{
    private const int Max = 8;

    private static string FilePath =>
        System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Settings.AppSettings.FilePath)!, "cam-recentes.json");

    /// <summary>Os recentes, do mais novo ao mais antigo.</summary>
    public static IReadOnlyList<CamRecentItem> Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<List<CamRecentItem>>(File.ReadAllText(FilePath)) ?? [] : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    /// <summary>Põe o trabalho no topo da lista (sem repetir o caminho) e guarda.</summary>
    public static void Add(string path, bool lathe)
    {
        var list = Load().Where(r => !string.Equals(r.Path, path, StringComparison.OrdinalIgnoreCase)).ToList();
        list.Insert(0, new CamRecentItem(path, lathe, DateTime.Now));
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list.Take(Max).ToList()));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // sem lista de recentes; o trabalho abriu ou salvou normalmente
        }
    }

    /// <summary>Quando, em texto curto: "Hoje, 03:40", "Ontem", "17/11".</summary>
    public static string WhenText(DateTime when)
    {
        DateTime today = DateTime.Today;
        return when.Date == today ? "Hoje, " + when.ToString("HH:mm")
            : when.Date == today.AddDays(-1) ? "Ontem"
            : when.ToString("dd/MM");
    }
}
