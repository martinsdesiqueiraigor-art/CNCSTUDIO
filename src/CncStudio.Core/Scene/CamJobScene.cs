using CncStudio.Core.Cam;
using CncStudio.Core.Geometry;
using CncStudio.Core.Rendering;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Scene;

/// <summary>
/// Cena fixa do 3D do Trabalho fresa: bruto inteiro, peça (contornos 2D em Z 0), trajetória e eixos do zero.
/// Não simula nem remove material (a simulação fica no Casillas Code). As escolhas dos chips
/// (<see cref="CamViewOptions"/>) decidem o que entra.
/// </summary>
public static class CamJobScene
{
    /// <summary>Cor do contorno da peça em aramado.</summary>
    public const uint PartColor = 0xE6EDF3;

    /// <summary>Cores das setas do zero: X vermelho, Y verde, Z azul.</summary>
    public const uint AxisX = 0xF85149, AxisY = 0x3FB950, AxisZ = 0x58A6FF;

    /// <summary>Id da malha do bruto na cena.</summary>
    public const string StockId = "bruto";

    /// <summary>Monta a cena com o bloco absoluto (topo em Z 0). "hasSolidPart" = o Trabalho tem a peça em 3D (hoje nunca: só contornos 2D).</summary>
    public static SceneSnapshot Build(JobStock? stock, IReadOnlyList<Contour> contours, ToolPath? path, CamViewOptions options, bool hasSolidPart = false) =>
        BuildResolved(stock is { Width: > 0, Length: > 0, Thickness: > 0 } s ? StockSolid.Box(s.X, s.Y, -s.Thickness, s.Width, s.Length, s.Thickness) : null,
            contours, path, options, hasSolidPart);

    /// <summary>
    /// Monta a cena com o bruto resolvido (<see cref="CamJobStock.Check"/>): caixa, cilindro ou tubo, na posição do programa.
    /// Bruto incompleto chega null e não aparece (nada suposto).
    /// </summary>
    public static SceneSnapshot BuildResolved(StockSolid? stock, IReadOnlyList<Contour> contours, ToolPath? path, CamViewOptions options, bool hasSolidPart = false)
    {
        ScenePalette palette = ScenePalette.Realistic;
        var objects = new List<SceneObjectState>();
        var lines = new List<SceneLinesState>();
        Bounds3 bounds = Bounds3.Empty;

        if (stock is { SizeX: > 0, SizeY: > 0, SizeZ: > 0 } s)
        {
            bounds = bounds.Include(new Point3(s.MinX, s.MinY, s.MinZ)).Include(new Point3(s.MaxX, s.MaxY, s.MaxZ));
            if (options.Stock != StockDisplay.Hidden)
            {
                SolidMesh mesh = StockMesh(s, palette.Stock);
                if (!options.Edges)
                {
                    mesh.UseExplicitEdges(); // nenhuma aresta marcada
                }

                objects.Add(new SceneObjectState(StockId, SceneRole.Material, mesh, 1, Point3.Origin, true));
            }
        }

        PartDisplay part = options.EffectivePart(hasSolidPart);
        if (part == PartDisplay.Wireframe)
        {
            var outline = new List<float>();
            foreach (Contour contour in contours)
            {
                List<Point2> points = contour.Sample(0.05);
                for (int k = 0; k + 1 < points.Count; k++)
                {
                    AddSegment(outline, new Point3(points[k].X, points[k].Y, 0), new Point3(points[k + 1].X, points[k + 1].Y, 0));
                }
            }

            if (outline.Count > 0)
            {
                lines.Add(new SceneLinesState(SceneRole.PartOutline, [.. outline], PartColor));
            }
        }

        foreach (Contour contour in contours)
        {
            (double minX, double minY, double maxX, double maxY) = contour.BoundsXY();
            bounds = bounds.Include(new Point3(minX, minY, 0)).Include(new Point3(maxX, maxY, 0));
        }

        if (path is { Moves.Count: > 0 })
        {
            (SceneLines feed, SceneLines rapid, _) = SceneLines.FromToolPath(path, partFrame: path.PartFrame);
            bounds = Include(bounds, feed.PositionArray);
            bounds = Include(bounds, rapid.PositionArray);
            if (options.Path)
            {
                lines.Add(new SceneLinesState(SceneRole.FeedPath, feed.PositionArray, palette.FeedPath));
            }

            if (options.ShowsRapids)
            {
                lines.Add(new SceneLinesState(SceneRole.RapidPath, rapid.PositionArray, palette.RapidPath));
            }
        }

        if (options.Axes)
        {
            double length = AxisLength(bounds);
            foreach ((Point3 tip, uint color) in Axes(length))
            {
                var axis = new List<float>();
                AddSegment(axis, Point3.Origin, tip);
                lines.Add(new SceneLinesState(SceneRole.Axis, [.. axis], color));
            }

            bounds = bounds.Include(Point3.Origin);
        }

        if (bounds.IsEmpty)
        {
            bounds = new Bounds3(new Point3(-10, -10, -10), new Point3(10, 10, 10));
        }

        return new SceneSnapshot(objects, lines, bounds, palette)
        {
            MaterialOpacity = options.Stock == StockDisplay.Transparent ? SimulationScene.TransparentOpacity : 1f,
        };
    }

