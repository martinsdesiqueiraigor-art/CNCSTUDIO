using CncStudio.Core;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Trajectory;

namespace CncStudio.App.Modules;

/// <summary>
/// Tempo estimado de cada operação do Trabalho (UI-05), só para a tela: o G-code gerado passa pelo mesmo analisador do Code
/// (<see cref="ProgramAnalyzer"/>, Core sem mudança) e o tempo de cada movimento (rápido pela velocidade de rápido da máquina
/// padrão, corte pelo avanço) é somado entre o comentário com o nome de uma operação e o da seguinte. Movimento de corte sem
/// avanço deixa a operação "sem tempo" (null): nada é inventado. Pausas (G4) e trocas de ferramenta não entram.
/// </summary>
public static class CamOpTimes
{
    /// <summary>
    /// <paramref name="ops"/>: índice da operação no trabalho e o nome que o pós escreve no comentário, na ordem do programa.
    /// Devolve o tempo por índice (null = sem tempo). Operação cujo comentário não for achado fica fora do dicionário.
    /// </summary>
    public static Dictionary<int, TimeSpan?> Estimate(string program, IReadOnlyList<(int Index, string Name)> ops, bool siemens, bool lathe, CancellationToken cancel = default)
    {
        var result = new Dictionary<int, TimeSpan?>();
        if (program.Length == 0 || ops.Count == 0)
        {
            return result;
        }

        // linha (1 = primeira) onde começa cada operação: o comentário com o nome, procurado em ordem
        string[] lines = program.Split('\n');
        var starts = new List<(int Index, int Line)>();
        int from = 0;
        foreach ((int index, string name) in ops)
        {
            string clean = Clean(name);
            string target = siemens ? "; " + clean : "(" + clean + ")";
            int found = -1;
            for (int k = from; k < lines.Length; k++)
            {
                if (lines[k].Trim() == target)
                {
                    found = k;
                    break;
                }
            }

            if (found < 0)
            {
                continue;
            }

            starts.Add((index, found + 1));
            from = found + 1;
        }

        if (starts.Count == 0)
        {
            return result;
        }

        var settings = MachineSettings.Default with { Machine = lathe ? MachineKind.Lathe : MachineKind.Mill };
        ProgramAnalysis analysis = ProgramAnalyzer.Analyze(program, siemens ? Dialect.Siemens : Dialect.Fanuc, settings, cancel);
        double rapid = settings.RapidRate;
        var minutes = new double[starts.Count];
        var unknown = new bool[starts.Count];
        foreach (Move move in analysis.ToolPath.Moves)
        {
            int slot = -1;
            for (int s = starts.Count - 1; s >= 0; s--)
            {
                if (move.LineNumber >= starts[s].Line)
                {
                    slot = s;
                    break;
                }
            }

            if (slot < 0)
            {
                continue;
            }

            double length = move.Length;
            if (move.IsRapid)
            {
                minutes[slot] += length / rapid;
            }
            else if (move.Feed > 0)
            {
                minutes[slot] += length / move.Feed;
            }
            else if (length > 0)
            {
                unknown[slot] = true;
            }
        }

        for (int s = 0; s < starts.Count; s++)
        {
            result[starts[s].Index] = unknown[s] ? null : TimeSpan.FromMinutes(minutes[s]);
        }

        return result;
    }

    /// <summary>Mesma limpeza do comentário que os pós do Core fazem com o nome da operação.</summary>
    private static string Clean(string text) => text.Replace('(', '[').Replace(')', ']').Replace(';', ',').Trim();

    /// <summary>"≈ 1 min 05 s", "≈ 42 s"; null = "sem tempo".</summary>
    public static string Text(TimeSpan? t) => t is not { } v
        ? "sem tempo"
        : v.TotalSeconds < 60 ? $"≈ {Math.Max(1, (int)Math.Round(v.TotalSeconds))} s" : $"≈ {(int)v.TotalMinutes} min {v.Seconds:00} s";
}
