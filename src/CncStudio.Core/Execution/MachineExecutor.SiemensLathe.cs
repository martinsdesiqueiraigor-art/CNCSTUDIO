using CncStudio.Core.Geometry;
using CncStudio.Core.Parsing;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Execution;

/// <summary>Ciclos de torno Siemens: CYCLE95 (desbaste/acabamento de contorno) e CYCLE97 (rosca).</summary>
public sealed partial class MachineExecutor
{
    // Passo da rosca métrica grossa (MPIT do CYCLE97), mm
    private static readonly Dictionary<int, double> MetricCoarsePitch = new()
    {
        [3] = 0.5, [4] = 0.7, [5] = 0.8, [6] = 1, [8] = 1.25, [10] = 1.5, [12] = 1.75, [14] = 2, [16] = 2,
        [18] = 2.5, [20] = 2.5, [22] = 2.5, [24] = 3, [27] = 3, [30] = 3.5, [33] = 3.5, [36] = 4, [39] = 4,
        [42] = 4.5, [45] = 4.5, [48] = 5, [52] = 5, [56] = 5.5, [60] = 5.5,
    };

    private void ExecuteSiemensLatheCycle(int line, Statement cycle)
    {
        if (_capturing)
        {
            Error(line, cycle, $"{cycle.Name} dentro do contorno de outro ciclo não é permitido.");
            return;
        }

        if (!ReadCycleArguments(line, cycle, out double?[] numbers, out string?[] texts))
        {
            return;
        }

        if (cycle.Name == "CYCLE95")
        {
            ExecuteCycle95(line, cycle, numbers, texts);
        }
        else
        {
            ExecuteCycle97(line, cycle, numbers);
        }
    }

    /// <summary>
    /// CYCLE95(NPP, MID, FALZ, FALX, FAL, FF1, FF2, FF3, VARI, DT, DAM, _VRT).
    /// NPP = nome do subprograma do contorno ("KONTUR") ou "INICIO:FIM" (rótulos no programa).
    /// VARI (unidade): 1–4 desbaste, 5–8 acabamento, 9–12 completo; dentro de cada grupo:
    /// longitudinal externo, face externo, longitudinal interno, face interno.
    /// O ponto inicial (posição atual) é o canto do bruto, como no G71.
    /// </summary>
    private void ExecuteCycle95(int line, Statement cycle, double?[] n, string?[] texts)
    {
        double? Num(int i) => i < n.Length ? n[i] : null;
        double? Len(int i) => Num(i) is { } v ? v * _unitScale : null;

        string? contourName = texts.Length > 0 ? texts[0] : null;
        if (string.IsNullOrWhiteSpace(contourName))
        {
            Error(line, cycle, "CYCLE95: falta o contorno (NPP), ex.: \"KONTUR\" ou \"INICIO:FIM\".");
            return;
        }

        int vari = Math.Clamp((int)(Num(8) ?? 1) % 100, 1, 12);
        int machining = (vari - 1) % 4;              // 0 long. ext., 1 face ext., 2 long. int., 3 face int.
        int operation = (vari - 1) / 4;              // 0 desbaste, 1 acabamento, 2 completo
        bool longitudinal = machining is 0 or 2;

        double depth = Math.Abs(Len(1) ?? 0);
        double falz = Math.Abs(Len(2) ?? 0);
        double falx = Math.Abs(Len(3) ?? 0);
        double fal = Math.Abs(Len(4) ?? 0);
        double roughFeed = (Num(5) ?? _feed / _unitScale) * _unitScale;
        double finishFeed = (Num(7) ?? Num(5) ?? _feed / _unitScale) * _unitScale;
        double retract = Math.Abs(Len(11) ?? 1);

        if (CaptureSiemensContour(line, contourName) is not { } contour)
        {
            return;
        }

        Point3 start = _position;
        if (operation is 0 or 2)
        {
            if (depth <= 0)
            {
                Error(line, cycle, "CYCLE95: profundidade de corte (MID) deve ser maior que zero.");
                return;
            }

            // Sobremetal sempre para o lado do material: X (raio) e Z, mais o paralelo ao contorno
            double depthAllowance = (longitudinal ? falx : falz) + fal;
            double feedAllowance = (longitudinal ? falz : falx) + fal;
            RoughContour(line, contour.Profile, longitudinal, Point3.Origin, depth, retract, roughFeed, contourPass: true,
                "CYCLE95", depthAllowance, feedAllowance);
        }

        if (operation is 1 or 2)
        {
            FinishContour(line, contour.Profile, longitudinal, finishFeed);
            AddLine(line, start, MotionMode.Rapid);
        }
    }

    /// <summary>Acabamento: aproxima pelo eixo de profundidade e percorre o contorno inteiro.</summary>
    private void FinishContour(int line, List<Move> profile, bool depthIsX, double feed)
    {
        Point3 first = profile[0].Start;
        Point3 corner = depthIsX ? new Point3(_position.X, _position.Y, first.Z) : new Point3(first.X, _position.Y, _position.Z);
        AddLine(line, corner, MotionMode.Rapid);
        AddLine(line, first, MotionMode.Rapid);
        foreach (Move move in profile)
        {
            AddMove(move with { Start = _position, Feed = feed, Tool = _activeTool });
            _position = move.End;
        }

        // Sai pelo eixo de profundidade
        Point3 last = _position;
        AddLine(line, depthIsX ? new Point3(corner.X, last.Y, last.Z) : new Point3(last.X, last.Y, corner.Z), MotionMode.Rapid);
    }

