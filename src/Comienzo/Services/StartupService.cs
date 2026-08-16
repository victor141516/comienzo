using Microsoft.Win32;

namespace Comienzo.Services;

internal static class StartupService
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Comienzo";

    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(KeyPath);
            return key?.GetValue(ValueName) is string;
        }
        catch { return false; }
    }

    public static void SetEnabled(bool enabled)
    {
        using RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath, true);
        if (enabled)
        {
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("The executable was not found.");
            key.SetValue(ValueName, $"\"{executable}\" --background", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }
}
