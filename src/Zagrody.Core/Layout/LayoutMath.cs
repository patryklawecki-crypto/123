namespace Zagrody.Core.Layout;

public readonly record struct Bounds(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public bool Intersects(Bounds other) =>
        X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
}

[Flags]
public enum ResizeEdges
{
    None = 0,
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8,
}

/// <param name="Gap">Space kept between neighbouring fences and from screen edges.</param>
/// <param name="Threshold">How close an edge has to come before it snaps.</param>
/// <param name="Grid">Grid step used when nothing is close enough; 0 disables the grid.</param>
public readonly record struct SnapOptions(double Gap, double Threshold, double Grid, bool SnapToNeighbours);

public static class LayoutMath
{
    public static double Snap(double value, int grid) =>
        grid <= 1 ? value : Math.Round(value / grid) * grid;

    /// <summary>
    /// Makes sure a fence restored from config is reachable: its title bar has to sit on some
    /// work area (monitors get unplugged, resolutions change). Otherwise it moves onto the primary one.
    /// </summary>
    public static Bounds EnsureVisible(Bounds window, IReadOnlyList<Bounds> workAreas, double titleHeight)
    {
        if (workAreas.Count == 0)
        {
            return window;
        }

        var title = window with { Height = titleHeight };
        foreach (var area in workAreas)
        {
            // Require a reasonable chunk of the title to be visible, not just one pixel.
            var visibleWidth = Math.Min(title.Right, area.Right) - Math.Max(title.X, area.X);
            if (title.Intersects(area) && title.Y >= area.Y && visibleWidth >= Math.Min(60, window.Width))
            {
                return window;
            }
        }

        var primary = workAreas[0];
        var width = Math.Min(window.Width, primary.Width);
        var height = Math.Min(window.Height, primary.Height);
        var x = Math.Clamp(window.X, primary.X, primary.Right - width);
        var y = Math.Clamp(window.Y, primary.Y, primary.Bottom - height);
        return new Bounds(x, y, width, height);
    }

    /// <summary>Picks a spot for a new fence that doesn't overlap existing ones, scanning in a grid.</summary>
    public static Bounds FindFreeSpot(Bounds workArea, double width, double height, IReadOnlyList<Bounds> occupied, double margin = 16)
    {
        for (var x = workArea.X + margin; x + width <= workArea.Right; x += width / 2)
        {
            for (var y = workArea.Y + margin; y + height <= workArea.Bottom; y += height / 2)
            {
                var candidate = new Bounds(x, y, width, height);
                var padded = new Bounds(x - margin / 2, y - margin / 2, width + margin, height + margin);
                if (!occupied.Any(o => o.Intersects(padded)))
                {
                    return candidate;
                }
            }
        }

        // Desktop is full: cascade from the corner.
        var offset = (occupied.Count % 10) * 24;
        return new Bounds(workArea.X + margin + offset, workArea.Y + margin + offset, width, height);
    }

    /// <summary>
    /// Magnetic snapping for a fence being dragged: edges stick to neighbouring fences (side by side
    /// or stacked, keeping <see cref="SnapOptions.Gap"/>), line up with their edges, and keep the gap
    /// from screen edges. When nothing is in reach the position falls onto the grid.
    /// </summary>
    public static Bounds SnapMove(Bounds moving, IReadOnlyList<Bounds> others, IReadOnlyList<Bounds> workAreas, SnapOptions o)
    {
        var y = SnapInterval(moving.Y, moving.Height, VerticalStarts(moving, others, workAreas, o), VerticalEnds(moving, others, workAreas, o), o);
        var placed = moving with { Y = y };
        var x = SnapInterval(moving.X, moving.Width, HorizontalStarts(placed, others, workAreas, o), HorizontalEnds(placed, others, workAreas, o), o);
        return placed with { X = x };
    }

    /// <summary>Snapping for a fence being resized: only the edges being dragged move.</summary>
    public static Bounds SnapResize(Bounds b, ResizeEdges edges, IReadOnlyList<Bounds> others, IReadOnlyList<Bounds> workAreas, SnapOptions o, double minWidth, double minHeight)
    {
        double left = b.X, top = b.Y, right = b.Right, bottom = b.Bottom;
        if (edges.HasFlag(ResizeEdges.Left))
        {
            left = Math.Min(SnapEdge(left, HorizontalStarts(b, others, workAreas, o), o), right - minWidth);
        }
        if (edges.HasFlag(ResizeEdges.Right))
        {
            right = Math.Max(SnapEdge(right, HorizontalEnds(b, others, workAreas, o), o), left + minWidth);
        }
        if (edges.HasFlag(ResizeEdges.Top))
        {
            top = Math.Min(SnapEdge(top, VerticalStarts(b, others, workAreas, o), o), bottom - minHeight);
        }
        if (edges.HasFlag(ResizeEdges.Bottom))
        {
            bottom = Math.Max(SnapEdge(bottom, VerticalEnds(b, others, workAreas, o), o), top + minHeight);
        }
        return new Bounds(left, top, right - left, bottom - top);
    }

