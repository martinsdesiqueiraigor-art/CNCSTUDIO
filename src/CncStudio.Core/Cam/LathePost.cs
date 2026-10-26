using System.Globalization;
using System.Text;

namespace CncStudio.Core.Cam;

/// <summary>Opções do pós de torno: dialeto, nome, número do programa, zero de peça e refrigeração.</summary>
public sealed record LathePostOptions(CamDialect Dialect, string ProgramName = "CASILLAS CAM TORNO", int ProgramNumber = 1002, int WorkOffset = 54, bool Coolant = true);

/// <summary>
/// Pós-processador de torno (Fanuc e Siemens): faceamento e furação em movimentos simples; desbaste e acabamento de perfil
/// com G71/G70 (Fanuc) ou CYCLE95 (Siemens). X é diâmetro; avanço em mm/rot (G99/G95); G96 com limite de rotação.
/// </summary>
public static class LathePost
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Generate(IReadOnlyList<LatheOperation> operations, LathePostOptions o)
    {
        bool fanuc = o.Dialect == CamDialect.Fanuc;
        var sb = new StringBuilder();
        var contours = new StringBuilder(); // Siemens: contornos depois do M30
        int tool = -1;
        string lastSpindle = "";
        int cycleCount = 0;
        double lastSafeZ = 0, lastSafeR = 0;

        string N(double v)
        {
            string s = Math.Round(v, 3).ToString("0.###", Inv);
            if (s is "-0" or "")
            {
                s = "0";
            }

            return fanuc && !s.Contains('.') ? s + "." : s;
        }

        string Clean(string t) => t.Replace('(', '[').Replace(')', ']').Replace(';', ',').Trim();
        void Line(string t) => sb.Append(t).Append('\n');
        string Comment(string t) => fanuc ? $"({Clean(t)})" : $"; {Clean(t)}";
        string Rapid(double z, double r) => $"{(fanuc ? "G00" : "G0")} X{N(2 * r)} Z{N(z)}";

        if (fanuc)
        {
            Line("%");
            Line($"O{o.ProgramNumber:0000} ({Clean(o.ProgramName)})");
            Line("(GERADO PELO CASILLAS CAM - SIMULE ANTES DE RODAR NA MAQUINA)");
            Line("G21 G18 G40 G97 G99");
        }
        else
        {
            Line($"; {Clean(o.ProgramName)}");
            Line("; GERADO PELO CASILLAS CAM - SIMULE ANTES DE RODAR NA MAQUINA");
            Line("G71 G18 G40 G90 G95 DIAMON");
        }

        Line($"G{o.WorkOffset}");

        foreach (LatheOperation op in operations)
        {
            Line(Comment(op.Name));
            string spindle = op.Tool.Rpm > 0 ? $"RPM{(int)Math.Round(op.Tool.Rpm)}" : "CSS";
            if (op.Tool.Number != tool)
            {
                if (tool >= 0)
                {
                    if (o.Coolant)
                    {
                        Line("M9");
                    }

                    Line("M5");
                }

                Line(Rapid(op.SafeZ, op.SafeR));
                string label = op.Tool.Name.Length > 0 ? Clean(op.Tool.Name) : $"FERRAMENTA {op.Tool.Number}";
                Line(fanuc ? $"T{op.Tool.Number:00}{op.Tool.Number:00} ({label})" : $"T{op.Tool.Number} D1 ; {label}");
                Spindle(op);
                if (o.Coolant)
                {
                    Line("M8");
                }

                tool = op.Tool.Number;
                lastSpindle = spindle;
            }
            else if (spindle != lastSpindle)
            {
                Spindle(op);
                lastSpindle = spindle;
            }

            lastSafeZ = op.SafeZ;
            lastSafeR = op.SafeR;
            if (op.Thread is { } thread)
            {
                Thread(op, thread);
            }
            else if (op.Cycle is { } cycle)
            {
                cycleCount++;
                Cycle(op, cycle, cycleCount);
            }
            else
            {
                Moves(op);
            }
        }

        if (o.Coolant)
        {
            Line("M9");
        }

        Line("M5");
        Line(Rapid(lastSafeZ, lastSafeR));
        Line("M30");
        if (fanuc)
        {
            Line("%");
        }
        else
        {
            sb.Append(contours);
        }

        return sb.ToString();

        void Spindle(LatheOperation op)
        {
            int max = (int)Math.Round(op.Tool.MaxRpm);
            if (op.Tool.Rpm > 0)
            {
                Line($"G97 S{(int)Math.Round(op.Tool.Rpm)} M3");
            }
            else if (fanuc)
            {
                Line($"G50 S{max}");
                Line($"G96 S{(int)Math.Round(op.Tool.SurfaceSpeed)} M3");
            }
            else
            {
                Line($"G96 S{(int)Math.Round(op.Tool.SurfaceSpeed)} M3 LIMS={max}");
            }
        }

        void Thread(LatheOperation op, LatheThread t)
        {
            double zIn = t.StartZ + t.Approach, zOut = t.EndZ - t.Overrun;
            Line(Rapid(zIn, t.StartRadius));
            if (fanuc)
            {
                string Mic(double v) => Math.Round(v * 1000).ToString("0", Inv);
                Line($"G76 P{t.FinishPasses:00}0060 Q{Mic(t.MinDepth)} R{N(t.Allowance)}");
                Line($"G76 X{N(2 * t.RootRadius)} Z{N(zOut)} P{Mic(t.Height)} Q{Mic(t.FirstDepth)} F{N(t.Pitch)}");
            }
            else
            {
                // Passes de desbaste = profundidades antes do sobremetal e do acabamento
                int rough = Math.Max(1, t.Depths().Count - t.FinishPasses - (t.Allowance > 0 ? 1 : 0));
                int idle = t.Allowance > 0 ? t.FinishPasses - 1 : t.FinishPasses;
                Line($"CYCLE97({N(t.Pitch)},,{N(t.StartZ)},{N(t.EndZ)},{N(2 * t.CrestRadius)},{N(2 * t.CrestRadius)},{N(t.Approach)},{N(t.Overrun)},{N(t.Height)},{N(t.Allowance)},29,0,{rough},{Math.Max(0, idle)},{(t.Internal ? 4 : 3)},1,{N(t.Clearance)})");
            }

            Line(Rapid(op.SafeZ, op.SafeR));
        }

        void Moves(LatheOperation op)
        {
            double feed = double.NaN;
            foreach (LatheMove m in op.Moves)
            {
                if (m.Kind == MoveKind.Rapid)
                {
                    Line(Rapid(m.Z, m.R));
                    continue;
                }

                string f = double.IsNaN(feed) || Math.Abs(m.Feed - feed) > 1e-9 ? $" F{N(m.Feed)}" : "";
                feed = m.Feed;
                Line($"G1 X{N(2 * m.R)} Z{N(m.Z)}{f}");
            }
        }

        List<string> ProfileBlocks(LatheCycle c, bool approachBlock)
        {
            var blocks = new List<string>();
            Point2 p0 = c.Profile.Start;
            if (fanuc)
            {
                if (approachBlock && c.HasUndercut)
                {
                    // G71 tipo II: o 1º bloco leva X e Z; o avanço do acabamento vem no primeiro corte
                    blocks.Add($"G00 X{N(2 * p0.Y)} Z{N(p0.X)}");
                    blocks.Add($"G01 Z{N(p0.X)} F{N(c.FinishFeed)}");
                }
                else if (approachBlock)
                {
                    blocks.Add($"G00 X{N(2 * p0.Y)}");
                    blocks.Add($"G01 Z{N(p0.X)} F{N(c.FinishFeed)}");
                }
            }
            else
            {
                blocks.Add($"G1 X{N(2 * p0.Y)} Z{N(p0.X)} F{N(c.FinishFeed)}");
            }

            foreach (Segment2 s in c.Profile.Segments)
            {
                if (s.IsArc)
                {
                    // Cartas com Z para a direita e raio para cima: anti-horário = G03
                    blocks.Add($"{(s.Ccw ? "G03" : "G02")} X{N(2 * s.End.Y)} Z{N(s.End.X)} I{N(s.Center.Y - s.Start.Y)} K{N(s.Center.X - s.Start.X)}");
                }
                else
                {
                    blocks.Add($"G01 X{N(2 * s.End.Y)} Z{N(s.End.X)}");
                }
            }

            blocks.Add($"G01 X{N(2 * c.StartR)}"); // sai do material
            return blocks;
        }

        void Cycle(LatheOperation op, LatheCycle c, int index)
        {
            Line(Rapid(c.StartZ, c.StartR));
            if (!c.Rough)
            {
                // só acabamento: o perfil em movimentos simples
                foreach (string b in ProfileBlocks(c, approachBlock: true))
                {
                    Line(b);
                }

                Line(Rapid(op.SafeZ, op.SafeR));
                return;
            }

            if (fanuc)
            {
                int ns = index * 100, nf = ns;
                List<string> blocks = ProfileBlocks(c, approachBlock: true);
                nf = ns + blocks.Count - 1;
                double allowX = (c.Internal ? -2 : 2) * c.AllowanceR;
                Line($"G71 U{N(c.DepthOfCut)} R{N(c.Retract)}");
                Line($"G71 P{ns} Q{nf} U{N(allowX)} W{N(c.AllowanceZ)} F{N(c.RoughFeed)}");
                for (int i = 0; i < blocks.Count; i++)
                {
                    Line($"N{ns + i} {blocks[i]}");
                }

                if (c.Finish)
                {
                    Line($"G70 P{ns} Q{nf}");
                }
            }
            else
            {
                string start = $"CONT{index}", end = $"FIM{index}";
                int vari = c.Finish ? (c.Internal ? 11 : 9) : (c.Internal ? 3 : 1);
                Line($"CYCLE95(\"{start}:{end}\",{N(c.DepthOfCut)},{N(c.AllowanceZ)},{N(c.AllowanceR)},,{N(c.RoughFeed)},{N(c.FinishFeed)},{N(c.FinishFeed)},{vari},,,{N(c.Retract)})");
                contours.Append($"{start}:\n");
                foreach (string b in ProfileBlocks(c, approachBlock: true))
                {
                    contours.Append(b).Append('\n');
                }

                contours.Append($"{end}:\n");
            }

            Line(Rapid(op.SafeZ, op.SafeR));
        }
    }
}
