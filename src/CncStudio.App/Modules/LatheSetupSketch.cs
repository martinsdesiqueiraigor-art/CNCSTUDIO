using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CncStudio.Core.Cam;

namespace CncStudio.App.Modules;

/// <summary>
/// Desenho do bruto e da fixação do torno (UI-04), vista de lado e fora de escala: placa, castanhas (dura, mole ou comuns), bruto,
/// contraponto e luneta quando marcados, cotas de Ø, comprimento, comprimento preso e balanço, e a marca do Z0. Só mostra o que está
/// no trabalho: medida não informada aparece como "?", nunca um valor inventado. Informativo, como a fixação: nada é verificado.
/// </summary>
public sealed class LatheSetupSketch : Control
{
    private static readonly CultureInfo Br = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly Typeface Face = new(CamColors.UiFont);

    private LatheJobStock? _stock;
    private LatheFixation? _fixation;
    private LatheZeroFace? _zero;

    public LatheSetupSketch()
    {
        Height = 230;
        ToolTip.SetTip(this, "Desenho fora de escala, só com o que está no trabalho. O app não verifica fixação nem colisão.");
    }

    /// <summary>Redesenha com os dados atuais do trabalho.</summary>
    public void Show(LatheJobStock? stock, LatheFixation? fixation, LatheZeroFace? zero)
    {
        _stock = stock;
        _fixation = fixation;
        _zero = zero;
        InvalidateVisual();
    }

    private static string Mm(double? v) => v is { } x ? x.ToString("0.###", Br) : "?";

