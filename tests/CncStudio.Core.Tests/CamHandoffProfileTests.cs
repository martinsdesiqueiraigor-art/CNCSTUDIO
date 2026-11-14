using CncStudio.Core.Cam;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Setup;
using CncStudio.Core.Simulation;
using Xunit;

namespace CncStudio.Core.Tests;

/// <summary>
/// UX-04h C2: o simulador do Code monta o perfil de corte com o tipo, o raio de canto e o ângulo da ponta que vieram no pacote, sem
/// SetupTool (opção (a) do Igor). Campo ausente segue topo reto; ferramenta sem os campos dá o mesmo resultado de antes.
/// </summary>
public class CamHandoffProfileTests
{
    // T1 desce 2 mm e anda 30 mm em X; T2 desce 3 mm e anda 30 mm em Y (dois T no mesmo programa)
    private const string Program = """
        O1000
        G90 G54 G17
        T1 M6
        S3000 M3
        G0 X0 Y0 Z5
        G1 Z-2 F200
        G1 X30
        G0 Z5
        T2 M6
        G0 X15 Y-10
        G1 Z-3 F200
        G1 Y20
        G0 Z5
        M30
        """;

    private static HandoffTool Tool(int t, double d, string? type, double? corner = null, double? tip = null) =>
        new(t, d, $"T{t}", 3000, 200, 100) { Type = type, CornerRadius = corner, TipAngle = tip };

    private static CamHandoff Package(params HandoffTool[] tools) => new("h", CamDialect.Fanuc) { Tools = tools };

    [Fact]
    public void Esferica_toroidal_com_raio_e_broca_com_angulo_viram_o_perfil_aprovado()
    {
        Assert.Equal(new ToolProfile(ToolTipShape.Ball, 5), CamHandoffCode.ProfileOf(Tool(1, 10, "esferica"), 10));
        Assert.Equal(new ToolProfile(ToolTipShape.BullNose, 4, 1.5), CamHandoffCode.ProfileOf(Tool(1, 8, "toroidal", corner: 1.5), 8));
        Assert.Equal(new ToolProfile(ToolTipShape.Cone, 3, TipAngle: 140), CamHandoffCode.ProfileOf(Tool(1, 6, "broca", tip: 140), 6));

        // UX-04h D5: facear com raio → toroidal; broca de centro com ângulo → cone
        Assert.Equal(new ToolProfile(ToolTipShape.BullNose, 25, 2), CamHandoffCode.ProfileOf(Tool(1, 50, "facear", corner: 2), 50));
        Assert.Equal(new ToolProfile(ToolTipShape.Cone, 2, TipAngle: 90), CamHandoffCode.ProfileOf(Tool(1, 4, "broca-centro", tip: 90), 4));

        // o Ø do perfil é o efetivo dado pelo chamador (o do CAM no programa), não um número escondido
        Assert.Equal(2.5, CamHandoffCode.ProfileOf(Tool(1, 10, "esferica"), 5)!.Radius);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("toroidal", null, null)]   // toroidal sem raio de canto
    [InlineData("broca", null, null)]      // broca sem ângulo da ponta
    [InlineData("topo", 1.0, 118.0)]
    [InlineData("facear", null, null)]     // facear sem raio de canto
    [InlineData("broca-centro", null, null)] // broca de centro sem ângulo
    [InlineData("facear", 6.0, null)]      // raio de canto maior que o raio da ferramenta
    [InlineData("broca-centro", null, 180.0)]
    [InlineData("macho", null, null)]
    [InlineData("alargador", null, null)]
    [InlineData("mandrilhadora", null, null)]
    [InlineData("tipo-desconhecido", null, null)]
    [InlineData("toroidal", 6.0, null)]    // raio de canto maior que o raio da ferramenta
    [InlineData("toroidal", -1.0, null)]
    [InlineData("broca", null, 0.0)]
    [InlineData("broca", null, 180.0)]
    public void Campo_que_falta_ou_outro_tipo_cai_no_topo_reto(string? type, double? corner, double? tip)
    {
        HandoffTool t = Tool(1, 10, type, corner, tip);
        Assert.Null(CamHandoffCode.ProfileOf(t, 10));
        Func<int, ToolProfile> profiles = SimulationFactory.MillProfiles(null, _ => 10, CamHandoffCode.PackageProfiles(Package(t)));
        Assert.Equal(ToolProfile.Flat(10), profiles(1));
    }

