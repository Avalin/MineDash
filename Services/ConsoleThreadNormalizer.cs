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
        new(@"-\d+$", RegexOptions.Compiled),
        new(@"-[0-9a-fA-F]{8,}$", RegexOptions.Compiled | RegexOptions.IgnoreCase),
        new(@"-[0-9a-zA-Z]{16,}$", RegexOptions.Compiled),
    ];

    private static readonly Regex RconFamilyRegex = new(
        @"^RCON\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly HashSet<string> DefaultOnThreads = new(StringComparer.OrdinalIgnoreCase)
    {
        "Server thread",
        "Async Chat Thread",
    };

    public static string Normalize(string thread)
    {
        if (string.IsNullOrWhiteSpace(thread))
            return string.Empty;

        var key = StripSuffixes(thread.Trim());

        if (RconFamilyRegex.IsMatch(key))
            return "RCON";

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