    public override void Render(DrawingContext dc)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 120)
        {
            return;
        }

        dc.FillRectangle(CamColors.ViewportBrush, new Rect(0, 0, w, h), 8);
        LatheFixationKind? kind = _fixation?.Kind;
        bool tail = _fixation is { } f && (f.Tailstock || f.Kind == LatheFixationKind.Center);
        bool steady = _fixation?.SteadyRest == true;
        double? diameter = _stock?.Diameter, length = _stock?.Length, grip = _fixation?.GripLength, stickOut = _fixation?.StickOut;

        // eixo, placa e bruto: proporções fixas (fora de escala); só a parte presa segue a razão preso/comprimento quando há os dois
        double axis = 92, chuckX = 14, chuckW = 34, barX = chuckX + chuckW;
        double barEnd = w - (tail ? 96 : 40);
        double barW = Math.Max(60, barEnd - barX);
        double half = 34;
        double gripRatio = grip is { } g && length is { } l && l > 0 ? Math.Clamp(g / l, 0.08, 0.7) : 0.22;
        double gripW = barW * gripRatio;

        var line = new Pen(CamColors.BorderStrongBrush, 1);
        var text = CamColors.TextBrush;
        var muted = CamColors.MutedBrush;

        // linha de centro
        dc.DrawLine(new Pen(CamColors.SelectBrush, 1) { DashStyle = new DashStyle([10, 3, 2, 3], 0) }, new Point(6, axis), new Point(w - 6, axis));

        // placa
        dc.FillRectangle(Brush.Parse("#4A515B"), new Rect(chuckX, axis - 70, chuckW, 140), 4);
        Label(dc, kind == LatheFixationKind.Faceplate ? "Placa lisa" : "Placa", chuckX + chuckW / 2, axis + 74, muted, center: true);

        // bruto
        var barRect = new Rect(barX, axis - half, barW, half * 2);
        dc.FillRectangle(Brush.Parse("#8E959E"), barRect);
        dc.DrawRectangle(new Pen(Brush.Parse("#C9CED4"), 1), barRect);

        // castanhas (não na placa lisa; sem fixação informada, nada é desenhado)
        if (kind is null)
        {
            Label(dc, "Fixação não informada", barX + 6, axis - half - 20, CamColors.PendingBrush, center: false);
        }
        else if (kind is not LatheFixationKind.Faceplate)
        {
            IBrush jaw = kind == LatheFixationKind.SoftJaws ? Brush.Parse("#9C8A6A") : Brush.Parse("#5B626C");
            dc.FillRectangle(jaw, new Rect(barX, axis - half - 16, gripW, 16));
            dc.FillRectangle(jaw, new Rect(barX, axis + half, gripW, 16));
            string jawName = kind switch
            {
                LatheFixationKind.SoftJaws => "Castanha mole",
                LatheFixationKind.HardJaws => "Castanha dura",
                _ => "Castanhas",
            };
            Label(dc, jawName, barX + gripW / 2, axis - half - 30, muted, center: true);
        }

        // luneta: anel no meio do balanço
        if (steady)
        {
            double sx = barX + gripW + (barW - gripW) / 2;
            dc.DrawRectangle(new Pen(CamColors.MutedBrush, 2), new Rect(sx - 6, axis - half - 12, 12, half * 2 + 24), 3);
            Label(dc, "Luneta", sx, axis - half - 30, muted, center: true);
        }

        // contraponto: ponta no centro da face livre
        if (tail)
        {
            double fx = barX + barW;
            var cone = new StreamGeometry();
            using (StreamGeometryContext g2 = cone.Open())
            {
                g2.BeginFigure(new Point(fx, axis), true);
                g2.LineTo(new Point(fx + 26, axis - 14));
                g2.LineTo(new Point(fx + 26, axis + 14));
                g2.EndFigure(true);
            }

            dc.DrawGeometry(Brush.Parse("#B4BAC2"), line, cone);
            dc.FillRectangle(Brush.Parse("#4A515B"), new Rect(fx + 26, axis - 24, Math.Min(64, w - fx - 32), 48), 4);
            Label(dc, "Contraponto", fx + 50, axis - 40, muted, center: true);
        }

        // Z0
        double zx = _zero == LatheZeroFace.Opposite ? barX + gripW : barX + barW;
        string zText = _zero switch
        {
            LatheZeroFace.Finished => "Z0 · face pronta",
            LatheZeroFace.Opposite => "Z0 · face oposta",
            _ => "Z0 · do desenho do perfil",
        };
        dc.DrawLine(new Pen(CamColors.AccentBrush, 2), new Point(zx, axis - half - 2), new Point(zx, axis - half - 14));
        Label(dc, zText, Math.Min(zx, w - 70), axis - half - 46, CamColors.AccentBrush, center: true);

        // cotas
        var dim = new Pen(text, 1);
        double dx = barX + barW - 28;
        dc.DrawLine(dim, new Point(dx, axis - half + 2), new Point(dx, axis + half - 2));
        Label(dc, "Ø " + Mm(diameter), dx - 6, axis - 8, text, center: false, right: true);
        double y1 = axis + half + 36, y2 = y1 + 26;
        dc.DrawLine(dim, new Point(barX, y1), new Point(barX + barW, y1));
        Label(dc, "Comprimento " + Mm(length), barX + barW / 2, y1 - 17, text, center: true);
        if (kind is not null and not LatheFixationKind.Faceplate)
        {
            dc.DrawLine(new Pen(CamColors.SelectBrush, 1), new Point(barX, y2), new Point(barX + gripW - 2, y2));
            Label(dc, "Preso " + Mm(grip), barX + gripW / 2, y2 - 17, CamColors.SelectBrush, center: true);
        }

        dc.DrawLine(new Pen(CamColors.PendingBrush, 1), new Point(barX + gripW + 2, y2), new Point(barX + barW, y2));
        Label(dc, "Balanço " + Mm(stickOut), barX + gripW + (barW - gripW) / 2, y2 - 17, CamColors.PendingBrush, center: true);
        Label(dc, "Fora de escala", w - 8, h - 18, muted, center: false, right: true);
    }

    private static void Label(DrawingContext dc, string s, double x, double y, IBrush brush, bool center, bool right = false)
    {
        var ft = new FormattedText(s, Br, FlowDirection.LeftToRight, Face, 11, brush);
        double left = center ? x - ft.Width / 2 : right ? x - ft.Width : x;
        dc.DrawText(ft, new Point(left, y));
    }
}
