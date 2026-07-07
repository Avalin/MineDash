using System.Text.RegularExpressions;
using MineDash.Models;

namespace MineDash.Services;

public interface IConsoleLogFilterService
{
    bool HasActiveFilters(ConsoleState state);
    bool LevelMatches(string? level, ConsoleState state);
    bool ThreadMatches(string? thread, ConsoleState state);
    HashSet<string> GetAvailableLevels(ConsoleState state);
    HashSet<string> GetAvailableThreads(ConsoleState state);
    void ToggleLevel(ConsoleState state, string level, bool isChecked);
    void ToggleThread(ConsoleState state, string thread, bool isChecked);
    string GetLevelFilterKey(string level);
    string GetThreadFilterKey(string thread);
    HashSet<string> NormalizeLevelSelections(IEnumerable<string> selections);
    HashSet<string> NormalizeThreadSelections(IEnumerable<string> selections);
}

public sealed class ConsoleLogFilterService : IConsoleLogFilterService
{
    private static readonly Regex AsyncChatThreadRegex = new(
        @"^Async\s+Chat\s+Thread(?:\s*-\s*)?\s*#\d+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex RconThreadRegex = new(
        @"^RCON\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // RCON client lines parse as e.g. "172.21.0.2 #12/INFO" — treat as INFO.
    private static readonly Regex RconIpLevelRegex = new(
        @"^(?:\d{1,3}\.){3}\d{1,3}\s+#\d+/(?:INFO|WARN|ERROR|DEBUG|TRACE)$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public bool HasActiveFilters(ConsoleState state) =>
        state.LevelFilterActive || state.ThreadFilterActive;

    public bool LevelMatches(string? level, ConsoleState state)
    {
        if (!state.LevelFilterActive)
            return true;

        var key = GetLevelFilterKey(level ?? string.Empty);
        return string.IsNullOrEmpty(key)
            ? state.SelectedLogLevels.Contains(string.Empty)
            : state.SelectedLogLevels.Contains(key);
    }

    public bool ThreadMatches(string? thread, ConsoleState state)
    {
        if (!state.ThreadFilterActive)
            return true;

        if (string.IsNullOrEmpty(thread))
            return state.SelectedThreads.Contains(string.Empty);

        return state.SelectedThreads.Contains(GetThreadFilterKey(thread));
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
        var threads = new HashSet<string>();
        foreach (var log in state.LiveLogs)
        {
            if (!string.IsNullOrEmpty(log.Thread))
                threads.Add(GetThreadFilterKey(log.Thread));
        }

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

        if (state.SelectedLogLevels.Count == availableLevels.Count)
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

        if (state.SelectedThreads.Count == availableThreads.Count)
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
        return RconIpLevelRegex.IsMatch(trimmed) ? "INFO" : trimmed;
    }

    public string GetThreadFilterKey(string thread)
    {
        if (string.IsNullOrWhiteSpace(thread))
            return string.Empty;

        var trimmed = thread.Trim();
        if (AsyncChatThreadRegex.IsMatch(trimmed))
            return "Async Chat Thread";

        if (RconThreadRegex.IsMatch(trimmed))
            return "RCON";

        return trimmed;
    }

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
        {
            if (!IsDefaultOffThread(thread))
                state.SelectedThreads.Add(thread);
        }
    }

    private static bool IsDefaultOffThread(string threadKey) =>
        threadKey.Equals("RCON", StringComparison.OrdinalIgnoreCase);
}
