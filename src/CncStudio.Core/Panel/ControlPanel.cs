using System.Globalization;
using System.Text;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Panel;

/// <summary>Um eixo do painel: posição absoluta (coordenadas do programa) e distância a percorrer.</summary>
public readonly record struct PanelAxis(string Name, double Position, double DistanceToGo);

/// <summary>
/// O que um painel de controle de máquina mostra num ponto da simulação: coordenadas absolutas,
/// distância a percorrer (distance to go), ferramenta, avanço, rotação, G ativos e o zero de peça.
/// Sempre em coordenadas do programa (sem o zero de peça aplicado); nunca de máquina.
/// </summary>
public sealed record ControlPanelState(
    IReadOnlyList<PanelAxis> Axes,
    bool Inches,
    int Tool,
    double? Feed,
    double Spindle,
    bool SurfaceSpeed,
    IReadOnlyList<string> GCodes,
    string WorkOffset,
    int Line)
{
    /// <summary>Texto simples para a tela (um valor por linha), no formato que o painel web lê.</summary>
    public string ToJson()
    {
        CultureInfo c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder("{\"axes\":[");
        for (int i = 0; i < Axes.Count; i++)
        {
            PanelAxis a = Axes[i];
            sb.Append(i > 0 ? "," : "")
              .Append("{\"n\":\"").Append(a.Name).Append("\",\"p\":").Append(Num(a.Position, c))
              .Append(",\"d\":").Append(Num(a.DistanceToGo, c)).Append('}');
        }

        sb.Append("],\"unit\":\"").Append(Inches ? "pol" : "mm").Append("\",\"tool\":").Append(Tool.ToString(c))
          .Append(",\"feed\":").Append(Feed is { } f ? Num(f, c) : "null")
          .Append(",\"spindle\":").Append(Num(Spindle, c))
          .Append(",\"css\":").Append(SurfaceSpeed ? "true" : "false")
          .Append(",\"g\":[");
        for (int i = 0; i < GCodes.Count; i++)
        {
            sb.Append(i > 0 ? "," : "").Append('"').Append(GCodes[i]).Append('"');
        }

        sb.Append("],\"wcs\":\"").Append(WorkOffset).Append("\",\"line\":").Append(Line.ToString(c)).Append('}');
        return sb.ToString();
    }

    private static string Num(double value, CultureInfo c) =>
        double.IsFinite(value) ? Math.Round(value, 4).ToString("0.####", c) : "0";
}

/// <summary>Monta o <see cref="ControlPanelState"/> a partir da trajetória e de quantos movimentos já foram executados.</summary>
public static class ControlPanel
{
    private const double MmPerInch = 25.4;

    /// <summary>
    /// Estado depois de executar <paramref name="executedMoves"/> movimentos. A posição é o fim do último
    /// movimento executado; a distância a percorrer é até o fim do próximo (o bloco que vai rodar).
    /// </summary>
    public static ControlPanelState? At(ToolPath path, int executedMoves, Dialect dialect)
    {
        IReadOnlyList<Move> moves = path.Moves;
        if (moves.Count == 0)
        {
            return null;
        }

        executedMoves = Math.Clamp(executedMoves, 0, moves.Count);
        bool lathe = path.Machine == MachineKind.Lathe;
        Move reference = executedMoves == 0 ? moves[0] : moves[executedMoves - 1];
        MachineModal modal = ModalOf(path, reference);
        Point3 now = executedMoves == 0 ? reference.Start : reference.End;
        Point3 nowProgram = now - modal.Offset;

        Point3 toGo = new(0, 0, 0);
        if (executedMoves < moves.Count)
        {
            Move next = moves[executedMoves];
            Point3 nextProgram = next.End - ModalOf(path, next).Offset;
            toGo = nextProgram - nowProgram;
        }

        double scale = modal.Inches ? 1 / MmPerInch : 1;
        // Torno: X em diâmetro, como o resto do app; sem eixo Y
        var axes = new List<PanelAxis>();
        axes.Add(new PanelAxis("X", nowProgram.X * (lathe ? 2 : 1) * scale, toGo.X * (lathe ? 2 : 1) * scale));
        if (!lathe)
        {
            axes.Add(new PanelAxis("Y", nowProgram.Y * scale, toGo.Y * scale));
        }

        axes.Add(new PanelAxis("Z", nowProgram.Z * scale, toGo.Z * scale));

        double? feed = reference.IsRapid || executedMoves == 0 ? null : reference.Feed * scale;
        return new ControlPanelState(axes, modal.Inches, reference.Tool, feed, modal.Spindle, modal.ConstantSurfaceSpeed,
            GCodes(reference, modal, lathe, dialect), WorkOffsetName(modal.WorkOffsetCode), executedMoves == 0 ? 0 : reference.LineNumber);
    }

    private static MachineModal ModalOf(ToolPath path, Move move) =>
        move.ModalId >= 0 && move.ModalId < path.Modals.Count
            ? path.Modals[move.ModalId]
            : new MachineModal(WorkPlane.XY, false, false, 54, 0, false, false, 0, DrillCycle.None, Point3.Origin);

    private static string WorkOffsetName(int code) => code == 0 ? "G500" : "G" + code.ToString(CultureInfo.InvariantCulture);

    /// <summary>G ativos, um por grupo: movimento, plano, absoluto/incremental, unidade, compensação, avanço, (torno) G96/G97, (fresa) ciclo.</summary>
    private static List<string> GCodes(Move move, MachineModal modal, bool lathe, Dialect dialect)
    {
        var g = new List<string>
        {
            move.Motion switch { MotionMode.Linear => "G01", MotionMode.ClockwiseArc => "G02", MotionMode.CounterClockwiseArc => "G03", _ => "G00" },
            modal.Plane switch { WorkPlane.ZX => "G18", WorkPlane.YZ => "G19", _ => "G17" },
            modal.Incremental ? "G91" : "G90",
            modal.Inches ? "G20" : "G21",
            modal.Compensation switch { > 0 => "G41", < 0 => "G42", _ => "G40" },
        };

        // Avanço por rotação: G95/G94; no torno Fanuc, G99/G98
        bool perRev = modal.FeedPerRevolution;
        g.Add(lathe && dialect == Dialect.Fanuc ? (perRev ? "G99" : "G98") : (perRev ? "G95" : "G94"));
        if (lathe)
        {
            g.Add(modal.ConstantSurfaceSpeed ? "G96" : "G97");
        }
        else
        {
            g.Add(modal.Cycle switch
            {
                DrillCycle.None => "G80",
                DrillCycle.Drill => "G81",
                DrillCycle.Peck => "G83",
                DrillCycle.ChipBreak => "G73",
                DrillCycle.Tap => "G84",
                _ => "G85",
            });
        }

        return g;
    }
}
