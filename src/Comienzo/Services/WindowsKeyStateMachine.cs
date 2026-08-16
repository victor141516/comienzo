namespace Comienzo.Services;

internal enum WindowsKeyAction
{
    PassThrough,
    Suppress,
    ToggleComienzo,
    BeginShortcut
}

internal readonly record struct WindowsKeyDecision(WindowsKeyAction Action, ushort WindowsKey);

internal sealed class WindowsKeyStateMachine
{
    internal const int LeftWindowsKey = 0x5B;
    internal const int RightWindowsKey = 0x5C;

    private bool _held;
    private bool _shortcutStarted;
    private bool _nativeBypass;
    private ushort _windowsKey;

    public WindowsKeyDecision Process(int virtualKey, bool isDown, bool isUp,
        bool shiftAlreadyDown, bool controlAlreadyDown, bool altAlreadyDown)
    {
        bool isWindowsKey = virtualKey is LeftWindowsKey or RightWindowsKey;
        if (isWindowsKey && isDown)
        {
            if (!_held)
            {
                _held = true;
                _shortcutStarted = false;
                _nativeBypass = shiftAlreadyDown || controlAlreadyDown || altAlreadyDown;
                _windowsKey = (ushort)virtualKey;
                return Decision(_nativeBypass ? WindowsKeyAction.PassThrough : WindowsKeyAction.Suppress);
            }

            if (virtualKey != _windowsKey && !_nativeBypass && !_shortcutStarted)
            {
                _shortcutStarted = true;
                return Decision(WindowsKeyAction.BeginShortcut);
            }

            return Decision(_nativeBypass || _shortcutStarted
                ? WindowsKeyAction.PassThrough
                : WindowsKeyAction.Suppress);
        }

        if (_held && !isWindowsKey && isDown)
        {
            if (_nativeBypass || _shortcutStarted) return Decision(WindowsKeyAction.PassThrough);
            _shortcutStarted = true;
            return Decision(WindowsKeyAction.BeginShortcut);
        }

        if (isWindowsKey && isUp && _held && virtualKey == _windowsKey)
        {
            WindowsKeyDecision decision = Decision(!_nativeBypass && !_shortcutStarted
                ? WindowsKeyAction.ToggleComienzo
                : WindowsKeyAction.PassThrough);
            Reset();
            return decision;
        }

        return Decision(WindowsKeyAction.PassThrough);
    }

    private WindowsKeyDecision Decision(WindowsKeyAction action) => new(action, _windowsKey);

    private void Reset()
    {
        _held = false;
        _shortcutStarted = false;
        _nativeBypass = false;
        _windowsKey = 0;
    }
}
