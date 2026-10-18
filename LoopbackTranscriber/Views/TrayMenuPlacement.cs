namespace LoopbackTranscriber.Views;

internal readonly record struct ScreenArea(int Left, int Top, int Right, int Bottom);

internal static class TrayMenuPlacement
{
    public static (int X, int Y) Compute(int cursorX, int cursorY, int width, int height, int margin, int gap, ScreenArea work)
    {
        var x = cursorX - width / 2;
        x = Math.Min(x, work.Right - width + margin - gap);
        x = Math.Max(x, work.Left - margin + gap);

        int y;
        if (cursorY > (work.Top + work.Bottom) / 2)
        {
            y = Math.Min(cursorY, work.Bottom) - height + margin - gap;
        }
        else
        {
            y = Math.Max(cursorY, work.Top) - margin + gap;
        }

        y = Math.Min(y, work.Bottom - height + margin - gap);
        y = Math.Max(y, work.Top - margin + gap);

        return (x, y);
    }
}
