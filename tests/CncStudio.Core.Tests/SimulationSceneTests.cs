using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;
using CncStudio.Core.Rendering;
using CncStudio.Core.Scene;
using CncStudio.Core.Setup;
using CncStudio.Core.Simulation;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Tests;

/// <summary>Cena 3D comum (Fase 1, S1): malhas do material, torno, ferramentas, fixações, linhas e câmera.</summary>
public class SimulationSceneTests
{
    // 120 × 70 células de 1 mm: 4 × 3 blocos de 32
    private static HeightMap Mapa() => new(0, 0, 120, 70, 1, 0, -20);

    private static void ConferirMalha(SolidMesh mesh)
    {
        Assert.Equal(mesh.TriangleCount * 3, mesh.IndexCount);
        foreach (int index in mesh.Indices)
        {
            Assert.InRange(index, 0, mesh.VertexCount - 1);
        }

        ReadOnlySpan<float> n = mesh.Normals;
        for (int i = 0; i < n.Length; i += 3)
        {
            Assert.Equal(1, Math.Sqrt(n[i] * n[i] + n[i + 1] * n[i + 1] + n[i + 2] * n[i + 2]), 4);
        }

        foreach (float c in mesh.Colors)
        {
            Assert.InRange(c, 0f, 1f);
        }
    }

    // Triângulos virados para fora: cada normal informada concorda com a ordem dos vértices
    private static void ConferirSentido(SolidMesh mesh)
    {
        ReadOnlySpan<float> p = mesh.Positions, n = mesh.Normals;
        ReadOnlySpan<int> idx = mesh.Indices;
        for (int i = 0; i < idx.Length; i += 3)
        {
            int a = idx[i] * 3, b = idx[i + 1] * 3, c = idx[i + 2] * 3;
            double ux = p[b] - p[a], uy = p[b + 1] - p[a + 1], uz = p[b + 2] - p[a + 2];
            double vx = p[c] - p[a], vy = p[c + 1] - p[a + 1], vz = p[c + 2] - p[a + 2];
            double gx = uy * vz - uz * vy, gy = uz * vx - ux * vz, gz = ux * vy - uy * vx;
            double dot = gx * n[a] + gy * n[a + 1] + gz * n[a + 2];
            Assert.True(dot >= -1e-6, $"triângulo {i / 3} virado para dentro");
        }
    }

    private static double VolumeDoMapa(HeightMap map)
    {
        double sum = 0;
        foreach (float h in map.Heights)
        {
            sum += (h - map.BottomZ) * map.CellSize * map.CellSize;
        }

        return sum;
    }

    // ── Material da fresa ──

    [Fact]
    public void BlocoIntactoEFechadoComOVolumeDoBruto()
    {
        var mesh = new HeightMapMesh(Mapa());
        SolidMesh all = mesh.Merged();
        ConferirMalha(all);
        ConferirSentido(all);
        Assert.Equal(120 * 70 * 20, all.EnclosedVolume(), 3);
        Assert.Equal(12, mesh.TileCount);
    }

    [Fact]
    public void DepoisDeCortarAMalhaContinuaFechadaComOVolumeQueSobrou()
    {
        HeightMap map = Mapa();
        map.CutDisc(30, 30, 8, -5, tool: 1);
        map.CutProfile(70, 35, new ToolProfile(ToolTipShape.Ball, 6), -9, tool: 2);
        map.CutDisc(110, 65, 15, -40, tool: 3);   // atravessa o fundo e sai pela borda

        SolidMesh all = new HeightMapMesh(map).Merged();
        ConferirMalha(all);
        ConferirSentido(all);
        Assert.Equal(VolumeDoMapa(map), all.EnclosedVolume(), 2);
    }

