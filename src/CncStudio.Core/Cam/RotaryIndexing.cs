namespace CncStudio.Core.Cam;

/// <summary>
/// Face de peça posicionada no eixo rotativo (modo calculado, 4º eixo na mesa): o desenho da face é feito "de cima"
/// (Z = 0 na face) e o CAM move tudo para a face girada. <see cref="AxisToFace"/> é a distância da linha de giro até a face
/// quando ela está virada para cima; <see cref="OffsetX"/>/<see cref="OffsetY"/> deslocam a origem da face em relação ao pivô.
/// </summary>
public sealed record IndexedFace(double Angle, double AxisToFace, double OffsetX = 0, double OffsetY = 0, double SafeRadius = 0);

/// <summary>
/// Face posicionada 3+2 na mesa dupla (modo calculado): <see cref="Tilt"/> é o ângulo do eixo basculante (o primeiro do perfil,
/// A ou B, que carrega o outro) e <see cref="Rotate"/> o do giratório (o segundo). Com os dois ângulos a face fica virada
/// para cima; o desenho da face é feito "de cima" nessa posição (Z = 0 na face). <see cref="AxisToFace"/> e os deslocamentos
/// valem como em <see cref="IndexedFace"/>, medidos a partir da linha de giro do basculante.
/// </summary>
public sealed record TiltedFace(double Tilt, double Rotate, double AxisToFace, double OffsetX = 0, double OffsetY = 0, double SafeRadius = 0);

public static class RotaryIndexing
{
    /// <summary>Aviso fixo de todo giro 3+2: nada disto foi testado em máquina.</summary>
    public const string NotMachineVerified = "NÃO VERIFICADO EM MÁQUINA: 3+2 nunca rodou numa máquina real. Confira o sentido dos dois giros, os pivôs e a ordem de giro, e rode primeiro no ar.";

    /// <summary>
    /// 3+2 na mesa dupla: leva uma operação 2,5D (feita com a face para cima) para a face posicionada pelos dois eixos da mesa.
    /// Na altura segura gira primeiro o giratório e depois o basculante; os dois ângulos são conferidos no curso do perfil.
    /// Não verifica colisão da mesa, da fixação nem da ferramenta durante os giros.
    /// </summary>
    public static OperationResult PlaceTilted(CamOperation op, MachineProfile machine, TiltedFace face)
    {
        if (machine.Mode != RotaryOutputMode.Calculated)
        {
            return new OperationResult(null, "Este recurso usa o modo calculado; troque o modo no perfil da máquina.");
        }

        if (machine.Configuration != MachineConfiguration.FiveAxisTableTable)
        {
            return new OperationResult(null, "3+2 nesta etapa só vale para mesa dupla (dois eixos rotativos na mesa). Escolha um perfil de 5 eixos mesa + mesa.");
        }

        RotaryAxisConfig tilt = machine.Axes[0], spin = machine.Axes[1];
        if (tilt.Letter is not ("A" or "B"))
        {
            return new OperationResult(null, $"O primeiro eixo do perfil (o basculante, que carrega o outro) precisa ser A ou B; está {tilt.Letter}.");
        }

        if (!double.IsFinite(face.Tilt) || !double.IsFinite(face.Rotate) || !double.IsFinite(face.AxisToFace))
        {
            return new OperationResult(null, "Ângulo ou distância inválidos.");
        }

        if (op.Drill is null && op.Moves.Count == 0)
        {
            return new OperationResult(null, "A operação não tem movimentos.");
        }

        double tiltAngle = tilt.Reversed ? -face.Tilt : face.Tilt;
        double spinAngle = spin.Reversed ? -face.Rotate : face.Rotate;
        if (!tilt.Contains(tiltAngle))
        {
            return new OperationResult(null, $"Ângulo {face.Tilt:0.###}° fora do curso do eixo {tilt.Letter} ({tilt.MinAngle:0.###}° a {tilt.MaxAngle:0.###}°).");
        }

        if (!spin.Contains(spinAngle))
        {
            return new OperationResult(null, $"Ângulo {face.Rotate:0.###}° fora do curso do eixo {spin.Letter} ({spin.MinAngle:0.###}° a {spin.MaxAngle:0.###}°).");
        }

        bool aAxis = tilt.Letter == "A";
        double dx = face.OffsetX + (aAxis ? 0 : tilt.PivotX);
        double dy = face.OffsetY + (aAxis ? tilt.PivotY : 0);
        double dz = tilt.PivotZ + face.AxisToFace;
        string name = $"{op.Name} ({tilt.Letter}{face.Tilt:0.###} {spin.Letter}{face.Rotate:0.###})";
        string where = $"Face 3+2 em {tilt.Letter}{face.Tilt:0.###} {spin.Letter}{face.Rotate:0.###} (gira {spin.Letter} e depois {tilt.Letter}, na altura segura).";
        if (op.Drill is { } drill)
        {
            double retract = drill.RetractZ + dz;
            var shifted = drill with
            {
                Points = drill.Points.Select(p => new Point2(p.X + dx, p.Y + dy)).ToList(),
                TopZ = drill.TopZ + dz,
                BottomZ = drill.BottomZ + dz,
                RetractZ = retract,
                SafeZ = Math.Max(tilt.PivotZ + Math.Max(face.SafeRadius, face.AxisToFace + 10), Math.Max(retract, drill.SafeZ + dz)),
                RotaryLetter = tilt.Letter,
                RotaryAngle = tiltAngle,
                RotaryLetter2 = spin.Letter,
                RotaryAngle2 = spinAngle,
            };
            return new OperationResult(new CamOperation(name, op.Tool, [], shifted, [.. op.Warnings, where, NotMachineVerified]), null);
        }

        double safe = Math.Max(tilt.PivotZ + Math.Max(face.SafeRadius, face.AxisToFace + 10), op.Moves.Max(m => m.Z) + dz);
        var list = new List<ToolMove>(op.Moves.Count + 4);
        ToolMove first = Shift(op.Moves[0], dx, dy, dz);
        list.Add(first with { Kind = MoveKind.Rapid, Z = safe });
        list.Add(new ToolMove(MoveKind.Rapid, first.X, first.Y, safe) { RotaryLetter = spin.Letter, RotaryAngle = spinAngle });
        list.Add(new ToolMove(MoveKind.Rapid, first.X, first.Y, safe) { RotaryLetter = tilt.Letter, RotaryAngle = tiltAngle });
        foreach (ToolMove m in op.Moves)
        {
            list.Add(Shift(m, dx, dy, dz));
        }

        ToolMove last = list[^1];
        list.Add(new ToolMove(MoveKind.Rapid, last.X, last.Y, safe));
        return new OperationResult(new CamOperation(name, op.Tool, list, null, [.. op.Warnings, where, NotMachineVerified]), null);
    }

