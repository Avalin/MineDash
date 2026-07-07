using MineDash.Models;

namespace MineDash.Services;

public interface IConsoleLogSessionService
{
    Task LoadRecentLogsAsync(ServerConfig server, ConsoleState state, CancellationToken ct = default);
    Task PollLogsAsync(ServerConfig server, ConsoleState state, CancellationToken ct = default);
}

public sealed class ConsoleLogSessionService : IConsoleLogSessionService
{
    private readonly ILogService _logService;
    private readonly IConsoleLogRetentionService _retention;
    private readonly IConsoleLogFilterService _filters;

    public ConsoleLogSessionService(
        ILogService logService,
        IConsoleLogRetentionService retention,
        IConsoleLogFilterService filters)
    {
        _logService = logService;
        _retention = retention;
        _filters = filters;
    }

    public async Task LoadRecentLogsAsync(
        ServerConfig server, ConsoleState state, CancellationToken ct = default)
    {
        _retention.NormalizeLimits(state);

        try
        {
            var tailLines = _retention.GetInitialTailLines(state);
            var (recentLogs, endPosition) = await _logService.GetRecentLogsAsync(server, tailLines, ct);

            state.LiveLogs.Clear();
            state.LiveLogs.AddRange(recentLogs);
            _retention.ApplyRetention(state);
            _filters.SyncFilterSelections(state);

            if (HasRenderableLogs(state) || endPosition == 0)
                state.LogFilePosition = endPosition;
            else
                state.LogFilePosition = 0;
        }
        catch (Exception ex)
        {
            state.LiveLogs.Add(new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                HasParsedTimestamp = true,
                Message = $"[ERROR] Failed to load logs: {ex.Message}",
                RawLine = $"[ERROR] Failed to load logs: {ex.Message}"
            });
        }
    }

    public async Task PollLogsAsync(ServerConfig server, ConsoleState state, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(server.LogPath?.Trim()))
            return;

        _retention.NormalizeLimits(state);

        try
        {
            if (state.ShowLogs && !state.LiveLogs.Any())
            {
                await LoadRecentLogsAsync(server, state, ct);
                return;
            }

            var logPath = server.LogPath!.Trim();
            if (File.Exists(logPath))
            {
                var fileLength = new FileInfo(logPath).Length;
                if (fileLength < state.LogFilePosition)
                {
                    await LoadRecentLogsAsync(server, state, ct);
                    return;
                }
            }

            var (newLogs, endPosition) = await _logService.ReadLogsFromPositionAsync(
                server, state.LogFilePosition, ct);

            if (newLogs.Count > 0)
            {
                state.LiveLogs.AddRange(newLogs);
                _retention.ApplyRetention(state);
                _filters.SyncFilterSelections(state);
            }

            state.LogFilePosition = endPosition;
            state.LastLogPoll = DateTime.UtcNow;
        }
        catch (Exception ex)
        {
            state.LiveLogs.Add(new LogEntry
            {
                Timestamp = DateTime.UtcNow,
                HasParsedTimestamp = true,
                Message = $"[MineDash] Log poll failed: {ex.Message}",
                RawLine = $"[MineDash] Log poll failed: {ex.Message}",
                Thread = "MineDash",
                Level = "WARN"
            });
        }
    }

    private static bool HasRenderableLogs(ConsoleState state) =>
        state.LiveLogs.Any(l => l.Thread != "MineDash");
}