    [Fact]
    public void AtualizacaoParcialIgualAMalhaFeitaDoZero()
    {
        HeightMap map = Mapa();
        var mesh = new HeightMapMesh(map);
        Assert.Equal(12, mesh.Update().Count);
        Assert.Empty(mesh.Update());

        map.CutDisc(31, 31, 4, -5, tool: 1);   // perto do canto de 4 blocos
        IReadOnlyList<int> changed = mesh.Update();
        Assert.InRange(changed.Count, 1, 6);
        Assert.Contains(0, changed);

        map.CutDisc(100, 10, 3, -2, tool: 2);
        changed = mesh.Update();
        Assert.InRange(changed.Count, 1, 4);

        for (int row = 0; row < mesh.TileRows; row++)
        {
            for (int column = 0; column < mesh.TileColumns; column++)
            {
                SolidMesh partial = mesh.Tile(row * mesh.TileColumns + column);
                SolidMesh full = HeightMapMesh.BuildTile(map, mesh.Palette, column, row);
                Assert.Equal(full.Positions.ToArray(), partial.Positions.ToArray());
                Assert.Equal(full.Normals.ToArray(), partial.Normals.ToArray());
                Assert.Equal(full.Colors.ToArray(), partial.Colors.ToArray());
                Assert.Equal(full.Indices.ToArray(), partial.Indices.ToArray());
            }
        }
    }

    [Fact]
    public void CorteMarcaSoOBlocoAlterado()
    {
        HeightMap map = Mapa();
        int before = map.TileVersion(3, 2);
        map.CutDisc(5, 5, 2, -1);
        Assert.Equal(before, map.TileVersion(3, 2));
        Assert.Equal(map.Version, map.TileVersion(0, 0));
    }

    [Fact]
    public void ModoTecnicoPintaOCortePelaFerramenta()
    {
        HeightMap map = Mapa();
        map.CutDisc(20, 20, 3, -5, tool: 1);
        map.CutDisc(60, 20, 3, -5, tool: 2);
        Assert.Equal(1, map.CutBy(20, 20));
        Assert.Equal(2, map.CutBy(60, 20));
        Assert.Equal(0, map.CutBy(40, 40));

        uint t1 = ScenePalette.Technical.Machined(0.25, 1), t2 = ScenePalette.Technical.Machined(0.25, 2);
        Assert.NotEqual(t1, t2);

        // No metal (padrão) a cor depende só da profundidade
        Assert.Equal(ScenePalette.Realistic.Machined(0.25, 1), ScenePalette.Realistic.Machined(0.25, 2));

        SolidMesh tile = HeightMapMesh.BuildTile(map, ScenePalette.Technical, 1, 0);
        Assert.True(TemCor(tile, t2));
        Assert.False(TemCor(tile, t1));
    }

    private static bool TemCor(SolidMesh mesh, uint rgb)
    {
        float r = ((rgb >> 16) & 0xFF) / 255f, g = ((rgb >> 8) & 0xFF) / 255f, b = (rgb & 0xFF) / 255f;
        ReadOnlySpan<float> c = mesh.Colors;
        for (int i = 0; i < c.Length; i += 3)
        {
            if (c[i] == r && c[i + 1] == g && c[i + 2] == b)
            {
                return true;
            }
        }

        return false;
    }

    // ── Torno ──

    private static double Poligono(double r) =>
        SceneMeshes.RevolutionSegments / 2.0 * r * r * Math.Sin(2 * Math.PI / SceneMeshes.RevolutionSegments);

    [Fact]
    public void PecaDoTornoFechadaInteiraEComUmQuartoRemovido()
    {
        var stock = new TurningStock(-50, 0, 20, 5);
        double ring = 50 * (Math.PI * 400 - Math.PI * 25);

        // A peça do torno usa 60 divisões na volta (mesma do desenho por CPU)
        static double Area(double r) => 30 * r * r * Math.Sin(2 * Math.PI / 60);
        double polygonRing = 50 * (Area(20) - Area(5));

        SolidMesh whole = SceneMeshes.Turning(stock, quarterSection: false, ScenePalette.Realistic);
        ConferirMalha(whole);
        ConferirSentido(whole);
        Assert.Equal(polygonRing, whole.EnclosedVolume(), 1);
        Assert.InRange(whole.EnclosedVolume() / ring, 0.99, 1.0);

        SolidMesh quarter = SceneMeshes.Turning(stock, quarterSection: true, ScenePalette.Realistic);
        ConferirMalha(quarter);
        ConferirSentido(quarter);
        Assert.Equal(polygonRing * 0.75, quarter.EnclosedVolume(), 1);
    }

