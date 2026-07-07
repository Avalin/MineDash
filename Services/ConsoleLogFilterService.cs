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
    string GetThreadFilterKey(string thread);
    HashSet<string> NormalizeThreadSelections(IEnumerable<string> selections);
}

public sealed class ConsoleLogFilterService : IConsoleLogFilterService
{
    private static readonly Regex AsyncChatThreadRegex = new(
        @"^Async\s+Chat\s+Thread(?:\s*-\s*)?\s*#\d+$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public bool HasActiveFilters(ConsoleState state) =>
        state.LevelFilterActive || state.ThreadFilterActive;

    public bool LevelMatches(string? level, ConsoleState state)
    {
        if (!state.LevelFilterActive)
            return true;

        return string.IsNullOrEmpty(level)
            ? state.SelectedLogLevels.Contains(string.Empty)
            : state.SelectedLogLevels.Contains(level);
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
                levels.Add(log.Level);
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

    public string GetThreadFilterKey(string thread)
    {
        if (string.IsNullOrWhiteSpace(thread))
            return string.Empty;

        return AsyncChatThreadRegex.IsMatch(thread.Trim())
            ? "Async Chat Thread"
            : thread.Trim();
    }

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
}