    /// <summary>Lados do cilindro e do tubo na malha.</summary>
    public const int RoundSegments = 48;

    /// <summary>Malha do bruto: caixa, sólido do STEP, ou cilindro/tubo girado em volta do eixo (Z em pé; X ou Y deitado).</summary>
    public static SolidMesh StockMesh(StockSolid s, uint color)
    {
        // STEP: o sólido lido; enquanto não foi lido nesta sessão, a caixa do resumo
        if (s.Kind == StockSolidKind.Step && s.Mesh is { TriangleCount: > 0 } stepMesh)
        {
            return stepMesh.ToSolidMesh(color);
        }

        SolidMesh mesh = SolidMesh.Create(6);
        if (s.Kind is StockSolidKind.Box or StockSolidKind.Step)
        {
            SceneMeshes.Box(mesh, new Point3(s.MinX, s.MinY, s.MinZ), new Point3(s.SizeX, s.SizeY, s.SizeZ), color);
            return mesh;
        }

        double ro = s.Diameter / 2, ri = s.Kind == StockSolidKind.Tube ? s.InnerDiameter / 2 : 0, h = s.Length / 2;
        var center = new Point3((s.MinX + s.MaxX) / 2, (s.MinY + s.MaxY) / 2, (s.MinZ + s.MaxZ) / 2);

        // contorno no plano (raio, altura) no sentido que deixa as normais para fora (o mesmo do SceneMeshes.Revolve)
        List<(double R, double Z)> outline = ri > 0
            ? [(ri, -h), (ro, -h), (ro, h), (ri, h), (ri, -h)]
            : [(0, -h), (ro, -h), (ro, h), (0, h)];

        // eixo local (x, y, z) → mundo: permutação cíclica, que não inverte o lado das faces
        Point3 Map(double x, double y, double z) => s.Axis switch
        {
            StockAxis.X => new Point3(z, x, y),
            StockAxis.Y => new Point3(y, z, x),
            _ => new Point3(x, y, z),
        };

        double step = 2 * Math.PI / RoundSegments;
        for (int i = 0; i + 1 < outline.Count; i++)
        {
            (double r0, double z0) = outline[i];
            (double r1, double z1) = outline[i + 1];
            double nr = z1 - z0, nz = -(r1 - r0);
            for (int k = 0; k < RoundSegments; k++)
            {
                double a0 = k * step, a1 = a0 + step, mid = a0 + step / 2;
                Point3 At(double r, double z, double a) => center + Map(r * Math.Cos(a), r * Math.Sin(a), z);
                mesh.AddQuad(At(r0, z0, a0), At(r0, z0, a1), At(r1, z1, a1), At(r1, z1, a0), Map(nr * Math.Cos(mid), nr * Math.Sin(mid), nz), color);
            }
        }

        return mesh;
    }

    /// <summary>Tamanho das setas do zero: um quarto da maior medida da cena, entre 10 e 100 mm.</summary>
    public static double AxisLength(Bounds3 bounds) =>
        bounds.IsEmpty ? 20 : Math.Clamp(Math.Max(bounds.SizeX, Math.Max(bounds.SizeY, bounds.SizeZ)) / 4, 10, 100);

    /// <summary>As três setas na origem do zero atual (X, Y, Z), cada uma com a ponta e a cor.</summary>
    public static IReadOnlyList<(Point3 Tip, uint Color)> Axes(double length) =>
    [
        (new Point3(length, 0, 0), AxisX),
        (new Point3(0, length, 0), AxisY),
        (new Point3(0, 0, length), AxisZ),
    ];

    private static void AddSegment(List<float> target, Point3 a, Point3 b)
    {
        target.Add((float)a.X);
        target.Add((float)a.Y);
        target.Add((float)a.Z);
        target.Add((float)b.X);
        target.Add((float)b.Y);
        target.Add((float)b.Z);
    }

    private static Bounds3 Include(Bounds3 bounds, float[] positions)
    {
        for (int i = 0; i + 2 < positions.Length; i += 3)
        {
            bounds = bounds.Include(new Point3(positions[i], positions[i + 1], positions[i + 2]));
        }

        return bounds;
    }
}
