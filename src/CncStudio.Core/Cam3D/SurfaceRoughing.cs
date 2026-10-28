using CncStudio.Core.Cam;
using CncStudio.Core.Geometry;

namespace CncStudio.Core.Cam3D;

/// <summary>
/// Parâmetros do desbaste: passo lateral, sobra axial (em Z) e radial (nas paredes) deixadas para o acabamento,
/// e distância entre amostras (mm). O passo em Z vem de <see cref="CamLevels.StepDown"/>.
/// </summary>
public sealed record RoughParams(double StepOver, double AxialStock = 0.3, double RadialStock = 0.3, double SampleStep = 0.5);

/// <summary>
/// Desbaste 3D por níveis (planos de Z constante) com fresa de ponta esférica sobre o modelo (STL).
/// O bruto é o prisma do tamanho do modelo no plano XY, do topo (<see cref="CamLevels.TopZ"/>) ao fundo.
/// Em cada nível a ferramenta corta linhas ao longo de X (zigue-zague) só onde o modelo, mais a sobra,
/// fica abaixo do nível. A sobra radial é aproximada engrossando a esfera de conferência; a axial sobe a
/// ponta. Só o corpo esférico é conferido (haste e porta-ferramenta, não), e não há promessa de colisão.
/// </summary>
public static class SurfaceRoughing
{
    public static OperationResult ByLevels(TriangleMesh model, CamTool tool, CamLevels levels, RoughParams p)
    {
        string? error = tool.Validate() ?? levels.Validate();
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

        if (p.AxialStock < 0 || p.RadialStock < 0)
        {
            return new OperationResult(null, "As sobras axial e radial não podem ser negativas.");
        }

        if (p.SampleStep <= 0)
        {
            return new OperationResult(null, "A distância entre amostras precisa ser maior que zero.");
        }

        if (levels.TopZ < model.Bounds.Max.Z)
        {
            return new OperationResult(null, "O topo do bruto precisa ficar na altura do ponto mais alto do modelo ou acima.");
        }

        var warnings = new List<string>
        {
            $"Desbaste por níveis com fresa esférica D{tool.Diameter:0.##}: sobra de {p.RadialStock:0.##} mm nas paredes e {p.AxialStock:0.##} mm no fundo para o acabamento.",
            "Bruto = prisma do tamanho do modelo em XY, do topo ao fundo; material fora dele não é visto.",
        };
        if (!model.IsClosed)
        {
            warnings.Add("O modelo tem malha aberta: confira o percurso na simulação.");
        }

        // Altura mínima da ponta (grade de amostras), calculada uma vez e usada em todos os níveis.
        var cutter = new DropCutter(model, tool.Radius + p.RadialStock);
        Bounds3 b = model.Bounds;
        double floor = levels.BottomZ;
        int lines = (int)Math.Ceiling(((b.Max.Y - b.Min.Y) / p.StepOver) - 1e-9) + 1;
        int samples = (int)Math.Ceiling(((b.Max.X - b.Min.X) / p.SampleStep) - 1e-9) + 1;
        var xs = new double[samples];
        for (int i = 0; i < samples; i++)
        {
            xs[i] = samples == 1 ? b.Min.X : b.Min.X + ((b.Max.X - b.Min.X) * i / (samples - 1));
        }

        var ys = new double[lines];
        var height = new double[lines][];
        for (int line = 0; line < lines; line++)
        {
            ys[line] = lines == 1 ? b.Min.Y : b.Min.Y + ((b.Max.Y - b.Min.Y) * line / (lines - 1));
            height[line] = new double[samples];
            for (int i = 0; i < samples; i++)
            {
                double? z = cutter.TipHeight(xs[i], ys[line]);
                height[line][i] = z is { } v ? Math.Max(v + p.AxialStock, floor) : floor;
            }
        }

        var moves = new List<ToolMove>();
        bool first = true;
        bool leftToRight = true;
        int levelCount = 0;
        foreach (double level in levels.Depths())
        {
            bool any = false;
            for (int line = 0; line < lines; line++)
            {
                int[] order = leftToRight ? Enumerable.Range(0, samples).ToArray() : Enumerable.Range(0, samples).Reverse().ToArray();
                int pos = 0;
                while (pos < order.Length)
                {
                    // Trecho contínuo onde a ponta pode estar neste nível (modelo + sobra abaixo do nível)
                    if (height[line][order[pos]] > level + 1e-9)
                    {
                        pos++;
                        continue;
                    }

                    int end = pos;
                    while (end + 1 < order.Length && height[line][order[end + 1]] <= level + 1e-9)
                    {
                        end++;
                    }

                    double x0 = xs[order[pos]], x1 = xs[order[end]], y = ys[line];
                    if (!first)
                    {
                        moves.Add(new ToolMove(MoveKind.Rapid, moves[^1].X, moves[^1].Y, levels.ClearanceZ));
                    }

                    moves.Add(new ToolMove(MoveKind.Rapid, x0, y, first ? levels.SafeZ : levels.ClearanceZ));
                    moves.Add(new ToolMove(MoveKind.Rapid, x0, y, levels.ClearanceZ));
                    moves.Add(new ToolMove(MoveKind.Feed, x0, y, level, Feed: tool.FeedZ));
                    if (end > pos)
                    {
                        moves.Add(new ToolMove(MoveKind.Feed, x1, y, level, Feed: tool.FeedXY));
                    }

                    first = false;
                    any = true;
                    pos = end + 1;
                }

                leftToRight = !leftToRight;
            }

            if (any)
            {
                levelCount++;
            }
        }

        if (moves.Count == 0)
        {
            return new OperationResult(null, "Nenhuma parte do bruto precisa ser removida (o modelo ocupa o bruto inteiro ou está fora do alcance).");
        }

        moves.Add(new ToolMove(MoveKind.Rapid, moves[^1].X, moves[^1].Y, levels.SafeZ));
        warnings.Add($"{levelCount} níveis de corte, passo em Z até {levels.StepDown:0.##} mm.");
        warnings.Add("A descida de cada trecho é vertical sobre material já retirado nos níveis acima; mantenha o avanço de descida baixo.");
        return new OperationResult(new CamOperation($"DESBASTE 3D BOLA D{tool.Diameter:0.##}", tool, moves, null, warnings), null);
    }
}
