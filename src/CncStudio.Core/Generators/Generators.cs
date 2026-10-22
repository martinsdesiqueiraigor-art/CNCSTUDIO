namespace CncStudio.Core.Generators;

/// <summary>Catálogo dos geradores disponíveis (a interface lista daqui).</summary>
public static class Generators
{
    public static IReadOnlyList<ProgramGenerator> All { get; } = [
        new BhcGenerator(), new GridGenerator(), new PocketGenerator(), new CircularPocketGenerator(),
        new SlotGenerator(), new FacingGenerator(), new HelicalHoleGenerator(), new ThreadMillGenerator(),
    ];

    public static ProgramGenerator? ById(string id) => All.FirstOrDefault(g => g.Id == id);
}
