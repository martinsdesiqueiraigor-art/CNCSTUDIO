using System.Globalization;
using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Execution;

/// <summary>Ciclos de furação Fanuc (modais) e Siemens.</summary>
public enum DrillCycle
{
    None,

    /// <summary>G81/G82, CYCLE81/CYCLE82: furo direto.</summary>
    Drill,

    /// <summary>G83, CYCLE83: furação profunda em bicadas.</summary>
    Peck,
}

/// <summary>Parte do executor que trata os ciclos de furação.</summary>
public sealed partial class MachineExecutor
{
    // ── Fanuc: ciclo modal G81/G82/G83 ──
    private DrillCycle _drillCycle = DrillCycle.None;
    private bool _returnToR;          // G99 = volta ao plano R; G98 (padrão) = volta ao nível inicial
    private double? _cycleR;          // valores como programados (já em mm)
    private double? _cycleZ;
    private double _cycleQ;
    private double _initialLevel;     // Z (programa) quando o ciclo começou
    private bool _cycleDwells;        // G82: espera no fundo
    private double _cycleDwellSeconds; // P do G82 (modal)

    // ── Siemens: ciclo chamado com MCALL ──
    private Statement? _modalCycle;

    /// <summary>Ativa/cancela o ciclo Fanuc e o modo de retorno (G98/G99).</summary>
    private void ApplyCycleModes(BlockWords w)
    {
        if (w.ReturnToR is { } returnToR)
        {
            _returnToR = returnToR;
        }

        if (w.Cycle is { } cycle)
        {
            if (_drillCycle == DrillCycle.None)
            {
                _initialLevel = (_position - Offset).Z;
            }

            _drillCycle = cycle;
            _cycleDwells = w.CycleDwell;
        }
        else if (w.CycleCancel)
        {
            _drillCycle = DrillCycle.None;
        }
    }

    /// <summary>
    /// Executa G81/G82/G83 em cada posição programada:
    /// rápido em XY → rápido até R → furação até Z → retorno a R (G99) ou ao nível inicial (G98).
    /// Em G91, R é medido a partir do nível inicial e Z a partir de R. K/L repetem o furo.
    /// </summary>
    private void ExecuteFanucDrill(NcBlock block, BlockWords w)
    {
        int line = block.LineNumber;

        if (Length(w.R) is { } r)
        {
            _cycleR = r;
        }

        if (Length(w.Z) is { } z)
        {
            _cycleZ = z;
        }

        if (Length(w.Q) is { } q)
        {
            _cycleQ = Math.Abs(q);
        }

        if (w.P is { } p && _cycleDwells)
        {
            _cycleDwellSeconds = p.Value / 1000; // P em milissegundos
        }

        if (_cycleR is not { } rValue || _cycleZ is not { } zValue)
        {
            Error(line, 0, 0, "Ciclo de furação sem plano R ou profundidade Z definidos.");
            return;
        }

        int repeats = w.K is { } k ? Math.Max(0, (int)k.Value) : w.Repeat;
        for (int repeat = 0; repeat < repeats; repeat++)
        {
            Point3 program = _position - Offset;
            double x = Resolve(w.X, program.X);
            double y = Resolve(w.Y, program.Y);

            double rLevel = _incremental ? _initialLevel + rValue : rValue;
            double bottom = _incremental ? rLevel + zValue : zValue;
            double returnLevel = _returnToR ? rLevel : Math.Max(_initialLevel, rLevel);

            AddLine(line, ToMachine(x, y, program.Z), MotionMode.Rapid);
            AddLine(line, ToMachine(x, y, rLevel), MotionMode.Rapid);

            if (_drillCycle == DrillCycle.Peck && _cycleQ > 0)
            {
                Peck(line, x, y, rLevel, bottom, _cycleQ, 0, fullRetract: true, retractLevel: rLevel);
            }
            else
            {
                AddLine(line, ToMachine(x, y, bottom), MotionMode.Linear);
                if (_cycleDwells)
                {
                    AddDwell(line, _cycleDwellSeconds);
                }
            }

            AddLine(line, ToMachine(x, y, returnLevel), MotionMode.Rapid);
        }
    }

