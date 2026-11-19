using System.Globalization;
using CncStudio.Core.Cam;
using CncStudio.Core.Cam3D;
using CncStudio.Core.Geometry;

namespace CncStudio.Core.Assist;

/// <summary>Peça STEP lida como "desenho" do Assist: contornos 2D no plano XY, profundidades reais e avisos do que ficou de fora.</summary>
public sealed record StepPlanDrawing(IReadOnlyList<Contour> Contours, double Thickness, IReadOnlyDictionary<int, double> Depths, IReadOnlyList<string> Notes)
{
    public bool Ok => Contours.Count > 0;
}

/// <summary>
/// STEP-OPS S4: o Assist faz com a peça STEP o que faz com o DXF. Lê da malha (<see cref="MeshFeatures"/>) o contorno externo, os furos
/// verticais e os rebaixos (fundos planos abaixo do topo, com as ilhas deles) e entrega no mesmo formato do desenho, mais a profundidade
/// real de cada um. Fica de fora, com aviso: furo inclinado, superfícies curvas, faces voltadas para baixo e degraus na borda.
/// Só lê o que a malha mostra; não verifica a peça.
/// </summary>
public static class StepPlanSource
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");

    private static string N(double v) => v.ToString("0.##", Br);

    private const double Tolerance = 0.05;

    /// <param name="features">Faces e arestas da peça já posicionada (topo em Z do bruto).</param>
    /// <param name="partBox">Caixa da peça posicionada: o topo dela é o Z0 do plano.</param>
    public static StepPlanDrawing FromMesh(MeshFeatures features, Bounds3 partBox)
    {
        var notes = new List<string>();
        var contours = new List<Contour>();
        var depths = new Dictionary<int, double>();
        double top = partBox.Max.Z;
        double thickness = partBox.Max.Z - partBox.Min.Z;

        List<MeshFace> up = [.. features.Faces.Where(f => f.Kind == FaceKind.Plane && f.Normal.Z >= 0.99)];
        if (up.Count == 0 || thickness <= 0)
        {
            return new StepPlanDrawing([], thickness, depths, ["O STEP não tem face plana voltada para cima: nada para planejar."]);
        }

        // contorno externo = o maior laço de fora entre as faces para cima (o contorno da peça visto de cima)
        IReadOnlyList<Point3>? outer = null;
        double best = -1;
        foreach (MeshFace f in up)
        {
            IReadOnlyList<IReadOnlyList<Point3>> loops = features.BoundaryLoops(f);
            if (loops.Count == 0)
            {
                continue;
            }

            (IReadOnlyList<Point3> o, _) = CamJobFromGeometry.SplitLoops(loops);
            double a = Math.Abs(CamJobPart.Polygon(o).SignedArea);
            if (a > best)
            {
                best = a;
                outer = o;
            }
        }

        if (outer is null)
        {
            return new StepPlanDrawing([], thickness, depths, ["Não achei o contorno externo da peça (malha aberta?)."]);
        }

        Contour outline = CamJobPart.Polygon(outer);
        contours.Add(outline);
        (double ox0, double oy0, double ox1, double oy1) = outline.BoundsXY();

        // furos verticais (Ø e profundidade reais)
        int inclined = 0, down = 0, curved = 0;
        foreach (MeshFace f in features.Faces)
        {
            switch (f.Kind)
            {
                case FaceKind.Hole when f.Axis is { } a && Math.Abs(a.Z) >= 0.99:
                    contours.Add(CamJobPart.Circle(new Point2(f.Center.X, f.Center.Y), f.Diameter / 2));
                    depths[contours.Count - 1] = Math.Round(top - f.Box.Min.Z, 3);
                    break;
                case FaceKind.Hole:
                    inclined++;
                    break;
                case FaceKind.Plane when f.Normal.Z <= -0.99:
                    if (f.Center.Z > partBox.Min.Z + Tolerance)
                    {
                        down++; // o fundo da peça (base sobre a mesa) não conta
                    }

                    break;
                case FaceKind.ConcaveCurve or FaceKind.ConvexCurve or FaceKind.Free:
                    curved++;
                    break;
            }
        }

        // rebaixos: fundos planos para cima abaixo do topo; o laço de fora é o bolsão e os de dentro viram as ilhas
        int steps = 0;
        foreach (MeshFace f in up.Where(f => f.Center.Z < top - Tolerance).OrderByDescending(f => f.Area))
        {
            IReadOnlyList<IReadOnlyList<Point3>> loops = features.BoundaryLoops(f);
            if (loops.Count == 0)
            {
                continue;
            }

            (IReadOnlyList<Point3> o, IReadOnlyList<IReadOnlyList<Point3>> inner) = CamJobFromGeometry.SplitLoops(loops);
            Contour pocket = CamJobPart.Polygon(o);
            (double x0, double y0, double x1, double y1) = pocket.BoundsXY();
            if (x0 <= ox0 + Tolerance || y0 <= oy0 + Tolerance || x1 >= ox1 - Tolerance || y1 >= oy1 - Tolerance)
            {
                steps++; // toca a borda da peça: é degrau, não bolsão fechado
                continue;
            }

            contours.Add(pocket);
            depths[contours.Count - 1] = Math.Round(top - f.Center.Z, 3);
            contours.AddRange(inner.Select(CamJobPart.Polygon));
        }

        if (steps > 0)
        {
            notes.Add($"{steps} degrau(s) na borda da peça não entram no plano (só rebaixos fechados). Use o Trabalho para eles.");
        }

        if (inclined > 0)
        {
            notes.Add($"{inclined} furo(s) inclinado(s) ficaram de fora: furação em 3 eixos só vertical.");
        }

        if (down > 0)
        {
            notes.Add($"{down} face(s) voltada(s) para baixo ficaram de fora: a fresa só chega por cima (3 eixos).");
        }

        if (curved > 0)
        {
            notes.Add($"{curved} superfície(s) curva(s) ficaram de fora: o plano 2D não faz raios nem acabamento 3D (use a varredura do Modelo 3D).");
        }

        notes.Add($"Lido do STEP: peça {N(ox1 - ox0)} × {N(oy1 - oy0)} mm, espessura {N(thickness)} mm, topo em Z0. Profundidades das faces do modelo.");
        return new StepPlanDrawing(contours, Math.Round(thickness, 3), depths, notes);
    }
}
