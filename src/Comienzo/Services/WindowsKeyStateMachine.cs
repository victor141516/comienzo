namespace Comienzo.Services;

internal enum WindowsKeyAction
{
    PassThrough,
    Suppress,
    ToggleComienzo,
    CaptureShortcut,
    ReplayShortcut
}

internal readonly record struct WindowsKeyDecision(WindowsKeyAction Action, ushort WindowsKey);

internal sealed class WindowsKeyStateMachine
{
    internal const int LeftWindowsKey = 0x5B;
    internal const int RightWindowsKey = 0x5C;

    private bool _held;
    private bool _capturingShortcut;
    private bool _windowsKeyReleased;
    private bool _nativeBypass;
    private ushort _windowsKey;
    private readonly HashSet<int> _pressedShortcutKeys = new();

    public WindowsKeyDecision Process(int virtualKey, bool isDown, bool isUp,
        bool shiftAlreadyDown, bool controlAlreadyDown, bool altAlreadyDown)
    {
        bool isWindowsKey = virtualKey is LeftWindowsKey or RightWindowsKey;
        if (isWindowsKey && isDown)
        {
            if (!_held)
            {
                _held = true;
                _capturingShortcut = false;
                _windowsKeyReleased = false;
                _nativeBypass = shiftAlreadyDown || controlAlreadyDown || altAlreadyDown;
                _windowsKey = (ushort)virtualKey;
                return Decision(_nativeBypass ? WindowsKeyAction.PassThrough : WindowsKeyAction.Suppress);
            }

            return Decision(_nativeBypass
                ? WindowsKeyAction.PassThrough
                : _capturingShortcut ? WindowsKeyAction.CaptureShortcut : WindowsKeyAction.Suppress);
        }

        if ((_held || _capturingShortcut) && !isWindowsKey)
        {
            if (_nativeBypass) return Decision(WindowsKeyAction.PassThrough);
            if (isDown)
            {
                _capturingShortcut = true;
                _pressedShortcutKeys.Add(virtualKey);
                return Decision(WindowsKeyAction.CaptureShortcut);
            }
            if (isUp && _capturingShortcut && _pressedShortcutKeys.Remove(virtualKey))
                return CompleteCapturedShortcutIfReleased();
        }

        if (isWindowsKey && isUp && _held && virtualKey == _windowsKey)
        {
            if (_nativeBypass)
            {
                WindowsKeyDecision native = Decision(WindowsKeyAction.PassThrough);
                Reset();
                return native;
            }
            if (!_capturingShortcut)
            {
                WindowsKeyDecision bare = Decision(WindowsKeyAction.ToggleComienzo);
                Reset();
                return bare;
            }

            _held = false;
            _windowsKeyReleased = true;
            return CompleteCapturedShortcutIfReleased();
        }

        return Decision(WindowsKeyAction.PassThrough);
    }

    private WindowsKeyDecision Decision(WindowsKeyAction action) => new(action, _windowsKey);

    private WindowsKeyDecision CompleteCapturedShortcutIfReleased()
    {
        if (!_windowsKeyReleased || _pressedShortcutKeys.Count > 0)
            return Decision(WindowsKeyAction.CaptureShortcut);

        WindowsKeyDecision replay = Decision(WindowsKeyAction.ReplayShortcut);
        Reset();
        return replay;
    }

    private void Reset()
    {
        _held = false;
        _capturingShortcut = false;
        _windowsKeyReleased = false;
        _nativeBypass = false;
        _windowsKey = 0;
        _pressedShortcutKeys.Clear();
    }
}
