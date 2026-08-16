using System.Windows.Automation;
using System.Runtime.InteropServices;
using System.Text;

namespace Comienzo.Services;

internal sealed class StartButtonLocator : IDisposable
{
    private readonly System.Threading.Timer _timer;
    private volatile StartButtonBounds[] _rectangles = Array.Empty<StartButtonBounds>();
    private int _refreshing;

    public StartButtonLocator()
    {
        _timer = new System.Threading.Timer(_ => Refresh(), null, 0, 2500);
    }

    public bool Contains(int x, int y) => _rectangles.Any(rect => rect.Contains(x, y));
    public bool HasAny => _rectangles.Length > 0;

    public StartButtonBounds? FindNearest(int x, int y)
    {
        StartButtonBounds[] snapshot = _rectangles;
        if (snapshot.Length == 0) return null;
        return snapshot.OrderBy(rect => rect.DistanceSquaredTo(x, y)).First();
    }

    private void Refresh()
    {
        if (Interlocked.Exchange(ref _refreshing, 1) != 0) return;
        try
        {
            var rectangles = new List<StartButtonBounds>();
            foreach (IntPtr taskbar in FindTaskbars())
            {
                AutomationElement root = AutomationElement.FromHandle(taskbar);
                var byId = new PropertyCondition(AutomationElement.AutomationIdProperty, "StartButton");
                AddRectangles(root.FindAll(TreeScope.Descendants, byId), rectangles);
                if (rectangles.Count == 0)
                {
                    var byClass = new PropertyCondition(AutomationElement.ClassNameProperty, "Start");
                    AddRectangles(root.FindAll(TreeScope.Descendants, byClass), rectangles);
                }
            }
            if (rectangles.Count > 0) _rectangles = rectangles.ToArray();
        }
        catch
        {
            // Explorer may be restarting. Keep the previous known rectangles.
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
        }
    }

    private static void AddRectangles(AutomationElementCollection elements, List<StartButtonBounds> output)
    {
        foreach (AutomationElement element in elements)
        {
            try
            {
                System.Windows.Rect rect = element.Current.BoundingRectangle;
                if (!rect.IsEmpty && rect.Width is >= 24 and <= 120 && rect.Height is >= 24 and <= 120)
                    output.Add(new StartButtonBounds((int)rect.Left, (int)rect.Top, (int)rect.Right, (int)rect.Bottom));
            }
            catch (ElementNotAvailableException) { }
        }
    }

    public void Dispose() => _timer.Dispose();

    private static IReadOnlyList<IntPtr> FindTaskbars()
    {
        var handles = new List<IntPtr>();
        EnumWindows((window, _) =>
        {
            var className = new StringBuilder(64);
            GetClassName(window, className, className.Capacity);
            if (className.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") handles.Add(window);
            return true;
        }, IntPtr.Zero);
        return handles;
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder className, int maximum);
}

internal readonly record struct StartButtonBounds(int Left, int Top, int Right, int Bottom)
{
    public int CenterX => Left + (Right - Left) / 2;
    public int CenterY => Top + (Bottom - Top) / 2;
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

    public long DistanceSquaredTo(int x, int y)
    {
        long dx = CenterX - x;
        long dy = CenterY - y;
        return dx * dx + dy * dy;
    }
}
