using System.Globalization;
using System.Net;
using System.Text;
using CncStudio.Core.Dialects;
using CncStudio.Core.Execution;
using CncStudio.Core.Geometry;
using CncStudio.Core.Statistics;
using CncStudio.Core.Trajectory;

namespace CncStudio.Core.Reports;

/// <summary>
/// Gera a folha de setup como uma página HTML pronta para imprimir (A4) ou salvar em PDF
/// pelo navegador. Tudo num arquivo só: estilos embutidos e o desenho da trajetória em SVG
/// (vetorial, fica nítido na impressão). Sem bibliotecas externas.
/// </summary>
public static class SetupSheetHtml
{
    // Cores para papel branco (mesma ideia do backplot: G0 vermelho, G1 verde, G2 azul-claro, G3 azul-escuro)
    private static readonly string[] MotionColors = ["#D62828", "#2A9D3F", "#2F9FD8", "#1D3FBF"];
    private const int MaxDrawingPoints = 150_000;

    /// <param name="printHint">Mostra a dica "Ctrl+P → Salvar como PDF" (desktop). A versão web tem botões próprios.</param>
    /// <param name="watermark">Texto em diagonal sobre a folha (ex.: "VERSÃO DE AVALIAÇÃO"); null = sem.</param>
    public static string Render(SetupSheet sheet, IReadOnlyList<PathPolyline> figures, CultureInfo? culture = null,
        bool printHint = true, string? watermark = null)
    {
        culture ??= CultureInfo.CurrentCulture;
        bool lathe = sheet.Machine == MachineKind.Lathe;
        string N(double value, string format = "0.###") => value.ToString(format, culture);
        string T(TimeSpan time) => DurationFormat.Format(time, culture);
        string E(string? text) => WebUtility.HtmlEncode(text ?? "");

        string title = sheet.ProgramNumber is { Length: > 0 } number ? $"{number} — {sheet.FileName}" : sheet.FileName;
        var html = new StringBuilder();
        html.Append($$"""
            <!doctype html>
            <html lang="pt-BR">
            <head>
            <meta charset="utf-8">
            <title>Folha de setup — {{E(title)}}</title>
            <style>
              @page { size: A4; margin: 12mm; }
              * { box-sizing: border-box; }
              body { font-family: "Segoe UI", Arial, sans-serif; font-size: 10.5pt; color: #111; margin: 0; padding: 16px; background: #fff; }
              h1 { font-size: 17pt; margin: 0; }
              h2 { font-size: 11.5pt; margin: 16px 0 6px; padding-bottom: 3px; border-bottom: 2px solid #222; text-transform: uppercase; letter-spacing: .04em; }
              .top { display: flex; justify-content: space-between; align-items: flex-end; border-bottom: 3px solid #111; padding-bottom: 6px; }
              .top .meta { text-align: right; font-size: 9pt; color: #444; }
              table { border-collapse: collapse; width: 100%; }
              th, td { border: 1px solid #999; padding: 3px 6px; vertical-align: top; }
              th { background: #eee; text-align: left; font-weight: 600; }
              td.n, th.n { text-align: right; white-space: nowrap; font-variant-numeric: tabular-nums; }
              tr.total td { font-weight: 700; background: #f4f4f4; }
              .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 0 16px; }
              .info td:first-child { width: 38%; color: #444; }
              .totals { display: flex; gap: 8px; margin: 6px 0; }
              .totals div { flex: 1; border: 1px solid #999; padding: 6px 8px; }
              .totals b { display: block; font-size: 14pt; }
              .totals span { font-size: 8.5pt; color: #444; }
              .bar { height: 8px; background: #2A9D3F; }
              .drawings { display: flex; gap: 10px; }
              .drawings figure { flex: 1; margin: 0; border: 1px solid #999; padding: 4px; }
              .drawings figcaption { font-size: 8.5pt; color: #444; }
              .legend span { margin-right: 10px; font-size: 8.5pt; }
              .notes { border: 1px solid #999; height: 70px; }
              .sign { display: flex; gap: 24px; margin-top: 18px; }
              .sign div { flex: 1; border-top: 1px solid #111; padding-top: 2px; font-size: 9pt; color: #444; }
              .small { font-size: 8.5pt; color: #555; }
              .warn { color: #A00; font-weight: 600; }
              .code { display: block; font-size: 8.5pt; color: #555; }
              .brand { color: #E8590C; font-weight: 700; }
              .wm { position: fixed; left: 0; right: 0; top: 40%; transform: rotate(-28deg); text-align: center; font-size: 46pt;
                    font-weight: 800; color: rgba(214, 40, 40, .16); pointer-events: none; z-index: 10; white-space: nowrap; }
              /* Celular: uma coluna, tabela de ferramentas sem as colunas secundárias */
              @media screen and (max-width: 640px) {
                body { padding: 10px; font-size: 10pt; }
                h1 { font-size: 13pt; word-break: break-all; }
                .grid { grid-template-columns: 1fr; }
                .totals { flex-wrap: wrap; }
                .totals div { flex: 1 1 40%; }
                .opt { display: none; }
                .drawings { flex-direction: column; }
                .wm { font-size: 26pt; }
              }
              @media print { body { padding: 0; } .noprint { display: none; } h2 { break-after: avoid; } tr { break-inside: avoid; } }
            </style>
            </head>
            <body>
            {{(printHint ? "<p class=\"noprint small\">Para salvar em PDF: Ctrl+P → \"Salvar como PDF\" (ou \"Microsoft Print to PDF\").</p>" : "")}}
            {{(watermark is { Length: > 0 } ? "<div class=\"wm\">" + E(watermark) + "</div>" : "")}}
            <div class="top">
              <div>
                <div class="small">FOLHA DE SETUP</div>
                <h1>{{E(title)}}</h1>
              </div>
              <div class="meta">Gerada em {{E(sheet.Generated.ToString("g", culture))}}<br><span class="brand">Casillas Code</span></div>
            </div>

            """);

        // ── Informações do programa ──
        html.Append("<h2>Programa</h2><div class=\"grid\"><table class=\"info\">");
        Row("Arquivo", E(sheet.FileName));
        Row("Local", E(sheet.FilePath ?? "(não salvo)"));
        Row("Programa", E(sheet.ProgramNumber ?? "—"));
        Row("Comando", sheet.Dialect == Dialect.Siemens ? "Siemens" : "Fanuc");
        Row("Máquina", lathe ? "Torno" : "Fresa");
        Row("Zeros de peça", E(sheet.WorkOffsets.Count == 0 ? "(nenhum programado)" : string.Join(", ", sheet.WorkOffsets)));
        html.Append("</table><table class=\"info\">");
        Row("Linhas / blocos", $"{sheet.LineCount.ToString("N0", culture)} / {sheet.BlockCount.ToString("N0", culture)}");
        Row("Movimentos", sheet.MoveCount.ToString("N0", culture));
        Row("Bruto", E(sheet.Stock ?? "(não informado)"));
        Row("Limites de corte", Limits(sheet.CuttingBounds));
        Row("Limites com G00", Limits(sheet.AllBounds));
        string problems = sheet.ErrorCount + sheet.WarningCount == 0
            ? "nenhum"
            : $"<span class=\"{(sheet.ErrorCount > 0 ? "warn" : "")}\">{sheet.ErrorCount} erro(s), {sheet.WarningCount} aviso(s)</span>";
        Row("Verificação", problems);
        html.Append("</table></div>");

        if (sheet.HeaderComments.Count > 0)
        {
            html.Append("<p class=\"small\"><b>Cabeçalho:</b> ")
                .Append(string.Join(" · ", sheet.HeaderComments.Select(E)))
                .Append("</p>");
        }

        // ── Tempos ──
        ProgramStatistics stats = sheet.Statistics;
        html.Append($"""
            <h2>Tempo de usinagem</h2>
            <div class="totals">
              <div><span>Tempo total estimado</span><b>{T(stats.TotalTime)}</b></div>
              <div><span>Corte</span><b>{T(stats.CuttingTime)}</b><span>{N(stats.CuttingDistance / 1000, "0.00")} m</span></div>
              <div><span>Rápido (G00)</span><b>{T(stats.RapidTime)}</b><span>{N(stats.RapidDistance / 1000, "0.00")} m a {N(stats.RapidRate, "N0")} mm/min</span></div>
              <div><span>Espera</span><b>{T(stats.DwellTime)}</b><span>{stats.ToolChanges} troca(s) de ferramenta</span></div>
            </div>
            """);

        // ── Ferramentas ──
        string reachHeader = lathe ? "Menor Ø" : "Z mín.";
        html.Append($"""
            <h2>Ferramentas</h2>
            <table>
            <tr><th>T</th><th>Operação / ferramenta</th>{(lathe ? "" : "<th class=\"n\">Ø</th>")}<th class="n opt">1ª linha</th><th class="n">{reachHeader}</th>
            <th class="n opt">Avanço (mm/min)</th><th class="n opt">Corte</th><th class="n opt">Rápido</th><th class="n opt">Espera</th><th class="n">Total</th><th style="width:16%">% do tempo</th></tr>
            """);
        foreach (SetupTool tool in sheet.Tools)
        {
            ToolStatistics t = tool.Statistics;
            string feed = t.MaxFeed <= 0 ? "—" : t.MinFeed == t.MaxFeed ? N(t.MaxFeed, "0") : $"{N(t.MinFeed, "0")}–{N(t.MaxFeed, "0")}";
            string diameterCell = lathe ? "" : $"<td class=\"n\">{(tool.Diameter is { } d ? N(d) : "—")}</td>";
            string percent = N(tool.Share * 100, "0.0");
            html.Append($"""
                <tr><td><b>{(tool.Number == 0 ? "—" : "T" + tool.Number)}</b></td><td>{E(tool.Description)}{(tool.Code is { Length: > 0 } code ? "<span class=\"code\">" + E(code) + "</span>" : "")}</td>{diameterCell}
                <td class="n opt">{tool.FirstLine}</td><td class="n">{ReachCell(tool)}</td><td class="n opt">{feed}</td>
                <td class="n opt">{T(t.CuttingTime)}</td><td class="n opt">{T(t.RapidTime)}</td><td class="n opt">{T(t.DwellTime)}</td>
                <td class="n"><b>{T(t.TotalTime)}</b></td>
                <td><div class="bar" style="width:{N(Math.Max(1, tool.Share * 100), "0.#").Replace(',', '.')}%"></div><span class="small">{percent}%</span></td></tr>
                """);
        }

        // Total: as colunas antes de "Corte" (T, operação, [Ø], 1ª linha, alcance, avanço); no celular somem 1ª linha e avanço
        int lead = lathe ? 2 : 3;
        html.Append($"""
            <tr class="total"><td colspan="{lead}">Total ({sheet.Tools.Count(t => t.Number != 0)} ferramenta(s))</td>
            <td class="opt"></td><td></td><td class="opt"></td>
            <td class="n opt">{T(stats.CuttingTime)}</td><td class="n opt">{T(stats.RapidTime)}</td><td class="n opt">{T(stats.DwellTime)}</td>
            <td class="n">{T(stats.TotalTime)}</td><td>100%</td></tr>
            </table>
            """);

        if (stats.MovesWithoutFeed > 0)
        {
            html.Append($"<p class=\"warn\">{stats.MovesWithoutFeed} movimento(s) de corte sem avanço F não entraram no tempo.</p>");
        }

        // ── Desenho da trajetória ──
        html.Append("<h2>Trajetória</h2><div class=\"drawings\">");
        if (lathe)
        {
            html.Append(Figure(figures, p => (p.Z, p.X), "Torno — Z × X (raio) · região de corte", 700, 260));
        }
        else
        {
            html.Append(Figure(figures, p => (p.X, p.Y), "Superior — X × Y · região de corte", 340, 260));
            html.Append(Figure(figures, p => (p.X, p.Z), "Frontal — X × Z · região de corte", 340, 260));
        }

        html.Append("</div><p class=\"legend\">");
        string[] names = ["G00 (tracejado)", "G01", "G02", "G03"];
        for (int i = 0; i < 4; i++)
        {
            html.Append($"<span style=\"color:{MotionColors[i]}\">■ {names[i]}</span>");
        }

        html.Append("""
            </p>
            <h2>Observações</h2>
            <div class="notes"></div>
            <div class="sign"><div>Programado por</div><div>Conferido por</div><div>Data</div></div>
            <p class="small">Tempos estimados sem aceleração (comprimento ÷ avanço; rápido na velocidade configurada; esperas G04/ciclos).
            Programas com muitos movimentos curtos levam mais tempo na máquina.</p>
            </body>
            </html>
            """);

        return html.ToString();

        void Row(string label, string value) => html.Append($"<tr><td>{label}</td><td>{value}</td></tr>");

        // Torno: menor Ø; broca no centro (Ø 0) mostra a profundidade em Z
        string ReachCell(SetupTool tool) =>
            lathe && tool.Reach is < 0.001 ? (tool.Statistics.MinZ is { } z ? "Z " + N(z) : "—")
            : tool.Reach is { } r ? N(r) : "—";

        string Limits(Bounds3 b)
        {
            if (b.IsEmpty)
            {
                return "—";
            }

            return lathe
                ? $"Ø {N(Math.Abs(b.Min.X) * 2)} a {N(Math.Abs(b.Max.X) * 2)} · Z {N(b.Min.Z)} a {N(b.Max.Z)}"
                : $"X {N(b.Min.X)} a {N(b.Max.X)} · Y {N(b.Min.Y)} a {N(b.Max.Y)} · Z {N(b.Min.Z)} a {N(b.Max.Z)}";
        }
    }

