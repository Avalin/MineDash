using System.Text.RegularExpressions;

namespace MineDash.Services;

/// <summary>
/// Collapses per-instance thread names into stable filter keys using suffix/prefix rules.
/// </summary>
public static class ConsoleThreadNormalizer
{
    private static readonly Regex[] SuffixPatterns =
    [
        new(@"\s*-\s*#\d+$", RegexOptions.Compiled),
        new(@"\s*-\s*\d+$", RegexOptions.Compiled),
        new(@"\s+#\d+$", RegexOptions.Compiled),
        new(@"-\d+-thread$", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"-\d+$", RegexOptions.Compiled),
        new(@"-[0-9a-fA-F]{8,}$", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"-[0-9a-zA-Z]{16,}$", RegexOptions.Compiled),
    ];

    private static readonly (Regex Pattern, string Key)[] PrefixFamilies =
    [
        (new(@"^VoiceChat", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Voice Chat"),
        (new(@"^modloading", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Mod Loading"),
        (new(@"^ForkJoinPool", RegexOptions.Compiled | RegexOptions.IgnoreCase), "ForkJoin Pool"),
        (new(@"^pool$", RegexOptions.Compiled | RegexOptions.IgnoreCase), "Pool"),
    ];

    private static readonly Regex RconFamilyRegex = new(
        @"^RCON\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> DefaultOnThreads = new(StringComparer.OrdinalIgnoreCase)
    {
        "main",
        "Server thread",
        "Async Chat Thread",
    };

    public static IReadOnlyList<string> DefaultFilterThreads { get; } =
    [
        "main",
        "Server thread",
        "Async Chat Thread",
    ];

    /// <summary>Threads selected when a console first loads (subset of pinned).</summary>
    public static IReadOnlyList<string> DefaultSelectedThreads { get; } =
    [
        "Server thread",
        "Async Chat Thread",
    ];

    private static readonly Dictionary<string, (string Label, string Tooltip)> FriendlyLabels =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["main"] = ("Mod Loader", "Java main thread — mod/plugin loading and startup logs (main)"),
            ["Server thread"] = ("Server Core", "Primary Minecraft server tick loop (Server thread)"),
            ["Async Chat Thread"] = ("Player Chat", "In-game chat and player messages (Async Chat Thread)"),
        };

    public static string GetThreadDisplayName(string normalizedKey) =>
        FriendlyLabels.TryGetValue(normalizedKey, out var entry) ? entry.Label : normalizedKey;

    public static string GetThreadTooltip(string normalizedKey) =>
        FriendlyLabels.TryGetValue(normalizedKey, out var entry)
            ? entry.Tooltip
            : normalizedKey;

    public static bool IsPinnedThread(string normalizedKey) =>
        DefaultFilterThreads.Contains(normalizedKey, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> OrderForFilterUi(IEnumerable<string> available)
    {
        var remaining = new HashSet<string>(available, StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();

        foreach (var pinned in DefaultFilterThreads)
        {
            ordered.Add(pinned);
            remaining.Remove(pinned);
        }

        ordered.AddRange(remaining.OrderBy(t => t, StringComparer.OrdinalIgnoreCase));
        return ordered;
    }

    public static string Normalize(string thread)
    {
        if (string.IsNullOrWhiteSpace(thread))
            return string.Empty;

        var key = StripSuffixes(thread.Trim());

        if (RconFamilyRegex.IsMatch(key))
            return "RCON";

        foreach (var (pattern, familyKey) in PrefixFamilies)
        {
            if (pattern.IsMatch(key))
                return familyKey;
        }

        return key;
    }

    public static bool IsDefaultOn(string normalizedKey) =>
        !string.IsNullOrEmpty(normalizedKey) && DefaultOnThreads.Contains(normalizedKey);

    public static IReadOnlyCollection<string> DefaultOnThreadNames => DefaultOnThreads;

    public static string? GetMessageColorClass(string normalizedThreadKey)
    {
        if (string.IsNullOrEmpty(normalizedThreadKey))
            return null;

        if (normalizedThreadKey.Equals("Server thread", StringComparison.OrdinalIgnoreCase))
            return "log-thread-server";

        if (normalizedThreadKey.Equals("Async Chat Thread", StringComparison.OrdinalIgnoreCase))
            return "log-thread-chat";

        return null;
    }

    private static string StripSuffixes(string key)
    {
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var pattern in SuffixPatterns)
            {
                var next = pattern.Replace(key, string.Empty).TrimEnd();
                if (next.Length < key.Length)
                {
                    key = next;
                    changed = true;
                }
            }
        }

        return key;
    }
}
