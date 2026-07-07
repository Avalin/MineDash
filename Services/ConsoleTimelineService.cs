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
    private readonly ITimeDisplayService _timeDisplay;

    public ConsoleTimelineService(IConsoleLogFilterService filters, ITimeDisplayService timeDisplay)
    {
        _filters = filters;
        _timeDisplay = timeDisplay;
    }

    public IReadOnlyList<ConsoleMergedEntry> BuildMergedEntries(
        ConsoleState state,
        IReadOnlyList<CommandHistoryItem> commandHistory)
    {
        var entries = new List<ConsoleMergedEntry>();

        if (state.ShowLogs)
        {
            foreach (var log in state.LiveLogs)
            {
                if (!_filters.LevelMatches(log.Level, state) || !_filters.ThreadMatches(log.Thread, state))
                    continue;

                entries.Add(new ConsoleMergedEntry
                {
                    Timestamp = log.Timestamp,
                    Sequence = log.Sequence,
                    IsLog = true,
                    LogLine = string.IsNullOrWhiteSpace(log.Message) ? log.RawLine : log.Message
                });
            }
        }

        if (state.ShowCommands)
        {
            var commandIndex = 0;
            foreach (var cmd in commandHistory)
            {
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