    // ── Ferramentas ──

    [Theory]
    [InlineData(ToolTipShape.Flat)]
    [InlineData(ToolTipShape.Ball)]
    [InlineData(ToolTipShape.BullNose)]
    [InlineData(ToolTipShape.Cone)]
    public void FerramentaDaFresaFechadaComAPontaNaOrigem(ToolTipShape shape)
    {
        var profile = new ToolProfile(shape, 5, CornerRadius: 1.5);
        var tool = new SetupTool { Number = 1, Diameter = 10, CuttingLength = 30, HolderDiameter = 32, HolderLength = 40 };
        (SolidMesh cutter, SolidMesh holder) = SceneMeshes.MillTool(profile, tool, ScenePalette.Realistic);
        foreach (SolidMesh m in new[] { cutter, holder })
        {
            ConferirMalha(m);
            ConferirSentido(m);
            Assert.True(m.EnclosedVolume() > 0);
        }

        Bounds3 b = cutter.Bounds;
        Assert.Equal(0, b.Min.Z, 6);
        Assert.Equal(30, b.Max.Z, 6);
        Assert.Equal(5, b.Max.X, 3);
        Assert.Equal(70, holder.Bounds.Max.Z, 6);

        // Fresa de topo: volume do prisma de 48 lados
        if (shape == ToolTipShape.Flat)
        {
            Assert.Equal(Poligono(5) * 30, cutter.EnclosedVolume(), 3);
        }
    }

    [Fact]
    public void InsertoDoTornoComAPontaNaOrigem()
    {
        (SolidMesh insert, SolidMesh holder) = SceneMeshes.LatheTool(ScenePalette.Realistic);
        ConferirMalha(insert);
        ConferirSentido(insert);
        ConferirSentido(holder);
        Bounds3 b = insert.Bounds;
        Assert.Equal(0, b.Min.X, 6);
        Assert.Equal(0, b.Min.Z, 6);
        Assert.True(insert.EnclosedVolume() > 0);
    }

    // ── Fixações ──

    [Fact]
    public void MorsaComBaseEDoisMordentes()
    {
        var vise = new SetupFixture { Kind = SetupFixtureKind.Vise, X = -10, Y = -20, Z = -50, Width = 150, Depth = 110, Height = 30, JawOpening = 60, JawThickness = 12 };
        (SolidMesh body, SolidMesh jaws) = SceneMeshes.Fixture(vise, null, ScenePalette.Realistic);
        ConferirSentido(body);
        ConferirSentido(jaws);
        Assert.Equal(150 * 110 * 30, body.EnclosedVolume(), 3);
        Assert.Equal(2 * 150 * 12 * 12, jaws.EnclosedVolume(), 3);
    }

    [Fact]
    public void PlacaComTresCastanhasQuandoHaBruto()
    {
        var chuck = new SetupFixture { Kind = SetupFixtureKind.ThreeJawChuck, Width = 160, Depth = 160, Height = 40, Z = -60, JawOpening = 50, JawThickness = 15 };
        var stock = new SetupStock { Kind = SetupStockKind.Round, Diameter = 50, Z = -20, Height = 40 };
        (SolidMesh body, SolidMesh jaws) = SceneMeshes.Fixture(chuck, stock, ScenePalette.Realistic);
        ConferirSentido(body);
        ConferirSentido(jaws);
        Assert.Equal(3 * 2 * 12, jaws.TriangleCount);     // 3 castanhas × 2 caixas × 12 triângulos
        Assert.Equal(Poligono(80) * 40, body.EnclosedVolume(), 1);

        (_, SolidMesh none) = SceneMeshes.Fixture(chuck, null, ScenePalette.Realistic);
        Assert.Equal(0, none.TriangleCount);
    }

