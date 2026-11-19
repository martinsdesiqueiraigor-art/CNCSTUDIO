using CncStudio.Core.Cam;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Setup;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>DESK-01 B: o CAM escreve o bruto no programa e manda a fixação do torno ao Code.</summary>
public class Desk01HandoffTests
{
    [Theory]
    [InlineData(HandoffStockKind.Prism, 0, 0, 100, 80, 20, "BRUTO 100 X 80 X 20")]
    [InlineData(HandoffStockKind.Round, 50, 0, 0, 0, 30, "BRUTO D50 X 30")]
    [InlineData(HandoffStockKind.Tube, 50, 30, 0, 0, 30, "BRUTO D50 X D30 X 30")]
    public void ComentarioDoBrutoNaEscritaQueOCodeLe(HandoffStockKind kind, double d, double inner, double w, double depth, double h, string expected)
    {
        var s = new HandoffStock(kind) { Diameter = d, InnerDiameter = inner, Width = w, Depth = depth, Height = h };
        Assert.Equal(expected, s.CommentText());
        // o que o CAM escreve o Code lê com a mesma forma e medidas
        SetupStock read = ProgramStock.Read("(" + expected + ")", MachineKind.Mill, Bounds3.Empty)!;
        Assert.Equal(h, read.Height);
    }

    [Fact]
    public void CilindroDeitadoEStepNaoViramComentario()
    {
        Assert.Null(new HandoffStock(HandoffStockKind.Round) { Axis = StockAxis.X, Diameter = 50, Height = 100 }.CommentText());
        Assert.Null(new HandoffStock(HandoffStockKind.Step).CommentText());
    }

    [Fact]
    public void BrutoDoTornoIncompletoViraPendencia()
    {
        Assert.Equal("Bruto incompleto: falta comprimento", LatheJobInfo.StockProblem(new LatheJobStock(30, null, 0)));
        Assert.Null(LatheJobInfo.StockProblem(new LatheJobStock(30, 125, 2)));
        Assert.StartsWith("Bruto inválido", LatheJobInfo.StockProblem(new LatheJobStock(-1, 125, 2)));
    }

    [Fact]
    public void FixacaoDoTornoVaiAoCodeEDesenhaContraponto()
    {
        var p = new CamHandoff("hash", CamDialect.Fanuc)
        {
            Machine = CamHandoff.Lathe,
            Stock = new HandoffStock(HandoffStockKind.Round) { Diameter = 30, Height = 125, Z = -123 },
            LatheGrip = new HandoffLatheGrip(50, 10, 80, true),
        };
        HandoffReadResult back = CamHandoffJson.Parse(CamHandoffJson.ToJson(p));
        Assert.Equal(p.LatheGrip, back.Package!.LatheGrip);

        MachiningSetup setup = MachiningSetup.CreateDefault("TORNO.nc");
        setup.Fixtures.Clear();
        Assert.True(CamHandoffCode.FillLatheFixture(setup, back.Package));
        SetupFixture chuck = Assert.Single(setup.Fixtures);
        Assert.Equal((SetupFixtureKind.ThreeJawChuck, 50.0, 10.0, true), (chuck.Kind, chuck.JawOpening, chuck.GripLength!.Value, chuck.Tailstock == true));
        // não sobrescreve
        Assert.False(CamHandoffCode.FillLatheFixture(setup, back.Package));
    }
}
