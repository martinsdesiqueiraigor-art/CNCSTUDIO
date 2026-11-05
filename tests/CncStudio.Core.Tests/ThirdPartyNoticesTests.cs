using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace CncStudio.Core.Tests;

/// <summary>
/// Avisos de terceiros do desktop (src/CncStudio.App/THIRD-PARTY-NOTICES.md): o arquivo existe,
/// não está vazio, o App o embute como recurso e todo componente tem versão e licença.
/// </summary>
public class ThirdPartyNoticesTests
{
    private static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "terceiros");
    private static string NoticesPath => Path.Combine(Folder, "THIRD-PARTY-NOTICES.md");

    [Fact]
    public void Notices_file_exists_and_is_not_empty()
    {
        Assert.True(File.Exists(NoticesPath), "THIRD-PARTY-NOTICES.md não foi encontrado.");
        string text = File.ReadAllText(NoticesPath);
        Assert.False(string.IsNullOrWhiteSpace(text));
        Assert.Contains("## Componentes", text);
    }

    [Fact]
    public void App_embeds_the_notices_as_resource()
    {
        var project = XDocument.Load(Path.Combine(Folder, "CncStudio.App.csproj.xml"));
        var resource = project.Descendants("EmbeddedResource")
            .SingleOrDefault(e => (string?)e.Attribute("Include") == "THIRD-PARTY-NOTICES.md");
        Assert.NotNull(resource);
        // Mesmo nome que CncStudio.App.ThirdPartyNotices.ResourceName
        Assert.Equal("CncStudio.App.THIRD-PARTY-NOTICES.md", (string?)resource!.Attribute("LogicalName"));
    }

    [Fact]
    public void Every_component_has_version_and_license()
    {
        var components = ReadComponents(File.ReadAllText(NoticesPath));
        Assert.NotEmpty(components);
        foreach (var (name, body) in components)
        {
            Assert.True(HasField(body, "Versão"), $"{name}: sem versão.");
            Assert.True(HasField(body, "Licença"), $"{name}: sem licença.");
        }
    }

    [Fact]
    public void Lists_the_components_published_by_the_desktop()
    {
        var names = ReadComponents(File.ReadAllText(NoticesPath)).Select(c => c.Name).ToList();
        foreach (string expected in new[]
                 {
                     "Avalonia", "AvaloniaEdit", "SkiaSharp", "HarfBuzzSharp",
                     "MicroCom.Runtime", "Tmds.DBus.Protocol", "ANGLE", ".NET (runtime)",
                     // Leitura de STEP: OpenCASCADE e as DLLs do pacote OcctNet
                     "OpenCASCADE Technology", "OcctNet.Wrapper", "zlib", "libpng", "bzip2", "brotli", "FreeType",
                     "Visual C++ (redistribuível)",
                 })
        {
            Assert.Contains(expected, names);
        }
    }

    [Fact]
    public void Has_the_Open_CASCADE_notice_and_license_texts()
    {
        // Exceção Open CASCADE 1.0: aviso destacado de que o programa usa o OCCT
        string text = File.ReadAllText(NoticesPath);
        Assert.Contains("Este programa usa o Open CASCADE Technology", text);
        Assert.Contains("Open CASCADE exception (version 1.0) to GNU LGPL version 2.1.", text);
        Assert.Contains("GNU LESSER GENERAL PUBLIC LICENSE", text);
        Assert.Contains("V7_9_3", text);
    }

    /// <summary>Seções "### Nome" dentro de "## Componentes", até o próximo "## ".</summary>
    private static List<(string Name, string Body)> ReadComponents(string text)
    {
        text = text.Replace("\r\n", "\n");
        int start = text.IndexOf("\n## Componentes", StringComparison.Ordinal);
        Assert.True(start >= 0, "Seção \"## Componentes\" não encontrada.");
        int end = text.IndexOf("\n## ", start + 1, StringComparison.Ordinal);
        string section = end < 0 ? text[start..] : text[start..end];

        var result = new List<(string, string)>();
        var matches = Regex.Matches(section, @"^### (.+)$", RegexOptions.Multiline);
        for (int i = 0; i < matches.Count; i++)
        {
            int bodyStart = matches[i].Index + matches[i].Length;
            int bodyEnd = i + 1 < matches.Count ? matches[i + 1].Index : section.Length;
            result.Add((matches[i].Groups[1].Value.Trim(), section[bodyStart..bodyEnd]));
        }

        return result;
    }

    private static bool HasField(string body, string field) =>
        Regex.IsMatch(body, $@"^- \*\*{Regex.Escape(field)}:\*\* *\S", RegexOptions.Multiline);
}
