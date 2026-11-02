using CncStudio.Core.Cam;

namespace CncStudio.Core.Tests;

/// <summary>"Desatualizado" por dependência (UX-01 PR 6): um teste por dependência da tabela.</summary>
public class DataStampTests
{
    private static readonly (TimeSpan, int, string) Program = (TimeSpan.FromMinutes(12), 3, "placa.nc");

    private static List<(string Label, string Text)> Fields(string qty = "10", string rate = "120") =>
        [("Quantidade do lote", qty), ("Hora-máquina", rate)];

    private static DataStamp Quote((TimeSpan, int, string)? program = null, List<(string, string)>? fields = null, string part = "placa", string company = "Oficina") =>
        DataStamp.OfQuote(program ?? Program, fields ?? Fields(), part, company);

    // ---------------------------------------------------------------- linha 2: perfil da máquina → programa do Trabalho fresa

    [Fact]
    public void SameMachineIsFresh()
    {
        DataStamp built = DataStamp.OfMachine(MachineProfile.FourAxisTableA());
        Assert.Null(DataStamp.StaleMessage(built, DataStamp.OfMachine(MachineProfile.FourAxisTableA())));
    }

    [Fact]
    public void MachineSpindleChangeMakesJobStale()
    {
        MachineProfile m = MachineProfile.FourAxisTableA();
        DataStamp built = DataStamp.OfMachine(m);
        m.MaxSpindleRpm = 8000;
        Assert.Equal("Desatualizado: mudou o perfil da máquina.", DataStamp.StaleMessage(built, DataStamp.OfMachine(m)));
    }

    [Fact]
    public void MachineRotaryLimitOrDirectionChangeMakesJobStale()
    {
        MachineProfile m = MachineProfile.FourAxisTableA();
        DataStamp built = DataStamp.OfMachine(m);
        m.Axes[0].MaxAngle = 90;
        Assert.NotNull(DataStamp.StaleMessage(built, DataStamp.OfMachine(m)));

        built = DataStamp.OfMachine(m);
        m.Axes[0].Reversed = !m.Axes[0].Reversed;
        Assert.NotNull(DataStamp.StaleMessage(built, DataStamp.OfMachine(m)));
    }

    [Fact]
    public void SwitchingOrRemovingMachineMakesJobStale()
    {
        DataStamp built = DataStamp.OfMachine(MachineProfile.ThreeAxis());
        Assert.NotNull(DataStamp.StaleMessage(built, DataStamp.OfMachine(MachineProfile.FourAxisTableA())));
        Assert.NotNull(DataStamp.StaleMessage(built, DataStamp.OfMachine(null)));
    }

    // ---------------------------------------------------------------- linha 3: programa do Code → orçamento

    [Fact]
    public void ProgramChangeMakesQuoteStale()
    {
        DataStamp calc = Quote();
        Assert.Null(DataStamp.StaleMessage(calc, Quote()));
        Assert.Equal("Desatualizado: mudou o programa do Code.", DataStamp.StaleMessage(calc, Quote(program: (TimeSpan.FromMinutes(13), 3, "placa.nc"))));
        Assert.NotNull(DataStamp.StaleMessage(calc, Quote(program: (TimeSpan.FromMinutes(12), 4, "placa.nc"))));
        Assert.NotNull(DataStamp.StaleMessage(calc, Quote(program: (TimeSpan.FromMinutes(12), 3, "outra.nc"))));
        Assert.NotNull(DataStamp.StaleMessage(calc, DataStamp.OfQuote(null, Fields(), "placa", "Oficina")));
    }

    // ---------------------------------------------------------------- linha 4: campos do orçamento → orçamento

    [Fact]
    public void QuoteFieldChangeMakesQuoteStaleAndNamesTheField()
    {
        DataStamp calc = Quote();
        Assert.Equal("Desatualizado: mudou quantidade do lote.", DataStamp.StaleMessage(calc, Quote(fields: Fields(qty: "20"))));
        Assert.Equal("Desatualizado: mudou quantidade do lote, hora-máquina.", DataStamp.StaleMessage(calc, Quote(fields: Fields("20", "130"))));
        Assert.Null(DataStamp.StaleMessage(calc, Quote(fields: Fields(qty: " 10 "))));
    }

    [Fact]
    public void PartNameAndCompanyChangeMakeProposalStale()
    {
        DataStamp calc = Quote();
        Assert.Equal("Desatualizado: mudou o nome da peça.", DataStamp.StaleMessage(calc, Quote(part: "tampa")));
        Assert.Equal("Desatualizado: mudou o cabeçalho da proposta.", DataStamp.StaleMessage(calc, Quote(company: "Outra")));
    }

    // ---------------------------------------------------------------- regra comum

    [Fact]
    public void NoResultYetIsNeverStale() => Assert.Null(DataStamp.StaleMessage(null, Quote()));

    [Fact]
    public void PartAddedOrRemovedCountsAsChange()
    {
        DataStamp a = DataStamp.Of([("x", "1")]);
        DataStamp b = DataStamp.Of([("x", "1"), ("y", "2")]);
        Assert.Equal(["y"], b.ChangedSince(a));
        Assert.Equal(["y"], a.ChangedSince(b));
    }
}