    // ── Câmera ──

    [Fact]
    public void AjustarEnquadraACaixaInteiraEmTodasAsVistas()
    {
        Bounds3 box = Bounds3.Empty.Include(new Point3(-10, 5, -30)).Include(new Point3(140, 80, 2));
        foreach (SceneView view in Enum.GetValues<SceneView>())
        {
            foreach (double aspect in new[] { 0.5, 1.0, 1.8 })
            {
                SceneCamera camera = SceneCamera.Fit(box, view, aspect);
                float[] m = camera.ViewProjection(aspect);
                foreach (double x in new[] { box.Min.X, box.Max.X })
                foreach (double y in new[] { box.Min.Y, box.Max.Y })
                foreach (double z in new[] { box.Min.Z, box.Max.Z })
                {
                    double cx = m[0] * x + m[4] * y + m[8] * z + m[12];
                    double cy = m[1] * x + m[5] * y + m[9] * z + m[13];
                    double cz = m[2] * x + m[6] * y + m[10] * z + m[14];
                    double cw = m[3] * x + m[7] * y + m[11] * z + m[15];
                    Assert.True(cw > 0);
                    Assert.InRange(cx / cw, -1, 1);
                    Assert.InRange(cy / cw, -1, 1);
                    Assert.InRange(cz / cw, -1, 1);
                }
            }
        }
    }

    [Fact]
    public void VistasProntasEMovimentosDaCamera()
    {
        Bounds3 box = Bounds3.Empty.Include(new Point3(0, 0, 0)).Include(new Point3(100, 50, 20));
        Assert.True(SceneCamera.Fit(box, SceneView.Top, 1).ToEye.Z > 0.99);
        Assert.Equal(-1, SceneCamera.Fit(box, SceneView.Front, 1).ToEye.Y, 9);
        Assert.Equal(1, SceneCamera.Fit(box, SceneView.Side, 1).ToEye.X, 9);

        SceneCamera camera = SceneCamera.Fit(box, SceneView.Isometric, 1);
        Assert.Equal(SceneCamera.MaxElevation, camera.Orbit(0, 500).Elevation);
        Assert.Equal(350, camera.Orbit(-45, 0).Azimuth, 9);
        Assert.Equal(camera.Distance / 2, camera.Zoom(0.5).Distance, 9);

        // Deslocar não muda a direção do olhar, só o alvo (perpendicular a ela)
        SceneCamera moved = camera.Pan(10, 5);
        Point3 delta = moved.Target - camera.Target;
        Assert.Equal(Math.Sqrt(125), delta.DistanceTo(Point3.Origin), 9);
        Point3 back = camera.ToEye;
        Assert.Equal(0, delta.X * back.X + delta.Y * back.Y + delta.Z * back.Z, 9);
    }

    // ── Linhas e cena completa ──

    private const string Programa = """
        O0001
        T1 M6
        G00 X0 Y0 Z5
        G01 Z-2 F100
        G01 X40
        G02 X60 Y20 R20
        G00 X10 Y10
        G00 Z5
        M30
        """;

    private static ToolPath Trajetoria() => MachineExecutor.Execute(GCodeParser.Parse(Programa, Dialect.Fanuc));

