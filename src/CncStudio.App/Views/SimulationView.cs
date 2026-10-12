using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using CncStudio.Core.Simulation;

namespace CncStudio.App.Views;

/// <summary>
/// Desenha um <see cref="SimulationFrame"/>: superior (mapa de profundidade),
/// frontal/lateral (perfis 2D) ou isométrica (3D sombreada). Zoom/pan vêm da base.
/// </summary>
public sealed class SimulationView : ZoomPanControl
{
    private static readonly IBrush SilhouetteBrush = new SolidColorBrush(Color.Parse("#4A525C")).ToImmutable();
    private static readonly IBrush SectionBrush = new SolidColorBrush(Color.Parse("#A9B4BF")).ToImmutable();
    private static readonly IBrush ToolBrush = new SolidColorBrush(Color.FromArgb(150, 255, 160, 40)).ToImmutable();
    private static readonly Pen ToolPen = new(new SolidColorBrush(Color.Parse("#FFB040")).ToImmutable(), 1.5);
    private static readonly Pen StockOutlinePen = new(new SolidColorBrush(Color.Parse("#606870")).ToImmutable(), 1);

    private SimulationFrame? _frame;
    private WriteableBitmap? _topBitmap;
    private WriteableBitmap? _isoBitmap;

    /// <summary>Mostra um novo quadro. "fit" reenquadra (ex.: troca de vista ou de programa).</summary>
    public void ShowFrame(SimulationFrame frame, bool fit)
    {
        _frame = frame;
        if (frame.TopPixels is not null)
        {
            CopyToBitmap(ref _topBitmap, frame.TopPixels, frame.Columns, frame.Rows);
        }

        if (frame.Iso is not null)
        {
            CopyToBitmap(ref _isoBitmap, frame.Iso.Pixels, frame.Iso.Width, frame.Iso.Height);
        }

        if (fit)
        {
            FitToContent();
        }
        else
        {
            InvalidateVisual();
        }
    }

    /// <summary>Limpa a vista (ex.: programa de torno, ainda sem simulação).</summary>
    public void Clear()
    {
        _frame = null;
        InvalidateVisual();
    }

    protected override Rect? ContentBounds
    {
        get
        {
            if (_frame is not { } f)
            {
                return null;
            }

            double toolTop = f.TopZ + f.ToolDrawLength;
            return f.Mode switch
            {
                SimulationViewMode.Top => new Rect(f.OriginX, f.OriginY, f.Width, f.Height),
                SimulationViewMode.Front => new Rect(f.OriginX, f.BottomZ, f.Width, toolTop - f.BottomZ),
                SimulationViewMode.Side => new Rect(f.OriginY, f.BottomZ, f.Height, toolTop - f.BottomZ),
                _ => f.Iso is { } iso
                    ? new Rect(iso.MinU, iso.MaxV - iso.HeightMm, iso.WidthMm, iso.HeightMm + f.ToolDrawLength)
                    : null,
            };
        }
    }

    protected override void RenderContent(DrawingContext context)
    {
        if (_frame is not { } f)
        {
            DrawLabel(context, "Sem programa para simular", new Point(12, 12), LabelBrush);
            return;
        }

        switch (f.Mode)
        {
            case SimulationViewMode.Top:
                RenderTop(context, f);
                break;
            case SimulationViewMode.Front:
                RenderProfile(context, f, f.OriginX, f.CellSize, f.ToolPosition.X, $"Corte em Y = {f.ToolPosition.Y:0.###}");
                break;
            case SimulationViewMode.Side:
                RenderProfile(context, f, f.OriginY, f.CellSize, f.ToolPosition.Y, $"Corte em X = {f.ToolPosition.X:0.###}");
                break;
            case SimulationViewMode.Iso:
                RenderIso(context, f);
                break;
        }
    }

    private void RenderTop(DrawingContext context, SimulationFrame f)
    {
        if (_topBitmap is null)
        {
            return;
        }

        Rect dest = WorldRect(f.OriginX, f.OriginY, f.OriginX + f.Width, f.OriginY + f.Height);
        context.DrawImage(_topBitmap, new Rect(0, 0, f.Columns, f.Rows), dest);
        context.DrawRectangle(null, StockOutlinePen, dest);

        // Ferramenta: círculo do diâmetro real
        Point center = ToScreen(f.ToolPosition.X, f.ToolPosition.Y);
        double radius = f.ToolDiameter / 2 * Scale;
        context.DrawEllipse(ToolBrush, ToolPen, center, radius, radius);
        DrawLabel(context, "Cor = profundidade · cinza = topo do bruto", new Point(8, Bounds.Height - 20), LabelBrush, 11);
    }

