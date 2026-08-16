using System.Windows.Media;
using Comienzo.Services;

namespace Comienzo.Models;

public enum ItemKind
{
    Application,
    Setting,
    Calculator
}

public enum LaunchKind
{
    None,
    Shell,
    ExplorerShellApp
}

public sealed class CatalogItem
{
    public required string Name { get; init; }
    public string Subtitle { get; init; } = "";
    public string SearchTerms { get; init; } = "";
    public required ItemKind Kind { get; init; }
    public LaunchKind LaunchKind { get; init; } = LaunchKind.Shell;
    public string Target { get; init; } = "";
    public string Arguments { get; init; } = "";
    public string WorkingDirectory { get; init; } = "";
    public string IconSource { get; init; } = "";
    public int IconIndex { get; init; }
    public double Score { get; set; }
    public int SectionOrder { get; set; }
    public string SectionName { get; set; } = "";
    public string UsageKey => $"{Kind}|{Target}".Trim().ToLowerInvariant();
    public ImageSource? Icon => IconService.Get(this);
}
