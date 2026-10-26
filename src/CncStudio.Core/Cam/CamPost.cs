using System.Globalization;
using System.Text;

namespace CncStudio.Core.Cam;

public enum CamDialect { Fanuc, Siemens }

/// <summary>Opções do pós-processador: dialeto, nome, zero de peça e refrigeração.</summary>
public sealed record PostOptions(CamDialect Dialect, string ProgramName = "CASILLAS CAM", int ProgramNumber = 1001, int WorkOffset = 54, bool Coolant = true);

/// <summary>
/// Pós-processador: transforma as operações do CAM (movimentos neutros) em programa Fanuc ou Siemens de fresa.
/// O texto passa pelo mesmo analisador e simulador do Code; os testes conferem o percurso simulado contra o gerado.
/// </summary>
public static class CamPost
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static string Generate(IReadOnlyList<CamOperation> operations, PostOptions options)
    {
        var writer = new Writer(options);
        writer.Header();
        foreach (CamOperation op in operations)
        {
            writer.Operation(op);
        }

        writer.Footer();
        return writer.ToString();
    }

    private sealed class Writer(PostOptions o)
    {
        private readonly StringBuilder _sb = new();
        private readonly bool _fanuc = o.Dialect == CamDialect.Fanuc;
        private int _tool = -1;
        private MoveKind? _modal;
        private double _feed = double.NaN;
        private double _x = double.NaN, _y = double.NaN, _z = double.NaN;

        public override string ToString() => _sb.ToString();

        private string N(double v)
        {
            string s = Math.Round(v, 3).ToString("0.###", Inv);
            if (s is "-0" or "")
            {
                s = "0";
            }

            return _fanuc && !s.Contains('.') ? s + "." : s;
        }

        private static string Clean(string text) => text.Replace('(', '[').Replace(')', ']').Replace(';', ',').Trim();

        private void Line(string text) => _sb.Append(text).Append('\n');

        public void Header()
        {
            string name = Clean(o.ProgramName);
            if (_fanuc)
            {
                Line("%");
                Line($"O{o.ProgramNumber:0000} ({name})");
                Line("(GERADO PELO CASILLAS CAM - SIMULE ANTES DE RODAR NA MAQUINA)");
                Line("G21 G17 G40 G49 G80 G90 G94");
            }
            else
            {
                Line($"; {name}");
                Line("; GERADO PELO CASILLAS CAM - SIMULE ANTES DE RODAR NA MAQUINA");
                Line("G71 G17 G40 G90 G94");
            }

            Line($"G{o.WorkOffset}");
        }

        public void Footer()
        {
            if (o.Coolant)
            {
                Line("M9");
            }

            Line("M5");
            Line(_fanuc ? "G91 G28 Z0" : "G0 G53 Z0");
            Line("G90");
            Line("M30");
            if (_fanuc)
            {
                Line("%");
            }
        }

        private void ChangeTool(CamTool tool, double safeZ)
        {
            if (tool.Number == _tool)
            {
                return;
            }

            if (_tool >= 0)
            {
                if (o.Coolant)
                {
                    Line("M9");
                }

                Line("M5");
            }

            Line(_fanuc ? "G91 G28 Z0" : "G0 G53 Z0");
            Line("G90");
            if (_fanuc)
            {
                Line($"T{tool.Number} M6");
            }
            else
            {
                Line($"T{tool.Number}");
                Line("M6");
                Line($"D{tool.Number}");
            }

            Line($"S{(int)Math.Round(tool.SpindleRpm)} M3");
            _tool = tool.Number;
            _modal = null;
            _feed = double.NaN;
            _x = _y = _z = double.NaN;
            _ = safeZ;
        }

        private void LengthOffset(CamTool tool, Point2 p, double safeZ)
        {
            Line($"G0 X{N(p.X)} Y{N(p.Y)}");
            Line(_fanuc ? $"G43 H{tool.Number} Z{N(safeZ)}" : $"G0 Z{N(safeZ)}");
            if (o.Coolant)
            {
                Line("M8");
            }

            _modal = MoveKind.Rapid;
            _x = Math.Round(p.X, 3);
            _y = Math.Round(p.Y, 3);
            _z = Math.Round(safeZ, 3);
        }

        public void Operation(CamOperation op)
        {
            Line(_fanuc ? $"({Clean(op.Name)})" : $"; {Clean(op.Name)}");
            if (op.Drill is { } drill)
            {
                Drilling(op.Tool, drill);
                return;
            }

            bool newTool = op.Tool.Number != _tool;
            ChangeTool(op.Tool, op.Moves.Count > 0 ? op.Moves[0].Z : 0);
            for (int i = 0; i < op.Moves.Count; i++)
            {
                ToolMove m = op.Moves[i];
                if (i == 0 && newTool && m.Kind == MoveKind.Rapid)
                {
                    LengthOffset(op.Tool, m.XY, m.Z);
                    continue;
                }

                Move(m, op.Tool);
            }
        }

        private void Move(ToolMove m, CamTool tool)
        {
            double x = Math.Round(m.X, 3), y = Math.Round(m.Y, 3), z = Math.Round(m.Z, 3);
            var sb = new StringBuilder();
            bool arc = m.IsArc;
            if (m.Kind != _modal || arc)
            {
                sb.Append(m.Kind switch
                {
                    MoveKind.Rapid => "G0",
                    MoveKind.Feed => "G1",
                    MoveKind.ArcCw => "G2",
                    _ => "G3",
                }).Append(' ');
            }

            if (m.Comp is { } comp)
            {
                string code = comp switch { CutterComp.Left => "G41", CutterComp.Right => "G42", _ => "G40" };
                sb.Append(code).Append(' ');
                if (_fanuc && comp != CutterComp.Off)
                {
                    sb.Append('D').Append(tool.Number).Append(' ');
                }
            }

            bool any = false;
            if (arc || Math.Abs(x - _x) > 1e-9 || double.IsNaN(_x))
            {
                sb.Append('X').Append(N(x)).Append(' ');
                any = true;
            }

            if (arc || Math.Abs(y - _y) > 1e-9 || double.IsNaN(_y))
            {
                sb.Append('Y').Append(N(y)).Append(' ');
                any = true;
            }

            if (Math.Abs(z - _z) > 1e-9 || double.IsNaN(_z))
            {
                sb.Append('Z').Append(N(z)).Append(' ');
                any = true;
            }

            if (arc)
            {
                sb.Append('I').Append(N(m.Center.X - _x)).Append(' ').Append('J').Append(N(m.Center.Y - _y)).Append(' ');
            }

            if (m.Kind != MoveKind.Rapid && (double.IsNaN(_feed) || Math.Abs(m.Feed - _feed) > 1e-9))
            {
                sb.Append('F').Append(N(m.Feed)).Append(' ');
                _feed = m.Feed;
            }

            if (any || m.Comp is not null)
            {
                Line(sb.ToString().TrimEnd());
            }

            _modal = m.Kind;
            _x = x;
            _y = y;
            _z = z;
        }

        private void Drilling(CamTool tool, DrillCycle d)
        {
            ChangeTool(tool, d.SafeZ);
            Point2 first = d.Points[0];
            LengthOffset(tool, first, d.SafeZ);
            if (_fanuc)
            {
                string cycle = d.Kind switch
                {
                    DrillCycleKind.Drill => "G81",
                    DrillCycleKind.Dwell => $"G82 P{(int)Math.Round(d.DwellSeconds * 1000)}",
                    DrillCycleKind.Peck => $"G83 Q{N(d.Peck)}",
                    _ => $"G73 Q{N(d.Peck)}",
                };
                Line($"G98 {cycle} X{N(first.X)} Y{N(first.Y)} Z{N(d.BottomZ)} R{N(d.RetractZ)} F{N(d.Feed)}");
                foreach (Point2 p in d.Points.Skip(1))
                {
                    Line($"X{N(p.X)} Y{N(p.Y)}");
                }

                Line("G80");
            }
            else
            {
                double sdis = d.RetractZ - d.TopZ;
                string args = $"{N(d.SafeZ)},{N(d.TopZ)},{N(sdis)},{N(d.BottomZ)},0";
                string cycle = d.Kind switch
                {
                    DrillCycleKind.Drill => $"CYCLE81({args})",
                    DrillCycleKind.Dwell => $"CYCLE82({args},{N(d.DwellSeconds)})",
                    DrillCycleKind.Peck => $"CYCLE83({args},{N(d.TopZ - d.Peck)},0,0,0,0,1,1)",
                    _ => $"CYCLE83({args},{N(d.TopZ - d.Peck)},0,0,0,0,1,0)",
                };
                Line($"F{N(d.Feed)}");
                Line($"MCALL {cycle}");
                foreach (Point2 p in d.Points)
                {
                    Line($"X{N(p.X)} Y{N(p.Y)}");
                }

                Line("MCALL");
            }

            _modal = null;
            _x = _y = _z = double.NaN;
            _feed = double.NaN;
        }
    }
}
