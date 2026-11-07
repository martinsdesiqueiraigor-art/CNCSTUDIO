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

    /// <summary>Monta a cena. "hasSolidPart" = o Trabalho tem a peça em 3D (hoje nunca: só contornos 2D).</summary>
    public static SceneSnapshot Build(JobStock? stock, IReadOnlyList<Contour> contours, ToolPath? path, CamViewOptions options, bool hasSolidPart = false)
    {
        ScenePalette palette = ScenePalette.Realistic;
        var objects = new List<SceneObjectState>();
        var lines = new List<SceneLinesState>();
        Bounds3 bounds = Bounds3.Empty;

        if (stock is { Width: > 0, Length: > 0, Thickness: > 0 } s)
        {
            // o topo do bruto fica em Z 0 (como no JobStock)
            var min = new Point3(s.X, s.Y, -s.Thickness);
            var size = new Point3(s.Width, s.Length, s.Thickness);
            bounds = bounds.Include(min).Include(new Point3(s.X + s.Width, s.Y + s.Length, 0));
            if (options.Stock != StockDisplay.Hidden)
            {
                SolidMesh mesh = SolidMesh.Create(6);
                SceneMeshes.Box(mesh, min, size, palette.Stock);
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
