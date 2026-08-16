using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace Comienzo.Services;

internal sealed class NativeHookService : IDisposable
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const uint LlkhfInjected = 0x10;
    private const uint LlkhfExtended = 0x01;
    private const uint KeyEventFExtendedKey = 0x01;
    private const uint KeyEventFKeyUp = 0x02;
    private static readonly IntPtr InjectedMarker = new(unchecked((long)0x434F4D49454E5A4F));
    internal static readonly IntPtr IntegrationTestMarker = new(unchecked((long)0x434F4D4954455354));

    private readonly Action _toggleMenu;
    private readonly Action<int, int>? _mouseDown;
    private readonly bool _allowIntegrationTestInput;
    private readonly StartButtonLocator _startButton = new();
    private readonly HookProc _keyboardProc;
    private readonly HookProc _mouseProc;
    private readonly WindowsKeyStateMachine _windowsKeyState = new();
    private readonly Dispatcher _dispatcher;
    private readonly List<KeyboardReplayEvent> _capturedShortcutEvents = new();
    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private bool _startMouseDown;

    public NativeHookService(Action toggleMenu, Action<int, int>? mouseDown = null,
        bool allowIntegrationTestInput = false)
    {
        _toggleMenu = toggleMenu;
        _mouseDown = mouseDown;
        _allowIntegrationTestInput = allowIntegrationTestInput;
        _keyboardProc = KeyboardCallback;
        _mouseProc = MouseCallback;
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    internal bool HasLocatedStartButton => _startButton.HasAny;
    internal StartButtonBounds? FindStartButtonNear(int x, int y) => _startButton.FindNearest(x, y);
    internal bool BypassIntegrationTestInput { get; set; }

    public void Start()
    {
        using Process process = Process.GetCurrentProcess();
        using ProcessModule? module = process.MainModule;
        IntPtr moduleHandle = GetModuleHandle(module?.ModuleName);
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, moduleHandle, 0);
        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, moduleHandle, 0);
        if (_keyboardHook == IntPtr.Zero || _mouseHook == IntPtr.Zero)
        {
            Dispose();
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not install the Start hooks.");
        }
    }

    private IntPtr KeyboardCallback(int code, IntPtr message, IntPtr data)
    {
        if (code < 0) return CallNextHookEx(IntPtr.Zero, code, message, data);
        var key = Marshal.PtrToStructure<KbdLlHookStruct>(data);
        bool down = message == (IntPtr)WmKeyDown || message == (IntPtr)WmSysKeyDown;
        bool up = message == (IntPtr)WmKeyUp || message == (IntPtr)WmSysKeyUp;
        if ((key.flags & LlkhfInjected) != 0)
        {
            if (!_allowIntegrationTestInput || BypassIntegrationTestInput ||
                key.dwExtraInfo != IntegrationTestMarker)
                return CallNextHookEx(IntPtr.Zero, code, message, data);
        }

        WindowsKeyDecision decision = _windowsKeyState.Process(key.vkCode, down, up,
            IsKeyDown(VkShift), IsKeyDown(VkControl), IsKeyDown(VkMenu));
        if (decision.Action == WindowsKeyAction.ToggleComienzo)
        {
            _capturedShortcutEvents.Clear();
            // The low-level hook runs on the thread that installed it (the WPF dispatcher thread).
            // Show and focus the already-warmed window before a following character can be delivered.
            _toggleMenu();
            return (IntPtr)1;
        }
        if (decision.Action == WindowsKeyAction.CaptureShortcut)
        {
            CaptureShortcutEvent(key, up);
            return (IntPtr)1;
        }
        if (decision.Action == WindowsKeyAction.ReplayShortcut)
        {
            CaptureShortcutEvent(key, up);
            QueueWindowsShortcutReplay(decision.WindowsKey);
            return (IntPtr)1;
        }
        if (decision.Action == WindowsKeyAction.Suppress) return (IntPtr)1;

        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    private IntPtr MouseCallback(int code, IntPtr message, IntPtr data)
    {
        if (code < 0) return CallNextHookEx(IntPtr.Zero, code, message, data);
        if (message != (IntPtr)WmLButtonDown && message != (IntPtr)WmLButtonUp)
            return CallNextHookEx(IntPtr.Zero, code, message, data);

        var mouse = Marshal.PtrToStructure<MsLlHookStruct>(data);
        bool onStart = _startButton.Contains(mouse.pt.x, mouse.pt.y);
        bool shift = IsKeyDown(VkShift);
        if (message == (IntPtr)WmLButtonDown)
        {
            _startMouseDown = onStart && !shift;
            if (!_startMouseDown && _mouseDown is not null)
                ThreadPool.QueueUserWorkItem(_ => _mouseDown(mouse.pt.x, mouse.pt.y));
            return _startMouseDown ? (IntPtr)1 : CallNextHookEx(IntPtr.Zero, code, message, data);
        }
        if (_startMouseDown)
        {
            _startMouseDown = false;
            ThreadPool.QueueUserWorkItem(_ => _toggleMenu());
            return (IntPtr)1;
        }
        return CallNextHookEx(IntPtr.Zero, code, message, data);
    }

    private static bool IsKeyDown(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;

    private void CaptureShortcutEvent(KbdLlHookStruct key, bool isKeyUp)
    {
        if (key.vkCode is WindowsKeyStateMachine.LeftWindowsKey or WindowsKeyStateMachine.RightWindowsKey)
            return;
        _capturedShortcutEvents.Add(new KeyboardReplayEvent((ushort)key.vkCode, (ushort)key.scanCode,
            (key.flags & LlkhfExtended) != 0, isKeyUp));
    }

    private void QueueWindowsShortcutReplay(ushort windowsKey)
    {
        KeyboardReplayEvent[] captured = _capturedShortcutEvents.ToArray();
        _capturedShortcutEvents.Clear();
        // A low-level hook runs before Windows updates asynchronous key state. Deferring until the
        // callback returns ensures SendInput sees the physical chord as released.
        _dispatcher.BeginInvoke(() => ReplayWindowsShortcut(windowsKey, captured), DispatcherPriority.Input);
    }

    private static void ReplayWindowsShortcut(ushort windowsKey,
        IReadOnlyList<KeyboardReplayEvent> capturedEvents)
    {
        KeyboardReplayEvent[] sequence = CreateShortcutReplay(windowsKey, capturedEvents);
        INPUT[] replay = sequence.Select(CreateKeyboardInput).ToArray();
        uint sent = SendInput((uint)replay.Length, replay, Marshal.SizeOf<INPUT>());
        if (sent != replay.Length)
            Debug.WriteLine($"Shortcut replay inserted {sent} of {replay.Length} keyboard events. " +
                $"Win32 error: {Marshal.GetLastWin32Error()}.");
    }

    internal static KeyboardReplayEvent[] CreateShortcutReplay(ushort windowsKey,
        IReadOnlyList<KeyboardReplayEvent> capturedEvents)
    {
        var sequence = new KeyboardReplayEvent[capturedEvents.Count + 2];
        sequence[0] = new KeyboardReplayEvent(windowsKey, 0, true, false);
        for (int index = 0; index < capturedEvents.Count; index++)
            sequence[index + 1] = capturedEvents[index];
        sequence[^1] = new KeyboardReplayEvent(windowsKey, 0, true, true);
        return sequence;
    }

    private static INPUT CreateKeyboardInput(KeyboardReplayEvent key) => new()
    {
        type = 1,
        union = new InputUnion
        {
            keyboard = new KEYBDINPUT
            {
                wVk = key.VirtualKey,
                wScan = key.ScanCode,
                dwFlags = (key.IsExtended ? KeyEventFExtendedKey : 0) |
                          (key.IsKeyUp ? KeyEventFKeyUp : 0),
                dwExtraInfo = InjectedMarker
            }
        }
    };

    public void Dispose()
    {
        if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        _keyboardHook = _mouseHook = IntPtr.Zero;
        _startButton.Dispose();
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int x; public int y; }
    [StructLayout(LayoutKind.Sequential)] private struct MsLlHookStruct { public Point pt; public uint mouseData, flags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct KbdLlHookStruct { public int vkCode, scanCode; public uint flags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public InputUnion union; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public KEYBDINPUT keyboard; }
    [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr module, uint threadId);
    [DllImport("user32.dll")] private static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? moduleName);
}

internal readonly record struct KeyboardReplayEvent(ushort VirtualKey, ushort ScanCode,
    bool IsExtended, bool IsKeyUp);
