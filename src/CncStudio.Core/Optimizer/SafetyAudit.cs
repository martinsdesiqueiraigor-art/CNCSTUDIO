namespace CncStudio.Core.Optimizer;

/// <summary>Checks the ORIGINAL motion of a candidate. A faithful macro of a dangerous move is
/// still dangerous, so these findings block the proposal or travel with it as warnings.</summary>
public static class SafetyAudit
{
    public static (IReadOnlyList<string> Blockers, IReadOnlyList<string> Warnings) Check(NcState entry, IReadOnlyList<NcStep> steps)
    {
        var blockers = new List<string>();
        var warnings = new List<string>();
        if (entry.Units is not (20 or 21)) warnings.Add("G20/G21 nao declarado antes do trecho: valores interpretados no modo do controle.");
        if (entry.Wcs == "?") warnings.Add("Origem de trabalho (G54-G59) nao declarada antes do trecho.");
        if (entry.CutterComp is 41 or 42) warnings.Add("Compensacao de raio G" + entry.CutterComp + " ativa: trajetoria real difere da programada.");
        if (entry.Tool < 0) warnings.Add("Ferramenta ativa desconhecida (sem T/M6 antes do trecho).");

        var cutTop = steps.Where(s => s.Motion is 1 or 2 or 3 && s.After.Z.IsAbsolute)
            .Select(s => s.After.Z.Value).DefaultIfEmpty(double.NaN).Max();
        var previous = entry;
        bool unknownZ = false, helical = false;
        foreach (var s in steps)
        {
            var a = s.After;
            bool xy = a.X != previous.X || a.Y != previous.Y;
            if (a.Tainted && !previous.Tainted)
                Add(blockers, "Linha " + s.Line + ": comando nao modelado no trecho (" + (s.Note ?? "?") + ").");
            if (s.Arc is { } arc)
            {
                // FANUC alarms when start and end radius differ beyond parameter 3410; 0.002 mm is conservative.
                if (arc.Plane is not (17 or 18 or 19)) Add(blockers, "Linha " + s.Line + ": arco sem plano G17/G18/G19 declarado.");
                else if (double.IsNaN(arc.StartRadius)) Add(blockers, "Linha " + s.Line + ": arco com posicao inicial desconhecida.");
                else if (Math.Abs(arc.StartRadius - arc.EndRadius) > 0.002)
                    Add(blockers, "Linha " + s.Line + ": raio inicial " + NcTokenizer.Decimal(arc.StartRadius) + " e final " + NcTokenizer.Decimal(arc.EndRadius) + " diferem.");
                if (arc.Helical)
                {
                    helical = true;
                    if (arc.UsesR) Add(blockers, "Linha " + s.Line + ": helice com R (exigir I/J para centro inequivoco).");
                    if (arc.Plane != 17) Add(blockers, "Linha " + s.Line + ": helice fora de G17 nao validada.");
                }
            }
            if (s.Motion is 1 or 2 or 3 or >= 73 && a.Feed <= 0)
                Add(blockers, "Linha " + s.Line + ": movimento de corte sem avanco F definido.");
            if (s.Motion >= 73 && a.Canned is { } c)
            {
                // Canned cycle hole: rapid to XY at the current level, rapid to R, feed to Z, return.
                if (!(c.R > c.Z)) Add(blockers, "Linha " + s.Line + ": ciclo fixo com R igual ou abaixo do fundo Z.");
                if (c.R <= 0) Add(blockers, "Linha " + s.Line + ": plano R <= 0 (aproximacao rapida abaixo da superficie).");
                if (xy && previous.Z.IsAbsolute && previous.Z.Value <= 0) Add(blockers, "Linha " + s.Line + ": posicionamento do ciclo com Z <= 0.");
                if (!a.Z.IsAbsolute) unknownZ = true;
                else if (a.Z.Value <= 0) Add(blockers, "Linha " + s.Line + ": retorno do ciclo em Z <= 0.");
            }
            if (s.Motion == 0 && xy)
            {
                if (!a.Z.IsAbsolute || !previous.Z.IsAbsolute) unknownZ = true;
                else
                {
                    var z = Math.Min(a.Z.Value, previous.Z.Value);
                    if (z <= 0) Add(blockers, "Linha " + s.Line + ": G00 em XY com Z <= 0 (" + NcTokenizer.Decimal(z) + ").");
                    if (!double.IsNaN(cutTop) && z <= cutTop) Add(blockers, "Linha " + s.Line + ": G00 em XY na altura de corte.");
                    if (a.Z.Value < previous.Z.Value)
                        Add(warnings, "Linha " + s.Line + ": G00 combinando XY e descida em Z (verificar interpolacao de rapido, parametro 1401#1).");
                }
            }
            previous = a;
        }
        if (unknownZ) warnings.Add("Z absoluto desconhecido em rapidos XY: auditoria de altura limitada.");
        if (helical) warnings.Add("Interpolacao helicoidal: conferir no simulador o passo por volta e o avanco (F no arco do plano XY).");
        return (blockers, warnings);
    }

    private static void Add(List<string> list, string message)
    {
        if (list.Count < 20 && !list.Contains(message)) list.Add(message);
    }
}
