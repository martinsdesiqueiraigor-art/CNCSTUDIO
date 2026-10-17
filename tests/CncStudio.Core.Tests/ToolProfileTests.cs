using CncStudio.Core.Geometry;
using CncStudio.Core.Setup;
using CncStudio.Core.Simulation;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

public class ToolProfileTests
{
    // Grade com células de 0,5 mm e topo em Z0: o centro da coluna c fica em X = (c + 0,5) × 0,5.
    private const double Cell = 0.5;
    private const double Tip = -5;

    private static HeightMap Map() => new(0, 0, 200, 200, Cell, 0, -30);

    // A ponta mergulha no centro da célula (100, 100): X = Y = 50,25
    private const double Center = 50.25;

    private static double HeightAtDistance(HeightMap map, int cells) => map[100 + cells, 100];

    [Fact]
    public void TopoRetoRebaixaTudoAteAPonta()
    {
        HeightMap map = Map();
        Assert.True(map.CutProfile(Center, Center, ToolProfile.Flat(10), Tip));
        Assert.Equal(Tip, HeightAtDistance(map, 0), 6);
        Assert.Equal(Tip, HeightAtDistance(map, 9), 6);   // 4,5 mm do eixo, dentro do raio 5
        Assert.Equal(0, HeightAtDistance(map, 11), 6);    // 5,5 mm: fora
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(6, 1)]            // d = 3: 5 − √(25 − 9) = 1
    [InlineData(8, 2)]            // d = 4: 5 − √(25 − 16) = 2
    public void EsfericaSegueACalota(int cells, double above)
    {
        HeightMap map = Map();
        map.CutProfile(Center, Center, new ToolProfile(ToolTipShape.Ball, 5), Tip);
        Assert.Equal(Tip + above, HeightAtDistance(map, cells), 5);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(6, 0)]                                // d = 3 = R − rc: ainda no fundo reto
    [InlineData(8, 2 - 1.7320508075688772)]           // d = 4: rc − √(rc² − 1²), rc = 2
    public void ToroidalTemFundoRetoECanto(int cells, double above)
    {
        HeightMap map = Map();
        map.CutProfile(Center, Center, new ToolProfile(ToolTipShape.BullNose, 5, CornerRadius: 2), Tip);
        Assert.Equal(Tip + above, HeightAtDistance(map, cells), 5);
    }

    [Theory]
    [InlineData(118, 6)]
    [InlineData(90, 4)]
    public void BrocaDeixaFundoConico(double angle, int cells)
    {
        HeightMap map = Map();
        map.CutProfile(Center, Center, new ToolProfile(ToolTipShape.Cone, 5, TipAngle: angle), Tip);
        double d = cells * Cell;
        double expected = Tip + d / Math.Tan(angle / 2 * Math.PI / 180);
        Assert.Equal(expected, HeightAtDistance(map, cells), 5);
        Assert.Equal(Tip, HeightAtDistance(map, 0), 6);
    }

    [Fact]
    public void PerfilAcimaDoTopoNaoRemove()
    {
        HeightMap map = Map();
        // Esférica com a ponta 1 mm abaixo do topo: só o miolo corta; a borda fica acima do bruto
        map.CutProfile(Center, Center, new ToolProfile(ToolTipShape.Ball, 5), -1);
        Assert.Equal(-1, HeightAtDistance(map, 0), 6);
        Assert.Equal(0, HeightAtDistance(map, 8), 6);
    }

    [Fact]
    public void NaoPassaDoFundoDoBruto()
    {
        HeightMap map = Map();
        map.CutProfile(Center, Center, new ToolProfile(ToolTipShape.Cone, 5), -100);
        Assert.Equal(-30, HeightAtDistance(map, 0), 6);
    }

    [Fact]
    public void SimuladorUsaOPerfilDaFerramentaAoLongoDoCanal()
    {
        // Canal em X com fresa esférica R5 a Z−5: a seção transversal é a calota
        var moves = new List<Move>
        {
            new(new Point3(20, Center, 5), new Point3(20, Center, Tip), MotionMode.Linear, 1, 100, tool: 2),
            new(new Point3(20, Center, Tip), new Point3(80, Center, Tip), MotionMode.Linear, 2, 300, tool: 2),
        };
        HeightMap map = Map();
        var sim = new MaterialSimulator(map, moves, t => t == 2 ? new ToolProfile(ToolTipShape.Ball, 5) : ToolProfile.Flat(10));
        sim.RunToEnd();

        Assert.Equal(10, sim.CurrentToolDiameter, 6);
        Assert.Equal(Tip, map[100, 100], 5);                  // fundo do canal
        Assert.Equal(Tip + 1, map[100, 106], 5);              // 3 mm de lado
        Assert.Equal(Tip + 1, map[100, 94], 5);
    }

    [Fact]
    public void SimuladorPorDiametroContinuaDeTopoReto()
    {
        var moves = new List<Move> { new(new Point3(Center, Center, 5), new Point3(Center, Center, Tip), MotionMode.Linear, 1, 100) };
        HeightMap map = Map();
        new MaterialSimulator(map, moves, _ => 10.0).RunToEnd();
        Assert.Equal(Tip, map[109, 100], 6);
    }

    // ───────── Perfil a partir do setup ─────────

    [Theory]
    [InlineData(ToolKind.FlatEndMill, ToolTipShape.Flat)]
    [InlineData(ToolKind.FaceMill, ToolTipShape.Flat)]
    [InlineData(ToolKind.Tap, ToolTipShape.Flat)]
    [InlineData(ToolKind.BallEndMill, ToolTipShape.Ball)]
    [InlineData(ToolKind.BullNoseEndMill, ToolTipShape.BullNose)]
    [InlineData(ToolKind.Drill, ToolTipShape.Cone)]
    public void TipoDoSetupViraPerfil(ToolKind kind, ToolTipShape shape)
    {
        ToolProfile profile = new SetupTool { Kind = kind, Diameter = 12, CornerRadius = 1.5, TipAngle = 130 }.ToProfile();
        Assert.Equal(shape, profile.Shape);
        Assert.Equal(6, profile.Radius);
    }

    [Fact]
    public void FerramentaForaDoSetupUsaTopoRetoComODiametroDaTabela()
    {
        var setup = new MachiningSetup { Tools = [new SetupTool { Kind = ToolKind.BallEndMill, Number = 3, Diameter = 8 }] };
        Assert.Equal(new ToolProfile(ToolTipShape.Ball, 4), SetupBinding.ProfileOf(setup, 3, 20));
        Assert.Equal(ToolProfile.Flat(20), SetupBinding.ProfileOf(setup, 5, 20));
        Assert.Equal(ToolProfile.Flat(20), SetupBinding.ProfileOf(null, 3, 20));
    }
}
