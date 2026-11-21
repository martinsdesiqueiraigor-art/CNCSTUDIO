using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;

namespace CncStudio.Core.Cam;

/// <summary>O que o bolsão automático faz com um laço da face (CAM-ILHAS-02): a cor e o texto da tela vêm daqui.</summary>
public enum LoopRole
{
    /// <summary>Laço de dentro cuja parede sobe: vira ilha.</summary>
    Island,

    /// <summary>Laço de dentro cuja parede desce (furo, escareado): fica de fora.</summary>
    Ignored,

    /// <summary>Laço de fora cuja parede desce: borda aberta, vai até o bruto.</summary>
    OpenBorder,

    /// <summary>Laço de fora cuja parede sobe: parede do bolsão.</summary>
    ClosedBorder,

    /// <summary>Parte sobe, parte desce: não suportado.</summary>
    Mixed,
}

/// <summary>Um laço da face com o papel no bolsão automático e o nome curto ("Furo Ø10", "Ilha 40 × 20").</summary>
public sealed record LoopItem(LoopRole Role, string Name, IReadOnlyList<Point3> Points);

/// <summary>Linha da lista da tela: laços iguais juntos ("8 furos Ø10").</summary>
public sealed record LoopGroup(LoopRole Role, int Count, string Name, string Effect);

/// <summary>
/// CAM-ILHAS-02: o que o filtro <see cref="LoopFilter.Auto"/> faz com cada laço das faces de um bolsão, para a tela (resumo, lista e cores).
/// Usa a mesma classificação da geração (<see cref="MeshFeatures.ClassifiedLoops"/>); nada vai para o arquivo nem para o G-code.
/// </summary>
public static class PocketLoopReport
{
    /// <summary>Laços das faces, o de fora de cada face primeiro.</summary>
    public static IReadOnlyList<LoopItem> Describe(MeshFeatures features, IEnumerable<MeshFace> faces)
    {
        var items = new List<LoopItem>();
        foreach (MeshFace face in faces)
        {
            IReadOnlyList<SidedLoop> loops = features.ClassifiedLoops(face);
            for (int k = 0; k < loops.Count; k++)
            {
                SidedLoop l = loops[k];
                bool outer = k == 0;
                LoopRole role = l.Side switch
                {
                    LoopSide.Mixed => LoopRole.Mixed,
                    LoopSide.Up => outer ? LoopRole.ClosedBorder : LoopRole.Island,
                    _ => outer ? LoopRole.OpenBorder : LoopRole.Ignored,
                };
                string noun = role switch
                {
                    LoopRole.Island => "Ilha",
                    LoopRole.Ignored => "Furo",
                    LoopRole.Mixed => outer ? "Borda de fora" : "Laço",
                    _ => "Borda de fora",
                };
                items.Add(new LoopItem(role, noun + " " + Size(l.Points), l.Points));
            }
        }

        return items;
    }

    /// <summary>Linhas da lista: mesmo papel e mesmo tamanho juntos, na ordem borda, ilhas, ignorados, mistos.</summary>
    public static IReadOnlyList<LoopGroup> Group(IReadOnlyList<LoopItem> items) =>
        [.. items.GroupBy(i => (i.Role, i.Name))
            .OrderBy(g => Order(g.Key.Role))
            .Select(g => new LoopGroup(g.Key.Role, g.Count(), g.Count() > 1 ? Plural(g.Count(), g.Key.Name) : g.Key.Name, Effect(g.Key.Role, g.Count() > 1)))];

    /// <summary>Resumo de uma linha: "1 ilha · 8 furos ignorados · borda aberta".</summary>
    public static string Summary(IReadOnlyList<LoopItem> items)
    {
        var parts = new List<string>();
        int islands = items.Count(i => i.Role == LoopRole.Island);
        int ignored = items.Count(i => i.Role == LoopRole.Ignored);
        int mixed = items.Count(i => i.Role == LoopRole.Mixed);
        parts.Add(islands == 1 ? "1 ilha" : $"{islands} ilhas");
        if (ignored > 0)
        {
            parts.Add(ignored == 1 ? "1 furo ignorado" : $"{ignored} furos ignorados");
        }

        if (items.Any(i => i.Role == LoopRole.OpenBorder))
        {
            parts.Add("borda aberta");
        }
        else if (items.Any(i => i.Role == LoopRole.ClosedBorder))
        {
            parts.Add("bolsão fechado");
        }

        if (mixed > 0)
        {
            parts.Add(mixed == 1 ? "1 laço misto" : $"{mixed} laços mistos");
        }

        return string.Join(" · ", parts);
    }

    /// <summary>Cor RGB de cada papel (a mesma na árvore, no 3D e nas propriedades).</summary>
    public static uint ColorOf(LoopRole role) => role switch
    {
        LoopRole.Island => 0x3DBE6A,
        LoopRole.Ignored => 0x8A939E,
        LoopRole.OpenBorder => 0xF08A24,
        LoopRole.ClosedBorder => 0x4C9BE8,
        _ => 0xE05A5A,
    };

    /// <summary>Ø quando o laço é um círculo (raio igual em volta do centro); senão as medidas em X × Y.</summary>
    public static string Size(IReadOnlyList<Point3> points)
    {
        double minX = points.Min(p => p.X), maxX = points.Max(p => p.X), minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        double cx = (minX + maxX) / 2, cy = (minY + maxY) / 2;
        double[] r = [.. points.Select(p => Math.Sqrt(((p.X - cx) * (p.X - cx)) + ((p.Y - cy) * (p.Y - cy))))];
        double mean = r.Average();
        if (points.Count >= 8 && mean > 1e-6 && r.All(x => Math.Abs(x - mean) <= Math.Max(0.02 * mean, 0.05)))
        {
            return "Ø" + Fmt(2 * mean);
        }

        return Fmt(maxX - minX) + " × " + Fmt(maxY - minY);
    }

    private static string Fmt(double v) => Math.Round(v, 1).ToString("0.#", System.Globalization.CultureInfo.GetCultureInfo("pt-BR"));

    private static int Order(LoopRole role) => role switch
    {
        LoopRole.OpenBorder or LoopRole.ClosedBorder => 0,
        LoopRole.Island => 1,
        LoopRole.Ignored => 2,
        _ => 3,
    };

    private static string Plural(int count, string name)
    {
        int space = name.IndexOf(' ');
        string noun = space < 0 ? name : name[..space], rest = space < 0 ? "" : name[space..];
        string plural = noun switch { "Ilha" => "ilhas", "Furo" => "furos", "Laço" => "laços", _ => noun.ToLowerInvariant() };
        return $"{count} {plural}{rest}";
    }

    private static string Effect(LoopRole role, bool many) => role switch
    {
        LoopRole.Island => many ? "sobem · ilhas" : "sobe · ilha",
        LoopRole.Ignored => many ? "descem · ignorados" : "desce · ignorado",
        LoopRole.OpenBorder => "desce · aberta",
        LoopRole.ClosedBorder => "sobe · parede",
        _ => "misto · não suportado",
    };
}