    /// <summary>
    /// Leva uma operação 2,5D (feita com a face para cima) para a face girada do eixo <paramref name="letter"/> (A ou B).
    /// Antes de girar, sobe para Z = pivô + raio seguro. Devolve erro em texto quando o perfil não permite.
    /// </summary>
    public static OperationResult Place(CamOperation op, MachineProfile machine, string letter, IndexedFace face)
    {
        if (machine.Mode != RotaryOutputMode.Calculated)
        {
            return new OperationResult(null, "Este recurso usa o modo calculado; troque o modo no perfil da máquina.");
        }

        RotaryAxisConfig? axis = machine.AxisOf(letter);
        if (axis is null)
        {
            return new OperationResult(null, $"O perfil da máquina não tem o eixo {letter}.");
        }

        if (axis.Carrier != RotaryCarrier.Table || letter is not ("A" or "B"))
        {
            return new OperationResult(null, "Face girada de um eixo só vale com A ou B na mesa (4º eixo). Mesa dupla: use 3+2. Cabeçote ainda não.");
        }

        if (op.Drill is null && op.Moves.Count == 0)
        {
            return new OperationResult(null, "A operação não tem movimentos.");
        }

        double machineAngle = axis.Reversed ? -face.Angle : face.Angle;
        if (!axis.Contains(machineAngle))
        {
            return new OperationResult(null, $"Ângulo {face.Angle:0.###}° fora do curso do eixo {letter} ({axis.MinAngle:0.###}° a {axis.MaxAngle:0.###}°).");
        }

        bool aAxis = letter == "A";
        double dx = face.OffsetX + (aAxis ? 0 : axis.PivotX);
        double dy = face.OffsetY + (aAxis ? axis.PivotY : 0);
        double dz = axis.PivotZ + face.AxisToFace;
        if (op.Drill is { } drill)
        {
            return PlaceDrill(op, drill, letter, face, machineAngle, dx, dy, dz, axis.PivotZ);
        }

        double safe = Math.Max(axis.PivotZ + Math.Max(face.SafeRadius, face.AxisToFace + 10), op.Moves.Max(m => m.Z) + dz);

        var list = new List<ToolMove>(op.Moves.Count + 3);
        ToolMove first = Shift(op.Moves[0], dx, dy, dz);
        list.Add(first with { Kind = MoveKind.Rapid, Z = safe });
        list.Add(new ToolMove(MoveKind.Rapid, first.X, first.Y, safe) { RotaryLetter = letter, RotaryAngle = machineAngle });
        foreach (ToolMove m in op.Moves)
        {
            list.Add(Shift(m, dx, dy, dz));
        }

        ToolMove last = list[^1];
        list.Add(new ToolMove(MoveKind.Rapid, last.X, last.Y, safe));

        var warnings = new List<string>(op.Warnings) { $"Face girada a {face.Angle:0.###}° no eixo {letter}: confira o sentido do giro e rode primeiro no ar." };
        return new OperationResult(new CamOperation($"{op.Name} ({letter}{face.Angle:0.###})", op.Tool, list, null, warnings), null);
    }

    // Furos radiais: os ciclos fixos (G81/G82/G83/G73) valem como estão com a face virada para cima; só os valores se deslocam
    // para a face girada e o eixo gira (na altura segura) antes do primeiro furo
    private static OperationResult PlaceDrill(CamOperation op, DrillCycle drill, string letter, IndexedFace face, double machineAngle, double dx, double dy, double dz, double pivotZ)
    {
        double retract = drill.RetractZ + dz;
        double safe = Math.Max(pivotZ + Math.Max(face.SafeRadius, face.AxisToFace + 10), Math.Max(retract, drill.SafeZ + dz));
        var points = drill.Points.Select(p => new Point2(p.X + dx, p.Y + dy)).ToList();
        var shifted = drill with
        {
            Points = points,
            TopZ = drill.TopZ + dz,
            BottomZ = drill.BottomZ + dz,
            RetractZ = retract,
            SafeZ = safe,
            RotaryLetter = letter,
            RotaryAngle = machineAngle,
        };
        var warnings = new List<string>(op.Warnings)
        {
            $"Furos na face girada a {face.Angle:0.###}° no eixo {letter}: confira o sentido do giro e o centro do eixo; rode primeiro no ar.",
        };
        return new OperationResult(new CamOperation($"{op.Name} ({letter}{face.Angle:0.###})", op.Tool, [], shifted, warnings), null);
    }

    private static ToolMove Shift(ToolMove m, double dx, double dy, double dz) =>
        m with { X = m.X + dx, Y = m.Y + dy, Z = m.Z + dz, Center = new Point2(m.Center.X + dx, m.Center.Y + dy) };
}
