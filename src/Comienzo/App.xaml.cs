using System.Threading;
using System.Windows;
using Comienzo.Services;

namespace Comienzo;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private MainWindow? _window;
    private NativeHookService? _hooks;
    private TrayService? _tray;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showRegistration;
    private bool _testWindow;
    private string _snapshotPath = "";
    private string _initialQuery = "";
    private string _integrationTestOutput = "";

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
        {
            int result = SelfTests.Run();
            Environment.ExitCode = result;
            Shutdown(result);
            return;
        }

        _singleInstance = new Mutex(true, "Local\\Comienzo.StartMenu.Instance", out bool created);
        if (!created)
        {
            if (!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase))
            {
                try { EventWaitHandle.OpenExisting("Local\\Comienzo.StartMenu.Show").Set(); }
                catch { }
            }
            Shutdown();
            return;
        }

        _testWindow = e.Args.Contains("--test-window", StringComparer.OrdinalIgnoreCase);
        int snapshotIndex = Array.FindIndex(e.Args, value => value.Equals("--snapshot", StringComparison.OrdinalIgnoreCase));
        if (snapshotIndex >= 0 && snapshotIndex + 1 < e.Args.Length)
            _snapshotPath = Path.GetFullPath(e.Args[snapshotIndex + 1]);
        int queryIndex = Array.FindIndex(e.Args, value => value.Equals("--query", StringComparison.OrdinalIgnoreCase));
        if (queryIndex >= 0 && queryIndex + 1 < e.Args.Length)
            _initialQuery = e.Args[queryIndex + 1];
        int integrationIndex = Array.FindIndex(e.Args,
            value => value.Equals("--integration-test", StringComparison.OrdinalIgnoreCase));
        if (integrationIndex >= 0 && integrationIndex + 1 < e.Args.Length)
            _integrationTestOutput = Path.GetFullPath(e.Args[integrationIndex + 1]);
        if (_integrationTestOutput.Length > 0) InstallIntegrationFailureLogging();
        _tray = new TrayService(ShowWindow, ShutdownCleanly);
        _hooks = new NativeHookService(ToggleWindow, DismissWindowIfOutside,
            allowIntegrationTestInput: _integrationTestOutput.Length > 0);
        _hooks.Start();
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\Comienzo.StartMenu.Show");
        _showRegistration = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => ShowWindow(), null, Timeout.Infinite, false);

        MainWindow window = EnsureWindow();
        if (_integrationTestOutput.Length > 0)
            _ = RunIntegrationTestsAsync(window);
        else if (!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase))
            window.ShowMenu();
    }

    private MainWindow EnsureWindow()
    {
        if (_window is not null) return _window;
        _window = new MainWindow
        {
            DismissOnDeactivate = !_testWindow,
            ShowInTaskbar = _testWindow,
            EnableBackgroundPrewarm = true,
            SnapshotPath = _snapshotPath,
            InitialQuery = _initialQuery,
            StartButtonBoundsProvider = () =>
            {
                System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
                return _hooks?.FindStartButtonNear(cursor.X, cursor.Y);
            }
        };
        _ = _window.LoadCatalogAsync();
        return _window;
    }

    private void ShowWindow()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(ShowWindow);
            return;
        }
        EnsureWindow().ShowMenu();
    }

    private void ToggleWindow()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(ToggleWindow);
            return;
        }
        EnsureWindow().ToggleMenu();
    }

    private void DismissWindowIfOutside(int x, int y)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => DismissWindowIfOutside(x, y));
            return;
        }
        _window?.DismissIfOutside(x, y);
    }

    private async Task RunIntegrationTestsAsync(MainWindow window)
    {
        int exitCode;
        try
        {
            exitCode = await VmIntegrationTests.RunAsync(window, _hooks!, _integrationTestOutput);
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(_integrationTestOutput);
            await File.WriteAllTextAsync(Path.Combine(_integrationTestOutput, "integration-error.txt"),
                exception.ToString());
            exitCode = 1;
        }

        Environment.ExitCode = exitCode;
        ShutdownCleanly();
    }

    private void InstallIntegrationFailureLogging()
    {
        Directory.CreateDirectory(_integrationTestOutput);
        DispatcherUnhandledException += (_, eventArgs) =>
            WriteIntegrationFailure("dispatcher-unhandled.txt", eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
            WriteIntegrationFailure("appdomain-unhandled.txt", eventArgs.ExceptionObject as Exception ??
                new Exception(eventArgs.ExceptionObject?.ToString() ?? "Excepción no identificada"));
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
            WriteIntegrationFailure("task-unobserved.txt", eventArgs.Exception);
    }

    private void WriteIntegrationFailure(string fileName, Exception exception)
    {
        try
        {
            File.AppendAllText(Path.Combine(_integrationTestOutput, fileName),
                $"{DateTimeOffset.UtcNow:O}{Environment.NewLine}{exception}{Environment.NewLine}");
        }
        catch { }
    }

    private void ShutdownCleanly()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(ShutdownCleanly);
            return;
        }
        _showRegistration?.Unregister(null);
        _showRegistration = null;
        _showEvent?.Dispose();
        _showEvent = null;
        _hooks?.Dispose();
        _hooks = null;
        _tray?.Dispose();
        _tray = null;
        _window?.CloseForReal();
        _singleInstance?.Dispose();
        _singleInstance = null;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hooks?.Dispose();
        _tray?.Dispose();
        _showRegistration?.Unregister(null);
        _showEvent?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
