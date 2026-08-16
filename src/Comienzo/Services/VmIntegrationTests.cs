using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace Comienzo.Services;

internal static class VmIntegrationTests
{
    private const int VkLeftWindows = 0x5B;
    private const int VkR = 0x52;
    private const int VkEscape = 0x1B;
    private const uint InputMouse = 0;
    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x2;
    private const uint MouseEventLeftDown = 0x2;
    private const uint MouseEventLeftUp = 0x4;

    public static async Task<int> RunAsync(MainWindow window, NativeHookService hooks, string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var checks = new List<IntegrationCheck>();
        string? failure = null;
        try
        {
            await window.WaitUntilCatalogReadyAsync().WaitAsync(TimeSpan.FromSeconds(60));
            checks.Add(new IntegrationCheck("catalogo_precargado", true,
                $"Fallos de caché acumulados durante la precarga: {IconService.CacheMissCount}"));

            await Task.Run(SendBareWindowsKey);
            bool openedFocused = await WaitForAsync(window, state =>
                state.IsVisible && state.IsActive && state.OwnsForeground && state.SearchHasFocus,
                TimeSpan.FromSeconds(5));
            MenuTestState openedState = await ReadStateAsync(window);
            Require(checks, "win_abre_y_enfoca", openedFocused,
                $"Visible={openedState.IsVisible}, activa={openedState.IsActive}, " +
                $"foreground={openedState.OwnsForeground}, foco={openedState.SearchHasFocus}, " +
                $"hwnd={openedState.WindowHandle}, foregroundHwnd={openedState.ForegroundHandle}, " +
                $"foregroundPid={openedState.ForegroundProcessId}, " +
                $"foregroundProcess={openedState.ForegroundProcessName}.");

            await Task.Run(() => SendKey(VkR));
            bool typedR = await WaitForAsync(window, state =>
                state.SearchText.Equals("r", StringComparison.OrdinalIgnoreCase), TimeSpan.FromSeconds(3));
            MenuTestState typedState = await ReadStateAsync(window);
            Require(checks, "r_se_escribe_en_busqueda", typedR && typedState.IsVisible,
                $"Texto observado: '{typedState.SearchText}', visible: {typedState.IsVisible}.");
            Require(checks, "windows_no_queda_pulsada", !IsKeyDown(VkLeftWindows),
                $"GetAsyncKeyState(VK_LWIN)={GetAsyncKeyState(VkLeftWindows)}.");

            await Task.Run(() => ClickOutside(typedState));
            bool closedAfterTyping = await WaitForAsync(window, state => !state.IsVisible,
                TimeSpan.FromSeconds(3));
            Require(checks, "clic_exterior_cierra", closedAfterTyping,
                "La ventana permaneció visible después del clic exterior.");

            await Task.Run(SendBareWindowsKey);
            bool reopened = await WaitForAsync(window, state => state.IsVisible,
                TimeSpan.FromSeconds(5));
            Require(checks, "segunda_apertura", reopened, "No se abrió por segunda vez.");
            MenuTestState immediateState = await ReadStateAsync(window);
            await Task.Run(() => ClickOutside(immediateState));
            bool closedImmediately = await WaitForAsync(window, state => !state.IsVisible,
                TimeSpan.FromSeconds(3));
            Require(checks, "clic_exterior_inmediato_cierra", closedImmediately,
                "El primer clic exterior tras abrir no cerró el menú.");

            hooks.BypassIntegrationTestInput = true;
            await Task.Run(SendWindowsR);
            bool directSyntheticWindowsR = await WaitForForegroundClassAsync("#32770", TimeSpan.FromSeconds(3));
            checks.Add(new IntegrationCheck("baseline_win_r_sintetico", directSyntheticWindowsR,
                $"Sandbox abrió Ejecutar sin intervención del hook: {directSyntheticWindowsR}."));
            await Task.Run(() => SendKey(VkEscape));
            await Task.Delay(250);
            hooks.BypassIntegrationTestInput = false;

            await Task.Run(SendWindowsR);
            bool runOpened = await WaitForForegroundClassAsync("#32770", TimeSpan.FromSeconds(5));
            Require(checks, "win_r_llega_a_windows", runOpened,
                $"Clase de la ventana en primer plano: '{GetForegroundClass()}'.");
            await Task.Run(() => SendKey(VkEscape));
            await Task.Delay(250);

            await Task.Run(SendBareWindowsKey);
            bool openedForLayout = await WaitForAsync(window, state => state.IsVisible && state.SearchHasFocus,
                TimeSpan.FromSeconds(5));
            Require(checks, "apertura_para_layout", openedForLayout, "No se abrió para validar el layout.");
            MenuTestState layout = await ReadStateAsync(window);
            System.Drawing.Rectangle work = System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point(layout.Left, layout.Top)).WorkingArea;
            Require(checks, "margen_de_pantalla", layout.Left >= work.Left + 8 && layout.Right <= work.Right - 8,
                $"Ventana [{layout.Left},{layout.Right}], área de trabajo [{work.Left},{work.Right}].");
            Require(checks, "ancho_reducido", Math.Abs(layout.LogicalWidth - 560) < 0.1,
                $"Ancho lógico: {layout.LogicalWidth}.");

            ScrollTestState scroll = await window.Dispatcher.InvokeAsync(window.ExerciseFullScrollForTest);
            Require(checks, "scroll_sin_cargar_iconos", scroll.CacheMisses == 0,
                $"Duración de 3 recorridos: {scroll.ElapsedMilliseconds} ms; nuevos fallos de caché: {scroll.CacheMisses}.");
            Require(checks, "lista_tiene_scroll", scroll.ScrollableHeight > 0,
                $"Altura desplazable: {scroll.ScrollableHeight:0.##}.");

            string screenshot = Path.Combine(outputDirectory, "comienzo-vm.png");
            await window.Dispatcher.InvokeAsync(() => window.SaveSnapshotForTest(screenshot));
            Require(checks, "captura_generada", File.Exists(screenshot), screenshot);
            await Task.Run(() => SendKey(VkEscape));
        }
        catch (Exception exception)
        {
            hooks.BypassIntegrationTestInput = false;
            failure = exception.ToString();
            checks.Add(new IntegrationCheck("ejecucion_completa", false, exception.Message));
        }