    // Candidate positions for the left edge: right of a neighbour beside us, aligned with a fence
    // stacked above/below us, or the gap from the screen edge. The other three directions mirror it.
    private static IEnumerable<double> HorizontalStarts(Bounds m, IReadOnlyList<Bounds> others, IReadOnlyList<Bounds> areas, SnapOptions o)
    {
        foreach (var a in areas) yield return a.X + o.Gap;
        if (!o.SnapToNeighbours) yield break;
        foreach (var n in others)
        {
            var distance = RangeDistance(m.Y, m.Bottom, n.Y, n.Bottom);
            if (distance < 0) yield return n.Right + o.Gap;
            else if (distance <= o.Gap + o.Threshold) yield return n.X;
        }
    }

    private static IEnumerable<double> HorizontalEnds(Bounds m, IReadOnlyList<Bounds> others, IReadOnlyList<Bounds> areas, SnapOptions o)
    {
        foreach (var a in areas) yield return a.Right - o.Gap;
        if (!o.SnapToNeighbours) yield break;
        foreach (var n in others)
        {
            var distance = RangeDistance(m.Y, m.Bottom, n.Y, n.Bottom);
            if (distance < 0) yield return n.X - o.Gap;
            else if (distance <= o.Gap + o.Threshold) yield return n.Right;
        }
    }

    private static IEnumerable<double> VerticalStarts(Bounds m, IReadOnlyList<Bounds> others, IReadOnlyList<Bounds> areas, SnapOptions o)
    {
        foreach (var a in areas) yield return a.Y + o.Gap;
        if (!o.SnapToNeighbours) yield break;
        foreach (var n in others)
        {
            var distance = RangeDistance(m.X, m.Right, n.X, n.Right);
            if (distance < 0) yield return n.Bottom + o.Gap;
            else if (distance <= o.Gap + o.Threshold) yield return n.Y;
        }
    }

    private static IEnumerable<double> VerticalEnds(Bounds m, IReadOnlyList<Bounds> others, IReadOnlyList<Bounds> areas, SnapOptions o)
    {
        foreach (var a in areas) yield return a.Bottom - o.Gap;
        if (!o.SnapToNeighbours) yield break;
        foreach (var n in others)
        {
            var distance = RangeDistance(m.X, m.Right, n.X, n.Right);
            if (distance < 0) yield return n.Y - o.Gap;
            else if (distance <= o.Gap + o.Threshold) yield return n.Bottom;
        }
    }

    /// <summary>Gap between two 1-D ranges; negative when they overlap.</summary>
    private static double RangeDistance(double a1, double a2, double b1, double b2) => Math.Max(a1 - b2, b1 - a2);

    private static double SnapInterval(double start, double size, IEnumerable<double> startTargets, IEnumerable<double> endTargets, SnapOptions o)
    {
        var best = double.MaxValue;
        var result = start;
        foreach (var t in startTargets)
        {
            var d = Math.Abs(start - t);
            if (d <= o.Threshold && d < best)
            {
                best = d;
                result = t;
            }
        }
        foreach (var t in endTargets)
        {
            var d = Math.Abs(start + size - t);
            if (d <= o.Threshold && d < best)
            {
                best = d;
                result = t - size;
            }
        }
        return best == double.MaxValue && o.Grid > 1 ? Math.Round(start / o.Grid) * o.Grid : result;
    }

    private static double SnapEdge(double edge, IEnumerable<double> targets, SnapOptions o)
    {
        var best = double.MaxValue;
        var result = edge;
        foreach (var t in targets)
        {
            var d = Math.Abs(edge - t);
            if (d <= o.Threshold && d < best)
            {
                best = d;
                result = t;
            }
        }
        return best == double.MaxValue && o.Grid > 1 ? Math.Round(edge / o.Grid) * o.Grid : result;
    }
}
