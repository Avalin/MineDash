using MineDash.Models;

namespace MineDash.Services;

public interface IConsoleLogFilterService
{
    bool HasActiveFilters(ConsoleState state);
    bool IsLevelFilterRestricting(ConsoleState state);
    bool IsThreadFilterRestricting(ConsoleState state);
    int GetLevelBadgeCount(ConsoleState state);
    int GetThreadBadgeCount(ConsoleState state);
    bool LevelMatches(string? level, ConsoleState state);
    bool ThreadMatches(string? thread, ConsoleState state);
    bool ThreadMatches(string? thread, ConsoleState state, IReadOnlyDictionary<string, int> threadLogCounts);
    HashSet<string> GetAvailableLevels(ConsoleState state);
    HashSet<string> GetAvailableThreads(ConsoleState state);
    IReadOnlyDictionary<string, int> GetThreadLogCounts(ConsoleState state);
    IReadOnlyList<string> GetThreadsForFilterUi(ConsoleState state);
    void SyncFilterSelections(ConsoleState state);
    void ToggleLevel(ConsoleState state, string level, bool isChecked);
    void ToggleThread(ConsoleState state, string thread, bool isChecked);
    void SelectAllThreads(ConsoleState state);
    void DeselectAllThreads(ConsoleState state);
    string GetLevelFilterKey(string level);
    string GetThreadFilterKey(string thread);
    HashSet<string> NormalizeLevelSelections(IEnumerable<string> selections);
    HashSet<string> NormalizeThreadSelections(IEnumerable<string> selections);
}

public sealed class ConsoleLogFilterService : IConsoleLogFilterService
{
    private static readonly string[] KnownLevels = ["TRACE", "DEBUG", "INFO", "WARN", "ERROR", "FATAL"];

    public bool HasActiveFilters(ConsoleState state) =>
        state.LevelFilterActive || state.ThreadFilterActive;

    public bool IsLevelFilterRestricting(ConsoleState state)
    {
        if (!state.LevelFilterActive)
            return false;

        var available = GetAvailableLevels(state);
        return state.SelectedLogLevels.Count < available.Count;
    }

    public bool IsThreadFilterRestricting(ConsoleState state)
    {
        if (!state.ThreadFilterActive)
            return false;

        var available = GetAvailableThreads(state);
        return state.SelectedThreads.Count < available.Count;
    }

    public int GetLevelBadgeCount(ConsoleState state)
    {
        var available = GetAvailableLevels(state);
        if (!state.LevelFilterActive)
            return available.Count;

        return state.SelectedLogLevels.Count;
    }

    public int GetThreadBadgeCount(ConsoleState state)
    {
        var available = GetAvailableThreads(state);
        if (!state.ThreadFilterActive)
            return available.Count;

        return state.SelectedThreads.Count;
    }

    public void SyncFilterSelections(ConsoleState state)
    {
        var availableLevels = GetAvailableLevels(state);
        var availableThreads = GetAvailableThreads(state);

        if (state.LevelFilterActive)
        {
            state.SelectedLogLevels.IntersectWith(availableLevels);

            if (availableLevels.Count > 0 && state.SelectedLogLevels.Count == availableLevels.Count)
            {
                state.LevelFilterActive = false;
                state.SelectedLogLevels.Clear();
            }
        }

        if (state.ThreadFilterActive)
        {
            if (!state.ThreadFilterInitialized && availableThreads.Count > 0)
            {
                ApplyDefaultThreadFilter(state);
                state.ThreadFilterInitialized = true;
            }
            else if (state.ThreadFilterInitialized)
            {
                MigrateThreadSelectionsToGroupKeys(state, GetThreadLogCounts(state));
                state.SelectedThreads.RemoveWhere(thread => !availableThreads.Contains(thread));
            }

            if (availableThreads.Count > 0
                && availableThreads.All(state.SelectedThreads.Contains))
            {
                state.ThreadFilterActive = false;
                state.SelectedThreads.Clear();
            }
        }
    }