    /// <summary>Contorno do CYCLE95: subprograma ("KONTUR") ou trecho entre rótulos ("INICIO:FIM").</summary>
    private CapturedContour? CaptureSiemensContour(int line, string name)
    {
        Frame frame = _currentFrame!;
        int colon = name.IndexOf(':');
        if (colon > 0)
        {
            string first = name[..colon].Trim().ToUpperInvariant();
            string last = name[(colon + 1)..].Trim().ToUpperInvariant();
            LoadedProgram program = frame.Program;
            int? start = program.FindLabel(first, frame.Pc, "GOTO", frame.RegionStart, frame.RegionEnd);
            int? end = start is { } s ? program.FindLabel(last, s - 1, "GOTOF", frame.RegionStart, frame.RegionEnd) : null;
            if (start is null || end is null)
            {
                Error(line, 0, 0, $"CYCLE95: rótulos {first}: e {last}: do contorno não encontrados.");
                return null;
            }

            CapturedContour? labels = CaptureBlocks(line, program, start.Value, end.Value, frame.RegionStart, frame.RegionEnd, frame.CallerLine, "CYCLE95");
            return labels is null ? null : labels with { EndIndex = frame.Pc }; // a execução continua normalmente
        }

        if (FindProgram(name) is not { } found)
        {
            Error(line, 0, 0, $"CYCLE95: contorno {name} não encontrado (nem no arquivo, nem na pasta do programa).");
            return null;
        }

        int callerLine = found.Program.IsExternal ? (frame.CallerLine > 0 ? frame.CallerLine : line) : 0;
        return CaptureBlocks(line, found.Program, found.Start, found.End - 1, found.Start, found.End, callerLine, "CYCLE95");
    }

    /// <summary>
    /// CYCLE97(PIT, MPIT, SPL, FPL, DM1, DM2, APP, ROP, TDEP, FAL, IANG, NSP, NRC, NID, VARI, NUMT, _VRT).
    /// Rosca longitudinal de SPL a FPL (diâmetros DM1 → DM2), com entrada APP e saída ROP,
    /// profundidade TDEP em NRC passes de desbaste (VARI 1/2: profundidade constante;
    /// 3/4: seção constante), acabamento e NID passes em vazio. VARI ímpar = externa, par = interna.
    /// </summary>
    private void ExecuteCycle97(int line, Statement cycle, double?[] n)
    {
        double? Num(int i) => i < n.Length ? n[i] : null;
        double? Len(int i) => Num(i) is { } v ? v * _unitScale : null;

        double? lead = Len(0);
        if (lead is null && Num(1) is { } size && MetricCoarsePitch.TryGetValue((int)Math.Round(size), out double pitch))
        {
            lead = pitch;
        }

        if (lead is not { } threadLead || threadLead <= 0 || Len(2) is not { } spl || Len(3) is not { } fpl
            || Len(4) is not { } dm1 || Len(8) is not { } tdep)
        {
            Error(line, cycle, "CYCLE97: faltam parâmetros (PIT ou MPIT, SPL, FPL, DM1 e TDEP).");
            return;
        }

        // Diâmetros: DM em diâmetro (como o X); internamente usamos raio
        double r1 = dm1 / 2;
        double r2 = (Len(5) ?? dm1) / 2;
        double app = Math.Abs(Len(6) ?? 0);
        double rop = Math.Abs(Len(7) ?? 0);
        double fal = Math.Abs(Len(9) ?? 0);
        int roughPasses = Math.Max(1, (int)(Num(12) ?? 1));
        int idlePasses = Math.Max(0, (int)(Num(13) ?? 0));
        int vari = (int)(Num(14) ?? 1);
        int starts = Math.Max(1, (int)(Num(15) ?? 1));
        double clearance = Math.Abs(Len(16) ?? 1);
        tdep = Math.Abs(tdep);

        double side = vari is 2 or 4 ? -1 : 1;       // externa: crista para fora (+X)
        bool constantSection = vari is 3 or 4;
        double direction = Math.Sign(fpl - spl);
        if (direction == 0)
        {
            Error(line, cycle, "CYCLE97: SPL e FPL iguais (rosca sem comprimento).");
            return;
        }

        // Raio da crista em qualquer Z (DM1 → DM2 ao longo de SPL → FPL, prolongado em APP/ROP)
        double CrestAt(double z) => r1 + (r2 - r1) * (z - spl) / (fpl - spl);
        double zStart = spl - direction * app;
        double zEnd = fpl + direction * rop;

        var depths = new List<double>();
        double roughDepth = tdep - fal;
        for (int k = 1; k <= roughPasses; k++)
        {
            double fraction = constantSection ? Math.Sqrt((double)k / roughPasses) : (double)k / roughPasses;
            depths.Add(roughDepth * fraction);
        }

        if (fal > 0)
        {
            depths.Add(tdep);
        }

        for (int k = 0; k < idlePasses; k++)
        {
            depths.Add(tdep);
        }

        Point3 start = _position;
        for (int thread = 0; thread < starts; thread++)
        {
            foreach (double depth in depths)
            {
                double clearStart = CrestAt(zStart) + side * clearance;
                double clearEnd = CrestAt(zEnd) + side * clearance;
                AddLine(line, LathePoint(clearStart, zStart), MotionMode.Rapid);
                AddLine(line, LathePoint(CrestAt(zStart) - side * depth, zStart), MotionMode.Rapid);
                AddThreadLine(line, LathePoint(CrestAt(zEnd) - side * depth, zEnd), threadLead);
                AddLine(line, LathePoint(clearEnd, zEnd), MotionMode.Rapid);
                AddLine(line, LathePoint(clearStart, zStart), MotionMode.Rapid);
            }
        }

        AddLine(line, start, MotionMode.Rapid);
    }
}