    [Fact]
    public void LinhasSeparamAvancoRapidoERapidoNoMaterial()
    {
        ToolPath path = Trajetoria();
        int collided = path.Moves.ToList().FindIndex(m => m.IsRapid && m.End.X == 10);
        (SceneLines feed, SceneLines rapid, SceneLines hit) = SceneLines.FromToolPath(path, [collided]);

        Assert.Equal(SceneRole.FeedPath, feed.Role);
        Assert.True(feed.SegmentCount > 3);                       // o arco vira vários segmentos
        Assert.Equal([collided], hit.MoveOfSegment);
        Assert.DoesNotContain(collided, rapid.MoveOfSegment);
        Assert.Equal(feed.SegmentCount * 6, feed.Positions.Count);
        Assert.All(feed.MoveOfSegment, i => Assert.False(path.Moves[i].IsRapid));
    }

    [Fact]
    public void CenaDaFresaAtualizaSoOQueMudouEPosicionaAFerramenta()
    {
        ToolPath path = Trajetoria();
        var map = new HeightMap(-10, -10, 200, 60, 1, 0, -10);
        var setup = new MachiningSetup();
        setup.Fixtures.Add(new SetupFixture { Kind = SetupFixtureKind.Vise, X = -30, Y = -40, Z = -40, Width = 160, Depth = 140, Height = 30 });
        setup.Tools.Add(new SetupTool { Number = 1, Diameter = 8, CuttingLength = 20, HolderLength = 30 });

        SimulationScene scene = SimulationScene.ForMill(map, path, _ => ToolProfile.Flat(8), setup);
        Assert.False(scene.IsLathe);
        Assert.Equal(14, scene.Objects.Count(o => o.Role == SceneRole.Material));  // 200 × 60 células = 7 × 2 blocos
        Assert.Contains(scene.Objects, o => o.Role == SceneRole.FixtureJaw);
        Assert.Empty(scene.Refresh());

        var sim = new MaterialSimulator(map, path.Moves, _ => ToolProfile.Flat(8));
        Assert.Equal(14, scene.Refresh().Count);   // criar o simulador volta o bruto ao início (tudo muda)
        sim.RunToEnd();
        IReadOnlyList<SceneObject> changed = scene.Refresh();
        Assert.NotEmpty(changed);
        Assert.True(changed.Count < 14);   // o programa só corta na metade esquerda
        Assert.All(changed, o => Assert.Equal(SceneRole.Material, o.Role));

        scene.SetTool(1, new Point3(40, 0, -2));
        SceneObject cutter = scene.Objects.Single(o => o.Id == "ferramenta/corte");
        Assert.Equal(new Point3(40, 0, -2), cutter.Offset);
        Assert.Equal(20, cutter.Mesh.Bounds.Max.Z, 6);
        int version = cutter.Version;
        scene.SetTool(1, new Point3(41, 0, -2));
        Assert.Equal(version, cutter.Version);                 // mesma ferramenta: só move

        // Ajustar inclui a morsa (que é maior que o bruto)
        scene.Fit(SceneView.Isometric, 1.5);
        Assert.Equal(-40, scene.Bounds.Min.Z, 6);

        // Modo técnico refaz as malhas e mantém a ferramenta no lugar
        scene.Palette = ScenePalette.Technical;
        Assert.Equal(new Point3(41, 0, -2), scene.Objects.Single(o => o.Id == "ferramenta/corte").Offset);
        foreach (SceneObject o in scene.Objects)
        {
            ConferirMalha(o.Mesh);
        }
    }

    [Fact]
    public void CenaDoTornoTrocaACorteDeUmQuarto()
    {
        var stock = new TurningStock(-40, 0, 15);
        SimulationScene scene = SimulationScene.ForLathe(stock, Trajetoria());
        Assert.True(scene.IsLathe);
        SceneObject part = scene.Objects.Single(o => o.Id == "material/0");
        double quarter = part.Mesh.EnclosedVolume();
        scene.QuarterSection = false;
        Assert.Equal(quarter / 0.75, part.Mesh.EnclosedVolume(), 1);

        stock.Cut(-10, 10, 2, 0);
        Assert.Single(scene.Refresh());
        Assert.True(part.Mesh.EnclosedVolume() < quarter / 0.75);
    }
}