    public IReadOnlyList<string> GetThreadsForFilterUi(ConsoleState state) =>
        ConsoleThreadNormalizer.OrderForFilterUi(GetAvailableThreads(state));

    public void SelectAllThreads(ConsoleState state)
    {
        var availableThreads = GetAvailableThreads(state);
        state.ThreadFilterActive = true;
        state.SelectedThreads.Clear();
        foreach (var thread in availableThreads)
            state.SelectedThreads.Add(thread);
        state.ThreadFilterInitialized = true;

        if (availableThreads.Count > 0)
        {
            state.ThreadFilterActive = false;
            state.SelectedThreads.Clear();
        }
    }

    public void DeselectAllThreads(ConsoleState state)
    {
        state.ThreadFilterActive = true;
        state.SelectedThreads.Clear();
        state.ThreadFilterInitialized = true;
    }

    public bool LevelMatches(string? level, ConsoleState state)
    {
        if (!state.LevelFilterActive)
            return true;

        var key = GetLevelFilterKey(level ?? string.Empty);
        return string.IsNullOrEmpty(key)
            ? state.SelectedLogLevels.Contains(string.Empty)
            : state.SelectedLogLevels.Contains(key);
    }

    public bool ThreadMatches(string? thread, ConsoleState state) =>
        ThreadMatches(thread, state, GetThreadLogCounts(state));

    public bool ThreadMatches(string? thread, ConsoleState state, IReadOnlyDictionary<string, int> threadLogCounts)
    {
        if (!state.ThreadFilterActive)
            return true;

        if (string.IsNullOrEmpty(thread))
            return state.SelectedThreads.Contains(string.Empty);

        var normalizedKey = GetThreadFilterKey(thread);
        var groupKey = GetThreadGroupKey(normalizedKey, threadLogCounts);
        return state.SelectedThreads.Contains(groupKey);
    }

