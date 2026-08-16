namespace Comienzo.Services;

internal readonly record struct WindowPlacement(int X, int Y);

internal static class WindowPositionService
{
    public static WindowPlacement Calculate(StartButtonBounds? start, System.Drawing.Rectangle full,
        System.Drawing.Rectangle work, int menuWidth, int menuHeight, int gap = 8)
    {
        bool taskbarLeft = work.Left > full.Left;
        bool taskbarRight = work.Right < full.Right;
        bool taskbarTop = work.Top > full.Top;

        int x;
        int y;
        if (taskbarLeft)
        {
            x = work.Left + gap;
            y = start?.Top ?? work.Top;
        }
        else if (taskbarRight)
        {
            x = work.Right - menuWidth - gap;
            y = start?.Top ?? work.Top;
        }
        else
        {
            int buttonCenter = start?.CenterX ?? work.Left;
            x = buttonCenter - menuWidth / 2;
            y = taskbarTop ? work.Top + gap : work.Bottom - menuHeight - gap;
        }

        int minimumX = work.Left + gap;
        int maximumX = Math.Max(minimumX, work.Right - menuWidth - gap);
        int minimumY = work.Top + gap;
        int maximumY = Math.Max(minimumY, work.Bottom - menuHeight - gap);
        x = Math.Clamp(x, minimumX, maximumX);
        y = Math.Clamp(y, minimumY, maximumY);
        return new WindowPlacement(x, y);
    }
}
