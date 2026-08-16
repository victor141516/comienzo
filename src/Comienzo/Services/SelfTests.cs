using Comienzo.Models;

namespace Comienzo.Services;

internal static class SelfTests
{
    public static int Run()
    {
        try
        {
            AssertMath("2+3*4", 14);
            AssertMath("(2+3)*4", 20);
            AssertMath("2^3^2", 512);
            AssertMath("-2^2", -4);
            if (MathEvaluator.TryEvaluate("2+hello", out _)) throw new Exception("Accepted unsafe expression");
            if (MathEvaluator.TryEvaluate("5/0", out _)) throw new Exception("Accepted division by zero");

            var chrome = new CatalogItem { Name = "Google Chrome", Kind = ItemKind.Application, Target = "chrome.exe" };
            var notepad = new CatalogItem { Name = "Bloc de notas", Kind = ItemKind.Application, Target = "notepad.exe" };
            var engine = new SearchEngine();
            engine.SetApplications(new[]
            {
                chrome,
                notepad
            });
            CatalogItem firstApp = engine.Search("Google Chrome").First();
            if (firstApp.Kind != ItemKind.Application) throw new Exception("Exact app ranking failed");
            CatalogItem firstSetting = engine.Search("display resolution").First();
            if (firstSetting.Kind != ItemKind.Setting) throw new Exception("Settings ranking failed");
            CatalogItem calculator = engine.Search("(8+2)/5").First();
            if (calculator.Kind != ItemKind.Calculator || calculator.Name != "2") throw new Exception("Calculator result failed");
            using (UsageService.ClearTemporarily())
            {
                IReadOnlyList<CatalogItem> completeList = engine.Search("");
                if (!completeList.Any(item => item.Name == "Google Chrome") ||
                    !completeList.Any(item => item.Name == "Bloc de notas") ||
                    completeList.Count != 2 + SettingsCatalog.Create().Count)
                    throw new Exception("The unfiltered list is incomplete");
                int lastApplication = completeList.Select((item, index) => (item, index))
                    .Where(pair => pair.item.Kind == ItemKind.Application).Max(pair => pair.index);
                int firstSettingIndex = completeList.Select((item, index) => (item, index))
                    .Where(pair => pair.item.Kind == ItemKind.Setting).Min(pair => pair.index);
                if (firstSettingIndex <= lastApplication) throw new Exception("Settings do not follow the application list");

                using (UsageService.SetTemporaryUsage(chrome, 12))
                {
                    CatalogItem frequent = engine.Search("").First();
                    if (frequent.Name != "Google Chrome" || frequent.SectionName != "Most used")
                        throw new Exception("Frequent item ranking failed");
                }

                var alphaOne = new CatalogItem { Name = "Alpha One", Kind = ItemKind.Application, Target = "one.exe" };
                var alphaTwo = new CatalogItem { Name = "Alpha Two", Kind = ItemKind.Application, Target = "two.exe" };
                var usageEngine = new SearchEngine();
                usageEngine.SetApplications(new[] { alphaOne, alphaTwo });
                using (UsageService.SetTemporaryUsage(alphaTwo, 20))
                {
                    if (usageEngine.Search("alpha").First().Name != "Alpha Two")
                        throw new Exception("Usage did not boost search ranking");
                }
            }

            AssertWindowsKeyBehavior();
            AssertWindowPositioning();
            if (!UsageService.VerifyPersistenceRoundTrip()) throw new Exception("Usage persistence round-trip failed");

            IReadOnlyList<CatalogItem> discovered = AppDiscovery.DiscoverAsync().GetAwaiter().GetResult();
            if (discovered.Count < 10) throw new Exception($"Only {discovered.Count} applications discovered");
            if (discovered.Select(x => SearchEngine.Normalize(x.Name)).Distinct().Count() != discovered.Count)
                throw new Exception("Application deduplication failed");
            CatalogItem[] overlaySources = discovered.Where(item =>
                item.IconSource.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
                item.IconSource.EndsWith(".url", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (overlaySources.Length > 0)
                throw new Exception("Shortcut overlay icon source was retained: " +
                    string.Join(", ", overlaySources.Take(5).Select(item => $"{item.Name}={item.IconSource}")));
            IconService.PrewarmAsync(discovered.Take(25)).GetAwaiter().GetResult();
            CatalogItem? unfrozenIcon = discovered.Take(25).FirstOrDefault(item => item.Icon is { IsFrozen: false });
            if (unfrozenIcon is not null)
                throw new Exception($"Prewarmed icon is not cross-thread safe: {unfrozenIcon.Name}");

            StartButtonBounds? locatedStart = null;
            using (var hooks = new NativeHookService(() => { }))
            {
                hooks.Start();
                DateTime deadline = DateTime.UtcNow.AddSeconds(5);
                while (!hooks.HasLocatedStartButton && DateTime.UtcNow < deadline) Thread.Sleep(100);
                if (!hooks.HasLocatedStartButton) throw new Exception("Windows Start button was not located");
                System.Drawing.Point cursor = System.Windows.Forms.Cursor.Position;
                locatedStart = hooks.FindStartButtonNear(cursor.X, cursor.Y);
            }
            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-report.txt"),
                $"Applications discovered: {discovered.Count}{Environment.NewLine}" +
                $"Start button: {locatedStart}{Environment.NewLine}" +
                string.Join(Environment.NewLine, discovered.Take(25).Select(x => $"- {x.Name} [{x.Target}]")));
            return 0;
        }
        catch (Exception exception)
        {
            try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "self-test-error.txt"), exception.ToString()); }
            catch { }
            return 1;
        }
    }

    private static void AssertMath(string expression, double expected)
    {
        if (!MathEvaluator.TryEvaluate(expression, out double actual) || Math.Abs(actual - expected) > 1e-10)
            throw new Exception($"Math failed for {expression}: {actual}");
    }

    private static void AssertWindowsKeyBehavior()
    {
        var shortcut = new WindowsKeyStateMachine();
        AssertAction(shortcut.Process(WindowsKeyStateMachine.LeftWindowsKey, true, false, false, false, false),
            WindowsKeyAction.Suppress,
            "Win down");
        AssertAction(shortcut.Process('R', true, false, false, false, false),
            WindowsKeyAction.ReplayShortcut, "Win+R down");
        AssertPass(shortcut.Process('R', false, true, false, false, false), "Win+R up");
        AssertPass(shortcut.Process(WindowsKeyStateMachine.LeftWindowsKey, false, true, false, false, false),
            "Win+R release");

        var bare = new WindowsKeyStateMachine();
        AssertAction(bare.Process(WindowsKeyStateMachine.LeftWindowsKey, true, false, false, false, false),
            WindowsKeyAction.Suppress,
            "bare Win down");
        WindowsKeyDecision bareRelease = bare.Process(WindowsKeyStateMachine.LeftWindowsKey, false, true,
            false, false, false);
        if (bareRelease.Action != WindowsKeyAction.ToggleComienzo)
            throw new Exception("Bare Windows key was not captured");
        AssertPass(bare.Process('R', true, false, false, false, false),
            "R after bare Windows release");

        var shifted = new WindowsKeyStateMachine();
        AssertPass(shifted.Process(WindowsKeyStateMachine.LeftWindowsKey, true, false, true, false, false), "Shift+Win down");
        AssertPass(shifted.Process(WindowsKeyStateMachine.LeftWindowsKey, false, true, true, false, false), "Shift+Win release");

        var shiftAfterWindows = new WindowsKeyStateMachine();
        AssertAction(shiftAfterWindows.Process(WindowsKeyStateMachine.LeftWindowsKey, true, false,
            false, false, false), WindowsKeyAction.Suppress, "Win before Shift");
        AssertAction(shiftAfterWindows.Process(0x10, true, false, true, false, false),
            WindowsKeyAction.ReplayShortcut,
            "Shift pressed after Win");
        AssertPass(shiftAfterWindows.Process(WindowsKeyStateMachine.LeftWindowsKey, false, true,
            true, false, false), "Win released after Shift combo");

        int[] shortcutKeys = Enumerable.Range('A', 26)
            .Concat(Enumerable.Range('0', 10))
            .Concat(new[] { 0x09, 0x0D, 0x1B, 0x20, 0x25, 0x26, 0x27, 0x28, 0x70, 0x7B })
            .ToArray();
        foreach (int shortcutKey in shortcutKeys)
        {
            var anyShortcut = new WindowsKeyStateMachine();
            AssertAction(anyShortcut.Process(WindowsKeyStateMachine.LeftWindowsKey, true, false,
                false, false, false), WindowsKeyAction.Suppress, $"Win+{shortcutKey:X2} initial");
            AssertAction(anyShortcut.Process(shortcutKey, true, false, false, false, false),
                WindowsKeyAction.ReplayShortcut, $"Win+{shortcutKey:X2} replay");
            AssertPass(anyShortcut.Process(shortcutKey, false, true, false, false, false),
                $"Win+{shortcutKey:X2} key release");
            AssertPass(anyShortcut.Process(WindowsKeyStateMachine.LeftWindowsKey, false, true,
                false, false, false), $"Win+{shortcutKey:X2} Win release");
        }

        var rightBare = new WindowsKeyStateMachine();
        AssertAction(rightBare.Process(WindowsKeyStateMachine.RightWindowsKey, true, false,
            false, false, false), WindowsKeyAction.Suppress, "right Win down");
        AssertAction(rightBare.Process(WindowsKeyStateMachine.RightWindowsKey, false, true,
            false, false, false), WindowsKeyAction.ToggleComienzo, "right Win release");
    }

    private static void AssertWindowPositioning()
    {
        var full = new System.Drawing.Rectangle(0, 0, 1920, 1080);
        var work = new System.Drawing.Rectangle(0, 0, 1920, 1040);
        WindowPlacement left = WindowPositionService.Calculate(
            new StartButtonBounds(0, 1040, 48, 1080), full, work, 560, 720);
        if (left.X != 8 || left.Y != 312) throw new Exception($"Left Start placement failed: {left}");

        WindowPlacement centered = WindowPositionService.Calculate(
            new StartButtonBounds(936, 1040, 984, 1080), full, work, 560, 720);
        if (centered.X != 680 || centered.Y != 312)
            throw new Exception($"Centered Start placement failed: {centered}");
    }

    private static void AssertPass(WindowsKeyDecision decision, string scenario)
    {
        AssertAction(decision, WindowsKeyAction.PassThrough, scenario);
    }

    private static void AssertAction(WindowsKeyDecision decision, WindowsKeyAction expected, string scenario)
    {
        if (decision.Action != expected)
            throw new Exception($"{scenario}: expected {expected}, got {decision.Action}");
    }
}