    public IReadOnlyDictionary<string, int> GetThreadLogCounts(ConsoleState state)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var log in state.LiveLogs)
        {
            if (string.IsNullOrEmpty(log.Thread))
                continue;

            var key = GetThreadFilterKey(log.Thread);
            counts.TryGetValue(key, out var count);
            counts[key] = count + 1;
        }

        return counts;
    }

    public HashSet<string> GetAvailableLevels(ConsoleState state)
    {
        var levels = new HashSet<string>();
        foreach (var log in state.LiveLogs)
        {
            if (!string.IsNullOrEmpty(log.Level))
                levels.Add(GetLevelFilterKey(log.Level));
        }

        return levels;
    }

    public HashSet<string> GetAvailableThreads(ConsoleState state)
    {
        var counts = GetThreadLogCounts(state);
        var threads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in counts.Keys)
            threads.Add(GetThreadGroupKey(key, counts));

        return threads;
    }

    public void ToggleLevel(ConsoleState state, string level, bool isChecked)
    {
        var availableLevels = GetAvailableLevels(state);
        EnsureExplicitLevelFilter(state, availableLevels);

        if (isChecked)
            state.SelectedLogLevels.Add(level);
        else
            state.SelectedLogLevels.Remove(level);

        if (availableLevels.Count > 0 && state.SelectedLogLevels.Count == availableLevels.Count)
        {
            state.LevelFilterActive = false;
            state.SelectedLogLevels.Clear();
        }
    }

    public void ToggleThread(ConsoleState state, string thread, bool isChecked)
    {
        var availableThreads = GetAvailableThreads(state);
        EnsureExplicitThreadFilter(state, availableThreads);

        if (isChecked)
            state.SelectedThreads.Add(thread);
        else
            state.SelectedThreads.Remove(thread);

        state.ThreadFilterInitialized = true;

        if (availableThreads.Count > 0 && availableThreads.All(state.SelectedThreads.Contains))
        {
            state.ThreadFilterActive = false;
            state.SelectedThreads.Clear();
        }
    }

    public string GetLevelFilterKey(string level)
    {
        if (string.IsNullOrWhiteSpace(level))
            return string.Empty;

        var trimmed = level.Trim();

        var exact = TryNormalizeKnownLevel(trimmed);
        if (exact is not null)
            return exact;

        // RCON lines: "172.21.0.2 #12/INFO", "0:0:0:0:0:0:0:1 #5/INFO", etc.
        var slash = trimmed.LastIndexOf('/');
        if (slash >= 0)
        {
            var suffix = TryNormalizeKnownLevel(trimmed[(slash + 1)..]);
            if (suffix is not null)
                return suffix;
        }

        foreach (var known in KnownLevels.OrderByDescending(l => l.Length))
        {
            if (trimmed.Contains($"/{known}", StringComparison.OrdinalIgnoreCase))
                return known;
        }

        foreach (var known in KnownLevels.OrderByDescending(l => l.Length))
        {
            if (trimmed.Contains(known, StringComparison.OrdinalIgnoreCase))
                return known;
        }

        return trimmed;
    }

    private static string? TryNormalizeKnownLevel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim();
        foreach (var known in KnownLevels)
        {
            if (trimmed.Equals(known, StringComparison.OrdinalIgnoreCase))
                return known;
        }

        return null;
    }

    public string GetThreadFilterKey(string thread) =>
        ConsoleThreadNormalizer.Normalize(thread);

    public HashSet<string> NormalizeLevelSelections(IEnumerable<string> selections) =>
        new(selections.Select(GetLevelFilterKey));

    public HashSet<string> NormalizeThreadSelections(IEnumerable<string> selections) =>
        new(selections.Select(GetThreadFilterKey));

    private static void EnsureExplicitLevelFilter(ConsoleState state, HashSet<string> availableLevels)
    {
        if (state.LevelFilterActive)
            return;

        state.LevelFilterActive = true;
        state.SelectedLogLevels.Clear();
        foreach (var level in availableLevels)
            state.SelectedLogLevels.Add(level);
    }

    private static void EnsureExplicitThreadFilter(ConsoleState state, HashSet<string> availableThreads)
    {
        if (state.ThreadFilterActive)
            return;

        state.ThreadFilterActive = true;
        state.SelectedThreads.Clear();
        foreach (var thread in availableThreads)
            state.SelectedThreads.Add(thread);
    }

    private static void ApplyDefaultThreadFilter(ConsoleState state)
    {
        state.ThreadFilterActive = true;
        state.SelectedThreads.Clear();
        foreach (var thread in ConsoleThreadNormalizer.DefaultSelectedThreads)
            state.SelectedThreads.Add(thread);
    }

    private static string GetThreadGroupKey(string normalizedKey, IReadOnlyDictionary<string, int> threadLogCounts)
    {
        var count = threadLogCounts.TryGetValue(normalizedKey, out var logCount) ? logCount : 0;
        return ConsoleThreadNormalizer.GetFilterGroupKey(normalizedKey, count);
    }

    private static void MigrateThreadSelectionsToGroupKeys(
        ConsoleState state,
        IReadOnlyDictionary<string, int> threadLogCounts)
    {
        if (state.SelectedThreads.Remove("Mod Loading"))
            state.SelectedThreads.Add("main");

        var miscMembers = threadLogCounts
            .Where(kvp => ConsoleThreadNormalizer.IsMiscGrouped(kvp.Key, kvp.Value))
            .Select(kvp => kvp.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (miscMembers.Count == 0)
            return;

        if (!state.SelectedThreads.Any(miscMembers.Contains))
            return;

        state.SelectedThreads.RemoveWhere(miscMembers.Contains);
        state.SelectedThreads.Add(ConsoleThreadNormalizer.MiscellaneousKey);
    }
}