    /// <summary>
    /// Siemens CYCLE81/82/83 na posição XY atual.
    /// CYCLE81(RTP, RFP, SDIS, DP, DPR) — CYCLE83(RTP, RFP, SDIS, DP, DPR, FDEP, FDPR, DAM, DTB, DTS, FRF, VARI).
    /// </summary>
    private void ExecuteSiemensCycle(NcBlock block, Statement cycle)
    {
        int line = block.LineNumber;
        if (cycle.Name is "CYCLE95" or "CYCLE97")
        {
            ExecuteSiemensLatheCycle(line, cycle);
            return;
        }

        if (!ReadCycleArguments(line, cycle, out double?[] args, out _))
        {
            return;
        }

        double? Arg(int index) => index < args.Length ? args[index] * _unitScale : null;

        double? rtp = Arg(0);
        double? rfp = Arg(1);
        double sdis = Math.Abs(Arg(2) ?? 0);
        double? dp = Arg(3);
        double? dpr = Arg(4);

        if (rtp is not { } retract || rfp is not { } reference || (dp is null && dpr is null))
        {
            Error(line, cycle.Column, cycle.Length, $"{cycle.Name}: faltam parâmetros (RTP, RFP e DP ou DPR).");
            return;
        }

        double bottom = dp ?? reference - Math.Abs(dpr!.Value);
        double safe = reference + sdis;
        Point3 program = _position - Offset;
        double x = program.X;
        double y = program.Y;

        AddLine(line, ToMachine(x, y, safe), MotionMode.Rapid);

        if (cycle.Name == "CYCLE83")
        {
            double? fdep = Arg(5);
            double? fdpr = Arg(6);
            double dam = Math.Abs(Arg(7) ?? 0);
            int vari = args.Length > 11 ? (int)(args[11] ?? 0) : 0; // VARI: 0 = quebra-cavaco, 1 = retorno total

            double firstAmount = fdep is { } f ? reference - f : fdpr is { } fr ? Math.Abs(fr) : reference - bottom;
            if (firstAmount <= 0)
            {
                firstAmount = reference - bottom;
            }

            // Primeira bicada é medida a partir do plano de referência
            AddLine(line, ToMachine(x, y, reference), MotionMode.Linear);
            Peck(line, x, y, reference, bottom, firstAmount, dam, fullRetract: vari == 1, retractLevel: safe);
            AddSiemensDwell(line, args, 8); // DTB
        }
        else
        {
            AddLine(line, ToMachine(x, y, bottom), MotionMode.Linear);
            if (cycle.Name == "CYCLE82")
            {
                AddSiemensDwell(line, args, 5); // DTB
            }
        }

        AddLine(line, ToMachine(x, y, retract), MotionMode.Rapid);
    }

    /// <summary>
    /// Furação em bicadas a partir de "start" até "bottom". Depois de cada bicada:
    /// retorno total (até retractLevel) ou quebra-cavaco (sobe a folga), e reaproximação
    /// rápida até a folga acima da última profundidade. "degression" reduz cada bicada (Siemens DAM).
    /// </summary>
    private void Peck(int line, double x, double y, double start, double bottom, double amount, double degression,
        bool fullRetract, double retractLevel)
    {
        double clearance = _settings.PeckClearance;
        double current = start;
        bool first = true;
        int guard = 0;

        while (current > bottom + 1e-9 && guard++ < 10_000)
        {
            double next = Math.Max(current - amount, bottom);
            if (!first)
            {
                AddLine(line, ToMachine(x, y, current + clearance), MotionMode.Rapid);
            }

            AddLine(line, ToMachine(x, y, next), MotionMode.Linear);
            current = next;
            first = false;

            if (current > bottom + 1e-9)
            {
                double up = fullRetract ? retractLevel : current + clearance;
                AddLine(line, ToMachine(x, y, up), MotionMode.Rapid);
            }

            if (degression > 0)
            {
                amount = Math.Max(amount - degression, degression);
            }
        }
    }

    /// <summary>DTB dos ciclos Siemens: positivo = segundos, negativo = rotações do fuso.</summary>
    private void AddSiemensDwell(int line, double?[] args, int index)
    {
        if (index >= args.Length || args[index] is not { } dtb || dtb == 0)
        {
            return;
        }

        double? seconds = dtb > 0 ? dtb : RevolutionsToSeconds(line, dtb);
        if (seconds is { } value)
        {
            AddDwell(line, value);
        }
    }

    private Point3 ToMachine(double x, double y, double z) => new Point3(x, y, z) + Offset;

    /// <summary>
    /// Argumentos de um ciclo Siemens: "10, 0, R2+1, , \"KONTUR\"" → números (expressões avaliadas)
    /// e textos (entre aspas). Argumento vazio = null nos dois. Retorna false (com erro) se algum
    /// argumento não puder ser avaliado.
    /// </summary>
    private bool ReadCycleArguments(int line, Statement cycle, out double?[] numbers, out string?[] texts)
    {
        string text = cycle.Arguments ?? "";
        List<string> parts = string.IsNullOrWhiteSpace(text) ? [] : SplitTopLevelOperator(text, ",");
        numbers = new double?[parts.Count];
        texts = new string?[parts.Count];

        for (int i = 0; i < parts.Count; i++)
        {
            string part = parts[i].Trim();
            if (part.Length == 0)
            {
                continue;
            }

            if (part.StartsWith('"'))
            {
                texts[i] = part.Trim('"');
                continue;
            }

            if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                numbers[i] = value;
                continue;
            }

            try
            {
                numbers[i] = Expressions.ExpressionEvaluator.Evaluate(part, _dialect, _variables, m => Warning(line, cycle, m));
            }
            catch (Expressions.ExpressionException ex)
            {
                Error(line, cycle, $"{cycle.Name}: parâmetro {i + 1} ('{part}') inválido: {ex.Message}");
                return false;
            }
        }

        return true;
    }
}
