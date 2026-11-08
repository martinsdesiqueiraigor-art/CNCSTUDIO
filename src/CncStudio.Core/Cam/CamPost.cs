using System.Globalization;
using System.Text;

namespace CncStudio.Core.Cam;

public enum CamDialect { Fanuc, Siemens }

/// <summary>Opções do pós-processador: dialeto, nome, zero de peça e refrigeração.</summary>
/// <summary>
/// Opções do pós. <see cref="Machine"/>: perfil da máquina, usado para conferir se o eixo rotativo pode voltar a 0° no fim
/// (null = não informado: o eixo não volta e o programa avisa).
/// </summary>
public sealed record PostOptions(CamDialect Dialect, string ProgramName = "CASILLAS CAM", int ProgramNumber = 1001, int WorkOffset = 54, bool Coolant = true, MachineProfile? Machine = null)
{
    /// <summary>Linha de comentário do zero da peça no cabeçalho (UX-04g PR B); null = nenhuma linha (como antes).</summary>
    public string? ZeroNote { get; init; }
}

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

        writer.Footer(RotaryAtEnd(operations));
        return writer.ToString();
    }

    /// <summary>
    /// Eixos rotativos que o programa deixa fora de 0° no fim (último ângulo de cada letra), na ordem do primeiro uso.
    /// Programa sem eixo rotativo, ou que já termina em 0°, devolve lista vazia.
    /// </summary>
    public static IReadOnlyList<(string Letter, double Angle)> RotaryAtEnd(IReadOnlyList<CamOperation> operations)
    {
        var last = new Dictionary<string, double>();
        foreach (CamOperation op in operations)
        {
            // 3+2 na furação: o segundo eixo (giratório) gira antes do primeiro, e também precisa voltar a zero
            if (op.Drill is { RotaryLetter2: { } drillLetter2 } d2)
            {
                last[drillLetter2] = Math.Round(d2.RotaryAngle2, 3);
            }

            if (op.Drill is { RotaryLetter: { } drillLetter } d)
            {
                last[drillLetter] = Math.Round(d.RotaryAngle, 3);
            }

            foreach (ToolMove m in op.Moves)
            {
                if (m.RotaryLetter is { } letter)
                {
                    last[letter] = Math.Round(m.RotaryAngle, 3);
                }
            }
        }

        return last.Where(kv => Math.Abs(kv.Value) > 1e-9).Select(kv => (kv.Key, kv.Value)).ToList();
    }

    /// <summary>Eixo rotativo pode voltar a 0° no fim: o perfil informa o eixo e 0° está dentro do curso.</summary>
    public static bool CanReturnToZero(MachineProfile? machine, string letter) => machine?.AxisOf(letter) is { } axis && axis.Contains(0);

    /// <summary>Aviso fixo do retorno a zero no fim do programa.</summary>
    public const string ReturnToZeroNotVerified = "NÃO VERIFICADO EM MÁQUINA: no fim do programa o eixo rotativo volta a 0° depois de Z subir à referência. Confira que a peça e a fixação giram livres e rode primeiro no ar.";

    /// <summary>Avisos antes de gerar: o mesmo T com diâmetros diferentes (o corretor e o raio do controle seriam os mesmos).</summary>
    public static IReadOnlyList<string> Check(IReadOnlyList<CamOperation> operations, MachineProfile? machine = null)
    {
        var warnings = new List<string>();
        foreach ((string letter, double angle) in RotaryAtEnd(operations))
        {
            warnings.Add(CanReturnToZero(machine, letter)
                ? $"Eixo {letter} termina em {angle.ToString("0.###", Inv)}° e volta a 0° no fim. " + ReturnToZeroNotVerified
                : $"Eixo {letter} termina em {angle.ToString("0.###", Inv)}° e NÃO volta a 0° no fim: {(machine?.AxisOf(letter) is null ? "o perfil da máquina não informa este eixo" : "0° está fora do curso do eixo no perfil")}. Leve o eixo a zero à mão antes do próximo programa.");
        }

        foreach (IGrouping<int, CamTool> group in operations.Select(o => o.Tool).GroupBy(t => t.Number))
        {
            double[] diameters = group.Select(t => Math.Round(t.Diameter, 3)).Distinct().ToArray();
            if (diameters.Length > 1)
            {
                warnings.Add($"T{group.Key} aparece com diâmetros diferentes ({string.Join(" e ", diameters.Select(d => "Ø" + d.ToString("0.###", Inv)))}): use um número de ferramenta para cada uma.");
            }
        }

        return warnings;
    }

    private sealed class Writer(PostOptions o)
    {
        private readonly StringBuilder _sb = new();
        private readonly bool _fanuc = o.Dialect == CamDialect.Fanuc;
        private int _tool = -1;
        private double _rpm;
        private MoveKind? _modal;
        private double _feed = double.NaN;
        private double _x = double.NaN, _y = double.NaN, _z = double.NaN;
        private readonly Dictionary<string, double> _rot = [];

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

        private void Comment(string text) => Line(_fanuc ? $"({text})" : $"; {text}");

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

            if (o.ZeroNote is { } zero)
            {
                Comment(Clean(zero));
            }

            Line($"G{o.WorkOffset}");
        }

        public void Footer(IReadOnlyList<(string Letter, double Angle)> rotary)
        {
            if (o.Coolant)
            {
                Line("M9");
            }

            Line("M5");
            Line(_fanuc ? "G91 G28 Z0" : "G0 G53 Z0");
            Line("G90");
            // Rotativo a zero só depois de Z na referência; o último eixo usado volta primeiro
            foreach ((string letter, _) in rotary.Reverse())
            {
                if (CanReturnToZero(o.Machine, letter))
                {
                    Comment($"EIXO {letter} A ZERO - NAO VERIFICADO EM MAQUINA");
                    Line($"G0 {letter}{N(0)}");
                }
                else
                {
                    Comment($"CONFIRA: EIXO {letter} NAO VOLTA A ZERO (FORA DO CURSO OU SEM PERFIL)");
                }
            }

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
                if (Math.Abs(tool.SpindleRpm - _rpm) > 1e-9)
                {
                    Line($"S{(int)Math.Round(tool.SpindleRpm)}");
                    _rpm = tool.SpindleRpm;
                }

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
            string label = tool.Name.Length > 0 ? Clean(tool.Name) : $"FRESA D{N(tool.Diameter).TrimEnd('.')}";
            if (_fanuc)
            {
                Line($"T{tool.Number} M6 ({label})");
            }
            else
            {
                Line($"T{tool.Number}");
                Line("M6");
                Line($"D{tool.Number} ; {label}");
            }

            Line($"S{(int)Math.Round(tool.SpindleRpm)} M3");
            _tool = tool.Number;
            _rpm = tool.SpindleRpm;
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

            // Eixo rotativo andando junto com X/Y/Z no avanço (gravação no cilindro): no Siemens o rotativo entra no avanço de trajetória
            string? continuous = null;
            for (int k = 1; k < op.Moves.Count && continuous is null; k++)
            {
                ToolMove prev = op.Moves[k - 1], cur = op.Moves[k];
                if (!_fanuc && cur.Kind == MoveKind.Feed && cur.RotaryLetter is { } l && prev.RotaryLetter == l && Math.Abs(cur.RotaryAngle - prev.RotaryAngle) > 1e-9 && (cur.XY.DistanceTo(prev.XY) > 1e-9))
                {
                    continuous = l;
                }
            }

            if (continuous is not null)
            {
                Line($"FGROUP(X,Y,Z,{continuous})");
            }

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

            if (continuous is not null)
            {
                Line("FGROUP(X,Y,Z)");
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

            if (m.RotaryLetter is { } letter)
            {
                double a = Math.Round(m.RotaryAngle, 3);
                if (!_rot.TryGetValue(letter, out double cur) || Math.Abs(cur - a) > 1e-9)
                {
                    sb.Append(letter).Append(N(a)).Append(' ');
                    _rot[letter] = a;
                    any = true;
                }
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

        // Gira um eixo rotativo em rápido, numa linha própria, só quando o ângulo muda
        private void Rotate(string letter, double angle)
        {
            angle = Math.Round(angle, 3);
            if (!_rot.TryGetValue(letter, out double current) || Math.Abs(current - angle) > 1e-9)
            {
                Line($"G0 {letter}{N(angle)}");
                _rot[letter] = angle;
            }
        }

        private void Drilling(CamTool tool, DrillCycle d)
        {
            ChangeTool(tool, d.SafeZ);
            Point2 first = d.Points[0];
            LengthOffset(tool, first, d.SafeZ);
            // Na altura segura (acima do cilindro), gira a face para cima antes de descer (3+2: o giratório antes do basculante)
            if (d.RotaryLetter2 is { } letter2)
            {
                Rotate(letter2, d.RotaryAngle2);
            }

            if (d.RotaryLetter is { } letter)
            {
                Rotate(letter, d.RotaryAngle);
            }

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
