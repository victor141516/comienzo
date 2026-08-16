using System.Reflection;
using System.Runtime.InteropServices;

namespace Comienzo.Services;

internal readonly record struct ShortcutIcon(string? Source, int Index);

internal static class ShortcutIconResolver
{
    public static ShortcutIcon Resolve(string shortcutPath)
    {
        if (shortcutPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
            return ResolveWindowsShortcut(shortcutPath);
        if (shortcutPath.EndsWith(".url", StringComparison.OrdinalIgnoreCase))
            return ResolveInternetShortcut(shortcutPath);
        return default;
    }

    private static ShortcutIcon ResolveWindowsShortcut(string shortcutPath)
    {
        object? shell = null;
        object? shortcut = null;
        try
        {
            Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return default;
            shell = Activator.CreateInstance(shellType);
            shortcut = Invoke(shell!, "CreateShortcut", shortcutPath);
            if (shortcut is null) return default;

            ShortcutIcon icon = ParseIconLocation(Convert.ToString(Get(shortcut, "IconLocation")) ?? "");
            if (!string.IsNullOrEmpty(icon.Source)) return icon;

            string target = Convert.ToString(Get(shortcut, "TargetPath"))?.Trim().Trim('"') ?? "";
            target = Environment.ExpandEnvironmentVariables(target);
            return File.Exists(target) ? new ShortcutIcon(target, 0) : default;
        }
        catch
        {
            return default;
        }
        finally
        {
            ReleaseCom(shortcut);
            ReleaseCom(shell);
        }
    }

    private static ShortcutIcon ResolveInternetShortcut(string shortcutPath)
    {
        try
        {
            string iconFile = "";
            int iconIndex = 0;
            foreach (string line in File.ReadLines(shortcutPath))
            {
                int separator = line.IndexOf('=');
                if (separator <= 0) continue;
                string key = line[..separator].Trim();
                string value = line[(separator + 1)..].Trim();
                if (key.Equals("IconFile", StringComparison.OrdinalIgnoreCase)) iconFile = value;
                else if (key.Equals("IconIndex", StringComparison.OrdinalIgnoreCase)) int.TryParse(value, out iconIndex);
            }
            iconFile = CleanPath(iconFile);
            return File.Exists(iconFile) ? new ShortcutIcon(iconFile, iconIndex) : default;
        }
        catch
        {
            return default;
        }
    }

    private static ShortcutIcon ParseIconLocation(string value)
    {
        value = value.Trim();
        int index = 0;
        int comma = value.LastIndexOf(',');
        if (comma > 0 && int.TryParse(value[(comma + 1)..].Trim(), out int parsed))
        {
            index = parsed;
            value = value[..comma];
        }
        string path = CleanPath(value);
        return File.Exists(path) ? new ShortcutIcon(path, index) : default;
    }

    private static string CleanPath(string value)
    {
        string path = value.Trim().Trim('"').TrimStart('@');
        return Environment.ExpandEnvironmentVariables(path);
    }

    private static object? Invoke(object target, string name, params object[] args) =>
        target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args);

    private static object? Get(object target, string name) =>
        target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null);

    private static void ReleaseCom(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }
}
