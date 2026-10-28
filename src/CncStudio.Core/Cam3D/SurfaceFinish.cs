using CncStudio.Core.Cam;
using CncStudio.Core.Geometry;

namespace CncStudio.Core.Cam3D;

/// <summary>Parâmetros da varredura: passo lateral, tolerância de linha e distância entre amostras (mm).</summary>
public sealed record SurfaceParams(double StepOver, double Tolerance = 0.02, double SampleStep = 0.5);

/// <summary>
/// Acabamento 3 eixos por varredura (raster ao longo de X, em zigue-zague) com fresa de ponta esférica
/// sobre o modelo (STL). Onde o modelo não existe, a ferramenta não corta. Só o corpo esférico é
/// conferido (haste e porta-ferramenta, não), e não há promessa de colisão: simule antes.
/// </summary>
public static class SurfaceFinish
{
    public static OperationResult Raster(TriangleMesh model, CamTool tool, CamLevels levels, SurfaceParams p)
    {
        string? error = tool.Validate();
        if (error is not null)
        {
            return new OperationResult(null, error);
        }

        if (model.TriangleCount == 0)
        {
            return new OperationResult(null, "O modelo não tem triângulos.");
        }

        if (p.StepOver <= 0 || p.StepOver > tool.Diameter)
        {
            return new OperationResult(null, "O passo lateral precisa ser maior que zero e no máximo o diâmetro da ferramenta.");
        }

        if (p.Tolerance <= 0 || p.SampleStep <= 0)
        {
            return new OperationResult(null, "Tolerância e distância entre amostras precisam ser maiores que zero.");
        }

        if (levels.ClearanceZ < model.Bounds.Max.Z || levels.SafeZ < levels.ClearanceZ)
        {
            return new OperationResult(null, "As alturas de aproximação e segura precisam ficar acima do ponto mais alto do modelo.");
        }

        var warnings = new List<string>();
        double r = tool.Radius;
        double cusp = r - Math.Sqrt((r * r) - (p.StepOver * p.StepOver / 4));
        warnings.Add($"Fresa esférica D{tool.Diameter:0.##}: cúspide teórica de {cusp:0.###} mm em superfície plana com passo de {p.StepOver:0.##} mm.");
        if (!model.IsClosed)
        {
            warnings.Add("O modelo tem malha aberta: confira o percurso na simulação.");
        }

        var cutter = new DropCutter(model, r);
        Bounds3 b = model.Bounds;
        int lines = (int)Math.Ceiling(((b.Max.Y - b.Min.Y) / p.StepOver) - 1e-9) + 1;
        int samples = (int)Math.Ceiling(((b.Max.X - b.Min.X) / p.SampleStep) - 1e-9) + 1;
        double floor = levels.BottomZ;
        var moves = new List<ToolMove>();
        bool leftToRight = true;
        bool first = true;

        for (int line = 0; line < lines; line++)
        {
            double y = lines == 1 ? b.Min.Y : b.Min.Y + ((b.Max.Y - b.Min.Y) * line / (lines - 1));
            // Amostras da linha (null = fora do modelo)
            var pts = new List<(double X, double? Z)>(samples);
            for (int i = 0; i < samples; i++)
            {
                double x = samples == 1 ? b.Min.X : b.Min.X + ((b.Max.X - b.Min.X) * i / (samples - 1));
                double? z = cutter.TipHeight(x, y);
                pts.Add((x, z is { } v ? Math.Max(v, floor) : null));
            }

            if (!leftToRight)
            {
                pts.Reverse();
            }

            // Trechos contínuos de amostras com modelo
            int start = 0;
            while (start < pts.Count)
            {
                if (pts[start].Z is null)
                {
                    start++;
                    continue;
                }

                int end = start;
                while (end + 1 < pts.Count && pts[end + 1].Z is not null)
                {
                    end++;
                }

                List<(double X, double Z)> run = Simplify([.. pts.Skip(start).Take(end - start + 1).Select(q => (q.X, q.Z!.Value))], p.Tolerance);
                if (!first)
                {
                    moves.Add(new ToolMove(MoveKind.Rapid, moves[^1].X, moves[^1].Y, levels.ClearanceZ));
                }

                moves.Add(new ToolMove(MoveKind.Rapid, run[0].X, y, first ? levels.SafeZ : levels.ClearanceZ));
                moves.Add(new ToolMove(MoveKind.Rapid, run[0].X, y, levels.ClearanceZ));
                moves.Add(new ToolMove(MoveKind.Feed, run[0].X, y, run[0].Z, Feed: tool.FeedZ));
                foreach ((double x, double z) in run.Skip(1))
                {
                    moves.Add(new ToolMove(MoveKind.Feed, x, y, z, Feed: tool.FeedXY));
                }

                first = false;
                start = end + 1;
            }

            leftToRight = !leftToRight;
        }

        if (moves.Count == 0)
        {
            return new OperationResult(null, "Nenhuma parte do modelo ficou ao alcance da ferramenta.");
        }

        moves.Add(new ToolMove(MoveKind.Rapid, moves[^1].X, moves[^1].Y, levels.SafeZ));
        return new OperationResult(new CamOperation($"VARREDURA 3D BOLA D{tool.Diameter:0.##}", tool, moves, null, warnings), null);
    }

    /// <summary>Tira pontos que ficam a menos de <paramref name="tolerance"/> da reta entre os vizinhos mantidos (Douglas-Peucker no plano X–Z).</summary>
    private static List<(double X, double Z)> Simplify(List<(double X, double Z)> pts, double tolerance)
    {
        if (pts.Count <= 2)
        {
            return pts;
        }

        var keep = new bool[pts.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, pts.Count - 1));
        while (stack.Count > 0)
        {
            (int lo, int hi) = stack.Pop();
            double worst = 0;
            int at = -1;
            for (int i = lo + 1; i < hi; i++)
            {
                double t = (pts[i].X - pts[lo].X) / (pts[hi].X - pts[lo].X);
                double dz = Math.Abs(pts[i].Z - (pts[lo].Z + (t * (pts[hi].Z - pts[lo].Z))));
                if (dz > worst)
                {
                    worst = dz;
                    at = i;
                }
            }

            if (worst > tolerance && at > 0)
            {
                keep[at] = true;
                stack.Push((lo, at));
                stack.Push((at, hi));
            }
        }

        return [.. pts.Where((_, i) => keep[i])];
    }
}
