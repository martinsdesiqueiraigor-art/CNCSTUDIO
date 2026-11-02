using System.Globalization;
using CncStudio.Core.Cam;

namespace CncStudio.Core;

/// <summary>
/// Registro dos dados de que um resultado depende (nome mostrado → impressão do valor), guardado quando o resultado é feito.
/// Comparado com os dados de agora, diz o que mudou: a tela marca o resultado "desatualizado" com texto, sem apagá-lo,
/// e desliga Aplicar/Gerar/Copiar até refazer. Uma parte que some ou aparece também conta como mudança.
/// </summary>
public sealed class DataStamp
{
    private readonly List<(string Name, string Value)> _parts;

    private DataStamp(List<(string Name, string Value)> parts) => _parts = parts;

    public static DataStamp Of(IEnumerable<(string Name, string Value)> parts) => new(parts.ToList());

    /// <summary>Nomes do que mudou de <paramref name="earlier"/> para este registro, na ordem deste registro.</summary>
    public IReadOnlyList<string> ChangedSince(DataStamp earlier)
    {
        var changed = new List<string>();
        foreach ((string name, string value) in _parts)
        {
            int i = earlier._parts.FindIndex(p => p.Name == name);
            if (i < 0 || earlier._parts[i].Value != value)
            {
                changed.Add(name);
            }
        }

        changed.AddRange(earlier._parts.Where(p => _parts.All(q => q.Name != p.Name)).Select(p => p.Name));
        return changed;
    }

    /// <summary>"Desatualizado: mudou X, Y." ou null se nada mudou (ou se não há resultado: <paramref name="earlier"/> nulo).</summary>
    public static string? StaleMessage(DataStamp? earlier, DataStamp now)
    {
        if (earlier is null)
        {
            return null;
        }

        IReadOnlyList<string> changed = now.ChangedSince(earlier);
        return changed.Count == 0 ? null : "Desatualizado: mudou " + string.Join(", ", changed) + ".";
    }

    /// <summary>
    /// Dados de que o programa do Trabalho fresa depende fora do próprio Trabalho: o perfil da máquina ativo (inteiro, como é salvo:
    /// fuso, eixos rotativos, limites, sentido, saída). O Trabalho em si já refaz o programa a cada mudança.
    /// </summary>
    public static DataStamp OfMachine(MachineProfile? machine) =>
        Of([("o perfil da máquina", machine is null ? "(nenhum)" : MachineProfileJson.Serialize(machine))]);

    /// <summary>
    /// Dados de que o orçamento depende: o programa do Code (nome, tempo simulado e trocas de ferramenta), o texto de cada campo
    /// (rótulo → texto digitado, sem espaços nas pontas), o nome da peça e o cabeçalho da proposta.
    /// </summary>
    public static DataStamp OfQuote((TimeSpan Time, int ToolChanges, string Name)? program, IEnumerable<(string Label, string Text)> fields, string partName, string company)
    {
        var parts = new List<(string Name, string Value)>
        {
            ("o programa do Code", program is { } p ? string.Join('|', p.Name, p.Time.Ticks.ToString(CultureInfo.InvariantCulture), p.ToolChanges.ToString(CultureInfo.InvariantCulture)) : "(nenhum)"),
        };
        parts.AddRange(fields.Select(f => (f.Label.ToLowerInvariant(), f.Text.Trim())));
        parts.Add(("o nome da peça", partName.Trim()));
        parts.Add(("o cabeçalho da proposta", company.Trim()));
        return Of(parts);
    }
}