        bool passed = failure is null && checks.All(check => check.Passed);
        var report = new IntegrationReport(DateTimeOffset.UtcNow, passed, checks, failure);
        string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "integration-report.json"), json);
        return passed ? 0 : 1;
    }

    private static void Require(List<IntegrationCheck> checks, string name, bool condition, string details)
    {
        checks.Add(new IntegrationCheck(name, condition, details));
        if (!condition) throw new InvalidOperationException($"Falló {name}: {details}");
    }

    private static async Task<MenuTestState> ReadStateAsync(MainWindow window) =>
        await window.Dispatcher.InvokeAsync(window.GetTestState);

    private static async Task<bool> WaitForAsync(MainWindow window, Func<MenuTestState, bool> predicate,
        TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < timeout)
        {
            if (predicate(await ReadStateAsync(window))) return true;
            await Task.Delay(25);
        }
        return false;
    }

    private static async Task<bool> WaitForForegroundClassAsync(string expected, TimeSpan timeout)
    {
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < timeout)
        {
            if (GetForegroundClass().Equals(expected, StringComparison.OrdinalIgnoreCase)) return true;
            await Task.Delay(25);
        }
        return false;
    }

    private static string GetForegroundClass()
    {
        IntPtr foreground = GetForegroundWindow();
        var name = new StringBuilder(256);
        return foreground != IntPtr.Zero && GetClassName(foreground, name, name.Capacity) > 0
            ? name.ToString()
            : "";
    }

    private static bool IsKeyDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    private static void SendBareWindowsKey()
    {
        SendInputs(Keyboard(VkLeftWindows, 0));
        Thread.Sleep(35);
        SendInputs(Keyboard(VkLeftWindows, KeyEventKeyUp));
    }

    private static void SendWindowsR()
    {
        SendInputs(Keyboard(VkLeftWindows, 0));
        Thread.Sleep(35);
        SendInputs(Keyboard(VkR, 0));
        Thread.Sleep(35);
        SendInputs(Keyboard(VkR, KeyEventKeyUp));
        Thread.Sleep(35);
        SendInputs(Keyboard(VkLeftWindows, KeyEventKeyUp));
    }

    private static void SendKey(int key) => SendInputs(
        Keyboard(key, 0), Keyboard(key, KeyEventKeyUp));

    private static void ClickOutside(MenuTestState state)
    {
        System.Drawing.Rectangle work = System.Windows.Forms.Screen.FromPoint(
            new System.Drawing.Point(state.Left, state.Top)).WorkingArea;
        int x = state.Right + 40 < work.Right ? state.Right + 40 : Math.Max(work.Left, state.Left - 4);
        int y = Math.Clamp(state.Top + 80, work.Top + 1, work.Bottom - 2);
        if (!SetCursorPos(x, y)) throw new InvalidOperationException("No se pudo mover el cursor de la VM.");
        SendInputs(Mouse(MouseEventLeftDown), Mouse(MouseEventLeftUp));
    }

    private static INPUT Keyboard(int key, uint flags) => new()
    {
        type = InputKeyboard,
        union = new InputUnion
        {
            keyboard = new KEYBDINPUT
            {
                wVk = (ushort)key,
                dwFlags = flags,
                dwExtraInfo = NativeHookService.IntegrationTestMarker
            }
        }
    };

    private static INPUT Mouse(uint flags) => new()
    {
        type = InputMouse,
        union = new InputUnion { mouse = new MOUSEINPUT { dwFlags = flags } }
    };

    private static void SendInputs(params INPUT[] inputs)
    {
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
            throw new InvalidOperationException($"SendInput insertó {sent} de {inputs.Length} eventos.");
    }

    private sealed record IntegrationCheck(string Name, bool Passed, string Details);
    private sealed record IntegrationReport(DateTimeOffset TimestampUtc, bool Passed,
        IReadOnlyList<IntegrationCheck> Checks, string? Failure);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion union; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mouse;
        [FieldOffset(0)] public KEYBDINPUT keyboard;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr window, StringBuilder className, int capacity);
}
