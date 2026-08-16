using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Comienzo.Models;
using Comienzo.Services;

namespace Comienzo;

public partial class MainWindow : Window
{
    private readonly SearchEngine _search = new();
    private bool _allowClose;
    private bool _menuIsOpen;
    private IntPtr _previousForegroundWindow;
    private bool _contentIsWarm;
    private bool _isPrewarming;
    private bool _catalogReady;
    private bool _showWhenReady;
    private readonly TaskCompletionSource _catalogReadySignal =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ObservableCollection<CatalogItem> Results { get; } = new();
    public bool DismissOnDeactivate { get; set; } = true;
    public bool EnableBackgroundPrewarm { get; set; } = true;
    public string SnapshotPath { get; set; } = "";
    public string InitialQuery { get; set; } = "";
    internal Func<StartButtonBounds?>? StartButtonBoundsProvider { get; set; }

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        Visibility = Visibility.Hidden;
    }

    public async Task LoadCatalogAsync()
    {
        IReadOnlyList<CatalogItem> applications = await AppDiscovery.DiscoverAsync();
        CatalogItem[] items = await Dispatcher.InvokeAsync(() =>
        {
            _search.SetApplications(applications);
            RefreshResults();
            return Results.ToArray();
        });
        await IconService.PrewarmAsync(items);
        await Dispatcher.InvokeAsync(() =>
        {
            PrepareForInstantShow();
            _catalogReady = true;
            _catalogReadySignal.TrySetResult();
            if (_showWhenReady)
            {
                _showWhenReady = false;
                ShowMenu();
            }
        });
        if (SnapshotPath.Length > 0)
            await Dispatcher.InvokeAsync(SaveSnapshot, System.Windows.Threading.DispatcherPriority.ContextIdle);
    }

    private void SaveSnapshot() => SaveSnapshot(SnapshotPath);

    private void SaveSnapshot(string path)
    {
        UpdateLayout();
        int width = Math.Max(1, (int)ActualWidth);
        int height = Math.Max(1, (int)ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? AppContext.BaseDirectory);
        using FileStream stream = File.Create(path);
        encoder.Save(stream);
    }

    internal Task WaitUntilCatalogReadyAsync() => _catalogReadySignal.Task;

    internal MenuTestState GetTestState()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        GetWindowRect(handle, out NativeRect bounds);
        IntPtr foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out uint foregroundProcessId);
        bool ownsForeground = foreground == handle || foregroundProcessId == Environment.ProcessId;
        string foregroundProcessName = "";
        try
        {
            using Process foregroundProcess = Process.GetProcessById((int)foregroundProcessId);
            foregroundProcessName = foregroundProcess.ProcessName;
        }
        catch { }
        return new MenuTestState(_menuIsOpen, IsActive, ownsForeground,
            SearchBox.Text, SearchBox.IsKeyboardFocusWithin, bounds.Left, bounds.Top,
            bounds.Right, bounds.Bottom, Width, handle.ToInt64(), foreground.ToInt64(),
            foregroundProcessId, foregroundProcessName);
    }

    internal ScrollTestState ExerciseFullScrollForTest()
    {
        UpdateLayout();
        ScrollViewer? viewer = FindVisualChild<ScrollViewer>(ResultsList);
        if (viewer is null) throw new InvalidOperationException("No se encontró el ScrollViewer de resultados.");

        long missesBefore = IconService.CacheMissCount;
        var timer = Stopwatch.StartNew();
        for (int pass = 0; pass < 3; pass++)
        {
            viewer.ScrollToEnd();
            UpdateLayout();
            viewer.ScrollToTop();
            UpdateLayout();
        }
        timer.Stop();
        return new ScrollTestState(timer.ElapsedMilliseconds,
            IconService.CacheMissCount - missesBefore, viewer.ScrollableHeight);
    }

    internal void SaveSnapshotForTest(string path) => SaveSnapshot(path);

    public void ToggleMenu()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(ToggleMenu);
            return;
        }
        if (!_catalogReady)
        {
            _showWhenReady = true;
            return;
        }
        if (_menuIsOpen) HideMenu(); else ShowMenu();
    }

    public void ShowMenu()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(ShowMenu);
            return;
        }
        if (!_catalogReady)
        {
            _showWhenReady = true;
            return;
        }
        if (SearchBox.Text.Length == 0 && InitialQuery.Length > 0) SearchBox.Text = InitialQuery;
        WindowState = WindowState.Normal;
        IntPtr handle = new WindowInteropHelper(this).EnsureHandle();
        IntPtr foreground = GetForegroundWindow();
        if (foreground != IntPtr.Zero && foreground != handle) _previousForegroundWindow = foreground;
        PositionBesideStartButton();
        _menuIsOpen = true;
        if (!IsVisible) Show();
        FocusWindowAndSearch();
        Dispatcher.BeginInvoke(FocusWindowAndSearch, System.Windows.Threading.DispatcherPriority.Input);
        _ = RetryForegroundFocusAsync();
    }

    private void HideMenu(bool resultsChanged = false)
    {
        _menuIsOpen = false;
        bool clearingSearchWillRefresh = SearchBox.Text.Length > 0;
        SearchBox.Clear();
        if (resultsChanged && !clearingSearchWillRefresh) RefreshResults();
        IntPtr handle = new WindowInteropHelper(this).Handle;
        bool restorePreviousFocus = handle != IntPtr.Zero && GetForegroundWindow() == handle;
        MoveWindowOffscreen();
        if (restorePreviousFocus && _previousForegroundWindow != IntPtr.Zero &&
            IsWindow(_previousForegroundWindow))
            SetForegroundWindow(_previousForegroundWindow);
        Dispatcher.BeginInvoke(PrepareForInstantShow,
            System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    public void PrepareForInstantShow()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(PrepareForInstantShow);
            return;
        }
        if (!EnableBackgroundPrewarm || _contentIsWarm || _isPrewarming || _menuIsOpen) return;

        _isPrewarming = true;
        bool firstShow = !IsVisible;
        bool previousShowActivated = ShowActivated;
        double previousOpacity = Opacity;
        WindowStartupLocation previousStartupLocation = WindowStartupLocation;
        try
        {
            ShowActivated = false;
            if (firstShow) Opacity = 0;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = SystemParameters.VirtualScreenLeft - Width - 100;
            Top = SystemParameters.VirtualScreenTop - Height - 100;
            if (firstShow) Show();
            MoveWindowOffscreen();
            UpdateLayout();

            int width = Math.Max(1, (int)Math.Ceiling(ActualWidth));
            int height = Math.Max(1, (int)Math.Ceiling(ActualHeight));
            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(this);
            _contentIsWarm = true;
        }
        finally
        {
            MoveWindowOffscreen();
            WindowStartupLocation = previousStartupLocation;
            Opacity = previousOpacity;
            ShowActivated = previousShowActivated;
            _isPrewarming = false;
        }
    }

    public void DismissIfOutside(int screenX, int screenY)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => DismissIfOutside(screenX, screenY));
            return;
        }
        if (!DismissOnDeactivate || !_menuIsOpen || _isPrewarming) return;

        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out NativeRect bounds) ||
            screenX < bounds.Left || screenX >= bounds.Right ||
            screenY < bounds.Top || screenY >= bounds.Bottom)
            HideMenu();
    }

    public void CloseForReal()
    {
        _allowClose = true;
        Close();
    }

    private void RefreshResults()
    {
        _contentIsWarm = false;
        IReadOnlyList<CatalogItem> found = _search.Search(SearchBox.Text);
        Results.Clear();
        foreach (CatalogItem item in found) Results.Add(item);
        ResultsList.SelectedIndex = Results.Count > 0 ? 0 : -1;
        Placeholder.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        Dispatcher.BeginInvoke(ScrollResultsToTop, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnSearchTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => RefreshResults();

    private void OnSearchPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            ResultsList.SelectedIndex = Math.Min(Results.Count - 1, ResultsList.SelectedIndex + 1);
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Up)
        {
            ResultsList.SelectedIndex = Math.Max(0, ResultsList.SelectedIndex - 1);
            ResultsList.ScrollIntoView(ResultsList.SelectedItem);
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            LaunchSelected();
            e.Handled = true;
        }
    }

    private void OnWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            HideMenu();
            e.Handled = true;
        }
    }

    private void OnResultClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source || FindAncestor<System.Windows.Controls.Primitives.ScrollBar>(source) is not null)
            return;
        if (ItemsControl.ContainerFromElement(ResultsList, source) is not System.Windows.Controls.ListBoxItem container)
            return;
        ResultsList.SelectedItem = container.DataContext;
        LaunchSelected();
        e.Handled = true;
    }

    private void LaunchSelected()
    {
        if (ResultsList.SelectedItem is not CatalogItem item) return;
        try
        {
            if (item.Kind == ItemKind.Calculator)
            {
                System.Windows.Clipboard.SetText(item.Target);
                return;
            }

            UsageService.Record(item);
            if (item.LaunchKind == LaunchKind.ExplorerShellApp)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{item.Target}")
                {
                    UseShellExecute = true
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo(item.Target)
                {
                    Arguments = item.Arguments,
                    WorkingDirectory = Directory.Exists(item.WorkingDirectory) ? item.WorkingDirectory : "",
                    UseShellExecute = true
                });
            }
            HideMenu(resultsChanged: true);
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, $"No se pudo abrir {item.Name}.\n\n{exception.Message}",
                "Comienzo", MessageBoxButton.OK, MessageBoxImage.Warning);
            FocusWindowAndSearch();
        }
    }

    private void ScrollResultsToTop()
    {
        FindVisualChild<ScrollViewer>(ResultsList)?.ScrollToTop();
    }

    private void PositionBesideStartButton()
    {
        StartButtonBounds? start = StartButtonBoundsProvider?.Invoke();
        System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
        System.Drawing.Point anchor = start is { } bounds
            ? new System.Drawing.Point(bounds.CenterX, bounds.CenterY)
            : cursor;
        System.Windows.Forms.Screen screen = System.Windows.Forms.Screen.FromPoint(anchor);
        System.Drawing.Rectangle work = screen.WorkingArea;
        System.Drawing.Rectangle full = screen.Bounds;

        double scale = GetScaleForPoint(anchor.X, anchor.Y);
        int menuWidth = (int)Math.Round(Width * scale);
        int menuHeight = (int)Math.Round(Height * scale);
        WindowPlacement placement = WindowPositionService.Calculate(start, full, work, menuWidth, menuHeight);
        IntPtr handle = new WindowInteropHelper(this).EnsureHandle();
        SetWindowPos(handle, HwndTopMost, placement.X, placement.Y, menuWidth, menuHeight,
            SwpNoActivate | SwpNoOwnerZOrder);
    }

    private void MoveWindowOffscreen()
    {
        if (!IsVisible) return;
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero) return;
        GetWindowRect(handle, out NativeRect bounds);
        System.Drawing.Rectangle virtualScreen = System.Windows.Forms.SystemInformation.VirtualScreen;
        int x = virtualScreen.Left - Math.Max(1, bounds.Right - bounds.Left) - 256;
        int y = virtualScreen.Top - Math.Max(1, bounds.Bottom - bounds.Top) - 256;
        SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0,
            SwpNoActivate | SwpNoOwnerZOrder | SwpNoSize | SwpNoZOrder);
    }

    private static double GetScaleForPoint(int x, int y)
    {
        IntPtr monitor = MonitorFromPoint(new NativePoint { X = x, Y = y }, MonitorDefaultToNearest);
        if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0 && dpiX > 0)
            return dpiX / 96d;
        return 1d;
    }

    private void FocusWindowAndSearch()
    {
        IntPtr handle = new WindowInteropHelper(this).EnsureHandle();
        IntPtr foreground = GetForegroundWindow();
        uint currentThread = GetCurrentThreadId();
        uint foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, out _);
        bool attached = foregroundThread != 0 && foregroundThread != currentThread &&
                        AttachThreadInput(currentThread, foregroundThread, true);
        try
        {
            ShowWindowAsync(handle, 5);
            BringWindowToTop(handle);
            bool foregroundSet = SetForegroundWindow(handle);
            if (!foregroundSet || GetForegroundWindow() != handle)
            {
                keybd_event(VkMenu, 0, 0, UIntPtr.Zero);
                keybd_event(VkMenu, 0, KeyEventFKeyUp, UIntPtr.Zero);
                SetForegroundWindow(handle);
            }
            SetFocus(handle);
        }
        finally
        {
            if (attached) AttachThreadInput(currentThread, foregroundThread, false);
        }
        Activate();
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    private async Task RetryForegroundFocusAsync()
    {
        int[] delays = { 35, 80, 180, 400, 800, 1400 };
        foreach (int delay in delays)
        {
            await Task.Delay(delay);
            await Dispatcher.InvokeAsync(() =>
            {
                if (!_menuIsOpen || _isPrewarming) return;
                IntPtr handle = new WindowInteropHelper(this).Handle;
                if (!IsActive || GetForegroundWindow() != handle) FocusWindowAndSearch();
            }, System.Windows.Threading.DispatcherPriority.Input);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            T? descendant = FindVisualChild<T>(child);
            if (descendant is not null) return descendant;
        }
        return null;
    }

    private static T? FindAncestor<T>(DependencyObject source) where T : DependencyObject
    {
        DependencyObject? current = source;
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (_isPrewarming || !DismissOnDeactivate || !_menuIsOpen) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (!_isPrewarming && DismissOnDeactivate && _menuIsOpen && !IsActive) HideMenu();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (_isPrewarming) return;
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        HideMenu();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        int enabled = 1;
        int corner = 2;
        int backdrop = 3;
        int borderColor = 0x003A3A3A;
        DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
        DwmSetWindowAttribute(handle, 33, ref corner, sizeof(int));
        DwmSetWindowAttribute(handle, 34, ref borderColor, sizeof(int));
        DwmSetWindowAttribute(handle, 38, ref backdrop, sizeof(int));
    }

    private static readonly IntPtr HwndTopMost = new(-1);
    private const byte VkMenu = 0x12;
    private const uint KeyEventFKeyUp = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint MonitorDefaultToNearest = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")] private static extern void keybd_event(byte virtualKey, byte scanCode,
        uint flags, UIntPtr extraInfo);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr window);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
        int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType,
        out uint dpiX, out uint dpiY);
}

internal readonly record struct MenuTestState(bool IsVisible, bool IsActive, bool OwnsForeground,
    string SearchText, bool SearchHasFocus, int Left, int Top, int Right, int Bottom, double LogicalWidth,
    long WindowHandle, long ForegroundHandle, uint ForegroundProcessId, string ForegroundProcessName);

internal readonly record struct ScrollTestState(long ElapsedMilliseconds, long CacheMisses,
    double ScrollableHeight);
