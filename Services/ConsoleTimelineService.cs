using MineDash.Models;

namespace MineDash.Services;

public interface IConsoleTimelineService
{
    IReadOnlyList<ConsoleMergedEntry> BuildMergedEntries(
        ConsoleState state,
        IReadOnlyList<CommandHistoryItem> commandHistory);
}

public sealed class ConsoleTimelineService : IConsoleTimelineService
{
    private readonly IConsoleLogFilterService _filters;
    private readonly IConsoleLogRetentionService _retention;
    private readonly ITimeDisplayService _timeDisplay;

    public ConsoleTimelineService(
        IConsoleLogFilterService filters,
        IConsoleLogRetentionService retention,
        ITimeDisplayService timeDisplay)
    {
        _filters = filters;
        _retention = retention;
        _timeDisplay = timeDisplay;
    }

    public IReadOnlyList<ConsoleMergedEntry> BuildMergedEntries(
        ConsoleState state,
        IReadOnlyList<CommandHistoryItem> commandHistory)
    {
        var entries = new List<ConsoleMergedEntry>();

        if (state.ShowLogs)
        {
            var threadLogCounts = _filters.GetThreadLogCounts(state);
            foreach (var log in state.LiveLogs)
            {
                if (!_retention.IsWithinWindow(state, log.Timestamp))
                    continue;

                if (!_filters.LevelMatches(log.Level, state) || !_filters.ThreadMatches(log.Thread, state, threadLogCounts))
                    continue;

                entries.Add(new ConsoleMergedEntry
                {
                    Timestamp = log.Timestamp,
                    Sequence = log.Sequence,
                    IsLog = true,
                    LogLine = string.IsNullOrWhiteSpace(log.Message) ? log.RawLine : log.Message,
                    ThreadKey = _filters.GetThreadFilterKey(log.Thread ?? string.Empty),
                    LevelKey = _filters.GetLevelFilterKey(log.Level ?? string.Empty)
                });
            }
        }

        if (state.ShowCommands)
        {
            var commandIndex = 0;
            foreach (var cmd in commandHistory)
            {
                if (!_retention.IsWithinWindow(state, cmd.Timestamp))
                    continue;

                entries.Add(new ConsoleMergedEntry
                {
                    Timestamp = cmd.Timestamp,
                    Sequence = commandIndex++,
                    IsLog = false,
                    Command = cmd.Command,
                    Response = cmd.Response,
                    ExecutedBy = cmd.ExecutedBy
                });
            }
        }

        return entries
            .OrderBy(e => _timeDisplay.NormalizeForSort(e.Timestamp))
            .ThenBy(e => e.IsLog ? 0 : 1)
            .ThenBy(e => e.Sequence)
            .ToList();
    }
}
