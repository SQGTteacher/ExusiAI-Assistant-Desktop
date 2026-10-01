using System.Windows;

namespace ExusiAI.Plugin.ClassIsland;

public static class ClassIslandWindowPlacement
{
    // Keep at least a draggable portion visible even when a saved display has been removed.
    public static Point Clamp(Point desired, Size window, Rect workArea)
    {
        const double visibleEdge = 48;
        var x = Math.Clamp(desired.X, workArea.Left - Math.Max(0, window.Width - visibleEdge),
            workArea.Right - Math.Min(visibleEdge, window.Width));
        var y = Math.Clamp(desired.Y, workArea.Top,
            Math.Max(workArea.Top, workArea.Bottom - Math.Min(visibleEdge, window.Height)));
        return new Point(x, y);
    }
}
