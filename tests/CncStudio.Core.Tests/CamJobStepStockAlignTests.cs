using CncStudio.Core.Cam;
using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;

namespace CncStudio.Core.Tests;

/// <summary>STEP-BRUTO-01: bruto STEP que é o mesmo arquivo da peça STEP segue a colocação da peça (giros e topo em Z0).</summary>
public class CamJobStepStockAlignTests
{
    private const string Path = @"C:\pecas\Flange.step";

    // caixa fora da origem e com Y "para cima" no arquivo, como o CAD exporta
    private static TriangleMesh Raw() => CamJobStepStockTests.BoxMesh(-50, -10, -50, 100, 26, 100);

    private static CamJob JobWith(string stockPath, IReadOnlyList<ModelTurn> turns, bool topAtZero = true)
    {
        var job = new CamJob { ProgramNumber = 1, WorkOffset = 54, Zero = JobZero.Legacy, Name = "ALINHA" };
        job.PartStep = CamJobPart.Import(Path, Raw(), turns, topAtZero);
        job.SetStock(null, CamJobStock.ImportStep(null, stockPath, Raw()));
        return job;
    }

    private static (double, double, double, double, double, double) Box(StockSolid s) => (s.MinX, s.MaxX, s.MinY, s.MaxY, s.MinZ, s.MaxZ);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mesmo_arquivo_cai_exatamente_na_caixa_da_peca(bool girar)
    {
        ModelTurn[] turns = girar ? [ModelTurn.XPlus90] : [];
        CamJob job = JobWith(Path, turns);

        StockSolid stock = CamJobStock.Check(job).Solid!;
        StepStockSummary part = job.PartStep!.Summary;

        Assert.Equal((part.MinX, part.MaxX, part.MinY, part.MaxY, part.MinZ, part.MaxZ), Box(stock));
        Assert.Equal(0, stock.MaxZ, 6); // topo em Z0
    }

    [Fact]
    public void Malha_do_bruto_e_a_mesma_da_peca_colocada()
    {
        CamJob job = JobWith(Path, [ModelTurn.XPlus90]);
        TriangleMesh placed = CamJobStock.PlaceStepMesh(job.StockShape!, Raw(), job.PartStep);
        TriangleMesh part = CamJobPart.Place(Raw(), job.PartStep!.Turns, job.PartStep.TopAtZero);

        Assert.Equal((part.Bounds.Min, part.Bounds.Max), (placed.Bounds.Min, placed.Bounds.Max));
    }

    [Fact]
    public void Arquivo_diferente_continua_como_esta_no_arquivo()
    {
        CamJob job = JobWith(@"C:\brutos\Fundido.step", []);

        StockSolid stock = CamJobStock.Check(job).Solid!;

        Assert.Equal((-50.0, 50.0, -10.0, 16.0, -50.0, 50.0), Box(stock));
    }

    [Fact]
    public void Sem_peca_STEP_continua_como_esta_no_arquivo()
    {
        CamJob job = JobWith(Path, []);
        job.PartStep = null;

        Assert.Equal((-50.0, 50.0, -10.0, 16.0, -50.0, 50.0), Box(CamJobStock.Check(job).Solid!));
    }

    [Fact]
    public void Deslocamento_digitado_continua_valendo_sobre_o_arquivo()
    {
        CamJob job = JobWith(Path, []);
        job.SetStock(null, job.StockShape! with { Placement = StepPlacement.Typed, OffsetX = 5, OffsetY = 6, OffsetZ = 7 });

        Assert.Equal((-45.0, 55.0, -4.0, 22.0, -43.0, 57.0), Box(CamJobStock.Check(job).Solid!));
    }

    [Fact]
    public void Caixa_colocada_nao_muda_o_volume()
    {
        StepStockSummary raw = StepStockSummary.From(Raw());

        Assert.Equal(raw.Volume, raw.Placed([ModelTurn.YMinus90, ModelTurn.Flip], true).Volume);
    }
}
