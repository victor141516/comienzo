using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;
using Comienzo.Models;

namespace Comienzo.Services;

internal static class AppDiscovery
{
    private static readonly string[] RejectedWords =
    {
        "uninstall", "uninstaller", "desinstalar", "desinstalador", "readme", "léeme", "leeme",
        "help", "ayuda", "manual", "documentation", "documentación", "license", "licencia",
        "website", "sitio web", "support", "soporte", "repair", "reparar", "release notes",
        "changelog", "update checker", "updater", "reset", "preferences and cache", "what's new",
        "console rar manual", "administrative tools", "sample", "samples"
    };

    public static Task<IReadOnlyList<CatalogItem>> DiscoverAsync() => Task.Run(Discover);

    private static IReadOnlyList<CatalogItem> Discover()
    {
        var candidates = new List<(CatalogItem Item, int Priority)>();
        AddStartMenuShortcuts(candidates);
        AddAppsFolder(candidates);
        AddAppPaths(candidates);

        return candidates
            .Where(x => IsUseful(x.Item))
            .GroupBy(x => NormalizeName(x.Item.Name), StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(x => HasUsableIcon(x.Item))
                .ThenByDescending(x => x.Priority)
                .First().Item)
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static void AddStartMenuShortcuts(List<(CatalogItem, int)> output)
    {
        string[] roots =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu)
        };

        foreach (string root in roots.Where(Directory.Exists))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                    .Where(path => path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
                                   path.EndsWith(".url", StringComparison.OrdinalIgnoreCase) ||
                                   path.EndsWith(".appref-ms", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }
            catch
            {
                continue;
            }

            foreach (string path in files)
            {
                string name = Path.GetFileNameWithoutExtension(path).Trim();
                ShortcutIcon icon = ShortcutIconResolver.Resolve(path);
                var item = new CatalogItem
                {
                    Name = name,
                    Subtitle = path.EndsWith(".url", StringComparison.OrdinalIgnoreCase)
                        ? "Aplicación o juego"
                        : "Aplicación",
                    Kind = ItemKind.Application,
                    Target = path,
                    IconSource = icon.Source ?? "",
                    IconIndex = icon.Index
                };
                output.Add((item, 300));
            }
        }
    }

    private static void AddAppsFolder(List<(CatalogItem, int)> output)
    {
        object? shell = null;
        object? folder = null;
        object? items = null;
        try
        {
            Type? shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return;
            shell = Activator.CreateInstance(shellType);
            folder = Invoke(shell!, "NameSpace", "shell:AppsFolder");
            if (folder is null) return;
            items = Invoke(folder, "Items");
            if (items is null) return;
            int count = Convert.ToInt32(Get(items, "Count"));

            for (int index = 0; index < count; index++)
            {
                object? item = null;
                try
                {
                    item = Invoke(items, "Item", index);
                    if (item is null) continue;
                    string name = Convert.ToString(Get(item, "Name"))?.Trim() ?? "";
                    string path = Convert.ToString(Get(item, "Path"))?.Trim() ?? "";
                    string appId = Convert.ToString(Invoke(item, "ExtendedProperty", "System.AppUserModel.ID"))?.Trim() ?? "";
                    if (name.Length == 0) continue;

                    string identifier = appId.Length > 0 ? appId : path;
                    if (identifier.Length == 0) continue;
                    output.Add((new CatalogItem
                    {
                        Name = name,
                        Subtitle = "Aplicación instalada",
                        Kind = ItemKind.Application,
                        LaunchKind = LaunchKind.ExplorerShellApp,
                        Target = identifier,
                        IconSource = path.Length > 0 ? path : $"shell:AppsFolder\\{identifier}"
                    }, 200));
                }
                catch
                {
                    // A single broken shell item must not abort discovery.
                }
                finally
                {
                    ReleaseCom(item);
                }
            }
        }
        catch
        {
            // AppsFolder is best-effort on customized or restricted shells.
        }
        finally
        {
            ReleaseCom(items);
            ReleaseCom(folder);
            ReleaseCom(shell);
        }
    }

    private static void AddAppPaths(List<(CatalogItem, int)> output)
    {
        (RegistryHive Hive, RegistryView View)[] locations =
        {
            (RegistryHive.CurrentUser, RegistryView.Default),
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32)
        };

        foreach (var location in locations)
        {
            try
            {
                using RegistryKey baseKey = RegistryKey.OpenBaseKey(location.Hive, location.View);
                using RegistryKey? appPaths = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");
                if (appPaths is null) continue;
                foreach (string subKeyName in appPaths.GetSubKeyNames())
                {
                    using RegistryKey? appKey = appPaths.OpenSubKey(subKeyName);
                    string? executable = appKey?.GetValue(null) as string;
                    executable = executable?.Trim().Trim('"');
                    if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)) continue;

                    string name = GetFriendlyExecutableName(executable!, subKeyName);
                    output.Add((new CatalogItem
                    {
                        Name = name,
                        Subtitle = "Aplicación de escritorio",
                        Kind = ItemKind.Application,
                        Target = executable,
                        WorkingDirectory = Path.GetDirectoryName(executable) ?? "",
                        IconSource = executable
                    }, 100));
                }
            }
            catch
            {
                // Registry access differs between Windows editions and policies.
            }
        }
    }

    private static string GetFriendlyExecutableName(string executable, string fallback)
    {
        try
        {
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(executable);
            string? description = info.ProductName?.Trim();
            if (string.IsNullOrWhiteSpace(description)) description = info.FileDescription?.Trim();
            if (!string.IsNullOrWhiteSpace(description) && description.Length <= 80)
                return description;
        }
        catch { }
        return Path.GetFileNameWithoutExtension(fallback);
    }

    private static bool IsUseful(CatalogItem item)
    {
        string check = $"{item.Name} {item.Target} {item.IconSource}".ToLowerInvariant();
        if (item.Name.Length < 2 || RejectedWords.Any(check.Contains)) return false;
        return !item.Name.StartsWith("{", StringComparison.Ordinal) &&
               !item.Name.Equals("desktop", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasUsableIcon(CatalogItem item) => !string.IsNullOrEmpty(item.IconSource);

    private static string NormalizeName(string value)
    {
        string normalized = SearchEngine.Normalize(value);
        foreach (string suffix in new[] { " app", " application", " aplicación", " x64", " x86" })
            if (normalized.EndsWith(suffix, StringComparison.Ordinal))
                normalized = normalized[..^suffix.Length].TrimEnd();
        return normalized;
    }

    private static object? Invoke(object target, string name, params object[] args) =>
        target.GetType().InvokeMember(name, BindingFlags.InvokeMethod, null, target, args);

    private static object? Get(object target, string name) =>
        target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null);

    private static void ReleaseCom(object? value)
    {
        if (value is not null && System.Runtime.InteropServices.Marshal.IsComObject(value))
            System.Runtime.InteropServices.Marshal.FinalReleaseComObject(value);
    }
}