    [Fact]
    public void Sem_pacote_ou_T_fora_do_pacote_igual_a_antes_e_o_setup_vale_por_cima()
    {
        Assert.Null(CamHandoffCode.PackageProfiles(null));
        Func<int, ToolProfile> profiles = SimulationFactory.MillProfiles(null, _ => 6, CamHandoffCode.PackageProfiles(Package(Tool(1, 10, "esferica"))));
        Assert.Equal(ToolProfile.Flat(6), profiles(2));

        // T no setup do programa: o setup vale (como o Ø); o pacote não cria nem muda SetupTool
        var setup = new MachiningSetup();
        setup.Tools.Add(new SetupTool { Number = 1, Kind = ToolKind.FlatEndMill, Diameter = 12 });
        string before = SetupJson.Serialize(setup);
        Func<int, ToolProfile> withSetup = SimulationFactory.MillProfiles(setup, _ => 10, CamHandoffCode.PackageProfiles(Package(Tool(1, 10, "esferica"))));
        Assert.Equal(new ToolProfile(ToolTipShape.Flat, 6), withSetup(1));
        Assert.Equal(before, SetupJson.Serialize(setup));
        Assert.Single(setup.Tools);
    }

    [Fact]
    public void A_ferramenta_de_cada_T_e_a_mesma_que_da_o_O()
    {
        // dois grupos com o mesmo T (Ø diferentes): vale o primeiro com Ø, como em CamHandoffCode.Diameters
        CamHandoff p = Package(Tool(1, 0, "broca", tip: 140), Tool(1, 8, "esferica"), Tool(1, 6, "broca", tip: 118));
        Assert.Equal(8, CamHandoffCode.Diameters(p)[1]);
        Assert.Equal(ToolTipShape.Ball, CamHandoffCode.PackageProfiles(p)!(1, 8)!.Shape);
    }

    private static float[] Cut(CamHandoff? p)
    {
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(Program, Dialect.Fanuc, CamHandoffCode.ApplyDiameters(MachineSettings.Default, p));
        Func<int, ToolProfile> profiles = SimulationFactory.MillProfiles(null, analysis.ToolDiameterOf, CamHandoffCode.PackageProfiles(p));
        HeightMap map = SimulationFactory.MillStock(null, analysis.ToolPath, profiles, 120);
        new MaterialSimulator(map, analysis.ToolPath.Moves, profiles, SimulationFactory.RotatedMoveSkipper(analysis.ToolPath)).RunToEnd();
        return map.Heights.ToArray();
    }

    [Fact]
    public void Ferramenta_sem_os_campos_remove_o_mesmo_material_de_antes_e_com_os_campos_muda_so_a_dela()
    {
        // antes do C2: o pacote só dava o Ø; o perfil era sempre topo reto
        float[] before = Cut(Package(Tool(1, 10, null), Tool(2, 6, null)));
        Assert.Equal(before, Cut(Package(Tool(1, 10, "toroidal"), Tool(2, 6, "broca"))));       // campo faltando
        Assert.Equal(before, Cut(Package(Tool(1, 10, "topo", 1, 118), Tool(2, 6, "macho"))));  // outro tipo
        Assert.Equal(before, Cut(Package(Tool(1, 10, "macho", 1, 118), Tool(2, 6, "alargador", 1, 118))));
        Assert.Equal(before, Cut(Package(Tool(1, 10, "facear"), Tool(2, 6, "broca-centro"))));  // sem raio / sem ângulo

        // T1 esférica: a canaleta dela fica redonda (menos material removido); T2 sem campos, a dela igual
        float[] ball = Cut(Package(Tool(1, 10, "esferica"), Tool(2, 6, null)));
        Assert.NotEqual(before, ball);
        Assert.True(ball.Sum() > before.Sum());
        float[] onlyT2 = Cut(Package(Tool(1, 10, null), Tool(2, 6, "broca", tip: 118)));
        Assert.NotEqual(before, onlyT2);
        Assert.NotEqual(ball, onlyT2);
    }

    [Fact]
    public void Broca_de_centro_com_angulo_corta_em_cone_e_facear_com_raio_corta_toroidal()
    {
        float[] before = Cut(Package(Tool(1, 10, null), Tool(2, 6, null)));

        // T2 broca de centro 90°: a ponta é cone, não topo reto; T1 fica igual
        float[] cone = Cut(Package(Tool(1, 10, null), Tool(2, 6, "broca-centro", tip: 90)));
        Assert.NotEqual(before, cone);
        Assert.True(cone.Sum() > before.Sum());

        // T1 facear com raio 2: canto arredondado, sobra material no canto da canaleta
        float[] torus = Cut(Package(Tool(1, 10, "facear", corner: 2), Tool(2, 6, null)));
        Assert.NotEqual(before, torus);
        Assert.True(torus.Sum() > before.Sum());
        Assert.NotEqual(cone, torus);
    }
}
