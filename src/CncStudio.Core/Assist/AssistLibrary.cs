namespace CncStudio.Core.Assist;

/// <summary>Grupo de material (para os dados de corte sugeridos).</summary>
public enum MaterialGroup { Steel, Stainless, Aluminum, Brass, CastIron, Titanium, Plastic }

/// <summary>
/// Material com dados de corte de partida para fresa (metal duro). <see cref="Draft"/>: valor ainda não revisado pelo dono do produto;
/// são pontos de partida, nunca garantia: o usinador confere com o catálogo da ferramenta.
/// </summary>
public sealed record MaterialEntry(string Name, MaterialGroup Group, double SurfaceSpeed, double FeedPerTooth, double DepthFactor, bool Draft = true)
{
    /// <summary>Rotação (rpm) e avanço (mm/min) para uma fresa de diâmetro <paramref name="diameter"/> com <paramref name="teeth"/> dentes. Null se os dados não servem.</summary>
    public (double Rpm, double Feed)? SuggestMill(double diameter, int teeth)
    {
        if (!(diameter > 0) || teeth < 1 || !(SurfaceSpeed > 0) || !(FeedPerTooth > 0))
        {
            return null;
        }

        double rpm = Math.Round(SurfaceSpeed * 1000 / (Math.PI * diameter));
        return (rpm, Math.Round(rpm * FeedPerTooth * teeth));
    }

    /// <summary>Profundidade de passe sugerida (mm) para a fresa.</summary>
    public double SuggestDepth(double diameter) => Math.Round(diameter * DepthFactor, 2);

    internal string? Validate() =>
        Name.Length is 0 or > 100 ? "Nome do material inválido."
        : !Enum.IsDefined(Group) ? $"Grupo desconhecido em \"{Name}\"."
        : !(double.IsFinite(SurfaceSpeed) && SurfaceSpeed > 0 && double.IsFinite(FeedPerTooth) && FeedPerTooth > 0 && double.IsFinite(DepthFactor) && DepthFactor > 0)
            ? $"Números do material \"{Name}\" precisam ser positivos."
        : null;
}

/// <summary>Morsa: abertura máxima, mandíbula (altura útil de pega) e medidas do corpo.</summary>
public sealed record ViseEntry(string Name, double MaxOpening, double JawHeight, double JawLength, double BodyWidth, double BodyHeight, bool Draft = true)
{
    /// <summary>Resultado da conferência de uma peça nesta morsa.</summary>
    public ViseFit Check(double partWidth, double partHeight, double minGrip = 5)
    {
        if (!(partWidth > 0) || !(partHeight > 0))
        {
            return new ViseFit(false, "Medidas da peça inválidas.");
        }

        if (partWidth > MaxOpening)
        {
            return new ViseFit(false, $"Peça de {partWidth:0.##} mm não cabe: abertura máxima {MaxOpening:0.##} mm.");
        }

        if (partWidth > JawLength)
        {
            return new ViseFit(false, $"Mandíbula de {JawLength:0.##} mm é menor que a peça ({partWidth:0.##} mm) no sentido do comprimento.");
        }

        return partHeight < minGrip
            ? new ViseFit(false, $"Peça baixa demais para pegar: {partHeight:0.##} mm (mínimo {minGrip:0.##} mm).")
            : new ViseFit(true, "");
    }

    internal string? Validate() =>
        Name.Length is 0 or > 100 ? "Nome da morsa inválido."
        : !(Pos(MaxOpening) && Pos(JawHeight) && Pos(JawLength) && Pos(BodyWidth) && Pos(BodyHeight)) ? $"Medidas da morsa \"{Name}\" precisam ser positivas."
        : null;

    private static bool Pos(double v) => double.IsFinite(v) && v > 0;
}

public sealed record ViseFit(bool Fits, string Reason);

/// <summary>Jogo de castanhas de torno: faixa de pega por fora e por dentro (0 = não serve), altura da castanha e Ø da placa.</summary>
public sealed record ChuckJawEntry(string Name, double ChuckDiameter, double ExternalMin, double ExternalMax, double InternalMin, double InternalMax, double JawHeight, bool Draft = true)
{
    /// <summary>A peça de diâmetro <paramref name="diameter"/> pode ser pega por fora (ou por dentro, num furo)?</summary>
    public bool Grips(double diameter, bool external)
    {
        (double min, double max) = external ? (ExternalMin, ExternalMax) : (InternalMin, InternalMax);
        return max > 0 && diameter >= min && diameter <= max;
    }

