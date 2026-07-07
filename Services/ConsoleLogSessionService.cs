using MineDash.Models;

namespace MineDash.Services;

public interface IConsoleLogSessionService
{
    Task LoadRecentLogsAsync(ServerConfig server, ConsoleState state, int tailLines = 500, CancellationToken ct = default);
    Task PollLogsAsync(ServerConfig server, ConsoleState state, CancellationToken ct = default);
}

public sealed class ConsoleLogSessionService : IConsoleLogSessionService
{
    private const int MaxLiveLogEntries = 1000;

    private readonly ILogService _logService;

    public ConsoleLogSessionService(ILogService logService)
    {
        _logService = logService;
    }

    public async Task LoadRecentLogsAsync(
        ServerConfig server, ConsoleState state, int tailLines = 500, CancellationToken ct = default)
    {
        try
        {
            var (recentLogs, endPosition) = await _logService.GetRecentLogsAsync(server, tailLines, ct);

            state.LiveLogs.Clear();
            state.LiveLogs.AddRange(recentLogs);

            if (HasRenderableLogs(state) || endPosition == 0)
                state.LogFilePosition = endPosition;
            else
                state.LogFilePosition = 0;

            TrimLiveLogs(state);
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

        try
        {
            if (state.ShowLogs && !state.LiveLogs.Any())
            {
                await LoadRecentLogsAsync(server, state, ct: ct);
                return;
            }

            var logPath = server.LogPath!.Trim();
            if (File.Exists(logPath))
            {
                var fileLength = new FileInfo(logPath).Length;
                if (fileLength < state.LogFilePosition)
                {
                    await LoadRecentLogsAsync(server, state, ct: ct);
                    return;
                }
            }

            var (newLogs, endPosition) = await _logService.ReadLogsFromPositionAsync(
                server, state.LogFilePosition, ct);

            if (newLogs.Count > 0)
            {
                state.LiveLogs.AddRange(newLogs);
                TrimLiveLogs(state);
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

    private static void TrimLiveLogs(ConsoleState state)
    {
        if (state.LiveLogs.Count > MaxLiveLogEntries)
            state.LiveLogs.RemoveRange(0, state.LiveLogs.Count - MaxLiveLogEntries);
    }
}
