using System.Text.Json;
using Comienzo.Models;

namespace Comienzo.Services;

internal static class UsageService
{
    private static readonly object Sync = new();
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Comienzo");
    private static readonly string FilePath = Path.Combine(DirectoryPath, "usage.json");
    private static readonly Dictionary<string, UsageEntry> Entries = LoadFrom(FilePath);

    public static void Record(CatalogItem item)
    {
        if (item.Kind == ItemKind.Calculator || item.Target.Length == 0) return;
        lock (Sync)
        {
            if (!Entries.TryGetValue(item.UsageKey, out UsageEntry? entry))
            {
                entry = new UsageEntry();
                Entries[item.UsageKey] = entry;
            }
            entry.Count++;
            entry.LastUsedUtc = DateTimeOffset.UtcNow;
            Save();
        }
    }

    public static int GetCount(CatalogItem item)
    {
        lock (Sync) return Entries.TryGetValue(item.UsageKey, out UsageEntry? entry) ? entry.Count : 0;
    }

    public static DateTimeOffset GetLastUsed(CatalogItem item)
    {
        lock (Sync) return Entries.TryGetValue(item.UsageKey, out UsageEntry? entry)
            ? entry.LastUsedUtc
            : DateTimeOffset.MinValue;
    }

    public static double GetSearchBoost(CatalogItem item)
    {
        lock (Sync)
        {
            if (!Entries.TryGetValue(item.UsageKey, out UsageEntry? entry) || entry.Count == 0) return 0;
            double frequency = Math.Min(18, Math.Log2(entry.Count + 1) * 4);
            double recency = entry.LastUsedUtc > DateTimeOffset.UtcNow.AddDays(-7) ? 4 :
                entry.LastUsedUtc > DateTimeOffset.UtcNow.AddDays(-30) ? 2 : 0;
            return frequency + recency;
        }
    }

    internal static bool VerifyPersistenceRoundTrip()
    {
        string testDirectory = Path.Combine(Path.GetTempPath(), "ComienzoTests", Guid.NewGuid().ToString("N"));
        string testFile = Path.Combine(testDirectory, "usage.json");
        try
        {
            var expected = new Dictionary<string, UsageEntry>(StringComparer.OrdinalIgnoreCase)
            {
                ["application|test.exe"] = new UsageEntry { Count = 7, LastUsedUtc = DateTimeOffset.UtcNow }
            };
            SaveTo(testFile, expected);
            Dictionary<string, UsageEntry> loaded = LoadFrom(testFile);
            return loaded.TryGetValue("application|test.exe", out UsageEntry? entry) && entry.Count == 7;
        }
        finally
        {
            try { if (Directory.Exists(testDirectory)) Directory.Delete(testDirectory, true); }
            catch { }
        }
    }

    internal static IDisposable SetTemporaryUsage(CatalogItem item, int count)
    {
        lock (Sync)
        {
            bool hadOriginal = Entries.TryGetValue(item.UsageKey, out UsageEntry? original);
            UsageEntry? copy = original is null ? null : new UsageEntry
            {
                Count = original.Count,
                LastUsedUtc = original.LastUsedUtc
            };
            Entries[item.UsageKey] = new UsageEntry { Count = count, LastUsedUtc = DateTimeOffset.UtcNow };
            return new RestoreUsage(() =>
            {
                lock (Sync)
                {
                    if (hadOriginal && copy is not null) Entries[item.UsageKey] = copy;
                    else Entries.Remove(item.UsageKey);
                }
            });
        }
    }

    internal static IDisposable ClearTemporarily()
    {
        lock (Sync)
        {
            Dictionary<string, UsageEntry> snapshot = Entries.ToDictionary(pair => pair.Key, pair => new UsageEntry
            {
                Count = pair.Value.Count,
                LastUsedUtc = pair.Value.LastUsedUtc
            }, StringComparer.OrdinalIgnoreCase);
            Entries.Clear();
            return new RestoreUsage(() =>
            {
                lock (Sync)
                {
                    Entries.Clear();
                    foreach ((string key, UsageEntry value) in snapshot) Entries[key] = value;
                }
            });
        }
    }

    private static Dictionary<string, UsageEntry> LoadFrom(string path)
    {
        try
        {
            if (!File.Exists(path)) return new Dictionary<string, UsageEntry>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, UsageEntry>? data = JsonSerializer.Deserialize<Dictionary<string, UsageEntry>>(
                File.ReadAllText(path));
            return data is null
                ? new Dictionary<string, UsageEntry>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, UsageEntry>(data, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, UsageEntry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void Save()
    {
        try
        {
            SaveTo(FilePath, Entries);
        }
        catch
        {
            // Usage data improves ranking but must never prevent launching an app.
        }
    }

    private static void SaveTo(string path, Dictionary<string, UsageEntry> entries)
    {
        string directory = Path.GetDirectoryName(path) ?? DirectoryPath;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $"usage-{Environment.ProcessId}-{Guid.NewGuid():N}.tmp");
        File.WriteAllText(temporary, JsonSerializer.Serialize(entries));
        File.Move(temporary, path, true);
    }

    public sealed class UsageEntry
    {
        public int Count { get; set; }
        public DateTimeOffset LastUsedUtc { get; set; }
    }

    private sealed class RestoreUsage(Action restore) : IDisposable
    {
        private Action? _restore = restore;
        public void Dispose() => Interlocked.Exchange(ref _restore, null)?.Invoke();
    }
}