    /// <summary>Vista frontal/lateral: silhueta (escura), corte na ferramenta (claro) e a ferramenta.</summary>
    private void RenderProfile(DrawingContext context, SimulationFrame f, double origin, double cell, double toolU, string caption)
    {
        if (f.Silhouette is null || f.Section is null)
        {
            return;
        }

        var transform = new MatrixTransform(WorldToScreen);

        StreamGeometry silhouette = ProfileGeometry(f.Silhouette, origin, cell, f.BottomZ);
        silhouette.Transform = transform;
        context.DrawGeometry(SilhouetteBrush, null, silhouette);

        StreamGeometry section = ProfileGeometry(f.Section, origin, cell, f.BottomZ);
        section.Transform = transform;
        context.DrawGeometry(SectionBrush, null, section);

        // Ferramenta: retângulo da largura do diâmetro, da ponta para cima
        double r = f.ToolDiameter / 2;
        Rect tool = WorldRect(toolU - r, f.ToolPosition.Z, toolU + r, f.ToolPosition.Z + f.ToolDrawLength);
        context.DrawRectangle(ToolBrush, ToolPen, tool);

        DrawLabel(context, $"{caption} · claro = material no corte · escuro = material à frente/atrás",
            new Point(8, Bounds.Height - 20), LabelBrush, 11);
    }

    private void RenderIso(DrawingContext context, SimulationFrame f)
    {
        if (_isoBitmap is null || f.Iso is not { } iso)
        {
            return;
        }

        Rect dest = WorldRect(iso.MinU, iso.MaxV - iso.HeightMm, iso.MinU + iso.WidthMm, iso.MaxV);
        context.DrawImage(_isoBitmap, new Rect(0, 0, iso.Width, iso.Height), dest);

        // Ferramenta: "cilindro" do diâmetro, da ponta para cima
        (double tipU, double tipV) = IsoProjection.Project(f.ToolPosition);
        Point tip = ToScreen(tipU, tipV);
        Point top = ToScreen(tipU, tipV + f.ToolDrawLength);
        var body = new Pen(ToolBrush, Math.Max(2, f.ToolDiameter * IsoProjection.Cos30 * Scale));
        context.DrawLine(body, tip, top);
        context.DrawEllipse(null, ToolPen, tip, f.ToolDiameter / 2 * IsoProjection.Cos30 * Scale, f.ToolDiameter / 4 * Scale);

        DrawLabel(context, "Isométrica (vista do canto X+ / Y−)", new Point(8, Bounds.Height - 20), LabelBrush, 11);
    }

    /// <summary>Polígono em degraus: base em "bottom", topo nas alturas de cada célula.</summary>
    private static StreamGeometry ProfileGeometry(float[] heights, double origin, double cell, double bottom)
    {
        var geometry = new StreamGeometry();
        using (StreamGeometryContext g = geometry.Open())
        {
            g.BeginFigure(new Point(origin, bottom), true);
            for (int i = 0; i < heights.Length; i++)
            {
                g.LineTo(new Point(origin + i * cell, heights[i]));
                g.LineTo(new Point(origin + (i + 1) * cell, heights[i]));
            }

            g.LineTo(new Point(origin + heights.Length * cell, bottom));
            g.EndFigure(true);
        }

        return geometry;
    }

    /// <summary>Retângulo na tela a partir de um retângulo no mundo (u0..u1, v0..v1).</summary>
    private Rect WorldRect(double u0, double v0, double u1, double v1) =>
        new(ToScreen(u0, v1), ToScreen(u1, v0));

    /// <summary>Copia pixels BGRA para o bitmap (recria se o tamanho mudou).</summary>
    private static unsafe void CopyToBitmap(ref WriteableBitmap? bitmap, uint[] pixels, int width, int height)
    {
        if (bitmap is null || bitmap.PixelSize.Width != width || bitmap.PixelSize.Height != height)
        {
            bitmap?.Dispose();
            bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        }

        using ILockedFramebuffer buffer = bitmap.Lock();
        int stride = buffer.RowBytes / 4;
        var destination = new Span<uint>((void*)buffer.Address, stride * height);
        for (int row = 0; row < height; row++)
        {
            pixels.AsSpan(row * width, width).CopyTo(destination.Slice(row * stride, width));
        }
    }
}