    internal string? Validate() =>
        Name.Length is 0 or > 100 ? "Nome da castanha inválido."
        : !(Pos(ChuckDiameter) && Pos(JawHeight)) ? $"Medidas da castanha \"{Name}\" precisam ser positivas."
        : !(Range(ExternalMin, ExternalMax) && Range(InternalMin, InternalMax)) ? $"Faixa de pega da castanha \"{Name}\" inválida."
        : null;

    private static bool Pos(double v) => double.IsFinite(v) && v > 0;

    private static bool Range(double min, double max) => double.IsFinite(min) && double.IsFinite(max) && min >= 0 && max >= min;
}

/// <summary>Bibliotecas do Casillas Assist: materiais, morsas e castanhas. Ferramentas e máquinas já têm cadastro próprio no Code.</summary>
public sealed class AssistLibrary
{
    public const int CurrentVersion = 1;

    public List<MaterialEntry> Materials { get; } = [];

    public List<ViseEntry> Vises { get; } = [];

    public List<ChuckJawEntry> Jaws { get; } = [];

    /// <summary>Morsas em que a peça cabe, da mais justa para a maior.</summary>
    public IReadOnlyList<ViseEntry> FindVises(double partWidth, double partHeight) =>
        Vises.Where(v => v.Check(partWidth, partHeight).Fits).OrderBy(v => v.MaxOpening).ToList();

    /// <summary>Castanhas que pegam o diâmetro, da faixa mais justa para a mais larga.</summary>
    public IReadOnlyList<ChuckJawEntry> FindJaws(double diameter, bool external) =>
        Jaws.Where(j => j.Grips(diameter, external)).OrderBy(j => external ? j.ExternalMax - j.ExternalMin : j.InternalMax - j.InternalMin).ToList();

    /// <summary>Confere nomes repetidos e números. Lista vazia = boa.</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        errors.AddRange(Materials.Select(m => m.Validate()).OfType<string>());
        errors.AddRange(Vises.Select(v => v.Validate()).OfType<string>());
        errors.AddRange(Jaws.Select(j => j.Validate()).OfType<string>());
        foreach (string dup in Materials.GroupBy(m => m.Name).Where(g => g.Count() > 1).Select(g => g.Key))
        {
            errors.Add($"Material repetido: \"{dup}\".");
        }

        foreach (string dup in Vises.GroupBy(v => v.Name).Where(g => g.Count() > 1).Select(g => g.Key))
        {
            errors.Add($"Morsa repetida: \"{dup}\".");
        }

        foreach (string dup in Jaws.GroupBy(j => j.Name).Where(g => g.Count() > 1).Select(g => g.Key))
        {
            errors.Add($"Castanha repetida: \"{dup}\".");
        }

        return errors;
    }

    /// <summary>Itens iniciais, todos como rascunho (valores de partida a conferir com o catálogo do fabricante).</summary>
    public static AssistLibrary Defaults()
    {
        var lib = new AssistLibrary();
        lib.Materials.AddRange(
        [
            new("Aço 1020", MaterialGroup.Steel, 120, 0.05, 0.5),
            new("Aço 1045", MaterialGroup.Steel, 100, 0.045, 0.4),
            new("Inox 304", MaterialGroup.Stainless, 70, 0.035, 0.3),
            new("Alumínio 6061", MaterialGroup.Aluminum, 300, 0.08, 1.0),
            new("Latão", MaterialGroup.Brass, 200, 0.06, 0.8),
            new("Ferro fundido", MaterialGroup.CastIron, 100, 0.06, 0.5),
            new("Titânio Ti6Al4V", MaterialGroup.Titanium, 45, 0.03, 0.2),
            new("Nylon / POM", MaterialGroup.Plastic, 250, 0.08, 1.0),
        ]);
        lib.Vises.AddRange(
        [
            new("Morsa 4\" (100 mm)", 100, 30, 100, 150, 50),
            new("Morsa 5\" (125 mm)", 125, 35, 125, 190, 55),
            new("Morsa 6\" (150 mm)", 150, 40, 150, 230, 60),
        ]);
        lib.Jaws.AddRange(
        [
            new("Placa 3 castanhas Ø160 · duras", 160, 20, 160, 0, 0, 25),
            new("Placa 3 castanhas Ø200 · duras", 200, 25, 200, 0, 0, 30),
            new("Placa 3 castanhas Ø200 · moles (furo)", 200, 0, 0, 30, 190, 35),
        ]);
        return lib;
    }
}