    /// <summary>Desenho SVG de uma projeção da trajetória, ajustado à caixa (largura × altura).</summary>
    internal static string Figure(IReadOnlyList<PathPolyline> figures, Func<Point3, (double U, double V)> project,
        string caption, int width, int height)
    {
        int totalPoints = figures.Sum(f => f.Points.Length);
        int step = Math.Max(1, (int)Math.Ceiling(totalPoints / (double)MaxDrawingPoints));

        // Enquadra a região de corte (os rápidos de afastamento, ex.: X100 Z100, ficam cortados na borda)
        bool hasCut = figures.Any(f => !f.IsRapid);
        double minU = double.MaxValue, minV = double.MaxValue, maxU = double.MinValue, maxV = double.MinValue;
        foreach (PathPolyline figure in figures.Where(f => !hasCut || !f.IsRapid))
        {
            foreach (Point3 p in figure.Points)
            {
                (double u, double v) = project(p);
                minU = Math.Min(minU, u); maxU = Math.Max(maxU, u);
                minV = Math.Min(minV, v); maxV = Math.Max(maxV, v);
            }
        }

        var svg = new StringBuilder();
        svg.Append($"<figure><svg viewBox=\"0 0 {width} {height}\" width=\"100%\" style=\"overflow:hidden\" xmlns=\"http://www.w3.org/2000/svg\">");
        if (minU <= maxU)
        {
            double padU = (maxU - minU) * 0.08 + 1, padV = (maxV - minV) * 0.08 + 1;
            minU -= padU; maxU += padU; minV -= padV; maxV += padV;
            const double margin = 4;
            double spanU = Math.Max(maxU - minU, 1e-6);
            double spanV = Math.Max(maxV - minV, 1e-6);
            double scale = Math.Min((width - 2 * margin) / spanU, (height - 2 * margin) / spanV);
            double offsetU = (width - spanU * scale) / 2;
            double offsetV = (height - spanV * scale) / 2;
            string X(double u) => ((u - minU) * scale + offsetU).ToString("0.##", CultureInfo.InvariantCulture);
            string Y(double v) => (height - ((v - minV) * scale + offsetV)).ToString("0.##", CultureInfo.InvariantCulture);

            for (int motion = 0; motion < 4; motion++)
            {
                string dash = motion == 0 ? " stroke-dasharray=\"3 3\"" : "";
                svg.Append($"<g fill=\"none\" stroke=\"{MotionColors[motion]}\" stroke-width=\"{(motion == 0 ? 0.6 : 0.9).ToString(CultureInfo.InvariantCulture)}\"{dash}>");
                foreach (PathPolyline figure in figures.Where(f => (int)f.Motion == motion))
                {
                    svg.Append("<polyline points=\"");
                    for (int i = 0; i < figure.Points.Length; i += step)
                    {
                        (double u, double v) = project(figure.Points[i]);
                        svg.Append(X(u)).Append(',').Append(Y(v)).Append(' ');
                    }

                    (double lu, double lv) = project(figure.Points[^1]);
                    svg.Append(X(lu)).Append(',').Append(Y(lv)).Append("\"/>");
                }

                svg.Append("</g>");
            }
        }

        svg.Append($"</svg><figcaption>{WebUtility.HtmlEncode(caption)}</figcaption></figure>");
        return svg.ToString();
    }
}
