using System.Globalization;
using System.Text;
using Comienzo.Models;

namespace Comienzo.Services;

internal sealed class SearchEngine
{
    private IReadOnlyList<CatalogItem> _applications = Array.Empty<CatalogItem>();
    private readonly IReadOnlyList<CatalogItem> _settings = SettingsCatalog.Create();

    public void SetApplications(IReadOnlyList<CatalogItem> applications) => _applications = applications;

    public IReadOnlyList<CatalogItem> Search(string query)
    {
        string normalized = Normalize(query.Trim());
        var calculator = CreateCalculator(query);

        if (calculator is not null)
        {
            SetSection(new[] { calculator }, 0, "Calculator");
            return new[] { calculator };
        }

        if (normalized.Length == 0)
        {
            var allItems = _applications.Concat(_settings).ToList();
            var frequent = allItems.Where(item => UsageService.GetCount(item) > 0)
                .OrderByDescending(UsageService.GetCount)
                .ThenByDescending(UsageService.GetLastUsed)
                .Take(8)
                .ToList();
            var frequentKeys = frequent.Select(item => item.UsageKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var applications = SortByUsage(_applications.Where(item => !frequentKeys.Contains(item.UsageKey))).ToList();
            var settings = SortByUsage(_settings.Where(item => !frequentKeys.Contains(item.UsageKey))).ToList();

            var initial = new List<CatalogItem>(allItems.Count);
            int initialSection = 0;
            if (frequent.Count > 0)
            {
                SetSection(frequent, initialSection++, "Most used");
                initial.AddRange(frequent);
            }
            SetSection(applications, initialSection++, "Applications");
            initial.AddRange(applications);
            SetSection(settings, initialSection, "Settings");
            initial.AddRange(settings);
            return initial;
        }

        var apps = ScoreItems(_applications, normalized).ToList();
        var settingsFound = ScoreItems(_settings, normalized).ToList();
        double appBest = apps.FirstOrDefault()?.Score ?? 0;
        double settingBest = settingsFound.FirstOrDefault()?.Score ?? 0;
        bool settingsFirst = settingBest >= 65 && settingBest >= appBest + 7;

        var result = new List<CatalogItem>();
        int section = 0;
        List<CatalogItem> first = settingsFirst ? settingsFound : apps;
        List<CatalogItem> second = settingsFirst ? apps : settingsFound;
        string firstName = settingsFirst ? "Settings" : "Applications";
        string secondName = settingsFirst ? "Applications" : "Settings";

        var firstTop = first.Take(5).ToList();
        if (firstTop.Count > 0)
        {
            SetSection(firstTop, section++, firstName);
            result.AddRange(firstTop);
        }
        var secondTop = second.Take(5).ToList();
        if (secondTop.Count > 0)
        {
            SetSection(secondTop, section++, secondName);
            result.AddRange(secondTop);
        }

        var remaining = new List<CatalogItem>();
        if (first.Count > 5) remaining.AddRange(first.GetRange(5, first.Count - 5));
        if (second.Count > 5) remaining.AddRange(second.GetRange(5, second.Count - 5));
        var other = remaining.OrderByDescending(x => x.Score)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (other.Count > 0)
        {
            SetSection(other, section, "Other results");
            result.AddRange(other);
        }
        return result;
    }

    private static IEnumerable<CatalogItem> ScoreItems(IEnumerable<CatalogItem> items, string query)
    {
        var found = new List<CatalogItem>();
        foreach (CatalogItem item in items)
        {
            double lexicalScore = Score(item, query);
            item.Score = lexicalScore > 0 ? lexicalScore + UsageService.GetSearchBoost(item) : 0;
            if (item.Score >= 18) found.Add(item);
        }
        return found
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase);
    }

    private static IOrderedEnumerable<CatalogItem> SortByUsage(IEnumerable<CatalogItem> items) => items
        .OrderByDescending(UsageService.GetCount)
        .ThenByDescending(UsageService.GetLastUsed)
        .ThenBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase);

    private static double Score(CatalogItem item, string query)
    {
        string name = Normalize(item.Name);
        string haystack = $"{name} {Normalize(item.SearchTerms)}";
        if (name == query) return 140;
        if (name.StartsWith(query, StringComparison.Ordinal)) return 115 - Math.Min(15, name.Length - query.Length);
        if (name.Split(' ').Any(word => word == query)) return 100;
        if (name.Contains(query, StringComparison.Ordinal)) return 82 - Math.Min(20, name.IndexOf(query, StringComparison.Ordinal));

        string[] terms = query.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term is not ("the" or "a" or "an" or "of" or "for" or "and" or "to"))
            .ToArray();
        if (terms.Length == 0) return 0;
        int matches = terms.Count(term => haystack.Contains(term, StringComparison.Ordinal));
        if (matches == terms.Length) return 65 + matches * 5;
        if (matches > 0) return 22 + 12.0 * matches / terms.Length;

        int distance = Levenshtein(name, query, 3);
        return distance <= 3 ? 45 - distance * 8 : 0;
    }

    private static CatalogItem? CreateCalculator(string query)
    {
        if (!MathEvaluator.TryEvaluate(query, out double value)) return null;
        string formatted = MathEvaluator.Format(value);
        return new CatalogItem
        {
            Name = formatted,
            Subtitle = "Press Enter to copy the result",
            SearchTerms = query,
            Kind = ItemKind.Calculator,
            LaunchKind = LaunchKind.None,
            Target = formatted,
            IconSource = "calculator",
            Score = 1000
        };
    }

    private static void SetSection(IEnumerable<CatalogItem> items, int order, string name)
    {
        foreach (CatalogItem item in items)
        {
            item.SectionOrder = order;
            item.SectionName = name;
        }
    }

    internal static string Normalize(string value)
    {
        string decomposed = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.IsLetterOrDigit(c) ? c : ' ');
        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static int Levenshtein(string left, string right, int maximum)
    {
        if (Math.Abs(left.Length - right.Length) > maximum) return maximum + 1;
        int[] previous = Enumerable.Range(0, right.Length + 1).ToArray();
        int[] current = new int[right.Length + 1];
        for (int i = 1; i <= left.Length; i++)
        {
            current[0] = i;
            int rowMin = current[0];
            for (int j = 1; j <= right.Length; j++)
            {
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (left[i - 1] == right[j - 1] ? 0 : 1));
                rowMin = Math.Min(rowMin, current[j]);
            }
            if (rowMin > maximum) return maximum + 1;
            (previous, current) = (current, previous);
        }
        return previous[right.Length];
    }
}
