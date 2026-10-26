namespace CncStudio.Core.Cam;

/// <summary>Converte o percurso de uma operação em linhas 2D para a prévia (só o que corta; o rápido não entra).</summary>
public static class ToolpathView
{
    public static List<List<Point2>> CuttingPolylines(CamOperation op, double tolerance = 0.02)
    {
        var lines = new List<List<Point2>>();
        List<Point2>? current = null;
        Point2? position = null;
        foreach (ToolMove m in op.Moves)
        {
            Point2 end = m.XY;
            if (m.Kind == MoveKind.Rapid)
            {
                current = null;
            }
            else if (position is { } start)
            {
                if (current is null)
                {
                    current = [start];
                    lines.Add(current);
                }

                if (m.IsArc)
                {
                    foreach (Point2 p in Segment2.Arc(start, end, m.Center, m.Kind == MoveKind.ArcCcw).Sample(tolerance).Skip(1))
                    {
                        current.Add(p);
                    }
                }
                else
                {
                    current.Add(end);
                }
            }

            position = end;
        }

        if (op.Drill is { } drill)
        {
            foreach (Point2 p in drill.Points)
            {
                lines.Add([new Point2(p.X - 1, p.Y), new Point2(p.X + 1, p.Y)]);
                lines.Add([new Point2(p.X, p.Y - 1), new Point2(p.X, p.Y + 1)]);
            }
        }

        return lines;
    }
}
