namespace Zagrody.Core.Layout;

public readonly record struct Bounds(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    public bool Intersects(Bounds other) =>
        X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;
}

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
}
