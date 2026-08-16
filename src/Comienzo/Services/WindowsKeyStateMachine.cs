namespace Comienzo.Services;

internal enum WindowsKeyAction
{
    PassThrough,
    Suppress,
    ToggleComienzo,
    ReplayShortcut
}

internal readonly record struct WindowsKeyDecision(WindowsKeyAction Action, ushort WindowsKey);

internal sealed class WindowsKeyStateMachine
{
    internal const int LeftWindowsKey = 0x5B;
    internal const int RightWindowsKey = 0x5C;

    private bool _held;
    private bool _shortcutReplayed;
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
                _shortcutReplayed = false;
                _nativeBypass = shiftAlreadyDown || controlAlreadyDown || altAlreadyDown;
                _windowsKey = (ushort)virtualKey;
                return Decision(_nativeBypass ? WindowsKeyAction.PassThrough : WindowsKeyAction.Suppress);
            }

            if (virtualKey != _windowsKey && !_nativeBypass && !_shortcutReplayed)
            {
                _shortcutReplayed = true;
                return Decision(WindowsKeyAction.ReplayShortcut);
            }

            return Decision(_nativeBypass || _shortcutReplayed
                ? WindowsKeyAction.PassThrough
                : WindowsKeyAction.Suppress);
        }

        if (_held && !isWindowsKey && isDown)
        {
            if (_nativeBypass || _shortcutReplayed) return Decision(WindowsKeyAction.PassThrough);
            _shortcutReplayed = true;
            return Decision(WindowsKeyAction.ReplayShortcut);
        }

        if (isWindowsKey && isUp && _held && virtualKey == _windowsKey)
        {
            WindowsKeyDecision decision = Decision(!_nativeBypass && !_shortcutReplayed
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
        _shortcutReplayed = false;
        _nativeBypass = false;
        _windowsKey = 0;
    }
}
