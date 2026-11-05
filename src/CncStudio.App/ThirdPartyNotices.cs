using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

namespace CncStudio.App;

/// <summary>Lê o THIRD-PARTY-NOTICES.md embutido no executável (fonte única dos avisos de terceiros).</summary>
public static partial class ThirdPartyNotices
{
    /// <summary>Nome do recurso embutido (LogicalName no .csproj).</summary>
    public const string ResourceName = "CncStudio.App.THIRD-PARTY-NOTICES.md";

    /// <summary>Texto para exibir, sem os comentários internos (&lt;!-- --&gt;) do arquivo.</summary>
    public static string Load()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream is null)
        {
            return "Não foi possível carregar o arquivo de licenças de terceiros.";
        }

        using var reader = new StreamReader(stream);
        string text = reader.ReadToEnd();
        return HtmlComment().Replace(text, string.Empty).Replace("\n\n\n", "\n\n");
    }

    [GeneratedRegex("<!--.*?-->\n?", RegexOptions.Singleline)]
    private static partial Regex HtmlComment();
}
